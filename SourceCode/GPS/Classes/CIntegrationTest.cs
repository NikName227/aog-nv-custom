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
        public int status;       // 0 good, 1 warn, 2 crit
        public string desc;      // one-line finding
        public string value;     // measured value
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

            double imuRad = mf.timerSim.Enabled
                ? mf.ahrs.imuHeading                             // sim: already radians
                : glm.toRadians(mf.ahrs.imuHeading);            // real: degrees

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

        // 0..1 progress + a short driver hint (localized).
        public double Progress(out string hint, bool hr)
        {
            if (mode == ITMode.Circle)
            {
                double p = cumHeadingRad / circleTargetRad;
                hint = hr ? "Drži konstantan kut — vozi krug"
                          : "Hold a constant angle — drive the circle";
                return Clamp01(p);
            }
            // straight: seconds in each band
            double[] sec = BandSeconds();
            double filled = 0; int worst = 0; double worstSec = double.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                filled += Math.Min(sec[i], targetPerBandSec);
                if (sec[i] < worstSec) { worstSec = sec[i]; worst = i; }
            }
            hint = (hr ? "Vozi ravno na " : "Drive straight at ")
                 + bandCenter[worst].ToString("0", CultureInfo.InvariantCulture) + " km/h";
            return Clamp01(filled / (3 * targetPerBandSec));
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

            r.total = r.checks.Count;
            r.okCount = 0;
            foreach (var c in r.checks) if (c.status == 0) r.okCount++;
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
            var fc = new ITCheck { name = hr ? "F2F heading (po brzini)" : "F2F heading (by speed)" };
            // nz[0..2] = std deg for 3/7/10
            int firstOk = -1;
            for (int i = 0; i < 3; i++) if (nz[i] >= 0 && nz[i] < 1.0) { firstOk = i; break; }
            if (firstOk < 0) { fc.status = 1; fc.desc = hr ? "F2F šumovit na svim brzinama." : "F2F noisy at all tested speeds."; fc.value = "—"; }
            else
            {
                fc.status = 0;
                fc.desc = hr ? "Šum pada s brzinom — upotrebljiv od navedene." : "Noise drops with speed — usable from shown speed.";
                fc.value = "≥" + bandCenter[firstOk].ToString("0") + " km/h";
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

        private void AnalyzeCircle(FormGPS mf, ITResult r, bool hr)
        {
            var idx = new List<int>();
            // steady-circle: drop entry/exit — keep where |steer| meaningful and stable
            double medSteer = Median(SteerAbs());
            for (int i = 0; i < s.Count; i++)
                if (Math.Abs(s[i].steer) > 0.4 * medSteer) idx.Add(i);
            if (idx.Count < 30) idx = AllIdx();

            double[] E = new double[idx.Count], N = new double[idx.Count];
            for (int k = 0; k < idx.Count; k++) { E[k] = s[idx[k]].E; N[k] = s[idx[k]].N; }
            double cx, cy, R; bool fitOk = CircleFit(E, N, out cx, out cy, out R);

            // geometric angle from centre (ground truth) + tangent
            double[] geo = new double[idx.Count];
            for (int k = 0; k < idx.Count; k++) geo[k] = Math.Atan2(N[k] - cy, E[k] - cx);
            double geoSweep = UnwrapSweep(geo);
            double sign = geoSweep >= 0 ? 1 : -1;

            // per-source tracking error vs tangent (mounting offset removed)
            double[] imuErr, f2fErr;
            double imuScale, f2fScale;
            SourceVsTangent(idx, geo, sign, true, out imuErr, out imuScale, geoSweep);
            SourceVsTangent(idx, geo, sign, false, out f2fErr, out f2fScale, geoSweep);
            double imuMax = MaxAbs(imuErr), f2fMax = MaxAbs(f2fErr);

            var c1 = new ITCheck { name = hr ? "IMU prati zavoj" : "IMU tracks turn" };
            c1.status = imuMax < 1.0 ? 0 : (imuMax < 2.5 ? 1 : 2);
            c1.desc = hr ? "Odstupanje IMU headinga od tangente kroz krug." : "IMU heading vs tangent over the lap.";
            c1.value = "± " + imuMax.ToString("0.0") + "°";
            r.checks.Add(c1);

            var c2 = new ITCheck { name = hr ? "IMU yaw scale" : "IMU yaw scale" };
            double sp = (imuScale - 1.0) * 100.0;
            c2.status = Math.Abs(sp) < 1.0 ? 0 : (Math.Abs(sp) < 3.0 ? 1 : 2);
            c2.desc = hr ? "Ukupno okretanje IMU-a vs geometrijski krug." : "IMU total turn vs geometric lap.";
            c2.value = (sp >= 0 ? "+" : "") + sp.ToString("0.0") + " %";
            r.checks.Add(c2);

            var c3 = new ITCheck { name = hr ? "F2F prati zavoj" : "F2F tracks turn" };
            c3.status = f2fMax < 1.0 ? 0 : (f2fMax < 2.5 ? 1 : 2);
            c3.desc = hr ? "Kurs preko tla vs tangenta." : "Course-over-ground vs tangent.";
            c3.value = "± " + f2fMax.ToString("0.0") + "°";
            r.checks.Add(c3);

            r.summary = BuildCircleSummary(hr, R, imuMax, sp, f2fMax, fitOk);
            r.svg = CircleSvg(E, N, cx, cy, R);
            r.subtitle = (hr ? "kružnica · R " : "circle · R ")
                       + R.ToString("0.0") + " m · " + (Math.Abs(geoSweep) / (2 * Math.PI)).ToString("0.0")
                       + (hr ? " kruga" : " laps");
        }

        // ---- source vs tangent, offset-removed error + yaw scale ----
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

        // ================================================================ helpers
        private List<int> AllIdx() { var l = new List<int>(); for (int i = 0; i < s.Count; i++) l.Add(i); return l; }
        private double[] SteerAbs() { var a = new double[s.Count]; for (int i = 0; i < s.Count; i++) a[i] = Math.Abs(s[i].steer); return a; }

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
                    if (Math.Abs(s[i].speed - bandCenter[b]) <= bandTol) d.Add(WrapPi(s[i].f2fRad - s[i].imuRad));
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

        private static double MaxAbs(double[] a) { double m = 0; for (int i = 0; i < a.Length; i++) if (Math.Abs(a[i]) > m) m = Math.Abs(a[i]); return m; }
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

        private string BuildCircleSummary(bool hr, double R, double imuMax, double sp, double f2fMax, bool fitOk)
        {
            if (!fitOk) return hr ? "Kružnica se ne da pouzdano fitati — vozi puniji, stabilniji krug."
                                  : "Circle could not be fit reliably — drive a fuller, steadier circle.";
            string s1 = hr ? ("Fit R = " + R.ToString("0.0") + " m. ") : ("Fit R = " + R.ToString("0.0") + " m. ");
            string s2 = (imuMax < 1.0 && f2fMax < 1.0)
                ? (hr ? "IMU i F2F ispravno prate skretanje." : "IMU and F2F track the turn correctly.")
                : (hr ? "Provjeri izvor s većim odstupanjem od tangente." : "Check the source with the larger tangent error.");
            string s3 = Math.Abs(sp) >= 1.0
                ? (hr ? " IMU yaw scale " : " IMU yaw scale ") + (sp >= 0 ? "+" : "") + sp.ToString("0.0") + "% — "
                  + (hr ? "objašnjava drift u dugim zavojima." : "explains drift in long turns.")
                : "";
            return s1 + s2 + s3;
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
