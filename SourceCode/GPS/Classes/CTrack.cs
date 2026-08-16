using AgOpenGPS.Core.Translations;
using OpenTK.Graphics.OpenGL;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace AgOpenGPS
{
    public enum TrackMode { None = 0, AB = 2, Curve = 4, bndTrackOuter = 8, bndTrackInner = 16, bndCurve = 32, waterPivot = 64 }

    /// <summary>NV custom F50: a side relative to a track's heading.</summary>
    public enum TrackSide { None = 0, Left = 1, Right = 2 };//, Heading, Circle, Spiral

    public class CTrack
    {
        //pointers to mainform controls
        private readonly FormGPS mf;

        public List<CTrk> gArr = new List<CTrk>();

        public int idx, autoTrack3SecTimer;

        public bool isAutoTrack = false, isAutoSnapToPivot = false, isAutoSnapped;

        /// <summary>
        /// NV custom F50 (t10): the driver picked the single "Orchard" entry in the track list
        /// instead of one line. While it is on, the orchard automatics run - the derived pass is
        /// driven as ONE fixed line (no width multiples), the nearest derived line is selected by
        /// itself, and AOG's side guide lines are off. Picking any hand-made line turns it off and
        /// gives fully stock behaviour, which is what the work AROUND the orchard needs.
        /// </summary>
        public bool isOrchardAuto;

        /// <summary>Orchard automatics are on AND there is something derived to drive.</summary>
        public bool IsOrchardAutoOn
        {
            get
            {
                if (!isOrchardAuto) return false;
                for (int i = 0; i < gArr.Count; i++) if (gArr[i].isOrchardDerived) return true;
                return false;
            }
        }

        /// <summary>Is the active track a derived orchard pass?</summary>
        public bool IsActiveOrchardPass
            => idx > -1 && idx < gArr.Count && gArr[idx].isOrchardDerived;

        // ── What the driver is shown (F50 t10, SIM 0730) ──────────────────────────
        // The orchard is ONE item everywhere in the UI, not one per inter-row: one row in the
        // track list, one step for the cycle buttons, and one line in the counter. So a field
        // with an orchard and no hand-made line reads 1/1, and 1/2 once a manual AB line is
        // added - never 1/24.

        /// <summary>Number of items the driver can pick: the orchard counts as one.</summary>
        public int DisplayCount
        {
            get
            {
                int n = 0;
                bool anyDerived = false;
                for (int i = 0; i < gArr.Count; i++)
                {
                    if (gArr[i].isOrchardDerived) anyDerived = true;
                    else n++;
                }
                return n + (anyDerived ? 1 : 0);
            }
        }

        /// <summary>1-based position of the active item in that same list; 0 = nothing active.</summary>
        public int DisplayIndex
        {
            get
            {
                if (idx < 0 || idx >= gArr.Count) return 0;

                bool anyDerived = false;
                for (int i = 0; i < gArr.Count; i++) if (gArr[i].isOrchardDerived) { anyDerived = true; break; }

                //the orchard sits first, as in the track list
                if (gArr[idx].isOrchardDerived) return 1;

                int n = anyDerived ? 1 : 0;
                for (int i = 0; i < gArr.Count; i++)
                {
                    if (gArr[i].isOrchardDerived) continue;
                    n++;
                    if (i == idx) return n;
                }
                return n;
            }
        }

        /// <summary>"1/2" for the counter label - the orchard as a single line. F50 2026-08-16:
        /// with a manual clearance offset set on the active hug pass, appends it (e.g. "1/2 +5cm")
        /// so the driver can see it without opening anything.</summary>
        public string DisplayCounterText
        {
            get
            {
                string s = DisplayIndex + "/" + DisplayCount;
                if (idx > -1 && idx < gArr.Count && gArr[idx].orchardRowSide != TrackSide.None
                    && mf.orchOffsets.TryGetValue(gArr[idx].name, out List<COrchard.OrchOffsetPoint> profile))
                {
                    double off = COrchard.CurrentOffset(profile, double.MaxValue);
                    if (Math.Abs(off) > 0.001)
                        s += " " + (off > 0 ? "+" : "") + (off * 100).ToString("N0") + "cm";
                }
                return s;
            }
        }

        /// <summary>Does this field have a derived orchard path at all?</summary>
        public bool HasOrchardDerived
        {
            get
            {
                for (int i = 0; i < gArr.Count; i++) if (gArr[i].isOrchardDerived) return true;
                return false;
            }
        }

        public CTrack(FormGPS _f)
        {
            //constructor
            mf = _f;
            idx = -1;
        }

        public int FindClosestRefTrack(vec3 pivot)
        {
            if (idx < 0 || gArr.Count == 0) return -1;

            //only 1 track
            if (gArr.Count == 1) return idx;

            int trak = -1;
            int cntr = 0;

            //Count visible
            for (int i = 0; i < gArr.Count; i++)
            {
                if (gArr[i].isVisible)
                {
                    cntr++;
                    trak = i;
                }
            }

            //only 1 track visible of the group
            if (cntr == 1) return trak;

            //no visible tracks
            if (cntr == 0) return -1;

            //determine if any aligned reasonably close
            bool[] isAlignedArr = new bool[gArr.Count];
            for (int i = 0; i < gArr.Count; i++)
            {
                if (gArr[i].mode == TrackMode.Curve) isAlignedArr[i] = true;
                else
                {
                    double diff = Math.PI - Math.Abs(Math.Abs(pivot.heading - gArr[i].heading) - Math.PI);
                    if (diff < 1 || diff > 2.14)
                        isAlignedArr[i] = true;
                    else
                        isAlignedArr[i] = false;
                }
            }

            double minDistA = double.MaxValue;
            double dist;

            vec2 endPtA, endPtB;

            for (int i = 0; i < gArr.Count; i++)
            {
                if (!isAlignedArr[i]) continue;
                if (!gArr[i].isVisible) continue;

                if (gArr[i].mode == TrackMode.AB)
                {
                    double abHeading = mf.trk.gArr[i].heading;

                    endPtA.easting = mf.trk.gArr[i].ptA.easting - (Math.Sin(abHeading) * 2000);
                    endPtA.northing = mf.trk.gArr[i].ptA.northing - (Math.Cos(abHeading) * 2000);

                    endPtB.easting = mf.trk.gArr[i].ptB.easting + (Math.Sin(abHeading) * 2000);
                    endPtB.northing = mf.trk.gArr[i].ptB.northing + (Math.Cos(abHeading) * 2000);

                    //x2-x1
                    double dx = endPtB.easting - endPtA.easting;
                    //z2-z1
                    double dy = endPtB.northing - endPtA.northing;

                    dist = ((dy * mf.steerAxlePos.easting) - (dx * mf.steerAxlePos.northing) + (endPtB.easting
                                            * endPtA.northing) - (endPtB.northing * endPtA.easting))
                                                / Math.Sqrt((dy * dy) + (dx * dx));

                    dist *= dist;

                    if (dist < minDistA)
                    {
                        minDistA = dist;
                        trak = i;
                    }
                }
                else
                {
                    for (int j = 0; j < gArr[i].curvePts.Count; j++)
                    {

                        dist = glm.DistanceSquared(gArr[i].curvePts[j], pivot);

                        if (dist < minDistA)
                        {
                            minDistA = dist;
                            trak = i;
                        }
                    }
                }
            }

            return trak;
        }

        //F50: orchard passes are FIXED reference lines (trees do not move), so nudging them
        //makes no sense. Since t10 the marker is the derived flag itself - the old check on
        //the "Row " name prefix stopped matching when the derived tracks were renamed to
        //"Orch ..." (name check kept as the fallback for tracks read from an older field file
        //before the first rebuild).
        public bool IsFixedTrack(int i)
        {
            return i > -1 && i < gArr.Count
                && (gArr[i].isOrchardDerived || COrchard.IsDerivedName(gArr[i].name));
        }

        public void NudgeTrack(double dist)
        {
            if (idx > -1)
            {
                if (IsFixedTrack(idx))
                {
                    mf.TimedMessageBox(1500, gStr.gsOrchard, gStr.gsFixedOrchardRowNoNudge);
                    return;
                }
                if (gArr[idx].mode == TrackMode.AB)
                {
                    mf.ABLine.isABValid = false;
                    gArr[idx].nudgeDistance += mf.ABLine.isHeadingSameWay ? dist : -dist;
                }
                else
                {
                    mf.curve.isCurveValid = false;
                    gArr[idx].nudgeDistance += mf.curve.isHeadingSameWay ? dist : -dist;

                }

                // Rebuild uturn after nudge to reflect new track position
                mf.yt.RebuildAfterNudge();

                //if (gArr[idx].nudgeDistance > 0.5 * mf.tool.width) gArr[idx].nudgeDistance -= mf.tool.width;
                //else if (gArr[idx].nudgeDistance < -0.5 * mf.tool.width) gArr[idx].nudgeDistance += mf.tool.width;
            }
        }

        public void NudgeDistanceReset()
        {
            if (idx > -1 && gArr.Count > 0)
            {
                if (gArr[idx].mode == TrackMode.AB)
                {
                    mf.ABLine.isABValid = false;
                }
                else
                {
                    mf.curve.isCurveValid = false;
                }

                gArr[idx].nudgeDistance = 0;

                // Rebuild uturn after reset to reflect new track position
                mf.yt.RebuildAfterNudge();
            }
        }

        public void SnapToPivot()
        {
            if (idx > -1)
            {
                NudgeTrack(gArr[idx].mode == TrackMode.AB ? mf.ABLine.distanceFromCurrentLinePivot : mf.curve.distanceFromCurrentLinePivot);
            }
        }

        public void NudgeRefTrack(double dist)
        {
            if (idx > -1)
            {
                if (IsFixedTrack(idx))
                {
                    mf.TimedMessageBox(1500, gStr.gsOrchard, gStr.gsFixedOrchardRowNoNudge);
                    return;
                }
                if (gArr[idx].mode == TrackMode.AB)
                {
                    mf.ABLine.isABValid = false;
                    NudgeRefABLine(mf.ABLine.isHeadingSameWay ? dist : -dist);
                }
                else
                {
                    mf.curve.isCurveValid = false;
                    NudgeRefCurve(mf.curve.isHeadingSameWay ? dist : -dist);
                }
            }
        }

        public void NudgeRefABLine(double dist)
        {
            double head = gArr[idx].heading;

            gArr[idx].ptA.easting += (Math.Sin(head + glm.PIBy2) * (dist));
            gArr[idx].ptA.northing += (Math.Cos(head + glm.PIBy2) * (dist));

            gArr[idx].ptB.easting += (Math.Sin(head + glm.PIBy2) * (dist));
            gArr[idx].ptB.northing += (Math.Cos(head + glm.PIBy2) * (dist));
        }

        public void NudgeRefCurve(double distAway)
        {
            mf.curve.isCurveValid = false;

            List<vec3> curList = new List<vec3>();

            double distSqAway = (distAway * distAway) - 0.01;
            vec3 point;

            for (int i = 0; i < gArr[idx].curvePts.Count; i++)
            {
                point = new vec3(
                gArr[idx].curvePts[i].easting + (Math.Sin(glm.PIBy2 + gArr[idx].curvePts[i].heading) * distAway),
                gArr[idx].curvePts[i].northing + (Math.Cos(glm.PIBy2 + gArr[idx].curvePts[i].heading) * distAway),
                gArr[idx].curvePts[i].heading);
                bool Add = true;

                for (int t = 0; t < gArr[idx].curvePts.Count; t++)
                {
                    double dist = ((point.easting - gArr[idx].curvePts[t].easting) * (point.easting - gArr[idx].curvePts[t].easting))
                        + ((point.northing - gArr[idx].curvePts[t].northing) * (point.northing - gArr[idx].curvePts[t].northing));
                    if (dist < distSqAway)
                    {
                        Add = false;
                        break;
                    }
                }

                if (Add)
                {
                    if (curList.Count > 0)
                    {
                        double dist = ((point.easting - curList[curList.Count - 1].easting) * (point.easting - curList[curList.Count - 1].easting))
                            + ((point.northing - curList[curList.Count - 1].northing) * (point.northing - curList[curList.Count - 1].northing));
                        if (dist > 1.0)
                            curList.Add(point);
                    }
                    else curList.Add(point);
                }
            }

            int cnt = curList.Count;
            if (cnt > 6)
            {
                // Set ptA and ptB from the shifted raw points before Catmull-Rom extension
                gArr[idx].ptA = new vec2(curList[0].easting, curList[0].northing);
                gArr[idx].ptB = new vec2(curList[cnt - 1].easting, curList[cnt - 1].northing);

                vec3[] arr = new vec3[cnt];
                curList.CopyTo(arr);

                curList.Clear();

                for (int i = 0; i < (arr.Length - 1); i++)
                {
                    arr[i].heading = Math.Atan2(arr[i + 1].easting - arr[i].easting, arr[i + 1].northing - arr[i].northing);
                    if (arr[i].heading < 0) arr[i].heading += glm.twoPI;
                    if (arr[i].heading >= glm.twoPI) arr[i].heading -= glm.twoPI;
                }

                arr[arr.Length - 1].heading = arr[arr.Length - 2].heading;

                //replace the array
                cnt = arr.Length;
                double distance;
                double spacing = 1.2;

                //add the first point of loop - it will be p1
                curList.Add(arr[0]);

                for (int i = 0; i < cnt - 3; i++)
                {
                    // add p2
                    curList.Add(arr[i + 1]);

                    distance = glm.Distance(arr[i + 1], arr[i + 2]);

                    if (distance > spacing)
                    {
                        int loopTimes = (int)(distance / spacing + 1);
                        for (int j = 1; j < loopTimes; j++)
                        {
                            vec3 pos = new vec3(glm.Catmull(j / (double)(loopTimes), arr[i], arr[i + 1], arr[i + 2], arr[i + 3]));
                            curList.Add(pos);
                        }
                    }
                }

                curList.Add(arr[cnt - 2]);
                curList.Add(arr[cnt - 1]);

                CABCurve.CalculateHeadings(ref curList);

                gArr[idx].curvePts.Clear();

                foreach (var item in curList)
                {
                    gArr[idx].curvePts.Add(new vec3(item));
                }

                //for (int i = 0; i < cnt; i++)
                //{
                //    arr[i].easting += Math.Cos(arr[i].heading) * (dist);
                //    arr[i].northing -= Math.Sin(arr[i].heading) * (dist);
                //    gArr[idx].curvePts.Add(arr[i]);
                //}
            }
        }
    }

    public class CTrk
    {
        public List<vec3> curvePts = new List<vec3>();
        public double heading;
        public string name;
        public bool isVisible;
        public vec2 ptA;
        public vec2 ptB;
        public vec2 endPtA;
        public vec2 endPtB;
        public TrackMode mode;
        public double nudgeDistance;
        public HashSet<int> workedTracks = new HashSet<int>();

        /// <summary>
        /// NV custom F50 (SIM 0730): which side of THIS track's own heading the tree line it
        /// belongs to lies on. Only the two passes that hug a row have one - the passes in the
        /// middle of an inter-row are None and can be driven either way. A side-mounted implement
        /// can only work a pass whose row is on the implement's side, and that flips when the pass
        /// is driven the other way round, so this is what the automatics filter on.
        /// </summary>
        public TrackSide orchardRowSide;

        /// <summary>
        /// NV custom F50 (2026-08-16): which lateral direction, in this track's OWN OffsetCurve
        /// convention (Math.Sin(heading + PIBy2) = right of heading), corresponds to MORE
        /// clearance from the row this pass hugs (signAB for the pass hugging row a, signBA for
        /// the one hugging row b - see COrchard.BuildRowPassTracks). 0 for orchardRowSide == None
        /// (no row to be closer to or further from). Lets the manual offset HUD turn a "+clearance"
        /// step into the correct geometric direction without recomputing signAB/signBA.
        /// </summary>
        public double orchardClearanceSign;

        /// <summary>
        /// NV custom F50 (2026-08-16): length of the straight guidance tail COrchard.ExtendEnds
        /// prepended to curvePts, in metres. The manual offset profile's breakpoints are measured
        /// from the real start of the row (before that tail), so the HUD subtracts this back out
        /// when it converts "distance along curvePts to the nearest point" into a breakpoint
        /// position.
        /// </summary>
        public double orchardFrontExtension;

        /// <summary>
        /// NV custom F50: this track was DERIVED from the orchard rows + the implement
        /// settings, not made by the user. Derived tracks are rebuilt on every change and
        /// are never written to TrackLines.txt, so they cannot pile up (SIM 0729 t5).
        /// </summary>
        public bool isOrchardDerived;

        public CTrk()
        {
            curvePts = new List<vec3>();
            heading = 3;
            name = "New Track";
            isVisible = true;
            ptA = new vec2();
            ptB = new vec2();
            endPtA = new vec2();
            endPtB = new vec2();
            mode = TrackMode.None;
            nudgeDistance = 0;
        }

        public CTrk(CTrk _trk)
        {
            curvePts = new List<vec3>(_trk.curvePts);
            heading = _trk.heading;
            name = _trk.name;
            isVisible = _trk.isVisible;
            ptA = _trk.ptA;
            ptB = _trk.ptB;
            endPtA = new vec2();
            endPtB = new vec2();
            mode = _trk.mode;
            nudgeDistance = _trk.nudgeDistance;
            isOrchardDerived = _trk.isOrchardDerived;
            orchardRowSide = _trk.orchardRowSide;
            orchardClearanceSign = _trk.orchardClearanceSign;
            orchardFrontExtension = _trk.orchardFrontExtension;
        }
    }
}
