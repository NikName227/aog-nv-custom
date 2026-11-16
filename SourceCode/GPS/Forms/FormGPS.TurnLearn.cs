using AgLibrary.Logging;
using AgOpenGPS.Forms;
using System;
using System.Collections.Generic;
using System.Windows.Forms;
using AgOpenGPS.Core.Translations;

namespace AgOpenGPS
{
    // Self learning of the U-turn side offset (REQ 2026-11-16-1).
    // After every clean U-turn the implement's mean offset from the new row is measured over the
    // first metres on the row, and the side offset of that turn side is nudged against it - slowly,
    // inside a band around the value the driver typed or accepted. Nothing is shown while it works;
    // a question comes up only when the first turn is far off, or three turns in a row ask for more
    // than the band allows.
    public partial class FormGPS
    {
        private const double learnDistance = 10.0;     // m on the new row that are measured
        private const double learnGain = 0.25;         // share of the measured offset taken per turn
        private const double learnMaxStep = 0.03;      // m per turn
        private const double learnBand = 0.15;         // m around the base value
        private const double learnFirstBig = 0.10;     // m: first turn this far off asks at once

        /// <summary>simulator is driving (no RTK flag to wait for)</summary>
        public bool IsSimRunning { get { return timerSim.Enabled; } }

        private bool lnTracking, lnValid, lnMeasuring, lnLeft;
        private double lnDist, lnSum; private int lnCount;
        private vec3 lnLastPos;

        private sealed class LearnSide
        {
            public readonly List<double> hist = new List<double>();
            public bool firstDone, rejected;
            public int outCount, outSign;
        }
        private readonly LearnSide lnRight = new LearnSide(), lnLeftSide = new LearnSide();

        /// <summary>The driver typed a new offset (or switched implement): learning starts over from it.</summary>
        public void TurnLearnReset()
        {
            foreach (var s in new[] { lnRight, lnLeftSide })
            {
                s.hist.Clear(); s.firstDone = false; s.rejected = false; s.outCount = 0; s.outSign = 0;
            }
            lnTracking = false; lnMeasuring = false;
        }

        /// <summary>Once per position update.</summary>
        private void TurnLearnFix()
        {
            try
            {
                bool uTurn = yt.isYouTurnTriggered && tnl.IsFeatureOn && yt.activeTurnStyle == 0 && !yt.isLineApproach;

                if (uTurn)
                {
                    if (!lnTracking) { lnTracking = true; lnValid = true; lnLeft = yt.TriggerSideLeft; }
                    lnMeasuring = false;        // a new turn ends any measurement still running
                    bool gpsOk = timerSim.Enabled || pn.fixQuality == 4;
                    if (!isBtnAutoSteerOn || !gpsOk || isReverse || !yt.turnCurvActive) lnValid = false;
                    return;
                }

                if (lnTracking)
                {
                    lnTracking = false;
                    //only a turn driven to the end of its path counts - not one cut short
                    if (lnValid && yt.lastEndAtPathEnd && !yt.isYouTurnTriggered)
                    {
                        lnMeasuring = true; lnDist = 0; lnSum = 0; lnCount = 0; lnLastPos = pivotAxlePos;
                    }
                }

                if (!lnMeasuring) return;

                if (!isBtnAutoSteerOn || isReverse || guidanceLineDistanceOff > 29000 || Math.Abs(avgSpeed) < 1.0)
                {
                    lnMeasuring = false;        // interrupted: this turn teaches nothing
                    return;
                }

                //offset of the implement from the line, + = to the right of travel
                double xte = guidanceLineDistanceOff * 0.001;
                if (trk.idx > -1 && trk.idx < trk.gArr.Count && trk.gArr[trk.idx].mode == TrackMode.AB)
                {
                    double hT = ABLine.abHeading + (ABLine.isHeadingSameWay ? 0 : Math.PI);
                    xte += (toolPivotPos.easting - pivotAxlePos.easting) * Math.Cos(hT)
                         - (toolPivotPos.northing - pivotAxlePos.northing) * Math.Sin(hT);
                }

                //after a right turn the pass just left lies to the right, so "beyond the new row" is left
                lnSum += lnLeft ? xte : -xte;
                lnCount++;
                lnDist += glm.Distance(lnLastPos, pivotAxlePos);
                lnLastPos = pivotAxlePos;

                if (lnDist >= learnDistance && lnCount > 5)
                {
                    lnMeasuring = false;
                    TurnLearnApply(lnLeft, lnSum / lnCount);
                }
            }
            catch (Exception ex)
            {
                lnTracking = false; lnMeasuring = false;
                Log.EventWriter("Turn learning skipped: " + ex.Message);
            }
        }

        /// <summary>beyond = how far past the new row the implement ran on average (m, negative = short of it)</summary>
        private void TurnLearnApply(bool left, double beyond)
        {
            var t = Properties.ToolSettings.Default;
            if (!t.setTurn_U_selfLearn) return;

            LearnSide s = left ? lnLeftSide : lnRight;
            double cur = yt.UOffsetForSide(left);
            double baseVal = left ? t.setTurn_U_offsetBaseL : t.setTurn_U_offsetBaseR;
            if (baseVal <= -900) baseVal = cur;

            s.hist.Add(beyond);
            if (s.hist.Count > 3) s.hist.RemoveAt(0);
            var sorted = new List<double>(s.hist); sorted.Sort();
            double med = sorted[sorted.Count / 2];

            string side = left ? "left" : "right";
            Log.EventWriter("Turn learning (" + side + "): implement " + (beyond * 100).ToString("N0")
                + " cm past the row, median " + (med * 100).ToString("N0") + " cm, offset " + cur.ToString("N2") + " m");

            //the very first clean turn, far off: say so at once
            if (!s.firstDone)
            {
                s.firstDone = true;
                if (Math.Abs(beyond) > learnFirstBig)
                {
                    TurnLearnAsk(left, s, beyond, cur, cur - beyond);
                    return;
                }
            }

            //full correction would leave the band: not applied, not shown - until it says so three times running
            if (Math.Abs((cur - med) - baseVal) > learnBand)
            {
                int sg = Math.Sign(med);
                s.outCount = sg == s.outSign ? s.outCount + 1 : 1;
                s.outSign = sg;
                if (s.outCount >= 3 && !s.rejected) TurnLearnAsk(left, s, med, cur, cur - med);
                return;
            }

            s.outCount = 0;
            double step = Math.Max(-learnMaxStep, Math.Min(learnMaxStep, -learnGain * med));
            double next = Math.Max(baseVal - learnBand, Math.Min(baseVal + learnBand, cur + step));
            TurnLearnStore(left, next, baseVal);
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
