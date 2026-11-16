using AgLibrary.Logging;
using System;
using System.Collections.Generic;

namespace AgOpenGPS
{
    /// <summary>
    /// Turn calibration (REQ 2026-11-16-1): two short test drives started from the Turn window.
    ///  - Teach: the wheels are stepped right / left while the tractor drives straight; out come the
    ///    steering speed (deg/s) and the lead time (s) the turn path and the turn steering are built from.
    ///  - Radius check: the angle the driver has put on the wheels is locked and held; the circle
    ///    driven is compared with what the bicycle model gives for that angle. A check only.
    /// Both use the existing free-drive mode: a fixed wheel angle is commanded, no line is followed.
    /// </summary>
    public class CTurnCalib
    {
        private readonly FormGPS mf;

        public enum Mode { Idle, Teach, Radius }
        public Mode mode = Mode.Idle;

        /// <summary>why the last run stopped on its own (speed, ...), empty = nothing to say</summary>
        public string abortReason = "";

        public const double minSpeed = 1.5, maxSpeed = 9.0;      // km/h

        private struct Smp { public double t, set, act, head, v, e, n; }
        private readonly List<Smp> smp = new List<Smp>(512);
        private DateTime t0;

        // ---- teach
        private double teachAngle;
        private static readonly double[] stepFactor = { 0, 1, -1, 1, 0 };
        private static readonly double[] stepEnd = { 1.0, 2.6, 5.8, 7.4, 9.0 };   // s, end of each step
        public bool teachDone, teachOk;
        public double teachRate, teachLead, teachGain, teachAccel;
        public double TeachProgress { get { return mode == Mode.Teach ? Math.Min(1.0, (DateTime.UtcNow - t0).TotalSeconds / stepEnd[stepEnd.Length - 1]) : 0; } }

        // ---- radius check
        public double holdAngle, sweep;
        private double lastHead;
        private bool rtkLost;

        public enum Unreliable { No, ShortCircle, AngleMoved, Gps }
        public class RadiusResult
        {
            /// <summary>angle = mean wheel angle actually held during the run</summary>
            public double angle, expected, measured, sweepDeg;
            public Unreliable why;
        }
        public readonly List<RadiusResult> results = new List<RadiusResult>();
        /// <summary>goes up with every new result - the window redraws its list on a change</summary>
        public int resultsVersion;
        /// <summary>the last teach run produced numbers (even if they were judged unreliable)</summary>
        public bool teachMeasured;

        public bool IsBusy { get { return mode != Mode.Idle; } }

        public CTurnCalib(FormGPS f) { mf = f; }

        private bool SpeedOk() { double v = Math.Abs(mf.avgSpeed); return v >= minSpeed && v <= maxSpeed; }

        private void Begin(Mode m)
        {
            smp.Clear();
            t0 = DateTime.UtcNow;
            abortReason = "";
            mode = m;
            mf.vehicle.isInFreeDriveMode = true;
        }

        private void End()
        {
            mode = Mode.Idle;
            mf.vehicle.driveFreeSteerAngle = 0;
            mf.vehicle.isInFreeDriveMode = false;
        }

        public bool StartTeach()
        {
            if (IsBusy || !SpeedOk() || mf.isReverse) return false;
            teachAngle = Math.Max(8.0, Math.Min(20.0, mf.vehicle.maxSteerAngle * 0.5));
            teachDone = false; teachOk = false; teachMeasured = false;
            mf.vehicle.driveFreeSteerAngle = 0;
            Begin(Mode.Teach);
            Log.EventWriter("Turn teach: started, steps of " + teachAngle.ToString("N0") + " deg");
            return true;
        }

        public bool StartRadius()
        {
            if (IsBusy || !SpeedOk() || mf.isReverse) return false;
            holdAngle = mf.mc.actualSteerAngleDegrees;
            if (Math.Abs(holdAngle) < 5.0) return false;
            sweep = 0; rtkLost = false;
            lastHead = mf.fixHeading;
            mf.vehicle.driveFreeSteerAngle = holdAngle;
            Begin(Mode.Radius);
            Log.EventWriter("Radius check: holding " + holdAngle.ToString("N1") + " deg");
            return true;
        }

        /// <summary>Driver pressed Stop (or the window closed): a radius run is evaluated, a teach run is dropped.</summary>
        public void Stop()
        {
            if (mode == Mode.Radius) FinishRadius();
            else if (mode == Mode.Teach) { End(); teachDone = true; teachOk = false; }
        }

        private double TeachSet(double t)
        {
            for (int i = 0; i < stepEnd.Length; i++) if (t < stepEnd[i]) return stepFactor[i] * teachAngle;
            return 0;
        }

        /// <summary>Once per position update.</summary>
        public void Update()
        {
            if (mode == Mode.Idle) return;

            if (!SpeedOk() || mf.isReverse)
            {
                abortReason = "speed";
                Log.EventWriter("Turn calibration stopped: speed outside " + minSpeed + "-" + maxSpeed + " km/h or reversing");
                if (mode == Mode.Radius) FinishRadius();
                else { End(); teachDone = true; teachOk = false; }
                return;
            }

            double t = (DateTime.UtcNow - t0).TotalSeconds;
            double set = mode == Mode.Teach ? TeachSet(t) : holdAngle;
            mf.vehicle.driveFreeSteerAngle = set;
            smp.Add(new Smp
            {
                t = t, set = set, act = mf.mc.actualSteerAngleDegrees, head = mf.fixHeading,
                v = Math.Abs(mf.avgSpeed) / 3.6, e = mf.pn.fix.easting, n = mf.pn.fix.northing
            });

            if (mode == Mode.Teach)
            {
                if (t >= stepEnd[stepEnd.Length - 1]) FinishTeach();
                return;
            }

            // radius check
            double dh = mf.fixHeading - lastHead;
            while (dh > Math.PI) dh -= glm.twoPI;
            while (dh < -Math.PI) dh += glm.twoPI;
            lastHead = mf.fixHeading;
            sweep += Math.Abs(dh);
            if (!mf.IsSimRunning && mf.pn.fixQuality != 4) rtkLost = true;
            if (sweep >= glm.twoPI) FinishRadius();
        }

        // ------------------------------------------------------------------ teach

        private void FinishTeach()
        {
            End();
            teachDone = true;
            teachOk = false;
            try
            {
                double wb = Math.Abs(mf.vehicle.VehicleConfig.Wheelbase);
                if (wb < 0.5 || smp.Count < 40) return;

                // 1. steering speed: time the wheels need from 10 % to 75 % of each step
                var up = new List<double>(); var dn = new List<double>();
                double prevEnd = 0;
                for (int s = 1; s < stepEnd.Length; s++)
                {
                    double tA = stepEnd[s - 1], a = stepFactor[s - 1] * teachAngle, b = stepFactor[s] * teachAngle;
                    prevEnd = stepEnd[s];
                    if (Math.Abs(b - a) < 5) continue;
                    double t10 = CrossTime(tA, prevEnd, a + 0.10 * (b - a), b > a);
                    double t75 = CrossTime(tA, prevEnd, a + 0.75 * (b - a), b > a);
                    if (t10 < 0 || t75 <= t10 + 0.02) continue;
                    (b > a ? up : dn).Add(0.65 * Math.Abs(b - a) / (t75 - t10));
                }
                if (up.Count == 0 || dn.Count == 0) return;
                double rate = Math.Min(Median(up), Median(dn));

                // 2. dead time of the wheels: the step, delayed and limited to that speed, against the measured angle
                double bestD = 0, bestErr = double.MaxValue;
                for (double d = 0; d <= 0.8001; d += 0.02)
                {
                    double sa = 0, sm = 0, err; var mdl = new double[smp.Count];
                    double a = 0, tt = 0;
                    for (int i = 0; i < smp.Count; i++)
                    {
                        while (tt < smp[i].t) { double target = TeachSet(tt - d); double st = rate * 0.01; a += Math.Max(-st, Math.Min(st, target - a)); tt += 0.01; }
                        mdl[i] = a; sa += a * smp[i].act; sm += a * a;
                    }
                    double g = sm > 1e-9 ? sa / sm : 1; err = 0;
                    for (int i = 0; i < smp.Count; i++) { double e = smp[i].act - g * mdl[i]; err += e * e; }
                    if (err < bestErr) { bestErr = err; bestD = d; }
                }

                // 2b. how fast the steering gets up to speed: from the step (after the dead time) until the
                //     wheels move at 80 % of their full speed
                var acc = new List<double>();
                for (int st = 1; st < stepEnd.Length; st++)
                {
                    double a0 = stepFactor[st - 1] * teachAngle, b0 = stepFactor[st] * teachAngle;
                    if (Math.Abs(b0 - a0) < 5) continue;
                    double tS = stepEnd[st - 1] + bestD, dir = Math.Sign(b0 - a0);
                    for (int i = 1; i < smp.Count; i++)
                    {
                        if (smp[i].t <= tS || smp[i].t >= stepEnd[st]) continue;
                        double dtS = smp[i].t - smp[i - 1].t;
                        if (dtS < 1e-3) continue;
                        if (dir * (smp[i].act - smp[i - 1].act) / dtS >= 0.8 * rate)
                        {
                            double tm = 0.5 * (smp[i].t + smp[i - 1].t);
                            acc.Add(0.8 * rate / Math.Max(0.05, tm - tS));
                            break;
                        }
                    }
                }
                teachAccel = acc.Count > 0 ? Math.Max(10, Math.Min(600, Median(acc))) : 0;

                // 3. from the wheel angle to the tractor actually turning (GPS heading)
                int n = smp.Count;
                var km = new double[n];
                for (int i = 1; i < n - 1; i++)
                {
                    double dh = smp[i + 1].head - smp[i - 1].head;
                    while (dh > Math.PI) dh -= glm.twoPI;
                    while (dh < -Math.PI) dh += glm.twoPI;
                    double dt = smp[i + 1].t - smp[i - 1].t;
                    km[i] = dt > 1e-3 && smp[i].v > 0.2 ? dh / dt / smp[i].v : 0;
                }
                double bestT = 0, bestG = 1, bestE2 = double.MaxValue, tot = 0;
                for (int i = 1; i < n - 1; i++) tot += km[i] * km[i];
                for (double d = 0; d <= 0.6001; d += 0.02)
                {
                    double sa = 0, sm = 0;
                    var ks = new double[n];
                    for (int i = 1; i < n - 1; i++) { ks[i] = Math.Tan(glm.toRadians(ActAt(smp[i].t - d))) / wb; sa += ks[i] * km[i]; sm += ks[i] * ks[i]; }
                    double g = sm > 1e-12 ? sa / sm : 1, err = 0;
                    for (int i = 1; i < n - 1; i++) { double e = km[i] - g * ks[i]; err += e * e; }
                    if (err < bestE2) { bestE2 = err; bestT = d; bestG = g; }
                }

                teachRate = Math.Max(5, Math.Min(60, rate));
                teachLead = Math.Max(0, Math.Min(1.0, bestD + bestT));
                teachGain = bestG;
                teachMeasured = true;
                teachOk = tot > 1e-9 && bestE2 / tot < 0.5;     // the heading really followed the wheel angle
                Log.EventWriter("Turn teach: steering " + rate.ToString("N1") + " deg/s (right " + Median(up).ToString("N1")
                    + ", left " + Median(dn).ToString("N1") + "), wheel delay " + bestD.ToString("N2") + " s, turn delay "
                    + bestT.ToString("N2") + " s, steering accel " + teachAccel.ToString("N0") + " deg/s2, turning vs model " + bestG.ToString("N2") + (teachOk ? "" : " - UNRELIABLE"));
            }
            catch (Exception ex)
            {
                Log.EventWriter("Turn teach failed: " + ex.Message);
            }
        }

        /// <summary>measured wheel angle at time t (linear between samples)</summary>
        private double ActAt(double t)
        {
            if (t <= smp[0].t) return smp[0].act;
            for (int i = 1; i < smp.Count; i++)
                if (smp[i].t >= t)
                {
                    double f = (t - smp[i - 1].t) / Math.Max(1e-6, smp[i].t - smp[i - 1].t);
                    return smp[i - 1].act + f * (smp[i].act - smp[i - 1].act);
                }
            return smp[smp.Count - 1].act;
        }

        /// <summary>first time in (tA, tB) the measured angle passes level, going up or down; -1 = never</summary>
        private double CrossTime(double tA, double tB, double level, bool rising)
        {
            for (int i = 1; i < smp.Count; i++)
            {
                if (smp[i].t <= tA || smp[i - 1].t >= tB) continue;
                double p = smp[i - 1].act, q = smp[i].act;
                if (rising ? (p < level && q >= level) : (p > level && q <= level))
                    return smp[i - 1].t + (level - p) / (q - p) * (smp[i].t - smp[i - 1].t);
            }
            return -1;
        }

        private static double Median(List<double> v)
        {
            var c = new List<double>(v); c.Sort();
            return c.Count % 2 == 1 ? c[c.Count / 2] : 0.5 * (c[c.Count / 2 - 1] + c[c.Count / 2]);
        }

        // ------------------------------------------------------------------ radius check

        private void FinishRadius()
        {
            End();
            try
            {
                if (smp.Count < 15 || sweep < 0.5) return;       // nothing worth a line

                // circle through the antenna track (least squares), then back to the rear axle:
                // independent of the computed heading, which is the weakest number in a bend
                double mx = 0, my = 0; int n = smp.Count;
                for (int i = 0; i < n; i++) { mx += smp[i].e; my += smp[i].n; }
                mx /= n; my /= n;
                double suu = 0, svv = 0, suv = 0, suuu = 0, svvv = 0, suvv = 0, svuu = 0;
                for (int i = 0; i < n; i++)
                {
                    double u = smp[i].e - mx, v = smp[i].n - my;
                    suu += u * u; svv += v * v; suv += u * v;
                    suuu += u * u * u; svvv += v * v * v; suvv += u * v * v; svuu += v * u * u;
                }
                double det = suu * svv - suv * suv;
                if (Math.Abs(det) < 1e-9) return;
                double bu = 0.5 * (suuu + suvv), bv = 0.5 * (svvv + svuu);
                double uc = (bu * svv - bv * suv) / det, vc = (bv * suu - bu * suv) / det;
                double rAnt = Math.Sqrt(uc * uc + vc * vc + (suu + svv) / n);

                double d = Properties.VehicleSettings.Default.setVehicle_antennaPivot;
                double rPiv = Math.Sqrt(Math.Max(0.01, rAnt * rAnt - d * d));
                double wb = Math.Abs(mf.vehicle.VehicleConfig.Wheelbase);

                //the angle the wheels really stood at (after the first second, once they had settled):
                //the model radius is worked out from that, not from the angle that was asked for - the
                //question is whether the reported angle matches the circle driven
                double sumA = 0, minA = double.MaxValue, maxA = double.MinValue; int nA = 0;
                for (int i = 0; i < n; i++)
                {
                    if (smp[i].t < 1.0 && n > 30) continue;
                    sumA += smp[i].act; nA++;
                    if (smp[i].act < minA) minA = smp[i].act;
                    if (smp[i].act > maxA) maxA = smp[i].act;
                }
                double meanA = nA > 0 ? sumA / nA : holdAngle;
                if (Math.Abs(meanA) < 1.0) return;

                var r = new RadiusResult
                {
                    angle = meanA,
                    expected = wb / Math.Tan(glm.toRadians(Math.Abs(meanA))),
                    measured = rPiv,
                    sweepDeg = glm.toDegrees(sweep),
                    why = sweep < glm.twoPI * 2.0 / 3.0 ? Unreliable.ShortCircle
                        : (maxA - minA > 3.0 ? Unreliable.AngleMoved : (rtkLost ? Unreliable.Gps : Unreliable.No))
                };
                results.Add(r);
                if (results.Count > 8) results.RemoveAt(0);
                resultsVersion++;
                Log.EventWriter("Radius check: " + r.angle.ToString("N1") + " deg held over " + r.sweepDeg.ToString("N0")
                    + " deg of circle - expected " + r.expected.ToString("N2") + " m, measured " + r.measured.ToString("N2") + " m"
                    + (r.why == Unreliable.No ? "" : " (unreliable: " + r.why + ")"));
            }
            catch (Exception ex)
            {
                Log.EventWriter("Radius check failed: " + ex.Message);
            }
        }
    }
}
