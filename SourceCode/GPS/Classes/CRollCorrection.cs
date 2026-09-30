// F10 Advanced Sensor Fusion (NV custom) — roll correction checked against the raw antenna
// (REQ 2026-09-30-1).
//
// AOG moves the antenna back by AntennaHeight * sin(roll) on every fix. That is right when the
// whole tractor tilts, but a roll value the antenna never follows - a false IMU excursion, or a
// cabin rocking about an axis high up on its suspension - goes straight into the position. In the
// field (21.09.2026) a +3.8 deg roll step with the raw antenna standing still made a 20 cm jump in
// one second, which the kinematic model passed on because it takes large innovations as real.
//
// The GPS antenna is the better witness: a real tilt MUST move it by H * d(sin roll). So every
// roll movement is judged against what the raw antenna did over the same movement:
//
//   dR = sin(roll) - sin(roll at the start of the movement)
//   dY = lateral move of the RAW antenna since the start, relative to the smoothed course
//   lever = (dY*dR + L*P) / (dR^2 + P)     P = (1 deg)^2, clamped 0..H
//   correction = correction_at_start + lever * dR
//
// A large movement decides its own lever; a small one leans on L, the lever learned from recent
// movements, because GPS noise is larger than the signal there. A movement ends when roll has
// been still for 0.5 s, when it turns back (one swing of the cabin) or after 2.5 s.
// Between movements - slow roll change, a side slope building up - the full height H is used,
// exactly as today: over seconds GPS cannot tell a roll shift from the tractor's own lateral
// motion, so there is nothing to check against.
// Always: the correction is pulled toward H * sin(roll) with the reaction time T. That is the one
// user setting - how long an UNCONFIRMED roll change is held back before it is believed anyway.
//
// Measured offline on the 21.09. log and on synthetic cases (scratch simulation, 30.09.):
//   false roll 3.5 deg / 2 s: 18.0 -> 5.7 cm (T 4 s) · real hole, side slope, rolling ground:
//   within ~1 cm of today · largest 1 s move of the model output 18.3 -> 12.3 cm (raw antenna
//   alone moves up to 13 cm in 1 s).
//
// Sign: the returned value is the antenna displacement to the RIGHT of the course [m], the same
// quantity as AntennaHeight * sin(roll); the caller applies it exactly like rollCorrectionDistance.
// The old and the new correction are both computed on every fix so the raw log always has both.
using System;
using System.Collections.Generic;

namespace AgOpenGPS
{
    public class CRollCorrection
    {
        // ---- the user settings ----------------------------------------------------------
        public bool isOn;                               // off = AOG's own H * sin(roll), unchanged
        public double reactionSec = 4.0;                // [s] T, 1..8

        // ---- fixed, from the simulation ------------------------------------------------
        private static readonly double EventStart = 0.15 * Math.PI / 180.0;   // |d sin roll| per fix
        private static readonly double TurnBack = 0.3 * Math.PI / 180.0;     // swing is over
        private static readonly double Prior = Math.Pow(1.0 * Math.PI / 180.0, 2);
        private const double QuietSec = 0.5;            // roll still this long = movement over
        private const double MaxEventSec = 2.5;
        private const double LeverMemorySec = 20.0;     // learned lever forgets over this
        private const double CourseTau = 1.5;           // [s] course smoothing for the lateral move
        private const double JumpReset = 1.0;           // [m] a larger raw step in one fix = restart

        // ---- state ----------------------------------------------------------------------
        private bool init;
        private double H;
        private double course;                          // smoothed course [rad]
        private double prevE, prevN, prevSr;
        private double y;                               // accumulated lateral move of raw antenna [m]
        private double c;                               // current correction [m]
        private double L, sumW, sumWL;                  // learned lever and its weights
        private bool inEvent;
        private double r0, y0, c0, peak, dur, quiet;
        private readonly Queue<double> rejectTimes = new Queue<double>();

        // ---- for the window and the log --------------------------------------------------
        public double corrOld;                          // [m] H * sin(roll), what AOG does today
        public double corrNew;                          // [m] this correction
        public double leverLearned;                     // [m] L
        public double leverUsed;                        // [m] lever applied this fix
        public bool eventActive;
        public int rejectedPerMin;                      // movements the antenna did not confirm

        public void Reset()
        {
            init = false; inEvent = false; eventActive = false;
            sumW = 0; sumWL = 0;
            rejectTimes.Clear(); rejectedPerMin = 0;
        }

        /// <summary>One fix. east/north = antenna position BEFORE the roll correction, rollDeg =
        /// ahrs.imuRoll, courseRad = the heading the caller corrects along, antennaHeight from the
        /// vehicle settings, t = running time [s]. Returns the displacement to remove [m]: the new
        /// correction when isOn, H * sin(roll) when off.</summary>
        public double Update(double east, double north, double rollDeg, double courseRad,
                             double antennaHeight, double dt, double t)
        {
            double sr = Math.Sin(rollDeg * Math.PI / 180.0);
            corrOld = antennaHeight * sr;

            if (antennaHeight <= 0.01)
            {
                Reset(); corrNew = corrOld; leverLearned = leverUsed = 0;
                return corrOld;
            }

            bool jumped = init && Math.Sqrt((east - prevE) * (east - prevE) + (north - prevN) * (north - prevN)) > JumpReset;
            if (!init || jumped || Math.Abs(antennaHeight - H) > 0.01)
            {
                H = antennaHeight;
                course = courseRad; prevE = east; prevN = north; prevSr = sr;
                y = 0; c = H * sr; L = H; sumW = 0; sumWL = 0;
                inEvent = false; init = true;
                corrNew = c; leverLearned = L; leverUsed = H; eventActive = false;
                return isOn ? corrNew : corrOld;
            }

            // lateral move of the raw antenna this fix, right-positive, against the smoothed course
            double dc = courseRad - course;
            while (dc > Math.PI) dc -= 2 * Math.PI;
            while (dc < -Math.PI) dc += 2 * Math.PI;
            course += dt / (CourseTau + dt) * dc;
            double dE = east - prevE, dN = north - prevN;
            prevE = east; prevN = north;
            double dy = dE * Math.Cos(course) - dN * Math.Sin(course);

            double dr = sr - prevSr;
            prevSr = sr;
            y += dy;

            double pull = dt / (Math.Max(reactionSec, 0.1) + dt);

            if (!inEvent && Math.Abs(dr) > EventStart) StartEvent(sr - dr, y - dy, c);

            double lu = L;
            if (inEvent)
            {
                double dR = sr - r0, dY = y - y0;
                lu = (dY * dR + L * Prior) / (dR * dR + Prior);
                if (lu < 0) lu = 0; else if (lu > H) lu = H;

                c0 += pull * (H * sr - c);                  // the slow pull acts on the base
                c = c0 + lu * dR;

                quiet = Math.Abs(dr) < EventStart ? quiet + dt : 0;
                dur += dt;
                if (Math.Abs(dR) > peak) peak = Math.Abs(dR);
                bool turned = Math.Abs(dR) < peak - TurnBack;

                if (quiet >= QuietSec || turned || dur > MaxEventSec)
                {
                    // remember this movement's lever; weight capped so one big event cannot own L
                    double w = Math.Min(dR * dR, Prior);
                    double lam = Math.Exp(-(QuietSec + 1.0) / LeverMemorySec);
                    sumW = lam * sumW + w; sumWL = lam * sumWL + w * lu;
                    if (sumW > 0.5 * Prior) L = sumWL / sumW;

                    // a movement of 1 deg or more that the antenna backed less than half of
                    if (dR * dR >= Prior && lu < 0.5 * H) rejectTimes.Enqueue(t);

                    inEvent = false;
                    if (turned) StartEvent(sr, y, c);       // the return swing starts right here
                }
            }
            else
            {
                // slow change: full height, as today
                c += H * dr + pull * (H * sr - c);
                lu = H;
            }

            while (rejectTimes.Count > 0 && t - rejectTimes.Peek() > 60.0) rejectTimes.Dequeue();
            rejectedPerMin = rejectTimes.Count;

            corrNew = c; leverLearned = L; leverUsed = lu; eventActive = inEvent;
            return isOn ? corrNew : corrOld;
        }

        private void StartEvent(double rStart, double yStart, double cStart)
        {
            inEvent = true; r0 = rStart; y0 = yStart; c0 = cStart;
            peak = 0; dur = 0; quiet = 0;
        }
    }
}
