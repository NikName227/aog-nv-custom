// F10 Advanced Sensor Fusion (NV custom) — heading observer.
//
// Same idea as a speed observer on a PWM motor: a model carries the fast, smooth part and a
// slow PI correction keeps it on the measurement.
//
//   model:      psiDot = v * tan(WAS - bias) / Leff      (WAS sits on the chassis, not the cab)
//   correction: psi   += Kp * wrap(course - psi) * dt
//   bias:       bias  -= Ki * wrap(course - psi) * dt     (finds the WAS zero by itself)
//
// Measured on the client tractor (16.09.2026), heading error vs a zero-phase reference:
//   f2f course 2.35 deg straight / 5.87 deg in the turn, IMU heading 0.74 / 5.01,
//   observer (Kp 0.3) 0.72 / 2.85 - i.e. as calm as the IMU on the straight and much better
//   in turns, without depending on how good the cab IMU is.
//
// Leff is the EFFECTIVE wheelbase (includes tyre slip): 4.2-4.4 m measured where the
// mechanical one is ~2.8 m. It is learned from the turns, together with the WAS zero.
using System;

namespace AgOpenGPS
{
    public class CHeadingObserver
    {
        // ---- settings ------------------------------------------------------
        public bool isOn;
        public double Kp = 0.3;         // correction strength, closed loop tau ~ 1/Kp
        public double Ki = 0.004;       // WAS zero estimation, works on a minute scale
        public double wImu = 0.25;      // share of the IMU yaw rate in the model rate
        public double Leff = 4.2;       // [m] effective wheelbase
        public double biasDeg;          // [deg] estimated WAS zero (kept between runs)

        public const double MinSpeedMs = 0.3;   // below this the GPS course is useless
        private const double KpBoost = 1.5;     // first seconds after start - snap onto the course
        private const double BoostSeconds = 10.0;

        // ---- live values ---------------------------------------------------
        public double psiRad;            // observer heading
        public double modelYaw, imuYaw;  // [deg/s]
        public bool isInitialized;
        private double movingSeconds;
        private double prevImuHeadingDeg = double.NaN;

        public void Reset()
        {
            isInitialized = false;
            movingSeconds = 0;
            prevImuHeadingDeg = double.NaN;
            learn.Reset();
        }

        private static double Wrap(double a)
        {
            while (a > Math.PI) a -= glm.twoPI;
            while (a < -Math.PI) a += glm.twoPI;
            return a;
        }

        /// <summary>
        /// One step. courseRad is the fix-to-fix course (valid only while moving),
        /// imuHeadingDeg is the raw IMU heading (any offset, only its change is used).
        /// Returns the observer heading [rad].
        /// </summary>
        public double Update(double wasDeg, double speedMs, double courseRad, bool courseValid,
                             double imuHeadingDeg, double dt)
        {
            bool moving = Math.Abs(speedMs) > MinSpeedMs;

            // IMU yaw rate from the heading change (unit safe, no PGN needed)
            imuYaw = 0;
            if (!double.IsNaN(prevImuHeadingDeg) && imuHeadingDeg < 65000 && dt > 1e-3)
            {
                double d = imuHeadingDeg - prevImuHeadingDeg;
                if (d > 180) d -= 360; else if (d < -180) d += 360;
                imuYaw = d / dt;
            }
            if (imuHeadingDeg < 65000) prevImuHeadingDeg = imuHeadingDeg;

            if (!isInitialized)
            {
                if (!courseValid || !moving) return psiRad;
                psiRad = courseRad;
                isInitialized = true;
                movingSeconds = 0;
                return psiRad;
            }

            modelYaw = glm.toDegrees(speedMs * Math.Tan(glm.toRadians(wasDeg - biasDeg)) / Math.Max(Leff, 0.5));
            double w = Math.Max(0, Math.Min(1, wImu));
            double rate = (1.0 - w) * modelYaw + w * imuYaw;

            psiRad += glm.toRadians(rate) * dt;

            if (courseValid && moving)
            {
                movingSeconds += dt;
                double kp = movingSeconds < BoostSeconds ? KpBoost : Kp;
                double err = Wrap(courseRad - psiRad);
                psiRad += kp * err * dt;
                biasDeg -= Ki * glm.toDegrees(err) * dt;
                if (biasDeg > 10) biasDeg = 10; else if (biasDeg < -10) biasDeg = -10;
            }

            if (psiRad < 0) psiRad += glm.twoPI;
            else if (psiRad >= glm.twoPI) psiRad -= glm.twoPI;
            return psiRad;
        }

        // ====================================================================
        // learning of Leff (and a check of the WAS zero): the real heading change over a
        // 10 s window against the model integral over the same window. Needs real steering,
        // so turns - on a straight pass the whole thing drowns in noise.
        // ====================================================================
        public CLearnModel learn = new CLearnModel();

        public class CLearnModel
        {
            private const int Win = 100;            // 10 s at 10 Hz
            private readonly double[] cumModel = new double[Win + 1];
            private readonly double[] cumCourse = new double[Win + 1];
            private int idx, filled;
            private double cm, cc, prevCourse = double.NaN;

            private double sxx, sxy;
            public int n;
            public int turnsLeft, turnsRight;
            public bool running;
            private double turnAccum;

            public void Reset()
            {
                idx = filled = 0; cm = cc = 0; prevCourse = double.NaN;
                sxx = sxy = 0; n = 0; turnsLeft = turnsRight = 0; turnAccum = 0;
            }

            public void Sample(double wasDeg, double biasDeg, double speedMs, double courseRad, double dt)
            {
                if (speedMs < 1.0) { prevCourse = double.NaN; return; }

                // model integral without 1/Leff, so the regression slope IS 1/Leff
                cm += glm.toDegrees(speedMs * Math.Tan(glm.toRadians(wasDeg - biasDeg))) * dt;

                double courseDeg = glm.toDegrees(courseRad);
                if (!double.IsNaN(prevCourse))
                {
                    double d = courseDeg - prevCourse;
                    if (d > 180) d -= 360; else if (d < -180) d += 360;
                    cc += d;

                    // count the turns so the user knows one in each direction is needed
                    turnAccum += d;
                    if (turnAccum > 60) { turnsRight++; turnAccum = 0; }
                    else if (turnAccum < -60) { turnsLeft++; turnAccum = 0; }
                    if (Math.Abs(d) < 0.02) turnAccum *= 0.999;
                }
                prevCourse = courseDeg;

                cumModel[idx] = cm; cumCourse[idx] = cc;
                int old = (idx + 1) % (Win + 1);
                idx = old;
                if (filled <= Win) { filled++; return; }
                if (!running) return;

                double x = cm - cumModel[old];
                double y = cc - cumCourse[old];
                if (Math.Abs(x) < 5.0) return;      // no real steering in this window

                sxx += x * x; sxy += x * y; n++;
            }

            /// <summary>Leff [m] from the regression slope (1/Leff), plus the correlation.</summary>
            public bool Result(out double leff, out double quality)
            {
                leff = 0; quality = 0;
                if (n < 30 || sxx < 1e-6) return false;
                double slope = sxy / sxx;
                if (slope < 1e-4) return false;
                leff = 1.0 / slope;
                quality = Math.Min(1.0, n / 100.0);
                return leff > 1.0 && leff < 20.0;
            }

            public bool HasBothTurns { get { return turnsLeft > 0 && turnsRight > 0; } }
        }
    }
}
