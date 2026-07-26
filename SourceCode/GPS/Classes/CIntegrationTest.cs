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
        // AB line geometry, NaN without a line. lat is measured in the line's own
        // frame (+ right of the A->B direction) rather than the travel direction,
        // so a there-and-back pair can be compared without sign bookkeeping.
        public double station;  // distance along the line from point A, m
        public double lat;      // signed perpendicular offset in the LINE frame, m
        public bool sameWay;    // driving A->B (true) or B->A (false)
        public bool steerOn;    // autosteer engaged — it regulates the very signal we measure
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
        // speed bands are opportunistic now — only the F2F-noise-vs-speed check
        // uses them; the geometry findings come from the two-way pass instead
        private static readonly double[] bandCenter = { 3, 7, 10 };
        private const double bandTol = 1.6;
        private const double circleTargetRad = 2 * Math.PI * 2; // ~2 laps

        // circle geometry: the tangent reference degrades as 1/R, so a tight circle
        // is graded with widened thresholds instead of a confident wrong verdict.
        private const double circleTargetR = 12.0;      // m, suggested to the driver
        private const double circleMinR = 8.0;          // below this: reduced confidence
        private const double lapFitTol = 0.08;          // reject a lap when fitRms/R exceeds this
        private const double refNoiseK = 3.0;           // threshold = k * reference noise floor

        // straight test: the same ground is driven both ways and compared per bin
        private const double passBinM = 3.0;            // station bin width, m
        private const double passTargetM = 100.0;       // wanted length per direction, m
        private const double latNearLine = 3.0;         // further off the line = turning, drop it
        private const int minPairedBins = 15;           // bins covered in both directions

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

        // Raw log for offline analysis. The '#' header carries the machine geometry,
        // without which the numbers cannot be interpreted afterwards.
        public string BuildCsv(FormGPS mf)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# AgOpenGPS F07 Integration Test raw log");
            sb.AppendLine("# mode," + mode);
            sb.AppendLine("# recorded," + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", ci));
            sb.AppendLine("# antennaHeight," + mf.vehicle.VehicleConfig.AntennaHeight.ToString("0.###", ci));
            sb.AppendLine("# antennaOffset," + mf.vehicle.VehicleConfig.AntennaOffset.ToString("0.###", ci));
            sb.AppendLine("# antennaPivot," + mf.vehicle.VehicleConfig.AntennaPivot.ToString("0.###", ci));
            sb.AppendLine("# wheelbase," + mf.vehicle.VehicleConfig.Wheelbase.ToString("0.###", ci));
            sb.AppendLine("# maxSteerAngle," + mf.vehicle.maxSteerAngle.ToString("0.#", ci));
            sb.AppendLine("# sampleRateHz," + (MeanDt() > 0 ? (1.0 / MeanDt()) : 0).ToString("0.#", ci));
            sb.AppendLine("# samples," + s.Count.ToString(ci));
            sb.AppendLine("t,rollDeg,imuRad,f2fRad,fixRad,E,N,xte,yaw,speed,steer,station,lat,sameWay");
            foreach (var x in s)
                sb.AppendLine(
                    x.t.ToString("0.###", ci) + "," + x.rollDeg.ToString("0.###", ci) + "," +
                    x.imuRad.ToString("0.#####", ci) + "," + x.f2fRad.ToString("0.#####", ci) + "," +
                    x.fixRad.ToString("0.#####", ci) + "," + x.E.ToString("0.###", ci) + "," +
                    x.N.ToString("0.###", ci) + "," + x.xte.ToString("0.####", ci) + "," +
                    x.yaw.ToString("0.###", ci) + "," + x.speed.ToString("0.##", ci) + "," +
                    x.steer.ToString("0.##", ci) + "," + x.station.ToString("0.###", ci) + "," +
                    x.lat.ToString("0.####", ci) + "," + (x.sameWay ? "1" : "0"));
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

            // Project onto the AB line ourselves: AOG's XTE sign follows the travel
            // direction, which would silently cancel exactly the there-and-back
            // difference this test is built on.
            double station = double.NaN, lat = double.NaN;
            bool sameWay = true;
            if (mf.trk.idx >= 0 && mf.trk.idx < mf.trk.gArr.Count)
            {
                CTrk tk = mf.trk.gArr[mf.trk.idx];
                if (tk.mode == TrackMode.AB)
                {
                    double h = tk.heading;
                    double dE = E - tk.ptA.easting, dN = N - tk.ptA.northing;
                    station = (dE * Math.Sin(h)) + (dN * Math.Cos(h));
                    lat = (dE * Math.Cos(h)) - (dN * Math.Sin(h));
                    sameWay = Math.Cos(mf.fixHeading - h) > 0;
                }
            }

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
                station = station,
                lat = lat,
                sameWay = sameWay,
                steerOn = mf.isBtnAutoSteerOn,
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
            // straight: one pass each way over the same line. Direction-agnostic —
            // the driver may start from either end.
            double lenA = PassLength(true), lenB = PassLength(false);
            double first = Math.Max(lenA, lenB), second = Math.Min(lenA, lenB);
            double done = Math.Min(first, passTargetM) + Math.Min(second, passTargetM);
            var ci = CultureInfo.InvariantCulture;

            if (first < passTargetM)
            {
                action = hr ? "Prolaz 1/2 — vozi uz liniju, autosteer ISKLJUČEN, gledaj u daleku točku"
                            : "Pass 1/2 — drive along the line, autosteer OFF, aim at a distant point";
                big = first.ToString("0", ci) + " / " + passTargetM.ToString("0", ci) + " m";
            }
            else if (second < 1.0)
            {
                action = hr ? "Okreni se i vrati po ISTOJ liniji" : "Turn around and come back along the SAME line";
                big = hr ? "okreni" : "turn around";
            }
            else
            {
                action = hr ? "Prolaz 2/2 — povratak istom linijom" : "Pass 2/2 — return along the same line";
                big = second.ToString("0", ci) + " / " + passTargetM.ToString("0", ci) + " m";
            }
            return Clamp01(done / (2 * passTargetM));
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
            return PassLength(true) >= passTargetM && PassLength(false) >= passTargetM;
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
                if (Math.Abs(s[i].steer) < 3.0 && Math.Abs(s[i].yaw) < 3.0 && !double.IsNaN(s[i].lat)) idx.Add(i);

            if (idx.Count < 30)
            {
                // Without an AB line there is no trustworthy lateral reference. Fitting
                // a straight line through a bowed path and blaming roll for the
                // curvature is exactly how v1 produced a confident wrong answer.
                r.checks.Add(new ITCheck
                {
                    name = hr ? "Geometrija / visina antene" : "Geometry / antenna height",
                    status = 3,
                    value = "—",
                    desc = hr ? "Nema dovoljno uzoraka uz AB liniju." : "Not enough samples along an AB line.",
                    note = hr ? "Aktiviraj AB liniju i vozi po njoj ravno i stabilno."
                              : "Activate an AB line and drive along it straight and steady."
                });
                r.summary = hr ? "Test nije mjerljiv bez aktivne AB linije." : "Not measurable without an active AB line.";
                r.subtitle = hr ? "pravac · bez linije" : "straight · no line";
                return;
            }

            // Lateral in the VEHICLE frame: roll is measured in the machine's own
            // frame, so the residual has to be flipped for the return pass or the
            // two directions cancel each other in the fit.
            double[] yRaw = new double[idx.Count];
            for (int k = 0; k < idx.Count; k++) yRaw[k] = s[idx[k]].lat * (s[idx[k]].sameWay ? 1.0 : -1.0);
            RemovePerDirectionMean(idx, yRaw);

            // Two-way pairing on the same ground. This is what makes the roll zero
            // offset observable at all: from one pass a constant bias is
            // indistinguishable from the mean cross slope of the terrain.
            double[] rA = new double[0], rB = new double[0], lA = new double[0], lB = new double[0];
            bool paired = false;
            if (HasBothDirections()) paired = PairedProfile(idx, out rA, out rB, out lA, out lB);
            if (!paired) { rA = rB = lA = lB = new double[0]; }

            // fit set: binned two-way data when available (the pairing doubles the
            // span of sin(roll) and makes it cross zero), raw samples otherwise
            double[] x, y;
            if (paired)
            {
                int nb = rA.Length;
                x = new double[nb * 2]; y = new double[nb * 2];
                double mLatA = Mean(lA), mLatB = Mean(lB);
                for (int k = 0; k < nb; k++)
                {
                    x[k] = Math.Sin(glm.toRadians(rA[k]));
                    y[k] = lA[k] - mLatA;
                    x[nb + k] = Math.Sin(glm.toRadians(rB[k]));
                    y[nb + k] = -(lB[k] - mLatB);
                }
            }
            else
            {
                x = new double[idx.Count]; y = yRaw;
                for (int k = 0; k < idx.Count; k++) x[k] = Math.Sin(glm.toRadians(s[idx[k]].rollDeg));
            }

            // roll excitation: RMS alone is not enough — a one-sided roll (never
            // crossing zero) leaves the slope badly conditioned, because the fit
            // divides the residual by a tiny span of sin(roll).
            double rollRms = 0; foreach (int i in idx) rollRms += s[i].rollDeg * s[i].rollDeg;
            rollRms = Math.Sqrt(rollRms / idx.Count);
            double sinMin = double.MaxValue, sinMax = double.MinValue;
            for (int k = 0; k < x.Length; k++) { if (x[k] < sinMin) sinMin = x[k]; if (x[k] > sinMax) sinMax = x[k]; }
            bool crossesZero = sinMin < -0.02 && sinMax > 0.02;
            double sinSpan = sinMax - sinMin;

            double a, b, r2; Ols(x, y, out a, out b, out r2);
            double antNow = mf.vehicle.VehicleConfig.AntennaHeight;
            double antNew = antNow + a;   // a = dh (m)

            string rollCond = "roll RMS " + rollRms.ToString("0.0") + "° · "
                            + (hr ? "raspon sin " : "sin span ") + sinSpan.ToString("0.00")
                            + " · R² " + r2.ToString("0.00")
                            + (paired ? (hr ? " · dvosmjerno, " : " · two-way, ") + rA.Length + (hr ? " binova" : " bins")
                                      : (hr ? " · jednosmjerno" : " · one-way"));

            AddTwoWayChecks(mf, r, hr, paired, rA, rB, lA, lB);

            var g = new ITCheck { name = hr ? "Geometrija / visina antene" : "Geometry / antenna height", note = rollCond };
            if (rollRms < 0.6)
            {
                g.status = 3; g.value = "—";
                g.desc = hr ? "Premala roll pobuda." : "Too little roll excitation.";
                g.note = rollCond + (hr ? " · vozi preko izraženijeg poprečnog nagiba"
                                        : " · drive over more cross slope");
            }
            else if (!crossesZero)
            {
                g.status = 3; g.value = "—";
                g.desc = hr ? "Roll je jednostran — nagib se ne da pouzdano odrediti."
                            : "Roll is one-sided — the slope cannot be determined reliably.";
                g.note = rollCond + (hr ? " · odvezi i povratni prolaz po istoj liniji"
                                        : " · drive the return pass along the same line");
            }
            else if (r2 < 0.25)
            {
                g.status = 3; g.value = "—";
                g.desc = hr ? "Roll objašnjava premali dio reziduala." : "Roll explains too little of the residual.";
                g.note = rollCond + (hr ? " · treba R² ≥ 0.25" : " · needs R² >= 0.25");
            }
            else if (Math.Abs(a) > 0.5)
            {
                g.status = 3;
                g.value = a.ToString("0.00") + " m";
                g.desc = hr ? "Izračunata korekcija nije fizikalna — fit nije upotrebljiv."
                            : "Computed correction is not physical — the fit is unusable.";
                g.note = rollCond;
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
                g.desc = (hr ? "Rezidual raste s rollom — " : "Residual grows with roll — ")
                       + dir + (hr ? " visinu antene." : " antenna height.");
                g.value = "h: " + antNow.ToString("0.00") + " → " + antNew.ToString("0.00") + " m";
            }
            r.checks.Add(g);

            // IMU lag via cross-correlation roll x residual. Sampling is ~10 Hz, so
            // the lag is quantised to one sample — reporting a smooth millisecond
            // figure would imply a precision this method does not have.
            const int maxLag = 12;
            double resMs = MeanDt() * 1000.0;
            // cross-correlation needs the raw time-ordered residual, not the binned one
            double[] rollSeq = new double[idx.Count];
            for (int k = 0; k < idx.Count; k++) rollSeq[k] = s[idx[k]].rollDeg;
            int lagN; double peak; CrossCorrLag(rollSeq, yRaw, maxLag, out lagN, out peak);
            double lagMs = Math.Abs(lagN) * resMs;

            var lc = new ITCheck { name = "IMU timing" };
            string lagCond = (hr ? "rezolucija ~" : "resolution ~") + resMs.ToString("0") + " ms · "
                           + (hr ? "raspon ±" : "range +/-") + (maxLag * resMs).ToString("0") + " ms";
            if (rollRms < 0.6)
            {
                lc.status = 3; lc.value = "—";
                lc.desc = hr ? "Premala roll pobuda za mjerenje kašnjenja." : "Too little roll excitation to measure lag.";
                lc.note = lagCond;
            }
            else if (Math.Abs(lagN) >= maxLag)
            {
                // the peak sits on the edge of the search range: there is no peak
                lc.status = 3; lc.value = "—";
                lc.desc = hr ? "Nema vrha korelacije — kašnjenje se ne da izmjeriti."
                             : "No correlation peak — lag is not measurable.";
                lc.note = lagCond;
            }
            else
            {
                lc.status = lagN == 0 ? 0 : (Math.Abs(lagN) == 1 ? 1 : 2);
                lc.desc = lagN == 0
                    ? (hr ? "Kašnjenje ispod rezolucije mjerenja." : "Lag below the measurement resolution.")
                    : (Math.Abs(lagN) == 1 ? (hr ? "Blago kašnjenje IMU rolla." : "Slight IMU roll lag.")
                                           : (hr ? "Znatno kašnjenje IMU rolla." : "Significant IMU roll lag."));
                lc.value = lagN == 0 ? "< " + resMs.ToString("0") + " ms" : "~" + lagMs.ToString("0") + " ms";
                lc.note = lagCond;
            }
            r.checks.Add(lc);

            // F2F heading noise vs speed
            double[] nz = F2FNoiseVsSpeed(hr);
            var fc = new ITCheck { name = hr ? "Heading (IMU/dual vs F2F)" : "Heading (IMU/dual vs F2F)" };
            int firstOk = -1;
            if (!HasImuHeading())
            {
                fc.status = 3;
                fc.desc = hr ? "Nema IMU/dual heading za usporedbu." : "No IMU/dual heading to compare.";
                fc.value = "—";
                fc.note = hr ? "Treba IMU ili dual-as-IMU." : "Needs an IMU or dual-as-IMU.";
            }
            else
            {
                int bandsDriven = 0;
                for (int i = 0; i < 3; i++) if (nz[i] >= 0) bandsDriven++;
                for (int i = 0; i < 3; i++) if (nz[i] >= 0 && nz[i] < 1.0) { firstOk = i; break; }
                fc.note = (hr ? "odvoženih pojaseva brzine: " : "speed bands driven: ") + bandsDriven + "/3";
                if (bandsDriven == 0)
                {
                    fc.status = 3; fc.value = "—";
                    fc.desc = hr ? "Nijedan pojas brzine nije odvožen dovoljno dugo."
                                 : "No speed band was driven long enough.";
                }
                else if (firstOk < 0) { fc.status = 1; fc.desc = hr ? "F2F šumovit na svim odvoženim brzinama." : "F2F noisy at every speed driven."; fc.value = "—"; }
                else
                {
                    fc.status = 0;
                    fc.desc = hr ? "F2F šum pada s brzinom — upotrebljiv od navedene." : "F2F noise drops with speed — usable from shown speed.";
                    fc.value = "≥" + bandCenter[firstOk].ToString("0") + " km/h";
                }
            }
            r.checks.Add(fc);

            // Mount / unexplained noise. Only meaningful once the roll fit itself
            // holds up — otherwise the "unexplained" part is just the bad fit.
            double unexpl = UnexplainedRms(y, x, a, b);
            var mc = new ITCheck { name = hr ? "Mount / šum" : "Mount / noise" };
            if (paired)
            {
                // superseded by pass repeatability, which separates real noise from
                // a merely bad fit
                mc = null;
            }
            else if (g.status == 3)
            {
                mc.status = 3; mc.value = "—";
                mc.desc = hr ? "Bez upotrebljivog roll fita nema smisla odvajati šum."
                             : "Without a usable roll fit the noise cannot be separated.";
                mc.note = rollCond;
            }
            else
            {
                if (unexpl < 0.03) { mc.status = 0; mc.desc = hr ? "Nizak nekorelirani rezidual — IMU čvrsto." : "Low uncorrelated residual — IMU solid."; }
                else { mc.status = 1; mc.desc = hr ? "Povišen nekorelirani rezidual — provjeri fiksiranje IMU-a." : "Elevated uncorrelated residual — check IMU mounting."; }
                mc.value = "RMS " + (unexpl * 100).ToString("0.0") + " cm";
            }
            if (mc != null) r.checks.Add(mc);

            // summary + chart
            r.summary = BuildStraightSummary(hr, g, lc, a, antNow, antNew, firstOk);
            // two-way plot when we have it — it shows the zero offset directly
            r.svg = paired ? TwoWaySvg(rA, rB, RollBias(rA, rB), hr) + MoneyPlotSvg(x, y, a, b, hr)
                           : MoneyPlotSvg(x, y, a, b, hr);
            r.subtitle = (hr ? "pravac · " : "straight · ")
                       + r.durationSec.ToString("0") + " s · roll RMS " + rollRms.ToString("0.0") + "°"
                       + (paired ? (hr ? " · dvosmjerno" : " · two-way") : (hr ? " · jednosmjerno" : " · one-way"));
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

        // Findings that exist only because the same ground was driven both ways.
        private void AddTwoWayChecks(FormGPS mf, ITResult r, bool hr, bool paired,
                                     double[] rA, double[] rB, double[] lA, double[] lB)
        {
            var bias = new ITCheck { name = hr ? "Roll zero-offset" : "Roll zero offset" };
            var off = new ITCheck { name = hr ? "Lateralni offset antene" : "Antenna lateral offset" };

            if (!paired)
            {
                string need = hr ? "Treba i povratni prolaz po istoj liniji (vozi do kraja, okreni se, vrati se)."
                                 : "Needs the return pass along the same line (drive it, turn around, come back).";
                bias.status = 3; bias.value = "—"; bias.note = need;
                bias.desc = hr ? "Iz jednog smjera se bias ne razlikuje od nagiba terena."
                               : "From one direction a bias is indistinguishable from the terrain slope.";
                off.status = 3; off.value = "—"; off.note = need;
                off.desc = hr ? "Mjerljivo samo usporedbom oba smjera." : "Only measurable by comparing both directions.";
                r.checks.Add(bias); r.checks.Add(off);
                return;
            }

            int n = rA.Length;
            // terrain roll flips sign with direction, a sensor bias does not:
            // the half-sum of the two passes over the same ground is the bias.
            var half = new double[n];
            for (int k = 0; k < n; k++) half[k] = (rA[k] + rB[k]) / 2.0;
            double b = RollBias(rA, rB);
            double scatter = Std(half, b);

            bias.value = (b >= 0 ? "+" : "") + b.ToString("0.00") + "°";
            bias.note = (hr ? "rasap po binu ± " : "per-bin scatter +/- ") + scatter.ToString("0.00") + "° · "
                      + n + (hr ? " binova" : " bins");
            if (scatter > 1.5)
            {
                bias.status = 3; bias.value = "—";
                bias.desc = hr ? "Prolazi se ne poklapaju dovoljno po terenu — bias nije pouzdan."
                               : "The passes do not line up on the terrain well enough for a reliable bias.";
            }
            else if (Math.Abs(b) < 0.2)
            {
                bias.status = 0;
                bias.desc = hr ? "Roll nula je dobra — oba smjera daju simetričan nagib."
                               : "Roll zero is good — both directions give a symmetric slope.";
            }
            else
            {
                bias.status = Math.Abs(b) < 0.6 ? 1 : 2;
                bias.desc = hr ? "Roll ima konstantni pomak — ponovi roll kalibraciju (nula) na ravnom."
                               : "Roll carries a constant offset — redo the roll zero calibration on level ground.";
            }
            r.checks.Add(bias);

            // Anything fixed in the machine frame shows up as a half-difference of
            // the two passes. That includes the lateral part of the roll bias, so
            // subtract what the measured bias already explains.
            var diff = new double[n];
            for (int k = 0; k < n; k++) diff[k] = (lA[k] - lB[k]) / 2.0;
            double eVeh = Mean(diff);
            // The driver does not retrace the exact same path, and that mismatch does
            // not fully average out over a finite line — so the estimate carries a
            // real uncertainty. Quote it instead of implying centimetre precision.
            double eSe = Std(diff, eVeh) / Math.Sqrt(n);
            double antH = mf.vehicle.VehicleConfig.AntennaHeight;
            double fromBias = antH * Math.Sin(glm.toRadians(b));
            double estOffset = eVeh - fromBias;
            double offNow = mf.vehicle.VehicleConfig.AntennaOffset;
            // AOG shifts the fix to the right of travel for a positive offset, so a
            // reported position sitting right of true needs the setting reduced.
            double offNew = offNow - estOffset;

            off.note = (hr ? "polurazlika " : "half-difference ") + (eVeh * 100).ToString("0.0") + " cm · "
                     + (hr ? "od roll biasa " : "from roll bias ") + (fromBias * 100).ToString("0.0") + " cm · "
                     + (hr ? "nesigurnost ± " : "uncertainty +/- ") + (eSe * 100).ToString("0.0") + " cm";
            if (AutosteerWasUsed())
            {
                // Autosteer regulates the reported cross-track error to zero, which
                // is the very quantity this estimate reads — it cancels the offset
                // instead of revealing it.
                off.status = 3; off.value = "—";
                off.desc = hr ? "Autosteer je bio uključen — regulacija poništava traženi pomak."
                              : "Autosteer was engaged — the control loop cancels the offset being measured.";
                off.note = hr ? "Ponovi ručno, gledaj u daleku točku, ne ispravljaj na liniju."
                              : "Re-run manually, aim at a distant point, do not correct to the line.";
            }
            else if (Math.Abs(estOffset) < Math.Max(0.03, 2.0 * eSe))
            {
                // not distinguishable from the pass-to-pass mismatch
                off.status = 0;
                off.value = (estOffset * 100).ToString("0.0") + " ± " + (eSe * 100).ToString("0.0") + " cm";
                off.desc = hr ? "Antena je na sredini — prolazi se poklapaju u oba smjera."
                              : "Antenna is centred — the passes line up in both directions.";
            }
            else
            {
                off.status = Math.Abs(estOffset) < 0.08 ? 1 : 2;
                off.value = "off: " + offNow.ToString("0.00") + " → " + offNew.ToString("0.00") + " m";
                off.desc = hr ? "Prolazi se razilaze u suprotnim smjerovima — antena nije na sredini."
                              : "The passes separate in opposite directions — the antenna is off centre.";
            }
            r.checks.Add(off);

            // What does not repeat between two passes over the same ground is noise,
            // not geometry — a far better mount check than unexplained variance.
            var rep = new ITCheck { name = hr ? "Ponovljivost prolaza" : "Pass repeatability" };
            rep.value = "± " + scatter.ToString("0.00") + "°";
            rep.note = (hr ? "neponovljivi dio rolla preko " : "non-repeating roll over ") + n + (hr ? " binova" : " bins");
            rep.status = scatter < 0.4 ? 0 : (scatter < 1.0 ? 1 : 2);
            rep.desc = scatter < 0.4
                ? (hr ? "Roll se dobro ponavlja preko istog terena — mount je čvrst."
                      : "Roll repeats well over the same ground — the mount is solid.")
                : (hr ? "Roll se slabo ponavlja — provjeri fiksiranje IMU-a ili vozi mirnije."
                      : "Roll repeats poorly — check the IMU mounting or drive more steadily.");
            r.checks.Add(rep);
        }

        private static double RollBias(double[] rA, double[] rB)
        {
            var half = new double[rA.Length];
            for (int k = 0; k < rA.Length; k++) half[k] = (rA[k] + rB[k]) / 2.0;
            return Mean(half);
        }

        private static double Mean(double[] a)
        {
            if (a.Length == 0) return 0;
            double m = 0; for (int i = 0; i < a.Length; i++) m += a[i];
            return m / a.Length;
        }

        private static double Std(double[] a, double mean)
        {
            if (a.Length < 2) return 0;
            double v = 0; for (int i = 0; i < a.Length; i++) v += (a[i] - mean) * (a[i] - mean);
            return Math.Sqrt(v / (a.Length - 1));
        }

        // Each direction sits at its own offset from the line (line placement, plus
        // anything fixed in the machine frame). Centring per direction leaves only
        // the part that varies with roll, which is what the slope fit is after.
        private void RemovePerDirectionMean(List<int> idx, double[] y)
        {
            double sumA = 0, sumB = 0; int nA = 0, nB = 0;
            for (int k = 0; k < idx.Count; k++)
            {
                if (s[idx[k]].sameWay) { sumA += y[k]; nA++; }
                else { sumB += y[k]; nB++; }
            }
            double mA = nA > 0 ? sumA / nA : 0, mB = nB > 0 ? sumB / nB : 0;
            for (int k = 0; k < idx.Count; k++) y[k] -= s[idx[k]].sameWay ? mA : mB;
        }

        // Pair the two directions on the same ground. Binning by station IS the
        // low-pass: cab and suspension sway does not repeat between passes, terrain
        // does — averaging a few metres of ground drops the sway and keeps the slope.
        private bool PairedProfile(List<int> idx, out double[] rollA, out double[] rollB,
                                   out double[] latA, out double[] latB)
        {
            var sums = new Dictionary<int, double[]>();   // bin -> {rA,nA,lA, rB,nB,lB}
            foreach (int i in idx)
            {
                if (Math.Abs(s[i].lat) > latNearLine) continue;      // off the line — turning
                int bin = (int)Math.Floor(s[i].station / passBinM);
                double[] acc;
                if (!sums.TryGetValue(bin, out acc)) { acc = new double[6]; sums[bin] = acc; }
                if (s[i].sameWay) { acc[0] += s[i].rollDeg; acc[1] += 1; acc[2] += s[i].lat; }
                else { acc[3] += s[i].rollDeg; acc[4] += 1; acc[5] += s[i].lat; }
            }

            var keys = new List<int>();
            foreach (var kv in sums) if (kv.Value[1] > 0 && kv.Value[4] > 0) keys.Add(kv.Key);
            keys.Sort();

            rollA = new double[keys.Count]; rollB = new double[keys.Count];
            latA = new double[keys.Count]; latB = new double[keys.Count];
            for (int k = 0; k < keys.Count; k++)
            {
                double[] acc = sums[keys[k]];
                rollA[k] = acc[0] / acc[1]; latA[k] = acc[2] / acc[1];
                rollB[k] = acc[3] / acc[4]; latB[k] = acc[5] / acc[4];
            }
            return keys.Count >= minPairedBins;
        }

        public bool AutosteerWasUsed()
        {
            for (int i = 0; i < s.Count; i++) if (s[i].steerOn) return true;
            return false;
        }

        // metres of line covered in one direction, used for progress and for the
        // "did we actually drive a full pass" gate
        public double PassLength(bool sameWay)
        {
            double mn = double.MaxValue, mx = double.MinValue;
            for (int i = 0; i < s.Count; i++)
            {
                if (double.IsNaN(s[i].station) || s[i].sameWay != sameWay) continue;
                if (Math.Abs(s[i].lat) > latNearLine) continue;
                if (s[i].station < mn) mn = s[i].station;
                if (s[i].station > mx) mx = s[i].station;
            }
            return mx > mn ? mx - mn : 0;
        }

        public bool HasBothDirections()
        {
            bool a = false, b = false;
            for (int i = 0; i < s.Count; i++)
            {
                if (double.IsNaN(s[i].lat)) continue;
                if (s[i].sameWay) a = true; else b = true;
                if (a && b) return true;
            }
            return false;
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
            if (g.status == 3)
                return (hr ? "Geometrija nije izmjerena — " : "Geometry not measured — ") + g.desc + " "
                     + (hr ? "Ostali nalazi u tablici vrijede."
                           : "The remaining findings in the table still hold.");
            if (g.status == 2)
                return hr
                    ? "Roll ne prati lateralno pomicanje antene — <b>geometrija nije dobra</b>. " + g.value + "."
                    : "Roll does not track antenna lateral movement — <b>geometry is off</b>. " + g.value + ".";
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
        private string MoneyPlotSvg(double[] x, double[] y, double a, double b, bool hrPlot)
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
            sb.Append("<text x='34' y='16' font-size='9' fill='#5b6a63'>")
              .Append(hrPlot ? "rezidual (m)" : "residual (m)").Append("</text>");
            sb.Append("</svg>");
            return sb.ToString();
        }

        // The two-way plot IS the argument: over the same ground the terrain roll
        // mirrors between passes, so plotting the return pass negated should land it
        // on top of the first. Whatever gap remains is the sensor's zero offset.
        private string TwoWaySvg(double[] rA, double[] rB, double bias, bool hr)
        {
            int n = rA.Length;
            if (n < 2) return "";
            double ymn = double.MaxValue, ymx = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                double v1 = rA[i], v2 = -rB[i];
                if (v1 < ymn) ymn = v1; if (v1 > ymx) ymx = v1;
                if (v2 < ymn) ymn = v2; if (v2 > ymx) ymx = v2;
            }
            if (ymx - ymn < 1e-6) { ymn -= 1; ymx += 1; }
            double pad = (ymx - ymn) * 0.12; ymn -= pad; ymx += pad;

            Func<int, double> MX = i => 34 + (double)i / (n - 1) * 316;
            Func<double, double> MY = v => 178 - (v - ymn) / (ymx - ymn) * 158;

            var sb = new System.Text.StringBuilder();
            sb.Append("<svg viewBox='0 0 360 200' width='100%' style='max-width:520px'>");
            if (ymn < 0 && ymx > 0)
                sb.Append("<line x1='34' y1='").Append(F(MY(0))).Append("' x2='350' y2='").Append(F(MY(0)))
                  .Append("' stroke='#cbd5d0'/>");
            AppendPolyline(sb, MX, MY, rA, n, "#0d8395", false);
            AppendPolyline(sb, MX, MY, rB, n, "#c9721b", true);
            sb.Append("<line x1='34' y1='").Append(F(MY(bias))).Append("' x2='350' y2='").Append(F(MY(bias)))
              .Append("' stroke='#c23a2b' stroke-width='1.5' stroke-dasharray='4 3'/>");
            sb.Append("<text x='38' y='14' font-size='9' fill='#0d8395'>")
              .Append(hr ? "prolaz A" : "pass A").Append("</text>");
            sb.Append("<text x='96' y='14' font-size='9' fill='#c9721b'>")
              .Append(hr ? "prolaz B (zrcaljen)" : "pass B (mirrored)").Append("</text>");
            sb.Append("<text x='214' y='14' font-size='9' fill='#c23a2b'>")
              .Append(hr ? "zero-offset " : "zero offset ").Append(bias.ToString("0.00", CultureInfo.InvariantCulture))
              .Append("°</text>");
            sb.Append("<text x='300' y='196' font-size='9' fill='#5b6a63'>")
              .Append(hr ? "stanica" : "station").Append("</text>");
            sb.Append("<text x='36' y='196' font-size='9' fill='#5b6a63'>roll °</text>");
            sb.Append("</svg>");
            return sb.ToString();
        }

        private static void AppendPolyline(System.Text.StringBuilder sb, Func<int, double> MX,
                                           Func<double, double> MY, double[] v, int n, string colour, bool negate)
        {
            sb.Append("<polyline fill='none' stroke='").Append(colour).Append("' stroke-width='1.8' points='");
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(F(MX(i))).Append(',').Append(F(MY(negate ? -v[i] : v[i])));
            }
            sb.Append("'/>");
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
