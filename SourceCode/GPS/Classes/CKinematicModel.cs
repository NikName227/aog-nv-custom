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
//  3. The excitation is DELTA WAS, not absolute WAS (REQ 2026-09-17-3). The steer sensor zero
//     wandered -0.35..-2.81 deg between passes of the same tractor on the same day and drove the
//     XTE offset at -3.65 cm per degree. Only differences are used, so that zero drops out - and
//     with it the free L_eff parameter: the delta-WAS fit lands on 2.93 m against the 3.020 m
//     wheelbase in the vehicle settings.
//
// The user sets two numbers: how much of the model reaches POSITION and how much reaches HEADING.
// They are separate because the field says the optimum is not the same value - the model earns its
// keep on position, while on heading it only attenuates the error Stanley steers on.
using System;

namespace AgOpenGPS
{
    public class CKinematicModel
    {
        // ---- the user settings ----------------------------------------------------------
        public bool isOn;
        public double imuShare = 0.25;      // [0..0.6] LEGACY. The static WAS/IMU weight is gone:
                                            // with delta WAS the split is by FREQUENCY, set by
                                            // anchorTau. No longer read by Update() and no longer
                                            // on the window; the field and the setting stay until
                                            // the parameters are fixed.
        public double leff = 0.0;           // [m] 0 = take the wheelbase from vehicle settings.
                                            // Measured by delta-WAS regression on 3.7 km of field
                                            // passes: 2.93 m, against 3.020 m in the settings -
                                            // 3 %. Every earlier estimate (3.67/4.20/1.66/2.61)
                                            // came from ABSOLUTE WAS and was contaminated by the
                                            // unknown sensor zero. It is not a free parameter.

        // How much of the model reaches guidance - SEPARATELY for position and for heading,
        // because the field says the optimum is not the same number (REQ 2026-09-17-3):
        //   share 0,0  heading gain 1,00  WASset/XTE -0,187 deg/cm  sd XTE 10,10 cm
        //   share 0,5  heading gain 0,95  WASset/XTE -0,229 deg/cm  sd XTE  6,94 cm
        //   share 1,0  heading gain 0,58  WASset/XTE -0,128 deg/cm  sd XTE 10,66 cm
        // The model earns its keep on POSITION. On heading it only attenuates, which starves
        // Stanley of the error it steers on - that is the slow weave, measured as a 66 % loss of
        // WASset per degree of heading error.
        public double mixPos = 1.00;        // [0..1]
        public double mixHdg = 0.20;        // [0..0.6] capped: see anchorTau note below

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
        public double posFloor = 0.40;
        public double posTau = PresetTau;                   // [s]
        private const double HdgFloor0 = 1.5;               // [deg] floor at IMU quantisation

        // Sway compensation, as four steps instead of a loose number (REQ 2026-09-21-1). The step
        // is chosen from the state of the cabin suspension - the one thing about this the operator
        // can actually judge - and it moves posFloor ONLY. posTau stays 0.3 s on every step
        // because tau is what sets the response to LARGE innovations: there g -> 1, so
        // tau_ef -> tau, and a real GPS jump has to be followed just as fast whatever the cabin
        // is like. The floor is what decides how much small-amplitude cabin sway is ignored.
        //   preset       floor  tau_ef @ 2 cm  @ 6 cm  share of the 1.9 s disturbance  self error
        //   Weak         0.55      0.52 s      0.41 s            27 %                   0.12 cm
        //   Medium       0.40      0.69 s      0.48 s            36 %                   0.21 cm
        //   Strong       0.27      0.96 s      0.56 s            51 %                   0.41 cm
        //   ExtraStrong  0.14      1.56 s      0.66 s            82 %                   1.09 cm
        // So the steps differ 4x on the small disagreements, where cabin sway lives, and only
        // 1.6x on the large ones - the progressive gain keeps the safety end to itself.
        // Medium is bit-identical to what the field test of 21.09.2026 ran on.
        public enum SwayPreset { Weak = 0, Medium = 1, Strong = 2, ExtraStrong = 3 }
        private static readonly double[] PresetFloor = { 0.55, 0.40, 0.27, 0.14 };
        public const double PresetTau = 0.3;                // [s] same on every step, see above
        public SwayPreset preset = SwayPreset.Medium;

        // Heading correction had the same trap as the position one: floor 0.10 with tau 2.0 s
        // gives an effective time constant of 6.4 s at a 1 deg error. Measured in the field
        // (Logs_2): the model heading sat 1.04 deg away from AOG's permanently, which at
        // 1.91 m/s is 3.47 cm/s of lateral drift - and that is the weave.
        public double hdgFloor = 0.35;
        public double hdgTau = 0.8;                         // [s]
        private const double HdgAvgTau = 1.5;               // [s] smoothing of the f2f course


        // Raised from 10/8 (REQ 2026-09-18-1). 10 deg was a guess, never measured. Replaying the
        // gate logic over 3118 s of field data: at 10/8 the model is off full share 9.5 % of the
        // time across 77 exits, at 15/13 it is 6.9 % across 57. The cost is not the time above the
        // threshold (0.3 %) but that every exit spends 1 s hold + 3 s ramp - the model dropped out
        // whenever WAS merely brushed the gate in a manoeuvre. Quality does not fall with steer
        // angle either: kinematics-vs-truth correlation is 0.356 in the 0-4 deg band and 0.705 in
        // 4-8 deg, slope steady. The 10-15 band itself has only 74 samples, so it is NOT verified -
        // the divergence watchdog is what covers that.
        private const double WasOut = 15.0;                 // [deg] above this: passive
        private const double WasIn = 13.0;                  // [deg] return below this
        private const double HoldIn = 1.0;                  // [s] and only after this long
        private const double RampSec = 3.0;                 // [s] handover ramp

        private const double VtgFull = 2.0 / 3.6;           // [m/s] above: VTG only
        private const double VtgNone = 0.5 / 3.6;           // [m/s] below: position only
        private const int RateN = 5;                        // IMU yaw rate: 5-point least squares

        // Delta WAS drives the CHANGE of yaw rate, the IMU gives its absolute level. Measured on
        // 15548 windows of 0.5 s across 3.7 km of field passes:
        //   absolute WAS -> heading change     r = 0.581 (slope 0.190, should be 1.0)
        //   delta WAS    -> heading change     r = -0.175   (wrong quantity)
        //   delta WAS    -> yaw RATE change    r = 0.692    <- this one
        // Physically that is exactly right: WAS gives psi-dot, delta WAS gives psi-dot-dot. The
        // point of it is that only DIFFERENCES are used, so the WAS zero drops out of the model.
        // That zero wandered -0.35..-2.81 deg between passes of the same tractor on the same day
        // and drove the XTE offset at -3.65 cm per degree (r = -0.994).
        // The price: two integrations instead of one, so the error grows with the SQUARE of time.
        // anchorTau is what stops that - and it is why the heading share is capped below 1.
        // Replayed over 14787 samples of field data (28 straight segments): the anchor bounds the
        // drift of the double integration, and the bound is set by this tau -
        //   tau 1.0 s -> worst per-segment drift 0.083 deg/s
        //   tau 2.5 s -> 0.210 deg/s
        //   tau 6.0 s -> 0.509 deg/s
        // It has to stay several times the 0.5 s events it is meant to pass, or the anchor simply
        // cancels the delta WAS again. 2.5 s is that compromise, but it is the ONE number here
        // that is not measured, so it is field-adjustable until it is.
        public double anchorTau = 2.5;                      // [s] yaw state -> IMU yaw rate
        public const double MixHdgMax = 0.60;

        private const double DivergeM = 0.25;               // [m] watchdog
        private const double DivergeSec = 2.0;

        // ---- state ----------------------------------------------------------------------
        private bool init;
        private double E, N, psi;                           // pivot axle position + heading
        public double biasDeg;                              // WAS zero - NOT used any more, see
                                                            // anchorTau. Kept at 0 for the log.
        private double yawState;                            // [deg/s] delta-WAS integrator
        private double tanWasPrev;
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
        public double leffUsed;             // [m] what the model actually used this step
        public double dWasYaw;              // [deg/s] this step's delta-WAS contribution
        public double yawStateOut { get { return yawState; } }
        public string passiveReason = "";

        /// <summary>Sway compensation step -> posFloor. posTau is fixed, see the table above.</summary>
        public void ApplyPreset(SwayPreset p)
        {
            int i = (int)p;
            if (i < 0 || i >= PresetFloor.Length) i = (int)SwayPreset.Medium;
            preset = (SwayPreset)i;
            posFloor = PresetFloor[i];
            posTau = PresetTau;
        }

        /// <summary>Tunables back to the values this release ships with - used whenever the
        /// install is not in service mode, so behaviour cannot drift with a profile's history.
        /// The sway preset is the one thing carried over: it is a customer setting, not a tuning
        /// leftover, so it has to survive this call or the choice would be lost on every start.</summary>
        public void ReleaseDefaults()
        {
            ApplyPreset(preset);            // posFloor from the step, posTau fixed
            hdgFloor = 0.35; hdgTau = 0.8;
            leff = 0.0;                     // = wheelbase from the vehicle profile
            anchorTau = 2.5;
        }

        public void Reset()
        {
            init = false; nBuf = 0; act = 0; belowSec = 0; divergeSec = 0;
            biasDeg = 0; anchorDeg = 0; prevPosOk = false;
            yawState = 0; tanWasPrev = 0; dWasYaw = 0;
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
        /// <param name="wheelbaseM">wheelbase from vehicle settings, used when leff is 0</param>
        public void Update(double aogE, double aogN, double aogPsi, double wasDeg,
                           double vtgMs, double imuHeadDeg, double courseRad, bool courseOk,
                           bool steerOn, double t, double dt, double wheelbaseM)
        {
            double rate = ImuRate(imuHeadDeg, t);
            leffUsed = leff > 0.1 ? leff : Math.Max(0.5, wheelbaseM);

            if (!init)
            {
                E = aogE; N = aogN; psi = aogPsi; f2fSmooth = aogPsi;
                yawState = rate; tanWasPrev = Math.Tan(glm.toRadians(wasDeg)); dWasYaw = 0;
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


            // The ramp is pure activation now (autosteer, turns, handover). The shares are applied
            // in Output() only, so the estimator always runs at full quality and we choose how
            // much of it to use - in build0017 mix was a CEILING on this ramp, which also left the
            // model state permanently half-pulled toward AOG through the passive block below.
            double step = dt / Math.Max(RampSec, 1e-3);
            act = want > 0.5 ? Math.Min(1.0, act + step) : Math.Max(0.0, act - step);
            if (act >= 1.0 - 1e-6) passiveReason = "";

            // ---- prediction: delta WAS leads the fast changes, the IMU holds the level -------
            double tanW = Math.Tan(glm.toRadians(wasDeg));
            dWasYaw = glm.toDegrees(v * (tanW - tanWasPrev) / leffUsed);
            tanWasPrev = tanW;
            imuYaw = rate;

            yawState += dWasYaw;                                         // fast: delta WAS
            yawState += (dt / (Math.Max(anchorTau, 0.1) + dt)) * (imuYaw - yawState);  // slow: IMU
            usedYaw = yawState;

            // kept from the absolute WAS, but ONLY as the kinematic ceiling for the display and
            // for the heading gate below - it never drives the prediction any more, so its zero
            // error cannot reach the position.
            modelYaw = glm.toDegrees(v * tanW / leffUsed);
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
                yawState = imuYaw;                  // the yaw integrator is what drifted - reseed it
                passiveReason = "reset (razilaženje)";
            }

            // ---- passive: the model is not the source, but it is kept fed so that taking
            //      over is bumpless
            if (act < 1.0)
            {
                double b = 1.0 - act;
                E += b * (aogE - E); N += b * (aogN - N);
                psi += b * Wrap(aogPsi - psi);
                yawState += b * (imuYaw - yawState);
                if (psi < 0) psi += glm.twoPI; else if (psi >= glm.twoPI) psi -= glm.twoPI;
            }
        }

        /// <summary>What guidance should use: the ramp says whether the model may speak at all,
        /// the two shares say how much of it reaches position and heading separately.</summary>
        public void Output(double aogE, double aogN, double aogPsi,
                           out double outE, out double outN, out double outPsi)
        {
            if (!init || act <= 0.0) { outE = aogE; outN = aogN; outPsi = aogPsi; return; }
            double mp = act * Math.Max(0.0, Math.Min(1.0, mixPos));
            double mh = act * Math.Max(0.0, Math.Min(MixHdgMax, mixHdg));
            outE = mp * E + (1 - mp) * aogE;
            outN = mp * N + (1 - mp) * aogN;
            outPsi = aogPsi + mh * Wrap(psi - aogPsi);
            if (outPsi < 0) outPsi += glm.twoPI; else if (outPsi >= glm.twoPI) outPsi -= glm.twoPI;
        }
    }
}
