using AgLibrary.Logging;
using AgOpenGPS.Forms;
using System;
using System.Collections.Generic;
using System.Windows.Forms;
using AgOpenGPS.Core.Translations;

namespace AgOpenGPS
{
    // Self learning of the U-turn side offset (REQ 2026-11-16-1).
    // After every clean U- or K-turn the implement's mean offset from the new row is measured over the
    // first metres on the row, and the side offset of that turn side is nudged against it - slowly,
    // inside a band around the value the driver typed or accepted. Nothing is shown while it works;
    // a question comes up only when the first turn is far off, or three turns in a row ask for more
    // than the band allows.
    public partial class FormGPS
    {
        private const double learnDistance = 10.0;     // m on the new row that are measured
        private const double learnLastLimit = 0.05;    // m: the step limit once learning has settled
        private const int learnAskAfter = 3;           // turns in a row wanting more than that -> question

        /// <summary>simulator is driving (no RTK flag to wait for)</summary>
        public bool IsSimRunning { get { return timerSim.Enabled; } }

        private bool lnTracking, lnValid, lnMeasuring, lnLeft;
        private double lnDist, lnSum; private int lnCount;
        private vec3 lnLastPos;
        //user 17.11.: the new row of the running turn (local frame of the turn: origin, lateral unit,
        //distance of the row along it) and a result that waits for the turn's end
        private double lnRowOx, lnRowOy, lnRowLx, lnRowLy, lnRowDist, lnResult;
        private bool lnDone;

        private sealed class LearnSide
        {
            public readonly List<double> hist = new List<double>();
            public bool firstDone, rejected;
            public int outCount, outSign;
        }
        private readonly LearnSide lnRight = new LearnSide(), lnLeftSide = new LearnSide();

        /// <summary>
        /// Learning schedule per turn side, by the number n of the clean turn for this tractor +
        /// implement (REQ 2026-11-16-1, user 16.11.): learn fast while nothing is known, then ever more
        /// slowly so one odd turn cannot upset a good value.
        ///  n = 1: the whole measured offset is taken, no limit, no question (a pulled implement may need 1 m)
        ///  n = 2..7: limit 20, 20, 15, 15, 10, 10 cm;  n >= 8: 5 cm
        ///  share of the measured offset: n = 1 100 %, n = 2..5 50 %, then 25 %
        /// </summary>
        private static void LearnSchedule(int n, out double gain, out double limit)
        {
            if (n <= 1) { gain = 1.0; limit = double.MaxValue; return; }
            gain = n <= 5 ? 0.5 : 0.25;
            if (n <= 3) limit = 0.20;
            else if (n <= 5) limit = 0.15;
            else if (n <= 7) limit = 0.10;
            else limit = learnLastLimit;
        }

        /// <summary>Learning starts again from the first turn (button "Reset learning", or another tractor).</summary>
        public void TurnLearnRestart()
        {
            var t = Properties.ToolSettings.Default;
            t.setTurn_U_learnCountR = 0;
            t.setTurn_U_learnCountL = 0;
            t.Save();
            TurnLearnReset();
            Log.EventWriter("Turn learning: restarted from the first turn");
        }

        /// <summary>The driver typed a new offset: the measurements so far are dropped, the turn count stays.</summary>
        public void TurnLearnReset()
        {
            foreach (var s in new[] { lnRight, lnLeftSide })
            {
                s.hist.Clear(); s.firstDone = false; s.rejected = false; s.outCount = 0; s.outSign = 0;
            }
            lnTracking = false; lnMeasuring = false; lnDone = false;
        }

        /// <summary>Once per position update.</summary>
        private void TurnLearnFix()
        {
            try
            {
                //REQ 2026-11-16-1 (user 16.11.): the K's entry is the U's, so a K teaches too - judged only
                //on its forward entry (exit and reverse may be driven by hand)
                bool uTurn = yt.isYouTurnTriggered && tnl.IsFeatureOn && !yt.isLineApproach
                    && (yt.activeTurnStyle == 0 || yt.activeTurnStyle == 1);

                if (uTurn)
                {
                    if (!lnTracking)
                    {
                        lnTracking = true; lnValid = true; lnLeft = yt.TriggerSideLeft;
                        lnMeasuring = false; lnDone = false;     // a new turn ends any measurement still running
                        //the new row of this turn (the next turn may be built before the measurement is over)
                        lnRowOx = yt.learnRowOx; lnRowOy = yt.learnRowOy;
                        lnRowLx = yt.learnRowLx; lnRowLy = yt.learnRowLy; lnRowDist = yt.learnRowDist;
                    }
                    bool judged = yt.activeTurnStyle == 0 || yt.kTurnStage == 0 || yt.kTurnStage == 3;
                    bool gpsOk = timerSim.Enabled || pn.fixQuality == 4;
                    //after a reverse by hand the K's entry may start off its path: there the curvature steering
                    //is only asked for from the way back onto the row on
                    bool onTail = yt.ytTailStart > 0 && yt.onA >= yt.ytTailStart;
                    bool curvNeeded = yt.activeTurnStyle == 0 || onTail;
                    if (judged && (!isBtnAutoSteerOn || !gpsOk || isReverse || (curvNeeded && !yt.turnCurvActive))) lnValid = false;

                    //user 17.11.: measured from the end of the entry arc on - that is where the implement is
                    //off the row; at the end of the path it has mostly come back already
                    bool lastPart = yt.activeTurnStyle == 0 || yt.kTurnStage == 3;
                    if (lnValid && lastPart && onTail && !lnMeasuring && !lnDone) LearnStart();
                    if (!lnValid) { lnMeasuring = false; lnDone = false; return; }
                    if (lnMeasuring) LearnSample();
                    return;
                }

                if (lnTracking)
                {
                    lnTracking = false;
                    //only a turn driven to the end of its path counts - not one cut short
                    if (lnValid && yt.lastEndAtPathEnd && !yt.isYouTurnTriggered)
                    {
                        if (lnDone) { lnDone = false; TurnLearnApply(lnLeft, lnResult); return; }
                        if (!lnMeasuring) LearnStart();     // no tail seen (should not happen): from here on
                    }
                    else { lnMeasuring = false; lnDone = false; return; }
                }

                if (!lnMeasuring) return;
                if (LearnSample() && lnDone) { lnDone = false; TurnLearnApply(lnLeft, lnResult); }
            }
            catch (Exception ex)
            {
                lnTracking = false; lnMeasuring = false;
                Log.EventWriter("Turn learning skipped: " + ex.Message);
            }
        }

        private void LearnStart()
        {
            lnMeasuring = true; lnDone = false; lnDist = 0; lnSum = 0; lnCount = 0; lnLastPos = pivotAxlePos;
        }

        /// <summary>One fix of the measurement; false = interrupted (this turn teaches nothing).
        /// lnDone + lnResult once the distance is covered.</summary>
        private bool LearnSample()
        {
            if (!isBtnAutoSteerOn || isReverse || Math.Abs(avgSpeed) < 1.0)
            {
                lnMeasuring = false; lnDone = false;
                return false;
            }
            //the implement against the new row itself, + = past it (away from the pass just left) -
            //the same on an AB line and a curve
            double beyond = ((toolPivotPos.easting - lnRowOx) * lnRowLx) + ((toolPivotPos.northing - lnRowOy) * lnRowLy) - lnRowDist;
            lnSum += beyond;
            lnCount++;
            lnDist += glm.Distance(lnLastPos, pivotAxlePos);
            lnLastPos = pivotAxlePos;
            if (lnDist >= learnDistance && lnCount > 5)
            {
                lnMeasuring = false; lnDone = true;
                lnResult = lnSum / lnCount;
            }
            return true;
        }

        /// <summary>beyond = how far past the new row the implement ran on average (m, negative = short of it)</summary>
        private void TurnLearnApply(bool left, double beyond)
        {
            var t = Properties.ToolSettings.Default;
            if (!t.setTurn_U_selfLearn) return;

            //what is learned belongs to this tractor + this implement (the offset is in the implement
            //profile): another tractor starts the fast learning again
            string veh = RegistrySettings.vehicleProfileName ?? "";
            if (t.setTurn_U_learnVehicle != veh)
            {
                t.setTurn_U_learnVehicle = veh;
                t.setTurn_U_learnCountR = 0;
                t.setTurn_U_learnCountL = 0;
                TurnLearnReset();
            }

            LearnSide s = left ? lnLeftSide : lnRight;
            int n = (left ? t.setTurn_U_learnCountL : t.setTurn_U_learnCountR) + 1;
            double cur = yt.UOffsetForSide(left);

            s.hist.Add(beyond);
            if (s.hist.Count > 3) s.hist.RemoveAt(0);
            var sorted = new List<double>(s.hist); sorted.Sort();
            double med = n <= 1 ? beyond : sorted[sorted.Count / 2];

            LearnSchedule(n, out double gain, out double limit);
            double step = -gain * med;
            if (step > limit) step = limit; else if (step < -limit) step = -limit;

            string side = left ? "left" : "right";
            Log.EventWriter("Turn learning (" + side + ", turn " + n + "): implement " + (beyond * 100).ToString("N0")
                + " cm past the row, median " + (med * 100).ToString("N0") + " cm, offset " + cur.ToString("N2")
                + " m -> " + (cur + step).ToString("N2") + " m");

            if (left) t.setTurn_U_learnCountL = n; else t.setTurn_U_learnCountR = n;
            TurnLearnStore(left, Math.Max(-1.0, Math.Min(10.0, cur + step)), cur + step);

            //settled (5 cm steps) and still three turns in a row wanting more than 5 cm the same way:
            //something changed - ask once for the full value instead of creeping towards it
            if (limit <= learnLastLimit + 1e-6 && Math.Abs(med) > learnLastLimit)
            {
                int sg = Math.Sign(med);
                s.outCount = sg == s.outSign ? s.outCount + 1 : 1;
                s.outSign = sg;
                if (s.outCount >= learnAskAfter && !s.rejected) TurnLearnAsk(left, s, med, cur + step, cur - med);
            }
            else s.outCount = 0;
        }

        private void TurnLearnAsk(bool left, LearnSide s, double beyond, double cur, double proposal)
        {
            proposal = Math.Max(-1.0, Math.Min(10.0, Math.Round(proposal, 2)));
            string msg = string.Format(gStr.gsLearnAskMsg,
                left ? gStr.gsLearnSideLeft : gStr.gsLearnSideRight,
                Math.Abs(beyond * 100).ToString("N0"),
                beyond > 0 ? gStr.gsLearnPast : gStr.gsLearnShort,
                cur.ToString("N2"), proposal.ToString("N2"));

            s.outCount = 0;
            if (FormDialog.ShowQuestion(gStr.gsLearnAskTitle, msg) == DialogResult.OK)
            {
                s.hist.Clear();
                TurnLearnStore(left, proposal, proposal);
                Log.EventWriter("Turn learning: new base offset accepted, " + proposal.ToString("N2") + " m");
            }
            else
            {
                s.rejected = true;      // quiet for the rest of this run
                Log.EventWriter("Turn learning: proposal declined");
            }
        }

        private void TurnLearnStore(bool left, double value, double baseVal)
        {
            var t = Properties.ToolSettings.Default;
            if (left)
            {
                t.setTurn_U_overshootLeft = value; t.setTurn_U_offsetBaseL = baseVal;
            }
            else
            {
                //the left side still follows the right one until it has a value of its own - pin it first
                if (t.setTurn_U_overshootLeft <= -900) t.setTurn_U_overshootLeft = t.setTurn_U_overshootDist;
                t.setTurn_U_overshootDist = value; t.setTurn_U_offsetBaseR = baseVal;
            }
            t.Save();
        }
    }
}
