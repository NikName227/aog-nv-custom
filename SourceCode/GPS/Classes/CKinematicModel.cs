// F10 Advanced Sensor Fusion (NV custom) — kinematic model of position and heading.
//
// Instead of filtering the GPS, the model predicts where the tractor CAN be from the steer
// angle and the speed, and then trusts the measurement only as far as it is physically
// possible. A jump the vehicle cannot make gets almost no weight; real movement passes with
// no delay at all, because it comes out of the prediction rather than the correction.
//
// Two things make this work, and both were found the hard way (features/10_.../faza2):
//
//  1. The gain GROWS with the error, it does not shrink. A gain that goes to zero for a large
//     error opens the loop and the model drifts away - measured 44 m in 50 s. With a growing
//     gain the loop is always closed and GPS always wins in the end.
//  2. Disturbance and real movement are separated by AMPLITUDE, not by time. Cabin sway is
//     physically bounded by the suspension (measured p99 +-6.6 cm) while drift grows without
//     limit. Time constants cannot separate them: the disturbances last a median of 1.9 s,
//     which is the same scale as any correction slow enough to be safe.
//
// Everything except the WAS/IMU slider is derived from vehicle geometry and from measurement,
// so there is nothing to tune in the field.
using System;

namespace AgOpenGPS
{
    public class CKinematicModel
    {
        // ---- the one user setting -------------------------------------------------------
        public bool isOn;
        public double imuShare = 0.25;      // [0..0.6] how much of the yaw rate comes from the IMU
        public double leff = 4.20;          // [m] effective wheelbase, includes slip

        // Test aid only: keeps the model active with autosteer off, in a turn, and before the
        // WAS zero has settled - so a data replay can drive it. NOT saved between sessions, so
        // it cannot be left on at a customer. The divergence watchdog stays live even here:
        // without it a drifting model would look like a working one.
        public bool forceOn;

        // ---- derived, not tuned ---------------------------------------------------------
        // position threshold = measured cabin sway (p99 +-6.6 cm) + 20 % margin
        public const double SwayP99 = 0.066;
        public const double Margin = 1.20;
        private const double PosE0 = SwayP99 * Margin;      // 0.079 m

        // Field-tunable (REQ 2026-09-17-2). The effective time constant of the correction is
        // posTau/gain, and the gain sits near posFloor most of the time because the innovation
        // is usually 2-6 cm. With 1.0/0.10 that is ~10 s of open-loop running - measured in the
        // field as a slow weave with a ~20 s period. Raise the floor and shorten the tau to fix it.
        public double posFloor = 0.30;
        public double posTau = 0.4;                         // [s]
        private const double HdgFloor0 = 1.5;               // [deg] floor at IMU quantisation

        // Heading correction had the same trap as the position one: floor 0.10 with tau 2.0 s
        // gives an effective time constant of 6.4 s at a 1 deg error. Measured in the field
        // (Logs_2): the model heading sat 1.04 deg away from AOG's permanently, which at
        // 1.91 m/s is 3.47 cm/s of lateral drift - and that is the weave.
        public double hdgFloor = 0.35;
        public double hdgTau = 0.8;                         // [s]
        private const double HdgAvgTau = 1.5;               // [s] smoothing of the f2f course


        private const double WasOut = 10.0;                 // [deg] above this: passive
        private const double WasIn = 8.0;                   // [deg] return below this
        private const double HoldIn = 1.0;                  // [s] and only after this long
        private const double RampSec = 3.0;                 // [s] handover ramp

        private const double VtgFull = 2.0 / 3.6;           // [m/s] above: VTG only
        private const double VtgNone = 0.5 / 3.6;           // [m/s] below: position only
        private const int RateN = 5;                        // IMU yaw rate: 5-point least squares

        private const double DivergeM = 0.25;               // [m] watchdog
        private const double DivergeSec = 2.0;

        // ---- state ----------------------------------------------------------------------
        private bool init;
        private double E, N, psi;                           // pivot axle position + heading
        public double biasDeg;                              // WAS zero, learned per session
        private double f2fSmooth;
        private readonly double[] hBuf = new double[RateN];
        private readonly double[] tBuf = new double[RateN];
        private int nBuf;
        private double act;                                 // 0 = passive, 1 = model drives
        private double belowSec, divergeSec;
        private double prevE, prevN;
        private bool prevPosOk;

        // ---- for the window and the log -------------------------------------------------
        public double activeFrac { get { return act; } }
        public double innovCm;                              // model - GPS, lateral [cm]
        public double rejectPct;                            // how much the gain rejected [%]
        public double modelYaw, imuYaw, usedYaw;            // [deg/s]
        public double kinLimit;                             // [deg/s] what geometry allows
        public double vtgShare;                             // 1 = VTG only, 0 = position only
        public double speedUsed;                            // [m/s]
        public double anchorDeg;            // model heading - AOG heading [deg]; poisons nothing
                                            // now, but tells us how clean the anchor is
        public string passiveReason = "";

        public void Reset()
        {
            init = false; nBuf = 0; act = 0; belowSec = 0; divergeSec = 0;
            biasDeg = 0; anchorDeg = 0; prevPosOk = false;
            innovCm = 0; rejectPct = 0; passiveReason = "";
        }

        /// <summary>Yaw rate from the IMU heading: slope of a line through the last 5 samples.
        /// Two points would give 0.408 deg/s of quantisation noise on a 0.56 deg/s signal - the
        /// IMU heading has 0.1 deg resolution. Five points: 0.091 deg/s, 0.2 s of lag.</summary>
        private double ImuRate(double headDeg, double t)
        {
            if (nBuf < RateN) nBuf++;
            for (int i = RateN - 1; i > 0; i--) { hBuf[i] = hBuf[i - 1]; tBuf[i] = tBuf[i - 1]; }
            hBuf[0] = headDeg; tBuf[0] = t;
            if (nBuf < 2) return 0;

            double mt = 0, mh = 0;
            for (int i = 0; i < nBuf; i++) { mt += tBuf[i]; mh += hBuf[i]; }
            mt /= nBuf; mh /= nBuf;
            double sxx = 0, sxy = 0;
            for (int i = 0; i < nBuf; i++)
            {
                double dt = tBuf[i] - mt;
                sxx += dt * dt; sxy += dt * (hBuf[i] - mh);
            }
            return sxx > 1e-9 ? sxy / sxx : 0;
        }

        /// <summary>0 for small errors, rises to 1 for large ones. e0 is the boundary between
        /// what the suspension can produce and what it cannot.</summary>
        private static double Progressive(double err, double e0, double floor)
        {
            double x = Math.Abs(err) / Math.Max(e0, 1e-6);
            double g = (x * x) / (1.0 + x * x);
            return floor + (1.0 - floor) * g;
        }

        private static double Wrap(double a)
        {
            while (a > Math.PI) a -= glm.twoPI;
            while (a < -Math.PI) a += glm.twoPI;
            return a;
        }

        /// <summary>
        /// One step, once per fix. Takes AOG's own chain as the passive solution and as the
        /// (independent) anchor for the WAS zero, and returns the model's position and heading.
        /// </summary>
        /// <param name="aogE">AOG pivot easting</param><param name="aogN">AOG pivot northing</param>
        /// <param name="aogPsi">AOG heading [rad] - the IMU/f2f fusion</param>
        /// <param name="wasDeg">actual steer angle</param>
        /// <param name="vtgMs">speed from VTG (Doppler) [m/s]</param>
        /// <param name="imuHeadDeg">raw IMU heading [deg], unwrapped by the caller</param>
        /// <param name="courseRad">f2f course [rad]</param>
        public void Update(double aogE, double aogN, double aogPsi, double wasDeg,
                           double vtgMs, double imuHeadDeg, double courseRad, bool courseOk,
                           bool steerOn, double t, double dt)
        {
            double rate = ImuRate(imuHeadDeg, t);

            if (!init)
            {
                E = aogE; N = aogN; psi = aogPsi; f2fSmooth = aogPsi;
                init = true; act = 0; prevE = aogE; prevN = aogN; prevPosOk = true;
                return;
            }

            // ---- speed: VTG leads above 2 km/h, position below (VTG shows up to 0.73 m/s of
            //      phantom speed on a standing tractor with a rocking cabin)
            double vPos = 0;
            if (prevPosOk && dt > 1e-3)
                vPos = Math.Sqrt((aogE - prevE) * (aogE - prevE) + (aogN - prevN) * (aogN - prevN)) / dt;
            prevE = aogE; prevN = aogN; prevPosOk = true;

            double vAbs = Math.Abs(vtgMs);
            vtgShare = vAbs >= VtgFull ? 1.0 : (vAbs <= VtgNone ? 0.0 : (vAbs - VtgNone) / (VtgFull - VtgNone));
            double v = vtgShare * vAbs + (1.0 - vtgShare) * vPos;
            speedUsed = v;

            // ---- passive when the model must not be trusted --------------------------------
            // WAS is reliable within +-10 deg; above that Ackermann geometry and the sensor
            // mounting bring errors, and the bicycle model stands or falls on WAS.
            double want;
            if (forceOn) { belowSec = HoldIn; want = 1; passiveReason = ""; }
            else if (!steerOn) { belowSec = 0; want = 0; passiveReason = "autosteer isključen"; }
            else if (Math.Abs(wasDeg) >= WasOut)
            {
                belowSec = 0; want = 0;
                passiveReason = "okret (WAS " + Math.Abs(wasDeg).ToString("N0") + "°)";
            }
            else if (Math.Abs(wasDeg) < WasIn)
            {
                belowSec += dt;
                want = belowSec >= HoldIn ? 1 : 0;
                if (want < 0.5) passiveReason = "čeka izlaz iz okreta";
            }
            else want = act > 0.5 ? 1 : 0;      // dead band between WasIn and WasOut


            double step = dt / Math.Max(RampSec, 1e-3);
            act = want > 0.5 ? Math.Min(1.0, act + step) : Math.Max(0.0, act - step);
            if (act >= 0.999) passiveReason = "";

            // ---- prediction: WAS and IMU lead the fast changes, up to about 1 s ------------
            double leff_ = Math.Max(0.5, leff);
            modelYaw = glm.toDegrees(v * Math.Tan(glm.toRadians(wasDeg - biasDeg)) / leff_);
            imuYaw = rate;
            double w = Math.Max(0, Math.Min(0.6, imuShare));
            usedYaw = (1.0 - w) * modelYaw + w * imuYaw;
            kinLimit = Math.Abs(modelYaw);

            psi += glm.toRadians(usedYaw) * dt;
            E += v * Math.Sin(psi) * dt;
            N += v * Math.Cos(psi) * dt;

            // ---- heading correction: f2f is absolutely referenced over the long run --------
            if (courseOk && v > 0.3)
            {
                f2fSmooth += (dt / (HdgAvgTau + dt)) * Wrap(courseRad - f2fSmooth);
                double errH = Wrap(f2fSmooth - psi);
                double e0 = Math.Max(Math.Abs(modelYaw) * hdgTau, HdgFloor0) * Margin;
                double gH = Progressive(glm.toDegrees(errH), e0, hdgFloor);
                psi += (dt / (Math.Max(hdgTau, 0.05) + dt)) * gH * errH;
            }
            if (psi < 0) psi += glm.twoPI; else if (psi >= glm.twoPI) psi -= glm.twoPI;

            // ---- WAS zero: REMOVED (REQ 2026-09-17-2) ---------------------------------------
            // It was learned from the difference against AOG's heading. Measured in the field:
            // that anchor is not biased (mean -0.21..+0.08 deg) but it is NOISY (std 0.54..1.63,
            // peaks to 8.8). The integrator chased that noise and invented a zero that ranged
            // -1.48..+0.88 deg across passes of the SAME tractor on the SAME day - it should have
            // been one value. A false zero of 1.5 deg makes 0.5 deg/s of false yaw, which is
            // several cm/s of lateral drift the position correction then has to fight.
            // Closed-loop simulation with a contaminated anchor: max XTE 27.6 cm with the
            // learning, 5.2 cm without. The zero stays at 0.
            anchorDeg = glm.toDegrees(Wrap(aogPsi - psi));

            // ---- position correction: gain GROWS with the error ---------------------------
            double ce = Math.Sin(psi), cn = Math.Cos(psi);
            double ie = aogE - E, inn = aogN - N;
            double along = ie * ce + inn * cn;
            double lat = ie * cn - inn * ce;
            innovCm = lat * 100.0;

            double g = Progressive(lat, PosE0, posFloor);
            rejectPct = (1.0 - g) * 100.0;
            double k = dt / (Math.Max(posTau, 0.05) + dt);
            E += k * (along * ce + g * lat * cn);
            N += k * (along * cn - g * lat * ce);

            // ---- watchdog: a lasting disagreement means the MODEL is wrong, not the GPS ----
            if (Math.Abs(lat) > DivergeM) divergeSec += dt; else divergeSec = 0;
            if (divergeSec > DivergeSec)
            {
                E = aogE; N = aogN; psi = aogPsi; divergeSec = 0; act = 0;
                passiveReason = "reset (razilaženje)";
            }

            // ---- passive: the model is not the source, but it is kept fed so that taking
            //      over is bumpless
            if (act < 1.0)
            {
                double b = 1.0 - act;
                E += b * (aogE - E); N += b * (aogN - N);
                psi += b * Wrap(aogPsi - psi);
                if (psi < 0) psi += glm.twoPI; else if (psi >= glm.twoPI) psi -= glm.twoPI;
            }
        }

        /// <summary>Blend of model and AOG by the ramp - what guidance should use.</summary>
        public void Output(double aogE, double aogN, double aogPsi,
                           out double outE, out double outN, out double outPsi)
        {
            if (!init || act <= 0.0) { outE = aogE; outN = aogN; outPsi = aogPsi; return; }
            outE = act * E + (1 - act) * aogE;
            outN = act * N + (1 - act) * aogN;
            outPsi = aogPsi + act * Wrap(psi - aogPsi);
            if (outPsi < 0) outPsi += glm.twoPI; else if (outPsi >= glm.twoPI) outPsi -= glm.twoPI;
        }
    }
}
