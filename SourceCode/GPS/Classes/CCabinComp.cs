// F10 Advanced Sensor Fusion (NV custom) — cabin (suspended cab) compensation.
//
// Measured on the client tractor (16.09.2026): on a fast roll change the antenna really
// moves 2.39 cm/deg while AOG applies 4.6 cm/deg (sin(roll) x full antenna height), so every
// roll spike injects ~2.2 cm/deg of position that never happened - that is what jerks the wheel.
//
// Roll is therefore split in two:
//   slow part (EMA, tauSlow)  = terrain, whole tractor leans  -> lever = antenna height
//   fast part (rest)          = cabin rocking on its mounts   -> lever = kFast (~1.4 m)
// and the corrected position then goes through a lateral-only filter: dead reckoning
// (speed x smoothed course) predicts the next fix, the along-track part is taken in full
// (no delay) and only the lateral residual is filtered, with `lead` giving part of the
// delay back.
//
// Everything is causal and runs at fix rate (10 Hz). With isOn = false the class returns
// exactly the old formula so behaviour is unchanged.
using System;

namespace AgOpenGPS
{
    public class CCabinComp
    {
        // ---- settings ------------------------------------------------------
        public bool isOn;
        public double kFast = 1.4;      // [m] lever of the fast (cabin) roll component
        public double fc = 0.0;         // [Hz] lateral filter cutoff, 0 = filter off
        public double lead = 0.7;       // [0..1] how much of the filter delay is given back
        public double tauSlow = 1.0;    // [s] terrain / cabin split

        public const double MinSpeedMs = 0.3;   // below this the lateral filter passes through

        // ---- live values (window display) ----------------------------------
        public double rollSlowDeg, rollFastDeg;     // the two roll parts [deg]
        public double corrSlow, corrFast, corrTotal; // correction parts [m]
        public double latBefore, latAfter;           // lateral offset from dead reckoning [m]

        // ---- state ---------------------------------------------------------
        private double slowSin;
        private bool slowInit;
        private double fltE, fltN;
        private bool fltInit;
        private double smoothCosE, smoothCosN;      // smoothed course unit vector
        private bool courseInit;

        public void Reset()
        {
            slowInit = false;
            fltInit = false;
            courseInit = false;
            learn.Reset();
        }

        /// <summary>
        /// Roll correction distance [m], same sign convention as the original code
        /// (rollCorrectionDistance = sin(roll) * -AntennaHeight).
        /// </summary>
        public double RollCorrection(double rollDeg, double antennaHeight, double dt)
        {
            double s = Math.Sin(glm.toRadians(rollDeg));

            if (!isOn)
            {
                rollSlowDeg = rollDeg; rollFastDeg = 0;
                corrSlow = corrTotal = -s * antennaHeight; corrFast = 0;
                return corrTotal;
            }

            if (!slowInit) { slowSin = s; slowInit = true; }
            else
            {
                double a = dt / (Math.Max(tauSlow, 0.05) + dt);
                slowSin += a * (s - slowSin);
            }

            double fast = s - slowSin;
            rollSlowDeg = glm.toDegrees(Math.Asin(Math.Max(-1, Math.Min(1, slowSin))));
            rollFastDeg = rollDeg - rollSlowDeg;

            corrSlow = -slowSin * antennaHeight;
            corrFast = -fast * kFast;
            corrTotal = corrSlow + corrFast;
            return corrTotal;
        }

        /// <summary>
        /// Lateral-only filter on the corrected fix. Along-track is untouched, so there is
        /// no delay in the driving direction. Call once per fix, after the roll correction.
        /// </summary>
        public void FilterFix(ref double east, ref double north, double headingRad, double speedMs, double dt)
        {
            latBefore = latAfter = 0;

            if (!isOn || fc <= 0.01 || Math.Abs(speedMs) < MinSpeedMs)
            {
                fltInit = false;
                courseInit = false;
                return;
            }

            // smoothed course - the instantaneous one rocks together with the cabin,
            // so dead reckoning would repeat the very noise we are removing
            double ce = Math.Sin(headingRad), cn = Math.Cos(headingRad);
            if (!courseInit) { smoothCosE = ce; smoothCosN = cn; courseInit = true; }
            else
            {
                double ac = dt / (1.0 + dt);          // ~1 s
                smoothCosE += ac * (ce - smoothCosE);
                smoothCosN += ac * (cn - smoothCosN);
                double nrm = Math.Sqrt(smoothCosE * smoothCosE + smoothCosN * smoothCosN);
                if (nrm > 1e-6) { smoothCosE /= nrm; smoothCosN /= nrm; }
            }

            if (!fltInit) { fltE = east; fltN = north; fltInit = true; return; }

            // dead reckoning
            fltE += smoothCosE * speedMs * dt;
            fltN += smoothCosN * speedMs * dt;

            double rx = east - fltE, ry = north - fltN;
            double along = rx * smoothCosE + ry * smoothCosN;
            double lat = rx * smoothCosN - ry * smoothCosE;

            double tau = 1.0 / (2.0 * Math.PI * fc);
            double alpha = dt / (tau + dt);
            double step = alpha * lat;

            fltE += along * smoothCosE + step * smoothCosN;
            fltN += along * smoothCosN - step * smoothCosE;

            double extra = lead * tau * step / Math.Max(dt, 1e-3);   // give part of the delay back

            latBefore = lat;
            latAfter = lat - step - extra;

            east = fltE + extra * smoothCosN;
            north = fltN - extra * smoothCosE;
        }

        // ====================================================================
        // learning of kFast: regression of the lateral move of the RAW antenna on the
        // fast part of sin(roll), both taken as a change over ~0.5 s
        // ====================================================================
        public CLearnKFast learn = new CLearnKFast();

        public class CLearnKFast
        {
            private const int Lag = 5;              // 0.5 s at 10 Hz
            private const double MinRollDelta = 0.3; // deg
            private readonly double[] bufE = new double[Lag + 1];
            private readonly double[] bufN = new double[Lag + 1];
            private readonly double[] bufRoll = new double[Lag + 1];
            private readonly double[] bufHead = new double[Lag + 1];
            private int idx, filled;

            private double sx, sy, sxx, syy, sxy;
            public int n;
            public double seconds;
            public bool running;

            public void Reset()
            {
                idx = filled = 0;
                sx = sy = sxx = syy = sxy = 0;
                n = 0; seconds = 0;
            }

            public void Sample(double east, double north, double rollDeg, double headingRad, double speedMs, double dt)
            {
                bufE[idx] = east; bufN[idx] = north; bufRoll[idx] = rollDeg; bufHead[idx] = headingRad;
                int old = (idx + 1) % (Lag + 1);
                idx = old;
                if (filled <= Lag) { filled++; return; }
                if (!running || speedMs < 1.0) return;

                seconds += dt;
                double dRoll = rollDeg - bufRoll[old];
                if (Math.Abs(dRoll) < MinRollDelta) return;

                // lateral part of the move, in the vehicle frame of the older sample
                double de = east - bufE[old], dn = north - bufN[old];
                double h = bufHead[old];
                double lat = de * Math.Cos(h) - dn * Math.Sin(h);        // right = (cos h, -sin h)

                double x = Math.Sin(glm.toRadians(rollDeg)) - Math.Sin(glm.toRadians(bufRoll[old]));
                sx += x; sy += lat; sxx += x * x; syy += lat * lat; sxy += x * lat; n++;
            }

            /// <summary>k [m] = lever of the fast roll component; r2 = quality of the fit.</summary>
            public bool Result(out double k, out double r2)
            {
                k = 0; r2 = 0;
                if (n < 50) return false;
                double den = sxx - sx * sx / n;
                if (Math.Abs(den) < 1e-9) return false;
                k = (sxy - sx * sy / n) / den;
                double denY = syy - sy * sy / n;
                r2 = denY > 1e-12 ? (sxy - sx * sy / n) * (sxy - sx * sy / n) / (den * denY) : 0;
                return true;
            }
        }
    }
}
