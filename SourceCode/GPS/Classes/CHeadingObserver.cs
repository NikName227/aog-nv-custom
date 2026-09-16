// F10 Advanced Sensor Fusion (NV custom) — heading observer.
//
// Same idea as a speed observer on a PWM motor: a model carries the fast, smooth part and a
// slow correction keeps it on the measurement.
//
//   model:      psiDot = v * tan(WAS - bias) / Leff      (WAS sits on the chassis, not the cab)
//   correction: psi   += Kp * wrap(measurement - psi) * dt
//   bias:       bias  -= Ki * wrap(measurement - psi) * dt     (finds the WAS zero by itself)
//
// The measurement is NOT always the fix-to-fix course. Measured on real logs:
//   - straight, working speed: the course is fine (observer 0.80 deg rms, yaw noise 0.6 deg/s
//     against 9.8 deg/s for the raw course)
//   - in a turn the course lags and gets noisy, while the IMU heading is clean; blending the
//     measurement over to the IMU by |WAS| dropped the turn error from 3.32 to 1.24 deg rms
//   - below ~2 km/h and while reversing the course is unusable, so the IMU carries
// The IMU has no north, so its offset is tracked while driving straight (measured drift of the
// cab IMU was only 0.5-0.75 deg/min, so it is a good short-term reference).
using System;

namespace AgOpenGPS
{
    public class CHeadingObserver
    {
        // ---- settings ------------------------------------------------------
        public bool isOn;
        public double Kp = 0.3;         // correction strength on the course, closed loop tau ~ 1/Kp
        public double Ki = 0.004;       // WAS zero estimation, works on a minute scale
        public double wImu = 0.25;      // share of the IMU yaw RATE in the model rate
        public double Leff = 4.2;       // [m] effective wheelbase (includes slip)
        public double biasDeg;          // [deg] estimated WAS zero (kept between runs)

        public double KpTurn = 1.0;     // correction strength while the IMU is the measurement
        public double wasLo = 5.0;      // [deg] below this: measurement = course
        public double wasHi = 15.0;     // [deg] above this: measurement = IMU
        public double lowSpeedHi = 2.0; // [km/h] above this the course is trusted
        public double lowSpeedLo = 0.8; // [km/h] below this the IMU carries (80 %)

        public const double MinSpeedMs = 0.22;   // ~0.8 km/h - below it nothing is corrected
        private const double KpBoost = 1.5;      // first seconds after start - snap onto the course
        private const double BoostSeconds = 10.0;
        private const double TauOffset = 5.0;    // [s] IMU offset tracking while straight

        // ---- live values ---------------------------------------------------
        public double psiRad;               // observer heading
        public double modelYaw, imuYaw;     // [deg/s]
        public double imuWeight;            // 0 = course, 1 = IMU (for the window)
        public double imuOffsetDeg;         // IMU heading -> real heading
        public bool isInitialized, offsetValid;
        private double movingSeconds;
        private double prevImuHeadingDeg = double.NaN;

        public void Reset()
        {
            isInitialized = false;
            offsetValid = false;
            movingSeconds = 0;
            imuWeight = 0;
            prevImuHeadingDeg = double.NaN;
            learn.Reset();
        }

        private static double Wrap(double a)
        {
            while (a > Math.PI) a -= glm.twoPI;
            while (a < -Math.PI) a += glm.twoPI;
            return a;
        }

        private static double Clamp01(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

        /// <summary>
        /// One step. courseRad is the fix-to-fix course (valid only while moving, already
        /// corrected for reverse by the caller check), imuHeadingDeg is the raw IMU heading.
        /// Returns the observer heading [rad].
        /// </summary>
        public double Update(double wasDeg, double speedMs, double courseRad, bool courseValid,
                             double imuHeadingDeg, bool isReverse, double dt)
        {
            bool imuOk = imuHeadingDeg < 65000 && imuHeadingDeg > -1;
            bool moving = Math.Abs(speedMs) > MinSpeedMs;
            double speedKmh = Math.Abs(speedMs) * 3.6;

            // --- IMU yaw rate from the heading change (unit safe, no extra PGN needed)
            imuYaw = 0;
            if (imuOk && !double.IsNaN(prevImuHeadingDeg) && dt > 1e-3)
            {
                double d = imuHeadingDeg - prevImuHeadingDeg;
                if (d > 180) d -= 360; else if (d < -180) d += 360;
                imuYaw = d / dt;
            }
            if (imuOk) prevImuHeadingDeg = imuHeadingDeg;

            // driving backwards: the course points the other way, the model turns the other way
            double course = isReverse ? courseRad + Math.PI : courseRad;

            if (!isInitialized)
            {
                if (!courseValid || !moving) return psiRad;
                psiRad = course;
                if (imuOk) { imuOffsetDeg = glm.toDegrees(Wrap(course - glm.toRadians(imuHeadingDeg))); offsetValid = true; }
                isInitialized = true;
                movingSeconds = 0;
                return psiRad;
            }

            // --- how much of the measurement comes from the IMU -----------------
            double wWas = Clamp01((Math.Abs(wasDeg - biasDeg) - wasLo) / Math.Max(wasHi - wasLo, 0.1));
            double wSlow = 0.8 * Clamp01((lowSpeedHi - speedKmh) / Math.Max(lowSpeedHi - lowSpeedLo, 0.1));
            double w = Math.Max(wWas, wSlow);
            if (isReverse) w = Math.Max(w, 0.8);          // course is least reliable here
            if (!imuOk || !offsetValid) w = 0;
            imuWeight = w;

            // --- IMU offset is learned only while the course is the trusted source
            if (imuOk && courseValid && moving && w < 0.2 && speedKmh > 2.0)
            {
                double a = dt / (TauOffset + dt);
                double e = Wrap(course - glm.toRadians(imuHeadingDeg) - glm.toRadians(imuOffsetDeg));
                imuOffsetDeg += glm.toDegrees(a * e);
                offsetValid = true;
            }
            else if (!offsetValid && imuOk && courseValid && moving)
            {
                imuOffsetDeg = glm.toDegrees(Wrap(course - glm.toRadians(imuHeadingDeg)));
                offsetValid = true;
            }

            // --- model -----------------------------------------------------------
            modelYaw = glm.toDegrees(speedMs * Math.Tan(glm.toRadians(wasDeg - biasDeg)) / Math.Max(Leff, 0.5));
            if (isReverse) modelYaw = -modelYaw;
            double wr = Clamp01(wImu);
            double rate = (1.0 - wr) * modelYaw + wr * imuYaw;
            psiRad += glm.toRadians(rate) * dt;

            // --- correction -------------------------------------------------------
            double errCourse = courseValid && moving ? Wrap(course - psiRad) : 0;
            double errImu = (imuOk && offsetValid)
                ? Wrap(glm.toRadians(imuHeadingDeg + imuOffsetDeg) - psiRad) : 0;
            bool haveCourse = courseValid && moving;
            bool haveImu = imuOk && offsetValid;

            double err, kp;
            if (haveCourse && haveImu) { err = (1 - w) * errCourse + w * errImu; kp = (1 - w) * Kp + w * KpTurn; }
            else if (haveImu && w > 0) { err = errImu; kp = KpTurn; }
            else if (haveCourse) { err = errCourse; kp = Kp; }
            else { err = 0; kp = 0; }

            if (kp > 0)
            {
                if (movingSeconds < BoostSeconds && haveCourse) kp = Math.Max(kp, KpBoost);
                psiRad += kp * err * dt;
                if (moving) movingSeconds += dt;

                // the WAS zero is only meaningful while driving forward on the course
                if (w < 0.3 && !isReverse && haveCourse)
                {
                    biasDeg -= Ki * glm.toDegrees(err) * dt;
                    if (biasDeg > 10) biasDeg = 10; else if (biasDeg < -10) biasDeg = -10;
                }
            }

            if (psiRad < 0) psiRad += glm.twoPI;
            else if (psiRad >= glm.twoPI) psiRad -= glm.twoPI;
            return psiRad;
        }

        // ====================================================================
        // Learning in two stages, because a single regression is degenerate - the WAS zero and
        // Leff cancel each other out (measured: the same tractor came out as 4.35 m / +2.6 deg
        // and 4.11 m / -3.0 deg on two runs).
        //   stage 1: WAS zero  = average WAS while driving straight (no real steering)
        //   stage 2: Leff      = regression of the real yaw rate on v*tan(WAS - zero), in turns only
        // ====================================================================
        public CLearnModel learn = new CLearnModel();

        public class CLearnModel
        {
            private const double StraightYaw = 0.6;   // [deg/s] below this: straight
            private const double TurnYaw = 3.0;       // [deg/s] above this: a real turn
            private const double MinSpeedStraight = 1.0;  // [m/s]
            private const double MinSpeedTurn = 1.4;      // [m/s] ~5 km/h

            private double prevCourseDeg = double.NaN;
            private double yawFilt;
            private double sumWas; private int nWas;
            private double sxx, sxy; public int n;
            public int turnsLeft, turnsRight;
            private double turnAccum;
            public bool running;
            public double seconds;

            public void Reset()
            {
                prevCourseDeg = double.NaN; yawFilt = 0;
                sumWas = 0; nWas = 0; sxx = sxy = 0; n = 0;
                turnsLeft = turnsRight = 0; turnAccum = 0; seconds = 0;
            }

            public double BiasSoFar { get { return nWas > 50 ? sumWas / nWas : 0; } }

            public void Sample(double wasDeg, double speedMs, double courseRad, bool isReverse, double dt)
            {
                if (!running || speedMs < MinSpeedStraight || isReverse) { prevCourseDeg = double.NaN; return; }
                seconds += dt;

                double courseDeg = glm.toDegrees(courseRad);
                if (double.IsNaN(prevCourseDeg)) { prevCourseDeg = courseDeg; return; }
                double d = courseDeg - prevCourseDeg;
                if (d > 180) d -= 360; else if (d < -180) d += 360;
                prevCourseDeg = courseDeg;

                double yaw = d / Math.Max(dt, 1e-3);
                yawFilt += 0.25 * (yaw - yawFilt);          // the raw course rate is very noisy

                // count the turns so the operator knows one in each direction is needed
                turnAccum += d;
                if (turnAccum > 60) { turnsRight++; turnAccum = 0; }
                else if (turnAccum < -60) { turnsLeft++; turnAccum = 0; }

                if (Math.Abs(yawFilt) < StraightYaw)
                {
                    // stage 1 - straight: the wheels are (on average) where "zero" is
                    sumWas += wasDeg; nWas++;
                }
                else if (Math.Abs(yawFilt) > TurnYaw && speedMs > MinSpeedTurn)
                {
                    // stage 2 - turn: yaw = (1/Leff) * v*tan(WAS - zero)
                    double x = glm.toDegrees(speedMs * Math.Tan(glm.toRadians(wasDeg - BiasSoFar)));
                    if (Math.Abs(x) > 1.0) { sxx += x * x; sxy += x * yawFilt; n++; }
                }
            }

            /// <summary>Leff [m] and the WAS zero from the recorded run.</summary>
            public bool Result(out double leff, out double bias, out double quality)
            {
                leff = 0; bias = BiasSoFar; quality = 0;
                if (n < 50 || sxx < 1e-6) return false;
                double slope = sxy / sxx;                    // = 1/Leff
                if (slope < 1e-4) return false;
                leff = 1.0 / slope;
                quality = Math.Min(1.0, n / 200.0);
                return leff > 1.5 && leff < 15.0;
            }

            public bool HasBothTurns { get { return turnsLeft > 0 && turnsRight > 0; } }
            public bool HasStraight { get { return nWas > 300; } }     // ~30 s at 10 Hz
        }
    }
}
