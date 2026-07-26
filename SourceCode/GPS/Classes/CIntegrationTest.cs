// F07 Integration Test (NV custom) — in-app validator of the sensing chain
// (roll compensation + heading). Two guided drives:
//   Straight (multi-speed) -> roll->lateral residual: antenna-height error,
//     IMU lag, F2F-noise-vs-speed, mount/noise.
//   Circle (constant angle) -> heading tracking in a sustained turn: per-source
//     error vs the geometric tangent, yaw scale.
// Records live signals (timer-sampled from FormGPS), analyses locally (rule
// based, offline) and writes a self-contained HTML report to the AOG documents
// folder. Diagnostic only — nothing is applied to the machine.
//
// Heading unit note: imuHeading is DEGREES on real hardware but RADIANS in the
// simulator (CSim sets imuHeading = headingTrue). We normalise to radians at
// record time (sim-aware). gpsHeading / fixHeading are already radians.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace AgOpenGPS
{
    public enum ITMode { None, Straight, Circle }

    public struct ITSample
    {
        public double t;        // seconds since test start
        public double rollDeg;  // IMU roll, degrees
        public double imuRad;   // IMU heading, radians (sim-aware)
        public double f2fRad;   // Fix2Fix (GPS) heading, radians
        public double fixRad;   // fused heading, radians
        public double E, N;     // corrected position (easting/northing)
        public double xte;      // cross-track error, m (0 when no line)
        public double yaw;      // IMU yaw rate (raw)
        public double speed;    // km/h
        public double steer;    // module steer angle, degrees
    }

    public sealed class ITCheck
    {
        public string name;      // localized check name
        public int status;       // 0 good, 1 warn, 2 crit, 3 not measured
        public string desc;      // one-line finding
        public string value;     // measured value
        public string note;      // measurement conditions, or what is missing when status 3
    }

    public sealed class ITResult
    {
        public bool ok;                 // enough data / analysable
        public string message;          // shown when !ok
        public ITMode mode;
        public List<ITCheck> checks = new List<ITCheck>();
        public string summary = "";
        public string svg = "";         // inline chart(s)
        public int okCount, total;
        public double durationSec;
        // meta for header
        public string subtitle = "";
    }

    public sealed class CIntegrationTest
    {
        public ITMode mode = ITMode.None;
        public bool running;
        private readonly List<ITSample> s = new List<ITSample>();
        private DateTime t0;
        private double lastE = double.NaN, lastN = double.NaN;
        private double cumHeadingRad;           // for circle live progress
        private double prevFixRad = double.NaN;

        // straight speed bands (km/h) — center + tolerance
        private static readonly double[] bandCenter = { 3, 7, 10 };
        private const double bandTol = 1.6;
        private const double targetPerBandSec = 12.0;   // needed per band
        private const double circleTargetRad = 2 * Math.PI * 2; // ~2 laps

        // circle geometry: the tangent reference degrades as 1/R, so a tight circle
        // is graded with widened thresholds instead of a confident wrong verdict.
        private const double circleTargetR = 12.0;      // m, suggested to the driver
        private const double circleMinR = 8.0;          // below this: reduced confidence
        private const double lapFitTol = 0.08;          // reject a lap when fitRms/R exceeds this
        private const double refNoiseK = 3.0;           // threshold = k * reference noise floor

        public int SampleCount { get { return s.Count; } }

        public void Start(ITMode m)
        {
            mode = m; running = true;
            s.Clear();
            t0 = DateTime.Now;
            lastE = lastN = double.NaN;
            cumHeadingRad = 0; prevFixRad = double.NaN;
        }

        public void Stop() { running = false; }

        public string BuildCsv()
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("t,rollDeg,imuRad,f2fRad,fixRad,E,N,xte,yaw,speed,steer");
            foreach (var x in s)
                sb.AppendLine(
                    x.t.ToString("0.###", ci) + "," + x.rollDeg.ToString("0.###", ci) + "," +
                    x.imuRad.ToString("0.#####", ci) + "," + x.f2fRad.ToString("0.#####", ci) + "," +
                    x.fixRad.ToString("0.#####", ci) + "," + x.E.ToString("0.###", ci) + "," +
                    x.N.ToString("0.###", ci) + "," + x.xte.ToString("0.####", ci) + "," +
                    x.yaw.ToString("0.###", ci) + "," + x.speed.ToString("0.##", ci) + "," +
                    x.steer.ToString("0.##", ci));
            return sb.ToString();
        }

        // Called ~10 Hz by the form's timer. Dedupes by position (a new fix moves
        // the machine); skips stationary (F2F heading undefined without motion)
        // and invalid roll.
        public void RecordFrom(FormGPS mf)
        {
            if (!running) return;
            if (mf.ahrs.imuRoll == 88888 || mf.ahrs.imuRoll == 99999) return;
            if (mf.avgSpeed < 0.4) return;                       // stop = pause

            double E = mf.pn.fix.easting, N = mf.pn.fix.northing;
            if (!double.IsNaN(lastE))
            {
                double d = Math.Abs(E - lastE) + Math.Abs(N - lastN);
                if (d < 1e-4) return;                           // same fix, no motion
            }
            lastE = E; lastN = N;

            // Roll arrives as ahrs.imuRoll (deg) whatever the source (IMU / dual / HPR),
            // so the roll test is source-agnostic. Heading: with dual-as-IMU, imuHeading
            // holds the dual heading (deg); 99999 = no IMU/dual heading -> NaN so the
            // heading checks skip instead of feeding garbage.
            double imuRaw = mf.ahrs.imuHeading;
            double imuRad = (imuRaw > 3600.0) ? double.NaN
                : (mf.timerSim.Enabled ? imuRaw : glm.toRadians(imuRaw));

            var smp = new ITSample
            {
                t = (DateTime.Now - t0).TotalSeconds,
                rollDeg = mf.ahrs.imuRoll,
                imuRad = imuRad,
                f2fRad = mf.gpsHeading,
                fixRad = mf.fixHeading,
                E = E, N = N,
                xte = mf.vehicle.modeActualXTE,
                yaw = mf.ahrs.imuYawRate,
                speed = mf.avgSpeed,
                steer = mf.mc.actualSteerAngleDegrees,
            };
            s.Add(smp);

            if (mode == ITMode.Circle && !double.IsNaN(prevFixRad))
                cumHeadingRad += Math.Abs(WrapPi(mf.fixHeading - prevFixRad));
            prevFixRad = mf.fixHeading;
        }

        // 0..1 progress + driver hint split into an action line and a big value
        // (so the UI can show the target speed large and readable while driving).
        public double Progress(FormGPS mf, out string action, out string big, bool hr)
        {
            if (mode == ITMode.Circle)
            {
                double p = Clamp01(cumHeadingRad / circleTargetRad);
                double tgtR, tgtSteer;
                TargetCircle(mf, out tgtR, out tgtSteer);
                action = (hr ? "Drži kut volana " : "Hold steering angle ")
                       + tgtSteer.ToString("0", CultureInfo.InvariantCulture) + "°  ("
                       + (hr ? "cilj R " : "target R ") + tgtR.ToString("0", CultureInfo.InvariantCulture) + " m)";
                // live implied radius is the feedback the driver can actually act on
                double st = Math.Abs(mf.mc.actualSteerAngleDegrees);
                big = st > 1.0
                    ? "R ≈ " + (mf.vehicle.VehicleConfig.Wheelbase / Math.Tan(glm.toRadians(st))).ToString("0.0", CultureInfo.InvariantCulture) + " m"
                    : (hr ? "zakreni" : "turn in");
                return p;
            }
            // straight: seconds accumulated in each band (monotonic — samples only add)
            double[] sec = BandSeconds();
            double filled = 0;
            for (int i = 0; i < 3; i++) filled += Math.Min(sec[i], targetPerBandSec);
            // guide sequentially: the first band (low -> high) that isn't full yet.
            // Stable order 3 -> 7 -> 10, no flicker (a band only advances once complete).
            int next = 2;
            for (int i = 0; i < 3; i++) { if (sec[i] < targetPerBandSec) { next = i; break; } }
            action = hr ? "Vozi ravno na" : "Drive straight at";
            big = bandCenter[next].ToString("0", CultureInfo.InvariantCulture) + " km/h";
            return Clamp01(filled / (3 * targetPerBandSec));
        }

        // Suggested circle for this machine: R = wheelbase / tan(steer). Aim for the
        // target radius, but fall back to what the machine can actually steer.
        public static void TargetCircle(FormGPS mf, out double targetR, out double targetSteerDeg)
        {
            double wb = mf.vehicle.VehicleConfig.Wheelbase;
            if (wb < 0.5) wb = 2.8;                        // unconfigured — assume a mid tractor
            double maxDeg = mf.vehicle.maxSteerAngle;
            if (maxDeg < 5) maxDeg = 30;

            targetSteerDeg = glm.toDegrees(Math.Atan(wb / circleTargetR));
            if (targetSteerDeg > maxDeg) targetSteerDeg = maxDeg;
            targetR = wb / Math.Tan(glm.toRadians(targetSteerDeg));
        }

        public bool IsComplete()
        {
            if (mode == ITMode.Circle) return cumHeadingRad >= circleTargetRad;
            double[] sec = BandSeconds();
            return sec[0] >= targetPerBandSec && sec[1] >= targetPerBandSec && sec[2] >= targetPerBandSec;
        }

        private double[] BandSeconds()
        {
            // approx seconds = count / sampleHz; use per-sample dt from timestamps
            double[] sec = new double[3];
            double dt = MeanDt();
            for (int i = 0; i < s.Count; i++)
                for (int b = 0; b < 3; b++)
                    if (Math.Abs(s[i].speed - bandCenter[b]) <= bandTol) { sec[b] += dt; break; }
            return sec;
        }

        private double MeanDt()
        {
            if (s.Count < 2) return 0.1;
            double span = s[s.Count - 1].t - s[0].t;
            return span > 0 ? span / (s.Count - 1) : 0.1;
        }

        // ---------------------------------------------------------------- analysis
        public ITResult Analyze(FormGPS mf, bool hr)
        {
            var r = new ITResult { mode = mode, durationSec = s.Count > 0 ? s[s.Count - 1].t : 0 };
            if (s.Count < 40)
            {
                r.ok = false;
                r.message = hr ? "Premalo podataka — vozi duže / stabilnije."
                               : "Not enough data — drive longer / steadier.";
                return r;
            }
            r.ok = true;
            if (mode == ITMode.Straight) AnalyzeStraight(mf, r, hr);
            else AnalyzeCircle(mf, r, hr);

            // checks that could not be measured (status 3) are not scored either way
            r.total = 0;
            r.okCount = 0;
            foreach (var c in r.checks)
            {
                if (c.status == 3) continue;
                r.total++;
                if (c.status == 0) r.okCount++;
            }
            return r;
        }

        private void AnalyzeStraight(FormGPS mf, ITResult r, bool hr)
        {
            // steady-straight mask
            var idx = new List<int>();
            for (int i = 0; i < s.Count; i++)
                if (Math.Abs(s[i].steer) < 3.0 && Math.Abs(s[i].yaw) < 3.0) idx.Add(i);
            if (idx.Count < 30) idx = AllIdx();

            // lateral residual: prefer XTE if a line drives it (has spread), else PCA line
            double[] resid;
            bool usedXte = HasSpread(idx, true);
            if (usedXte)
            {
                resid = new double[idx.Count];
                for (int k = 0; k < idx.Count; k++) resid[k] = s[idx[k]].xte;
            }
            else resid = PcaResidual(idx);

            // roll excitation check
            double rollRms = 0; foreach (int i in idx) rollRms += s[i].rollDeg * s[i].rollDeg;
            rollRms = Math.Sqrt(rollRms / idx.Count);

            // OLS resid = a*sin(roll)+b
            double[] x = new double[idx.Count], y = resid;
            for (int k = 0; k < idx.Count; k++) x[k] = Math.Sin(glm.toRadians(s[idx[k]].rollDeg));
            double a, b, r2; Ols(x, y, out a, out b, out r2);

            double antNow = mf.vehicle.VehicleConfig.AntennaHeight;
            double antNew = antNow + a;   // a = dh (m)

            // geometry / antenna height check
            var g = new ITCheck { name = hr ? "Geometrija / visina antene" : "Geometry / antenna height" };
            if (rollRms < 0.6)
            {
                g.status = 1;
                g.desc = hr ? "Premala roll pobuda — vozi preko izraženijeg nagiba za pouzdan nalaz."
                            : "Too little roll excitation — drive over more slope for a reliable result.";
                g.value = "roll RMS " + rollRms.ToString("0.0") + "°";
            }
            else if (Math.Abs(a) < 0.03)
            {
                g.status = 0;
                g.desc = hr ? "Rezidual ne raste s rollom — visina antene dobra." : "Residual flat vs roll — antenna height good.";
                g.value = "R²=" + r2.ToString("0.00");
            }
            else
            {
                g.status = 2;
                string dir = a > 0 ? (hr ? "povećaj" : "raise") : (hr ? "smanji" : "lower");
                g.desc = (hr ? "Rezidual raste s rollom (nagib " : "Residual grows with roll (slope ")
                       + a.ToString("0.00") + ", R²=" + r2.ToString("0.00") + ") — "
                       + dir + (hr ? " visinu antene." : " antenna height.");
                g.value = "h: " + antNow.ToString("0.00") + " → " + antNew.ToString("0.00") + " m";
            }
            r.checks.Add(g);

            // IMU lag via cross-correlation roll x residual
            double[] rollSeq = new double[idx.Count];
            for (int k = 0; k < idx.Count; k++) rollSeq[k] = s[idx[k]].rollDeg;
            int lagN; double peak; CrossCorrLag(rollSeq, y, 12, out lagN, out peak);
            double lagMs = Math.Abs(lagN) * MeanDt() * 1000.0;
            var lc = new ITCheck { name = hr ? "IMU timing" : "IMU timing" };
            if (lagMs < 40) { lc.status = 0; lc.desc = hr ? "Roll bez značajnog kašnjenja." : "Roll with no significant lag."; }
            else if (lagMs < 120) { lc.status = 1; lc.desc = hr ? "Blago kašnjenje IMU rolla." : "Slight IMU roll lag."; }
            else { lc.status = 2; lc.desc = hr ? "Znatno kašnjenje IMU rolla." : "Significant IMU roll lag."; }
            lc.value = "lag ~" + lagMs.ToString("0") + " ms";
            r.checks.Add(lc);

            // F2F heading noise vs speed
            double[] nz = F2FNoiseVsSpeed(hr);
            var fc = new ITCheck { name = hr ? "Heading (IMU/dual vs F2F)" : "Heading (IMU/dual vs F2F)" };
            int firstOk = -1;
            if (!HasImuHeading())
            {
                fc.status = 1;
                fc.desc = hr ? "Nema IMU/dual heading za usporedbu — preskočeno (treba IMU ili dual-as-IMU)."
                             : "No IMU/dual heading to compare — skipped (needs IMU or dual-as-IMU).";
                fc.value = "N/A";
            }
            else
            {
                for (int i = 0; i < 3; i++) if (nz[i] >= 0 && nz[i] < 1.0) { firstOk = i; break; }
                if (firstOk < 0) { fc.status = 1; fc.desc = hr ? "F2F šumovit na svim brzinama." : "F2F noisy at all tested speeds."; fc.value = "—"; }
                else
                {
                    fc.status = 0;
                    fc.desc = hr ? "F2F šum pada s brzinom — upotrebljiv od navedene." : "F2F noise drops with speed — usable from shown speed.";
                    fc.value = "≥" + bandCenter[firstOk].ToString("0") + " km/h";
                }
            }
            r.checks.Add(fc);

            // mount / unexplained noise
            double unexpl = UnexplainedRms(y, x, a, b);
            var mc = new ITCheck { name = hr ? "Mount / šum" : "Mount / noise" };
            if (unexpl < 0.03) { mc.status = 0; mc.desc = hr ? "Nizak nekorelirani rezidual — IMU čvrsto." : "Low uncorrelated residual — IMU solid."; }
            else { mc.status = 1; mc.desc = hr ? "Povišen nekorelirani rezidual — provjeri fiksiranje IMU-a." : "Elevated uncorrelated residual — check IMU mounting."; }
            mc.value = "RMS " + (unexpl * 100).ToString("0.0") + " cm";
            r.checks.Add(mc);

            // summary + chart
            r.summary = BuildStraightSummary(hr, g, lc, a, antNow, antNew, firstOk);
            r.svg = MoneyPlotSvg(x, y, a, b);
            r.subtitle = (hr ? "pravac · " : "straight · ")
                       + r.durationSec.ToString("0") + " s · roll RMS " + rollRms.ToString("0.0") + "°";
        }

        // One lap, fitted on its own. Hand-driven laps are never identical, so a
        // single global fit smears them together — each lap gets its own centre.
        private sealed class ITLap
        {
            public List<int> idx;
            public double cx, cy, R, fitRms;
            public double imuP95, f2fP95;   // heading error vs tangent, 95th pct (deg)
            public double imuScale, f2fScale;
            public double sweepDeg;
            public bool usable;
        }

        private void AnalyzeCircle(FormGPS mf, ITResult r, bool hr)
        {
            var idx = new List<int>();
            // steady-circle: drop entry/exit — keep where |steer| meaningful and stable
            double medSteer = Median(SteerAbs());
            for (int i = 0; i < s.Count; i++)
                if (Math.Abs(s[i].steer) > 0.4 * medSteer) idx.Add(i);
            if (idx.Count < 30) idx = AllIdx();

            // coarse fit over everything — used only to split the run into laps
            double[] aE = new double[idx.Count], aN = new double[idx.Count];
            for (int k = 0; k < idx.Count; k++) { aE[k] = s[idx[k]].E; aN[k] = s[idx[k]].N; }
            double gcx, gcy, gR; bool fitOk = CircleFit(aE, aN, out gcx, out gcy, out gR);

            bool hasImu = HasImuHeading();
            List<ITLap> laps = SplitLaps(idx, gcx, gcy);
            foreach (ITLap L in laps) MeasureLap(L, hasImu);

            var used = new List<ITLap>();
            foreach (ITLap L in laps) if (L.usable) used.Add(L);

            if (used.Count == 0)
            {
                var bad = new ITCheck
                {
                    name = hr ? "Kvaliteta kruga" : "Circle quality",
                    status = 3,
                    value = "—",
                    desc = hr ? "Nijedan krug nije dovoljno pravilan za analizu."
                              : "No lap is round enough to analyse.",
                    note = hr ? "Drži konstantan kut volana i vozi širi krug (R ≥ 8 m)."
                              : "Hold a constant steering angle and drive a wider circle (R >= 8 m)."
                };
                r.checks.Add(bad);
                r.summary = hr ? "Snimka nije upotrebljiva — centar kruga bježi. Ponovi test s konstantnim kutom."
                               : "Recording unusable — the circle centre drifts. Re-run holding a constant angle.";
                r.svg = CircleSvg(aE, aN, gcx, gcy, gR);
                r.subtitle = (hr ? "kružnica · neupotrebljivo" : "circle · unusable");
                return;
            }

            double medR = MedianOf(used, "R");
            double medFit = MedianOf(used, "fit");
            double imuP95 = MedianOf(used, "imuP95");
            double f2fP95 = MedianOf(used, "f2fP95");
            double imuSp = (MedianOf(used, "imuScale") - 1.0) * 100.0;

            // reference noise floor: position scatter becomes an angular error of
            // scatter/R, so a tight circle simply cannot be graded as finely.
            double sigRef = medR > 0.1 ? glm.toDegrees(medFit / medR) : 0;
            double warnAt = Math.Max(1.0, refNoiseK * sigRef);
            double critAt = Math.Max(2.5, 2.0 * refNoiseK * sigRef);
            bool tight = medR < circleMinR;

            string cond = (hr ? "R " : "R ") + medR.ToString("0.0") + " m · fitRMS "
                        + (medFit * 100).ToString("0") + " cm · " + used.Count
                        + (hr ? " kr. · prag " : " laps · threshold ") + warnAt.ToString("0.0") + "°"
                        + (used.Count > 1 ? (hr ? " · rasap " : " · spread ") + SpreadOf(used).ToString("0.0") + "°" : "");
            if (tight) cond += hr ? " · smanjena pouzdanost (R < 8 m)" : " · reduced confidence (R < 8 m)";

            var c1 = new ITCheck { name = hr ? "IMU/dual prati zavoj" : "IMU/dual tracks turn", note = cond };
            var c2 = new ITCheck { name = hr ? "IMU/dual yaw scale" : "IMU/dual yaw scale", note = cond };
            if (hasImu)
            {
                c1.status = Grade(imuP95, warnAt, critAt, tight);
                c1.desc = hr ? "Odstupanje IMU/dual headinga od tangente (p95, medijan po krugu)."
                             : "IMU/dual heading vs tangent (p95, median across laps).";
                c1.value = "± " + imuP95.ToString("0.0") + "°";

                c2.status = Grade(Math.Abs(imuSp), 1.0, 3.0, tight);
                c2.desc = hr ? "Ukupno okretanje IMU/dual vs geometrijski krug."
                             : "IMU/dual total turn vs geometric lap.";
                c2.value = (imuSp >= 0 ? "+" : "") + imuSp.ToString("0.0") + " %";
            }
            else
            {
                c1.status = 3; c1.value = "—";
                c1.desc = hr ? "Nema IMU/dual heading za usporedbu." : "No IMU/dual heading to compare.";
                c1.note = hr ? "Treba IMU ili dual-as-IMU." : "Needs an IMU or dual-as-IMU.";
                c2.status = 3; c2.value = "—"; c2.desc = c1.desc; c2.note = c1.note;
            }
            r.checks.Add(c1);
            r.checks.Add(c2);

            var c3 = new ITCheck
            {
                name = hr ? "F2F prati zavoj" : "F2F tracks turn",
                status = Grade(f2fP95, warnAt, critAt, tight),
                desc = hr ? "Kurs preko tla vs tangenta (p95)." : "Course-over-ground vs tangent (p95).",
                value = "± " + f2fP95.ToString("0.0") + "°",
                note = cond
            };
            r.checks.Add(c3);

            // per-lap repeatability — only meaningful with more than one usable lap
            if (used.Count > 1)
            {
                double spread = SpreadOf(used);
                r.checks.Add(new ITCheck
                {
                    name = hr ? "Ponovljivost po krugu" : "Per-lap repeatability",
                    status = spread < Math.Max(1.0, warnAt) ? 0 : (spread < critAt ? 1 : 2),
                    desc = hr ? "Rasap nalaza između krugova." : "Spread of the finding across laps.",
                    value = "± " + spread.ToString("0.0") + "°",
                    note = LapTable(used, hr)
                });
            }

            r.summary = BuildCircleSummary(hr, medR, imuP95, imuSp, f2fP95, fitOk, used.Count, tight, warnAt);
            r.svg = CircleSvg(aE, aN, gcx, gcy, medR);
            r.subtitle = (hr ? "kružnica · R " : "circle · R ")
                       + medR.ToString("0.0") + " m · " + used.Count
                       + (hr ? " kruga" : " laps")
                       + (tight ? (hr ? " · tijesno" : " · tight") : "");
        }

        // Split the run into laps using the unwrapped position angle around the
        // coarse centre; each lap is then fitted on its own.
        private List<ITLap> SplitLaps(List<int> idx, double gcx, double gcy)
        {
            var laps = new List<ITLap>();
            int n = idx.Count;
            if (n == 0) return laps;

            var ang = new double[n];
            for (int k = 0; k < n; k++) ang[k] = Math.Atan2(s[idx[k]].E - gcx, s[idx[k]].N - gcy);
            Unwrap(ang);

            double start = ang[0];
            var cur = new List<int>();
            int lapNo = 0;
            for (int k = 0; k < n; k++)
            {
                int no = (int)Math.Floor(Math.Abs(ang[k] - start) / (2 * Math.PI));
                if (no != lapNo && cur.Count > 0)
                {
                    laps.Add(new ITLap { idx = cur });
                    cur = new List<int>();
                    lapNo = no;
                }
                cur.Add(idx[k]);
            }
            if (cur.Count > 0) laps.Add(new ITLap { idx = cur });
            return laps;
        }

        // Fit one lap and measure every source against its own tangent.
        private void MeasureLap(ITLap L, bool hasImu)
        {
            int n = L.idx.Count;
            L.usable = false;
            if (n < 40) return;

            var E = new double[n]; var N = new double[n];
            for (int k = 0; k < n; k++) { E[k] = s[L.idx[k]].E; N[k] = s[L.idx[k]].N; }
            if (!CircleFit(E, N, out L.cx, out L.cy, out L.R)) return;
            if (L.R < 1.0) return;

            double acc = 0;
            for (int k = 0; k < n; k++)
            {
                double d = Math.Sqrt((E[k] - L.cx) * (E[k] - L.cx) + (N[k] - L.cy) * (N[k] - L.cy)) - L.R;
                acc += d * d;
            }
            L.fitRms = Math.Sqrt(acc / n);
            if (L.fitRms / L.R > lapFitTol) return;     // centre drifted — not a circle

            // position angle in the COMPASS frame (Atan2(east, north)) so it lives in
            // the same frame as AOG heading; the tangent is then geo +/- 90 degrees.
            var geo = new double[n];
            for (int k = 0; k < n; k++) geo[k] = Math.Atan2(E[k] - L.cx, N[k] - L.cy);
            double geoSweep = UnwrapSweep(geo);
            if (Math.Abs(geoSweep) < Math.PI) return;   // less than half a lap
            L.sweepDeg = glm.toDegrees(geoSweep);
            double sign = geoSweep >= 0 ? 1 : -1;

            double[] err;
            SourceVsTangent(L.idx, geo, sign, false, out err, out L.f2fScale, geoSweep);
            L.f2fP95 = Pct95Abs(err);
            if (hasImu)
            {
                SourceVsTangent(L.idx, geo, sign, true, out err, out L.imuScale, geoSweep);
                L.imuP95 = Pct95Abs(err);
            }
            else { L.imuScale = 1.0; L.imuP95 = 0; }
            L.usable = true;
        }

        // ---- source vs tangent, offset-removed error + yaw scale ----
        // The constant part (IMU mounting yaw + tyre sideslip) is removed, so what
        // remains is how well the source tracks through the turn.
        private void SourceVsTangent(List<int> idx, double[] geo, double sign, bool imu,
                                     out double[] err, out double scale, double geoSweep)
        {
            int n = idx.Count;
            err = new double[n];
            double[] raw = new double[n];
            double[] head = new double[n];
            for (int k = 0; k < n; k++)
            {
                double tangent = geo[k] + sign * Math.PI / 2.0;
                double h = imu ? s[idx[k]].imuRad : s[idx[k]].f2fRad;
                head[k] = h;
                raw[k] = WrapPi(h - tangent);
            }
            double off = CircularMean(raw);
            for (int k = 0; k < n; k++) err[k] = glm.toDegrees(WrapPi(raw[k] - off));
            double headSweep = UnwrapSweep(head);
            scale = Math.Abs(geoSweep) > 1e-6 ? headSweep / geoSweep : 1.0;
        }

        // A tight circle cannot be graded confidently — cap at warn instead of
        // reporting a fault that is really just geometry.
        private static int Grade(double v, double warnAt, double critAt, bool tight)
        {
            int st = v < warnAt ? 0 : (v < critAt ? 1 : 2);
            if (tight && st == 2) st = 1;
            return st;
        }

        private static double MedianOf(List<ITLap> l, string field)
        {
            var a = new double[l.Count];
            for (int i = 0; i < l.Count; i++)
            {
                ITLap L = l[i];
                a[i] = field == "R" ? L.R
                     : field == "fit" ? L.fitRms
                     : field == "imuP95" ? L.imuP95
                     : field == "f2fP95" ? L.f2fP95
                     : field == "imuScale" ? L.imuScale
                     : L.f2fScale;
            }
            return Median(a);
        }

        // how much the per-lap IMU finding moves between laps
        private static double SpreadOf(List<ITLap> l)
        {
            double mn = double.MaxValue, mx = double.MinValue;
            foreach (ITLap L in l) { if (L.imuP95 < mn) mn = L.imuP95; if (L.imuP95 > mx) mx = L.imuP95; }
            return mx - mn;
        }

        private static string LapTable(List<ITLap> l, bool hr)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < l.Count; i++)
            {
                if (i > 0) sb.Append(" · ");
                sb.Append(hr ? "kr." : "lap").Append(i + 1).Append(": R ")
                  .Append(l[i].R.ToString("0.0", CultureInfo.InvariantCulture)).Append(" m, ± ")
                  .Append(l[i].imuP95.ToString("0.0", CultureInfo.InvariantCulture)).Append("°");
            }
            return sb.ToString();
        }

        // ================================================================ helpers
        private List<int> AllIdx() { var l = new List<int>(); for (int i = 0; i < s.Count; i++) l.Add(i); return l; }
        private double[] SteerAbs() { var a = new double[s.Count]; for (int i = 0; i < s.Count; i++) a[i] = Math.Abs(s[i].steer); return a; }
        private bool HasImuHeading() { for (int i = 0; i < s.Count; i++) if (!double.IsNaN(s[i].imuRad)) return true; return false; }

        private bool HasSpread(List<int> idx, bool xte)
        {
            double mn = double.MaxValue, mx = double.MinValue;
            foreach (int i in idx) { double v = s[i].xte; if (v < mn) mn = v; if (v > mx) mx = v; }
            return (mx - mn) > 0.02;   // >2 cm spread => a line is driving XTE
        }

        private double[] PcaResidual(List<int> idx)
        {
            int n = idx.Count;
            double mE = 0, mN = 0;
            foreach (int i in idx) { mE += s[i].E; mN += s[i].N; }
            mE /= n; mN /= n;
            double sEE = 0, sNN = 0, sEN = 0;
            foreach (int i in idx) { double de = s[i].E - mE, dn = s[i].N - mN; sEE += de * de; sNN += dn * dn; sEN += de * dn; }
            // principal axis angle of the 2x2 covariance
            double phi = 0.5 * Math.Atan2(2 * sEN, sEE - sNN);
            double px = -Math.Sin(phi), py = Math.Cos(phi);   // perpendicular unit
            var res = new double[n];
            for (int k = 0; k < n; k++) { double de = s[idx[k]].E - mE, dn = s[idx[k]].N - mN; res[k] = de * px + dn * py; }
            return res;
        }

        private static void Ols(double[] x, double[] y, out double a, out double b, out double r2)
        {
            int n = x.Length; double mx = 0, my = 0;
            for (int i = 0; i < n; i++) { mx += x[i]; my += y[i]; }
            mx /= n; my /= n;
            double sxx = 0, sxy = 0, syy = 0;
            for (int i = 0; i < n; i++) { double dx = x[i] - mx, dy = y[i] - my; sxx += dx * dx; sxy += dx * dy; syy += dy * dy; }
            a = sxx > 1e-12 ? sxy / sxx : 0;
            b = my - a * mx;
            r2 = (sxx > 1e-12 && syy > 1e-12) ? (sxy * sxy) / (sxx * syy) : 0;
        }

        private double UnexplainedRms(double[] y, double[] x, double a, double b)
        {
            double ss = 0; int n = y.Length;
            for (int i = 0; i < n; i++) { double e = y[i] - (a * x[i] + b); ss += e * e; }
            return Math.Sqrt(ss / n);
        }

        private static void CrossCorrLag(double[] x, double[] y, int maxLag, out int bestLag, out double bestPeak)
        {
            int n = Math.Min(x.Length, y.Length);
            double mx = 0, my = 0;
            for (int i = 0; i < n; i++) { mx += x[i]; my += y[i]; }
            mx /= n; my /= n;
            bestLag = 0; bestPeak = 0;
            for (int lag = -maxLag; lag <= maxLag; lag++)
            {
                double sum = 0; int cnt = 0;
                for (int i = 0; i < n; i++)
                {
                    int j = i + lag; if (j < 0 || j >= n) continue;
                    sum += (x[i] - mx) * (y[j] - my); cnt++;
                }
                if (cnt < 5) continue;
                double c = sum / cnt;
                if (Math.Abs(c) > Math.Abs(bestPeak)) { bestPeak = c; bestLag = lag; }
            }
        }

        private double[] F2FNoiseVsSpeed(bool hr)
        {
            double[] outp = { -1, -1, -1 };
            for (int b = 0; b < 3; b++)
            {
                var d = new List<double>();
                for (int i = 0; i < s.Count; i++)
                    if (Math.Abs(s[i].speed - bandCenter[b]) <= bandTol && !double.IsNaN(s[i].imuRad))
                        d.Add(WrapPi(s[i].f2fRad - s[i].imuRad));
                if (d.Count < 15) continue;
                double off = CircularMean(d.ToArray());
                double ss = 0; foreach (double v in d) { double e = WrapPi(v - off); ss += e * e; }
                outp[b] = glm.toDegrees(Math.Sqrt(ss / d.Count));
            }
            return outp;
        }

        private static bool CircleFit(double[] x, double[] y, out double cx, out double cy, out double R)
        {
            // Kasa algebraic fit: solve [x y 1][u v w]' = [x^2+y^2]  (least squares)
            int n = x.Length;
            double Sx = 0, Sy = 0, Sxx = 0, Syy = 0, Sxy = 0, Sxz = 0, Syz = 0, Sz = 0, S1 = n;
            for (int i = 0; i < n; i++)
            {
                double xi = x[i], yi = y[i], z = x[i] * x[i] + y[i] * y[i];
                Sx += xi; Sy += yi; Sxx += xi * xi; Syy += yi * yi; Sxy += xi * yi;
                Sxz += xi * z; Syz += yi * z; Sz += z;
            }
            // normal equations M*[u v w] = rhs
            double[,] M = { { Sxx, Sxy, Sx }, { Sxy, Syy, Sy }, { Sx, Sy, S1 } };
            double[] rhs = { Sxz, Syz, Sz };
            double[] sol;
            if (!Solve3(M, rhs, out sol)) { cx = cy = 0; R = 1; return false; }
            cx = sol[0] / 2.0; cy = sol[1] / 2.0;
            double r2 = sol[2] + cx * cx + cy * cy;
            R = r2 > 0 ? Math.Sqrt(r2) : 0;
            return R > 0.1;
        }

        private static bool Solve3(double[,] m, double[] r, out double[] x)
        {
            x = new double[3];
            double det =
                m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) -
                m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) +
                m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
            if (Math.Abs(det) < 1e-9) return false;
            double[,] inv = new double[3, 3];
            inv[0, 0] = (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) / det;
            inv[0, 1] = (m[0, 2] * m[2, 1] - m[0, 1] * m[2, 2]) / det;
            inv[0, 2] = (m[0, 1] * m[1, 2] - m[0, 2] * m[1, 1]) / det;
            inv[1, 0] = (m[1, 2] * m[2, 0] - m[1, 0] * m[2, 2]) / det;
            inv[1, 1] = (m[0, 0] * m[2, 2] - m[0, 2] * m[2, 0]) / det;
            inv[1, 2] = (m[0, 2] * m[1, 0] - m[0, 0] * m[1, 2]) / det;
            inv[2, 0] = (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]) / det;
            inv[2, 1] = (m[0, 1] * m[2, 0] - m[0, 0] * m[2, 1]) / det;
            inv[2, 2] = (m[0, 0] * m[1, 1] - m[0, 1] * m[1, 0]) / det;
            for (int i = 0; i < 3; i++) x[i] = inv[i, 0] * r[0] + inv[i, 1] * r[1] + inv[i, 2] * r[2];
            return true;
        }

        // wrap to (-pi, pi]
        private static double WrapPi(double a)
        {
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a <= -Math.PI) a += 2 * Math.PI;
            return a;
        }

        private static double CircularMean(double[] a)
        {
            double sx = 0, sy = 0;
            for (int i = 0; i < a.Length; i++) { sx += Math.Cos(a[i]); sy += Math.Sin(a[i]); }
            return Math.Atan2(sy, sx);
        }

        // cumulative signed sweep of an angle sequence (radians), unwrapped
        private static double UnwrapSweep(double[] a)
        {
            double total = 0;
            for (int i = 1; i < a.Length; i++) total += WrapPi(a[i] - a[i - 1]);
            return total;
        }

        // unwrap an angle sequence in place (removes the 0/360 jumps)
        private static void Unwrap(double[] a)
        {
            for (int i = 1; i < a.Length; i++) a[i] = a[i - 1] + WrapPi(a[i] - a[i - 1]);
        }

        // 95th percentile of |a| — robust where max is pure noise-chasing
        private static double Pct95Abs(double[] a)
        {
            if (a.Length == 0) return 0;
            var c = new double[a.Length];
            for (int i = 0; i < a.Length; i++) c[i] = Math.Abs(a[i]);
            Array.Sort(c);
            int k = (int)Math.Floor(0.95 * (c.Length - 1));
            return c[k];
        }

        private static double Median(double[] a) { if (a.Length == 0) return 0; var c = (double[])a.Clone(); Array.Sort(c); return c[c.Length / 2]; }
        private static double Clamp01(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

        private string BuildStraightSummary(bool hr, ITCheck g, ITCheck lc, double a, double antNow, double antNew, int firstOk)
        {
            if (g.status == 2)
                return hr
                    ? "Roll ne prati lateralno pomicanje antene — <b>geometrija nije dobra</b>. " + g.value + "."
                    : "Roll does not track antenna lateral movement — <b>geometry is off</b>. " + g.value + ".";
            if (g.status == 1)
                return hr ? "Ponovi test preko izraženijeg nagiba (nedovoljna roll pobuda)."
                          : "Repeat over more slope (insufficient roll excitation).";
            return hr ? "Roll kompenzacija i geometrija izgledaju dobro." : "Roll compensation and geometry look good.";
        }

        private string BuildCircleSummary(bool hr, double R, double imuP95, double sp, double f2fP95,
                                          bool fitOk, int lapsUsed, bool tight, double warnAt)
        {
            if (!fitOk) return hr ? "Kružnica se ne da pouzdano fitati — vozi puniji, stabilniji krug."
                                  : "Circle could not be fit reliably — drive a fuller, steadier circle.";
            string s1 = "R = " + R.ToString("0.0") + " m, " + lapsUsed + (hr ? " analiziranih krugova. " : " laps analysed. ");
            string s2 = (imuP95 < warnAt && f2fP95 < warnAt)
                ? (hr ? "IMU i F2F ispravno prate skretanje." : "IMU and F2F track the turn correctly.")
                : (hr ? "Provjeri izvor s većim odstupanjem od tangente." : "Check the source with the larger tangent error.");
            string s3 = Math.Abs(sp) >= 1.0
                ? " IMU yaw scale " + (sp >= 0 ? "+" : "") + sp.ToString("0.0") + "% — "
                  + (hr ? "objašnjava drift u dugim zavojima." : "explains drift in long turns.")
                : "";
            string s4 = tight
                ? (hr ? " <b>R je ispod 8 m</b> — referenca tangente je razmjerno šumna (skalira s 1/R), pa su nalazi orijentacijski; ponovi na R ≈ 12 m za ocjenu."
                      : " <b>R is below 8 m</b> — the tangent reference is proportionally noisy (scales with 1/R), so findings are indicative; re-run at R ~ 12 m to grade.")
                : "";
            return s1 + s2 + s3 + s4;
        }

        // ---------------------------------------------------------------- charts
        private string MoneyPlotSvg(double[] x, double[] y, double a, double b)
        {
            // x = sin(roll) [-.., ..], y = residual (m). Map to 360x200 box.
            double xmn = double.MaxValue, xmx = double.MinValue, ymn = double.MaxValue, ymx = double.MinValue;
            for (int i = 0; i < x.Length; i++) { if (x[i] < xmn) xmn = x[i]; if (x[i] > xmx) xmx = x[i]; if (y[i] < ymn) ymn = y[i]; if (y[i] > ymx) ymx = y[i]; }
            if (xmx - xmn < 1e-6) { xmn -= 0.1; xmx += 0.1; }
            if (ymx - ymn < 1e-6) { ymn -= 0.05; ymx += 0.05; }
            Func<double, double> MX = v => 30 + (v - xmn) / (xmx - xmn) * 320;
            Func<double, double> MY = v => 180 - (v - ymn) / (ymx - ymn) * 160;
            var sb = new System.Text.StringBuilder();
            sb.Append("<svg viewBox='0 0 360 200' width='100%' style='max-width:520px'>");
            sb.Append("<line x1='30' y1='").Append(F(MY(0))).Append("' x2='350' y2='").Append(F(MY(0))).Append("' stroke='#cbd5d0'/>");
            // fit line
            double y1 = a * xmn + b, y2 = a * xmx + b;
            sb.Append("<line x1='").Append(F(MX(xmn))).Append("' y1='").Append(F(MY(y1)))
              .Append("' x2='").Append(F(MX(xmx))).Append("' y2='").Append(F(MY(y2)))
              .Append("' stroke='#0d8395' stroke-width='2'/>");
            for (int i = 0; i < x.Length; i++)
                sb.Append("<circle cx='").Append(F(MX(x[i]))).Append("' cy='").Append(F(MY(y[i]))).Append("' r='2.4' fill='#c23a2b'/>");
            sb.Append("<text x='330' y='196' font-size='9' fill='#5b6a63'>sin(roll)</text>");
            sb.Append("<text x='34' y='16' font-size='9' fill='#5b6a63'>rezidual (m)</text>");
            sb.Append("</svg>");
            return sb.ToString();
        }

        private string CircleSvg(double[] E, double[] N, double cx, double cy, double R)
        {
            var sb = new System.Text.StringBuilder();
            double span = R * 1.4; if (span < 1) span = 1;
            Func<double, double> MX = e => 110 + (e - cx) / span * 90;
            Func<double, double> MY = n => 110 - (n - cy) / span * 90;
            sb.Append("<svg viewBox='0 0 220 220' width='100%' style='max-width:300px'>");
            sb.Append("<circle cx='").Append(F(MX(cx))).Append("' cy='").Append(F(MY(cy))).Append("' r='")
              .Append(F(90.0 * R / span)).Append("' fill='none' stroke='#0d8395' stroke-width='2'/>");
            sb.Append("<circle cx='").Append(F(MX(cx))).Append("' cy='").Append(F(MY(cy))).Append("' r='2' fill='#5b6a63'/>");
            for (int i = 0; i < E.Length; i += Math.Max(1, E.Length / 60))
                sb.Append("<circle cx='").Append(F(MX(E[i]))).Append("' cy='").Append(F(MY(N[i]))).Append("' r='2.2' fill='#c9721b'/>");
            sb.Append("</svg>");
            return sb.ToString();
        }

        private static string F(double v) { return v.ToString("0.0", CultureInfo.InvariantCulture); }
    }
}
