using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;

namespace AgOpenGPS
{
    public enum SkipMode { Normal, Alternative, IgnoreWorkedTracks };

    public class CYouTurn
    {
        #region Fields
        //copy of the mainform address
        private readonly FormGPS mf;

        /// <summary>/// triggered right after youTurnTriggerPoint is set /// </summary>
        public bool isYouTurnTriggered, isGoingStraightThrough = false;

        /// <summary>  /// turning right or left?/// </summary>
        public bool isTurnLeft;
        private int semiCircleIndex = -1;

        /// <summary> /// Is the youturn button enabled? /// </summary>
        public bool isYouTurnBtnOn;

        public double youTurnRadius;

        public int rowSkipsWidth = 1, uTurnSmoothing;

        public bool previousBigSkip = true;
        public SkipMode skipMode = SkipMode.Normal;
        public int rowSkipsWidth2 = 3, turnSkips = 2;

        /// <summary>  /// distance from headland as offset where to start turn shape /// </summary>
        public int youTurnStartOffset;

        //guidance values
        public double distanceFromCurrentLine, uturnDistanceFromBoundary;

        private int A, B;
        private bool isHeadingSameWay = true;
        public bool isTurnCreationTooClose = false, isTurnCreationNotCrossingError = false, turnTooCloseTrigger = false;

        public vec2 goalPointYT = new vec2(0, 0);
        public vec2 radiusPointYT = new vec2(0, 0);
        public double steerAngleYT, rEastYT, rNorthYT, ppRadiusYT;

        //list of points for scaled and rotated YouTurn line, used for pattern, dubins, abcurve, abline
        public List<vec3> ytList = new List<vec3>();
        private List<vec3> ytList2 = new List<vec3>();

        //next curve or line to build out turn and point over
        public List<vec3> nextCurve = new List<vec3>();

        //if we continue on the same line or change to the next one after the uTurn
        public bool isOutSameCurve;

        public int uTurnStyle = 0;

        /// <summary> style resolved for the current turn (turn limit lines can override) </summary>
        public int activeTurnStyle = 0;

        //is UTurn pattern in or out of bounds
        public bool isOutOfBounds = false;

        // turn-after-stop (turn limit lines): set once the vehicle has stopped near the
        // armed turn; the maneuver only fires after it then starts moving forward again
        public bool afterStopStopped = false;

        // turn limit lines: true when the pivot has driven PAST the armed turn start, so the
        // wheels would cross the line — the driver must reverse. Drawn red instead of green.
        public bool turnOvershot = false;

        // turn limit lines: true when the pivot is inside the trigger window, so the turn is
        // the NEXT thing that will happen (even while stopped) — drawn orange so the driver
        // knows the AB line is no longer what gets followed next.
        public bool turnArmed = false;

        //sequence of operations of finding the next turn 0 to 3
        public int youTurnPhase;

        // Returns 1 if the lines intersect, otherwis
        public double iE = 0, iN = 0;

        // the list of possible bounds points
        public List<CClose> turnClosestList = new List<CClose>();

        //point at the farthest turn segment from pivotAxle
        public CClose closestTurnPt = new CClose();

        //where the in and out tangents cross for Albin curve
        public CClose inClosestTurnPt = new CClose();
        public CClose outClosestTurnPt = new CClose();
        public CClose startOfTurnPt = new CClose();

        //how far should the distance between points on the uTurn be
        private double pointSpacing;

        // Saved state before YouTurnTrigger, used to restore on mid-turn cancel
        private int savedCurvePathsAway, savedABLinePathsAway;
        private bool savedCurveHeadingSameWay, savedABLineHeadingSameWay;
        private bool savedIsTurnLeft;
        private int savedRowSkipsWidth, savedTurnSkips;
        private bool savedPreviousBigSkip;
        #endregion

        //constructor
        public CYouTurn(FormGPS _f)
        {
            mf = _f;

            uturnDistanceFromBoundary = Properties.Settings.Default.set_youTurnDistanceFromBoundary;

            //how far before or after boundary line should turn happen
            youTurnStartOffset = Properties.Settings.Default.set_youTurnExtensionLength;

            rowSkipsWidth = Properties.Settings.Default.set_youSkipWidth;
            Set_Alternate_skips();

            ytList.Capacity = 128;

            youTurnRadius = Properties.Settings.Default.set_youTurnRadius;

            uTurnStyle = Properties.Settings.Default.set_uTurnStyle;
            activeTurnStyle = uTurnStyle;

            uTurnSmoothing = (int)Properties.Settings.Default.setAS_uTurnSmoothing;
        }

        #region Turn limit lines (NV custom)

        /// <summary>
        /// A turn path point is allowed inside the regular turn area OR inside the extra
        /// corridor up to a turn limit line (so the maneuver may use the road).
        /// </summary>
        private bool IsPointAllowedForTurn(vec3 pt)
        {
            // Stock drivable turn area, OR the extra strip out to a drawn turn limit line
            // (bounded so the maneuver can use the road up to the line but never run far).
            if (mf.bnd.IsPointInsideTurnArea(pt) == 0) return true;
            return mf.tnl.IsInAllowedExtension(pt);
        }

        /// <summary>
        /// Maneuver for the next turn: stock style unless the limit lines feature is on;
        /// then K primary = always K, combined = U-turn only when it fits between the
        /// turn line and the limit line.
        /// </summary>
        private int ResolveTurnStyle(bool isAB)
        {
            if (!mf.tnl.IsFeatureOn) return uTurnStyle;
            if (mf.tnl.isKPrimary) return 1;
            //combined: clean U (wide turn, drives out and loops back) when the next pass is far
            //enough; a K-turn is only for tight spacing (a K at wide spacing bites into the field)
            return DoesUTurnFit(isAB) ? 0 : 1;
        }

        /// <summary>
        /// Depth of the U-turn bulb beyond its start/end points for the current widths
        /// and skips (measured on a generated Dubins path), + 1 m safety.
        /// </summary>
        public double GetUTurnDepth()
        {
            double turnOffset = Math.Abs((mf.tool.width - mf.tool.overlap) * rowSkipsWidth)
                + Math.Abs(mf.tool.offset * 2.0);

            //wide turn = quarter circles in/out, depth is the radius
            if (turnOffset >= youTurnRadius * 2.0) return youTurnRadius + 1.0;

            CDubins.turningRadius = youTurnRadius;
            CDubins dubPath = new CDubins();
            List<vec3> pts = dubPath.GenerateDubins(new vec3(0, 0, 0), new vec3(turnOffset, 0, Math.PI));

            double depth = youTurnRadius;
            for (int i = 0; i < pts.Count; i++)
                if (pts[i].northing > depth) depth = pts[i].northing;
            return depth + 1.0;
        }

        //combined mode: a clean U-turn (wide turn) works when the spacing to the next pass is
        //at least the turn diameter (2 * radius). Below that a plain U would loop back on
        //itself, so a K-turn is used instead. (isAB kept for signature symmetry, unused.)
        private bool DoesUTurnFit(bool isAB)
        {
            double turnOffset = Math.Abs((mf.tool.width - mf.tool.overlap) * rowSkipsWidth)
                + Math.Abs(mf.tool.offset * 2.0);
            return turnOffset >= UWideThreshold;
        }

        // Per-vehicle turn-shape radii (Params tab). A stored 0 means "inherit the general
        // turn radius". Only consulted while the turn limit lines feature is on, so stock
        // youturn geometry is left exactly as it was.
        private double KExitRadius
        {
            get { double v = Properties.Settings.Default.set_turnK_exitRadius; return v > 0.5 ? v : youTurnRadius; }
        }
        private double UOutRadius
        {
            get { double v = Properties.Settings.Default.set_turnU_exitRadius; return v > 0.5 ? v : youTurnRadius; }
        }
        private double UInRadius
        {
            get { double v = Properties.Settings.Default.set_turnU_entryRadius; return v > 0.5 ? v : youTurnRadius; }
        }

        // Spacing (turnOffset) at/above which a clean U-turn fits. Stock = 2x the general
        // radius; with the feature on it uses the actual U exit+entry radii, so a U with
        // e.g. 5 m + 4 m radii already fits at 9 m (not 16 m).
        private double UWideThreshold
        {
            get { return mf.tnl.IsFeatureOn ? (UOutRadius + UInRadius) : (youTurnRadius * 2.0); }
        }

        #endregion

        //find next not worked lane after the defined lanes to skip
        private int GetNextNotWorkedTrack(bool isTurnLeft, int rowSkipsWidth, bool isAB)
        {
            int goalLane;

            if (isAB)
            {

                if ((isTurnLeft && !mf.ABLine.isHeadingSameWay) || (!isTurnLeft && mf.ABLine.isHeadingSameWay))
                    goalLane = mf.ABLine.howManyPathsAway + rowSkipsWidth;
                else
                    goalLane = mf.ABLine.howManyPathsAway - rowSkipsWidth;

                while (mf.trk.gArr[mf.trk.idx].workedTracks.Contains(goalLane))
                {
                    rowSkipsWidth++;
                    if ((isTurnLeft && !mf.ABLine.isHeadingSameWay) || (!isTurnLeft && mf.ABLine.isHeadingSameWay))
                        goalLane++;
                    else
                        goalLane--;
                }
            }
            else
            {
                if ((isTurnLeft && !mf.curve.isHeadingSameWay) || (!isTurnLeft && mf.curve.isHeadingSameWay))
                    goalLane = mf.curve.howManyPathsAway + rowSkipsWidth;
                else
                    goalLane = mf.curve.howManyPathsAway - rowSkipsWidth;

                while (mf.trk.gArr[mf.trk.idx].workedTracks.Contains(goalLane))
                {
                    rowSkipsWidth++;
                    if ((isTurnLeft && !mf.curve.isHeadingSameWay) || (!isTurnLeft && mf.curve.isHeadingSameWay))
                        goalLane++;
                    else
                        goalLane--;
                }
            }


            return rowSkipsWidth;
        }

        //Finds the point where an AB Curve crosses the turn line
        public bool BuildCurveDubinsYouTurn()
        {
            //if mode is skip workedTracks -> mark current track as worked if sections were on, then find next
            if (skipMode == SkipMode.IgnoreWorkedTracks)
            {
                // Mark the current track as worked only if sections were actually on
                if (mf.trk.idx >= 0 && mf.trk.idx < mf.trk.gArr.Count)
                {
                    if (mf.autoBtnState == btnStates.Auto || mf.manualBtnState == btnStates.On)
                    {
                        mf.trk.gArr[mf.trk.idx].workedTracks.Add(mf.curve.howManyPathsAway);
                    }
                }

                rowSkipsWidth = GetNextNotWorkedTrack(isTurnLeft, Properties.Settings.Default.set_youSkipWidth, false);
            }

            //NV follow plan: the plan picks the next row - skip UI/modes are ignored
            //while ON. The plan only ever targets a pass that exists inside the field,
            //so when nothing is left to do on this track we must NOT fall through to the
            //stock skip (it extrapolates onto a lane outside the boundary). Abort the
            //turn instead - the vehicle just runs out at the boundary.
            if (mf.plan.IsRuntimeOn)
            {
                //manual-flex pause: the wedge end is driven by hand - no automatic
                //turns until the driver picks up the dashed resume pass
                if (mf.plan.isFlexPause)
                {
                    ResetCreatedYouTurn();
                    return false;
                }

                if (mf.plan.TryNextTarget(mf.curve.howManyPathsAway, out int planTargetC)
                    && mf.plan.IsTargetReachable(planTargetC, youTurnRadius))
                {
                    int delta = planTargetC - mf.curve.howManyPathsAway;
                    isTurnLeft = delta > 0 ? !mf.curve.isHeadingSameWay : mf.curve.isHeadingSameWay;
                    rowSkipsWidth = Math.Abs(delta);
                }
                else
                {
                    //no target left, or the wedge cut the next pass short of a turn
                    //anchor: hand the area to the driver when a manual resume pass
                    //exists, otherwise plain abort (vehicle runs out at the boundary)
                    mf.plan.TryEnterFlexPause();
                    ResetCreatedYouTurn();
                    return false;
                }
            }

            //TODO: is calculated many taimes after the priveous turn is complete
            //grab the vehicle widths and offsets
            double turnOffset = (mf.tool.width - mf.tool.overlap) * rowSkipsWidth + (isTurnLeft ? -mf.tool.offset * 2.0 : mf.tool.offset * 2.0);
            pointSpacing = youTurnRadius * 0.1;

            //turn limit lines: per-field maneuver override (K primary / combined)
            if (youTurnPhase == 0) activeTurnStyle = ResolveTurnStyle(false);

            if (activeTurnStyle == 0)
            {
                //Albin turn
                if (turnOffset > UWideThreshold)
                {
                    return CreateCurveWideTurn();
                }

                //Ohmega turn
                else
                {
                    return CreateCurveOmegaTurn();
                }
            }
            else if (activeTurnStyle == 1)
            {
                return (KStyleTurnCurve());
            }

            //prgramming error if you got here
            return false;
        }

        public bool BuildABLineDubinsYouTurn()
        {
            //if mode is skip workedTracks -> mark current track as worked if sections were on, then find next
            if (skipMode == SkipMode.IgnoreWorkedTracks)
            {
                // Mark the current track as worked only if sections were actually on
                if (mf.trk.idx >= 0 && mf.trk.idx < mf.trk.gArr.Count)
                {
                    if (mf.autoBtnState == btnStates.Auto || mf.manualBtnState == btnStates.On)
                    {
                        mf.trk.gArr[mf.trk.idx].workedTracks.Add(mf.ABLine.howManyPathsAway);
                    }
                }

                rowSkipsWidth = GetNextNotWorkedTrack(isTurnLeft, Properties.Settings.Default.set_youSkipWidth, true);
            }

            //NV follow plan: the plan picks the next row - skip UI/modes are ignored
            //while ON. The plan only ever targets a pass that exists inside the field,
            //so when nothing is left to do on this track we must NOT fall through to the
            //stock skip (it extrapolates onto a lane outside the boundary). Abort the
            //turn instead - the vehicle just runs out at the boundary.
            if (mf.plan.IsRuntimeOn)
            {
                //manual-flex pause: the wedge end is driven by hand - no automatic
                //turns until the driver picks up the dashed resume pass
                if (mf.plan.isFlexPause)
                {
                    ResetCreatedYouTurn();
                    return false;
                }

                if (mf.plan.TryNextTarget(mf.ABLine.howManyPathsAway, out int planTargetAB)
                    && mf.plan.IsTargetReachable(planTargetAB, youTurnRadius))
                {
                    int delta = planTargetAB - mf.ABLine.howManyPathsAway;
                    isTurnLeft = delta > 0 ? !mf.ABLine.isHeadingSameWay : mf.ABLine.isHeadingSameWay;
                    rowSkipsWidth = Math.Abs(delta);
                }
                else
                {
                    //no target left, or the wedge cut the next pass short of a turn
                    //anchor: hand the area to the driver when a manual resume pass
                    //exists, otherwise plain abort (vehicle runs out at the boundary)
                    mf.plan.TryEnterFlexPause();
                    ResetCreatedYouTurn();
                    return false;
                }
            }

            double turnOffset = (mf.tool.width - mf.tool.overlap) * rowSkipsWidth
                + (isTurnLeft ? -mf.tool.offset * 2.0 : mf.tool.offset * 2.0);

            pointSpacing = youTurnRadius * 0.1;

            //turn limit lines: per-field maneuver override (K primary / combined)
            if (youTurnPhase == 0) activeTurnStyle = ResolveTurnStyle(true);

            if (activeTurnStyle == 0)
            {

                //Wide turn
                if (turnOffset > UWideThreshold)
                {
                    return CreateABWideTurn();
                }
                //Small turn
                else
                {
                    return CreateABOmegaTurn();
                }
            }
            else if (activeTurnStyle == 1)
            {
                return KStyleTurnAB();
            }

            //prgramming error if you got here
            return false;
        }

        #region CreateTurn

        private bool CreateCurveOmegaTurn()
        {
            //keep from making turns constantly - wait 1.5 seconds
            if (mf.makeUTurnCounter < 4)
            {
                youTurnPhase = 0;
                return true;
            }

            if (mf.trk.idx < 0 || mf.trk.gArr.Count < mf.trk.idx) return true;
            CTrk track = mf.trk.gArr[mf.trk.idx];

            //grab the vehicle widths and offsets
            double turnOffset = (mf.tool.width - mf.tool.overlap) * rowSkipsWidth + (isTurnLeft ? -mf.tool.offset * 2.0 : mf.tool.offset * 2.0);

            switch (youTurnPhase)
            {
                case 0: //find the crossing points
                    if (!FindCurveTurnPoint(mf.curve, false))
                    {
                        if (track.mode == TrackMode.waterPivot || track.mode == TrackMode.bndCurve)
                        {
                            youTurnPhase = 11;//ignore
                        }
                        else
                            FailCreate();
                        return false;
                    }

                    //save a copy 
                    inClosestTurnPt = new CClose(closestTurnPt);

                    ytList?.Clear();

                    int count = mf.curve.isHeadingSameWay ? -1 : 1;
                    int curveIndex = inClosestTurnPt.curveIndex;

                    isOutOfBounds = true;
                    int stopIfWayOut = 0;

                    double head = 0; //gives error since isnt updated

                    while (isOutOfBounds)
                    {
                        stopIfWayOut++;
                        isOutOfBounds = false;

                        //creates half a circle starting at the crossing point
                        ytList.Clear();

                        curveIndex += count;

                        vec3 currentPos = new vec3(mf.curve.curList[curveIndex]);

                        if (!mf.curve.isHeadingSameWay) currentPos.heading += Math.PI;
                        if (currentPos.heading >= glm.twoPI) currentPos.heading -= glm.twoPI;
                        head = currentPos.heading;

                        CDubins dubYouTurnPath = new CDubins();
                        CDubins.turningRadius = youTurnRadius;

                        //now we go the other way to turn round
                        double invertHead = currentPos.heading - Math.PI;
                        if (invertHead <= -Math.PI) invertHead += glm.twoPI;
                        if (invertHead >= Math.PI) invertHead -= glm.twoPI;

                        vec3 goal = new vec3();

                        //neat trick to not have to add pi/2
                        if (isTurnLeft)
                        {
                            goal.easting = mf.curve.curList[curveIndex - count].easting + (Math.Cos(-invertHead) * turnOffset);
                            goal.northing = mf.curve.curList[curveIndex - count].northing + (Math.Sin(-invertHead) * turnOffset);
                        }
                        else
                        {
                            goal.easting = mf.curve.curList[curveIndex - count].easting - (Math.Cos(-invertHead) * turnOffset);
                            goal.northing = mf.curve.curList[curveIndex - count].northing - (Math.Sin(-invertHead) * turnOffset);
                        }

                        goal.heading = invertHead;

                        //generate the turn points
                        ytList = dubYouTurnPath.GenerateDubins(currentPos, goal);
                        if (ytList.Count == 0)
                        {
                            FailCreate();
                            return false;
                        }

                        if (stopIfWayOut == 300 || curveIndex < 1 || curveIndex > (mf.curve.curList.Count - 2))
                        {
                            //for some reason it doesn't go inside boundary
                            FailCreate();
                            return false;
                        }

                        for (int i = 0; i < ytList.Count; i++)
                        {
                            if (!IsPointAllowedForTurn(ytList[i]))
                            {
                                isOutOfBounds = true;
                                break;
                            }
                        }
                    }
                    inClosestTurnPt.curveIndex = curveIndex;

                    //too many points from Dubins - so cut
                    double distance;
                    int cnt = ytList.Count;
                    for (int i = 1; i < cnt - 2; i++)
                    {
                        distance = glm.DistanceSquared(ytList[i], ytList[i + 1]);
                        if (distance < pointSpacing)
                        {
                            ytList.RemoveAt(i + 1);
                            i--;
                            cnt = ytList.Count;
                        }
                    }

                    //move the turn to exact at the turnline
                    ytList = MoveTurnInsideTurnLine(ytList, head, false, false);
                    if (ytList.Count == 0)
                    {
                        FailCreate();
                        return false;
                    }

                    youTurnPhase = 1;
                    break;

                case 1:
                    //build the next line to add sequencelines
                    double widthMinusOverlap = mf.tool.width - mf.tool.overlap;

                    double distAway = widthMinusOverlap * (mf.curve.howManyPathsAway + ((isTurnLeft ^ mf.curve.isHeadingSameWay) ? rowSkipsWidth : -rowSkipsWidth)) + (mf.curve.isHeadingSameWay ? mf.tool.offset : -mf.tool.offset) + track.nudgeDistance;

                    distAway += (0.5 * widthMinusOverlap);

                    //create the next line
                    nextCurve = mf.curve.BuildNewOffsetList(distAway, track);

                    //get the index of the last yt point
                    double dis = double.MaxValue;
                    if (nextCurve.Count > 1)
                    {
                        for (int i = 1; i < nextCurve.Count; i++)
                        {
                            double newdis = glm.Distance(nextCurve[i], ytList[ytList.Count - 1]);
                            if (newdis < dis)
                            {
                                dis = newdis;
                                if (mf.curve.isHeadingSameWay) outClosestTurnPt.curveIndex = i - 1;
                                else outClosestTurnPt.curveIndex = i;
                            }
                        }

                        if (outClosestTurnPt.curveIndex >= 0)
                        {
                            outClosestTurnPt.closePt = new vec3(nextCurve[outClosestTurnPt.curveIndex]);
                            inClosestTurnPt.closePt = new vec3(mf.curve.curList[inClosestTurnPt.curveIndex]);

                            if (!AddCurveSequenceLines()) return false;
                        }
                    }

                    //fill in the gaps
                    double distanc;

                    int cnt4 = ytList.Count;
                    for (int i = 1; i < cnt4 - 2; i++)
                    {
                        int j = i + 1;
                        if (j == cnt4 - 1) continue;
                        distanc = glm.DistanceSquared(ytList[i], ytList[j]);
                        if (distanc > 1)
                        {
                            vec3 pointB = new vec3((ytList[i].easting + ytList[j].easting) / 2.0,
                                (ytList[i].northing + ytList[j].northing) / 2.0, ytList[i].heading);

                            ytList.Insert(j, pointB);
                            cnt4 = ytList.Count;
                            i--;
                        }
                    }

                    //calculate the new points headings based on fore and aft of point - smoother turns
                    cnt4 = ytList.Count;
                    vec3[] arr = new vec3[cnt4];
                    cnt4 -= 2;
                    ytList.CopyTo(arr);
                    ytList.Clear();

                    for (int i = 2; i < cnt4; i++)
                    {
                        vec3 pt3 = new vec3(arr[i]);
                        pt3.heading = Math.Atan2(arr[i + 1].easting - arr[i - 1].easting,
                            arr[i + 1].northing - arr[i - 1].northing);
                        if (pt3.heading < 0) pt3.heading += glm.twoPI;
                        ytList.Add(pt3);
                    }

                    //check to close
                    if (glm.Distance(ytList[0], mf.pivotAxlePos) < 3)
                    {
                        FailCreate();
                        return false;
                    }

                    isOutOfBounds = false;
                    youTurnPhase = 10;
                    turnTooCloseTrigger = false;
                    isTurnCreationTooClose = false;
                    return true;
            }
            return true;
        }

        private bool CreateCurveWideTurn()
        {
            //keep from making turns constantly - wait 1.5 seconds
            if (mf.makeUTurnCounter < 4)
            {
                youTurnPhase = 0;
                return true;
            }

            if (mf.trk.idx < 0 || mf.trk.gArr.Count < mf.trk.idx) return true;
            CTrk track = mf.trk.gArr[mf.trk.idx];

            //we are doing a wide turn
            double head = 0;
            int count = mf.curve.isHeadingSameWay ? -1 : 1;
            switch (youTurnPhase)
            {
                case 0:
                    //Create first semicircle
                    if (!FindCurveTurnPoint(mf.curve, false))
                    {
                        if (track.mode == TrackMode.waterPivot || track.mode == TrackMode.bndCurve)
                        {
                            youTurnPhase = 11;//ignore
                        }
                        else
                            FailCreate();
                        return false;
                    }
                    inClosestTurnPt = new CClose(closestTurnPt);
                    startOfTurnPt = new CClose(inClosestTurnPt);

                    //NV rigid U (sandbox port): fixed construction replaces the phased build
                    if (mf.tnl.IsFeatureOn)
                    {
                        int rci = inClosestTurnPt.curveIndex;
                        if (rci < 0) rci = 0;
                        if (rci >= mf.curve.curList.Count) rci = mf.curve.curList.Count - 1;
                        vec3 rPos = new vec3(mf.curve.curList[rci]);
                        double rHead = rPos.heading;
                        if (!mf.curve.isHeadingSameWay) rHead += Math.PI;
                        if (rHead >= glm.twoPI) rHead -= glm.twoPI;
                        double rigidOffset = (mf.tool.width - mf.tool.overlap) * rowSkipsWidth
                            + (isTurnLeft ? -mf.tool.offset * 2.0 : mf.tool.offset * 2.0);
                        return BuildRigidUTurn(rPos, rHead, rigidOffset);
                    }

                    int stopIfWayOut = 0;
                    isOutOfBounds = true;

                    while (isOutOfBounds)
                    {
                        isOutOfBounds = false;
                        stopIfWayOut++;

                        vec3 currentPos = new vec3(mf.curve.curList[inClosestTurnPt.curveIndex]);

                        head = currentPos.heading;
                        if (!mf.curve.isHeadingSameWay) head += Math.PI;
                        if (head > glm.twoPI) head -= glm.twoPI;
                        currentPos.heading = head;

                        // creates half a circle starting at the crossing point
                        ytList.Clear();
                        ytList.Add(currentPos);

                        //Taken from Dubbins
                        while (Math.Abs(head - currentPos.heading) < Math.PI)
                        {
                            //Update the position of the car
                            currentPos.easting += pointSpacing * Math.Sin(currentPos.heading);
                            currentPos.northing += pointSpacing * Math.Cos(currentPos.heading);

                            //Which way are we turning?
                            double turnParameter = isTurnLeft ? -1.0 : 1.0;

                            //Update the heading (U exit radius when turn limit lines on)
                            currentPos.heading += (pointSpacing / (mf.tnl.IsFeatureOn ? UOutRadius : youTurnRadius)) * turnParameter;

                            //Add the new coordinate to the path
                            ytList.Add(currentPos);
                        }

                        int cnt4 = ytList.Count;
                        if (cnt4 == 0)
                        {
                            FailCreate();
                            return false;
                        }

                        //Are we out of bounds?
                        for (int j = 0; j < cnt4; j += 2)
                        {
                            if (!IsPointAllowedForTurn(ytList[j]))
                            {
                                isOutOfBounds = true;
                                break;
                            }
                        }

                        //first check if not out of bounds, add a bit more to clear turn line, set to phase 2
                        if (!isOutOfBounds)
                        {
                            ytList = MoveTurnInsideTurnLine(ytList, head, true, false);
                            if (ytList.Count == 0)
                            {
                                FailCreate();
                                return false;
                            }
                            youTurnPhase = 1;
                            return true;
                        }

                        if (stopIfWayOut == 300 || inClosestTurnPt.curveIndex < 1 || inClosestTurnPt.curveIndex > (mf.curve.curList.Count - 2))
                        {
                            //for some reason it doesn't go inside boundary
                            FailCreate();
                            return false;
                        }

                        //keep moving infield till pattern is all inside
                        inClosestTurnPt.curveIndex = inClosestTurnPt.curveIndex + count;
                        inClosestTurnPt.closePt = new vec3(mf.curve.curList[inClosestTurnPt.curveIndex]);


                        //set the flag to Critical stop machine
                        if (glm.Distance(ytList[0], mf.pivotAxlePos) < 3)
                        {
                            FailCreate();
                            return false;
                        }
                    }

                    return false;

                case 1:
                    //build the next line to add sequencelines
                    double widthMinusOverlap = mf.tool.width - mf.tool.overlap;

                    double distAway = widthMinusOverlap * (mf.curve.howManyPathsAway + ((isTurnLeft ^ mf.curve.isHeadingSameWay) ? rowSkipsWidth : -rowSkipsWidth)) + (mf.curve.isHeadingSameWay ? mf.tool.offset : -mf.tool.offset) + track.nudgeDistance;

                    distAway += (0.5 * widthMinusOverlap);

                    //create the next line
                    nextCurve = mf.curve.BuildNewOffsetList(distAway, track);

                    //going with or against boundary?
                    bool isTurnLineSameWay = true;
                    double headingDifference = Math.Abs(inClosestTurnPt.turnLineHeading - ytList[ytList.Count - 1].heading);
                    if (headingDifference > glm.PIBy2 && headingDifference < 3 * glm.PIBy2) isTurnLineSameWay = false;

                    if (!FindCurveOutTurnPoint(mf.curve, ref nextCurve, startOfTurnPt, isTurnLineSameWay))
                    {
                        //error
                        FailCreate();
                        return false;
                    }
                    outClosestTurnPt = new CClose(closestTurnPt);

                    //move the turn inside of turnline with help from the crossingCurvePoints
                    isOutOfBounds = true;
                    while (isOutOfBounds)
                    {
                        isOutOfBounds = false;
                        vec3 currentPos = new vec3(nextCurve[outClosestTurnPt.curveIndex]);

                        head = currentPos.heading;
                        if ((!mf.curve.isHeadingSameWay && !isOutSameCurve) || (mf.curve.isHeadingSameWay && isOutSameCurve)) head += Math.PI;
                        if (head > glm.twoPI) head -= glm.twoPI;
                        currentPos.heading = head;

                        // creates half a circle starting at the crossing point
                        ytList2?.Clear();
                        ytList2.Add(currentPos);

                        //Taken from Dubbins
                        while (Math.Abs(head - currentPos.heading) < Math.PI)
                        {
                            //Update the position of the car
                            currentPos.easting += pointSpacing * Math.Sin(currentPos.heading);
                            currentPos.northing += pointSpacing * Math.Cos(currentPos.heading);

                            //Which way are we turning?
                            double turnParameter = isTurnLeft ? 1.0 : -1.0;

                            //Update the heading (U entry radius when turn limit lines on)
                            currentPos.heading += (pointSpacing / (mf.tnl.IsFeatureOn ? UInRadius : youTurnRadius)) * turnParameter;

                            //Add the new coordinate to the path
                            ytList2.Add(currentPos);
                        }

                        int cnt3 = ytList2.Count;
                        if (cnt3 == 0)
                        {
                            FailCreate();
                            return false;
                        }

                        //Are we out of bounds?
                        for (int j = 0; j < cnt3; j += 2)
                        {
                            if (!IsPointAllowedForTurn(ytList2[j]))
                            {
                                isOutOfBounds = true;
                                break;
                            }
                        }

                        //first check if not out of bounds, add a bit more to clear turn line, set to phase 2
                        if (!isOutOfBounds)
                        {
                            ytList2 = MoveTurnInsideTurnLine(ytList2, head, true, true);
                            if (ytList2.Count == 0)
                            {
                                FailCreate();
                                return false;
                            }
                            youTurnPhase = 2;
                            return true;
                        }

                        if (outClosestTurnPt.curveIndex < 1 || outClosestTurnPt.curveIndex > (nextCurve.Count - 2))
                        {
                            //for some reason it doesn't go inside boundary
                            FailCreate();
                            return false;
                        }

                        //keep moving infield till pattern is all inside
                        if (!isOutSameCurve) outClosestTurnPt.curveIndex = outClosestTurnPt.curveIndex + count;
                        else outClosestTurnPt.curveIndex = outClosestTurnPt.curveIndex - count;
                        outClosestTurnPt.closePt = new vec3(nextCurve[outClosestTurnPt.curveIndex]);

                    }
                    return false;

                case 2:
                    //Bind the two turns together
                    int cnt1 = ytList.Count;
                    int cnt2 = ytList2.Count;

                    //Validate that both lists have elements to prevent crashes
                    if (cnt1 == 0 || cnt2 == 0)
                    {
                        FailCreate();
                        return false;
                    }

                    //Find if the turn goes same way as turnline heading
                    bool isFirstTurnLineSameWay = true;
                    double firstHeadingDifference = Math.Abs(inClosestTurnPt.turnLineHeading - ytList[ytList.Count - 1].heading);
                    if (firstHeadingDifference > glm.PIBy2 && firstHeadingDifference < 3 * glm.PIBy2) isFirstTurnLineSameWay = false;

                    //finds out start and goal point along the tunline
                    FindInnerTurnPoints(ytList[cnt1 - 1], ytList[0].heading, inClosestTurnPt, isFirstTurnLineSameWay);
                    CClose startClosestTurnPt = new CClose(closestTurnPt);

                    FindInnerTurnPoints(ytList2[cnt2 - 1], ytList2[0].heading + Math.PI, outClosestTurnPt, !isFirstTurnLineSameWay);
                    CClose goalClosestTurnPt = new CClose(closestTurnPt);

                    //we have 2 different turnLine crossings
                    if (startClosestTurnPt.turnLineNum != goalClosestTurnPt.turnLineNum)
                    {
                        FailCreate();
                        return false;
                    }

                    //segment index is the "A" of the segment. segmentIndex+1 would be the "B"
                    //is in and out on same segment? so only 1 segment
                    if (startClosestTurnPt.turnLineIndex == goalClosestTurnPt.turnLineIndex)
                    {
                        for (int a = 0; a < cnt2; a++)
                        {
                            ytList.Add(new vec3(ytList2[cnt2 - 1 - a]));
                        }
                    }
                    else
                    {
                        //mulitple segments
                        vec3 tPoint = new vec3();
                        int turnCount = mf.bnd.bndList[startClosestTurnPt.turnLineNum].turnLine.Count;

                        //how many points from turnline do we add
                        int loops = Math.Abs(startClosestTurnPt.turnLineIndex - goalClosestTurnPt.turnLineIndex);

                        //are we crossing a border?
                        if (loops > (mf.bnd.bndList[startClosestTurnPt.turnLineNum].turnLine.Count / 2))
                        {
                            if (startClosestTurnPt.turnLineIndex < goalClosestTurnPt.turnLineIndex)
                            {
                                loops = (turnCount - goalClosestTurnPt.turnLineIndex) + startClosestTurnPt.turnLineIndex;
                            }
                            else
                            {
                                loops = (turnCount - startClosestTurnPt.turnLineIndex) + goalClosestTurnPt.turnLineIndex;
                            }
                        }

                        //countExit up - start with B which is next A
                        if (isFirstTurnLineSameWay)
                        {
                            for (int i = 0; i < loops; i++)
                            {
                                if ((startClosestTurnPt.turnLineIndex + 1) >= turnCount) startClosestTurnPt.turnLineIndex = -1;

                                tPoint = mf.bnd.bndList[startClosestTurnPt.turnLineNum].turnLine[startClosestTurnPt.turnLineIndex + 1];
                                startClosestTurnPt.turnLineIndex++;
                                if (startClosestTurnPt.turnLineIndex >= turnCount)
                                    startClosestTurnPt.turnLineIndex = 0;
                                ytList.Add(tPoint);
                            }
                        }
                        else //countExit down = start with A
                        {
                            for (int i = 0; i < loops; i++)
                            {
                                tPoint = mf.bnd.bndList[startClosestTurnPt.turnLineNum].turnLine[startClosestTurnPt.turnLineIndex];
                                startClosestTurnPt.turnLineIndex--;
                                if (startClosestTurnPt.turnLineIndex == -1)
                                    startClosestTurnPt.turnLineIndex = turnCount - 1;
                                ytList.Add(tPoint);
                            }
                        }

                        //add the out from ytList2
                        for (int a = 0; a < cnt2; a++)
                        {
                            ytList.Add(new vec3(ytList2[cnt2 - 1 - a]));
                        }
                    }

                    if (!AddCurveSequenceLines()) return false;

                    //fill in the gaps
                    double distance;

                    int cnt = ytList.Count;
                    for (int i = 1; i < cnt - 2; i++)
                    {
                        int j = i + 1;
                        if (j == cnt - 1) continue;
                        distance = glm.DistanceSquared(ytList[i], ytList[j]);
                        if (distance > 1)
                        {
                            vec3 pointB = new vec3((ytList[i].easting + ytList[j].easting) / 2.0,
                                (ytList[i].northing + ytList[j].northing) / 2.0, ytList[i].heading);

                            ytList.Insert(j, pointB);
                            cnt = ytList.Count;
                            i--;
                        }
                    }

                    //calculate the new points headings based on fore and aft of point - smoother turns
                    cnt = ytList.Count;
                    vec3[] arr = new vec3[cnt];
                    cnt -= 2;
                    ytList.CopyTo(arr);
                    ytList.Clear();

                    for (int i = 2; i < cnt; i++)
                    {
                        vec3 pt3 = new vec3(arr[i]);
                        pt3.heading = Math.Atan2(arr[i + 1].easting - arr[i - 1].easting,
                            arr[i + 1].northing - arr[i - 1].northing);
                        if (pt3.heading < 0) pt3.heading += glm.twoPI;
                        ytList.Add(pt3);
                    }

                    //check to close
                    if (glm.Distance(ytList[0], mf.pivotAxlePos) < 3)
                    {
                        FailCreate();
                        return false;
                    }

                    //are we continuing the same way?
                    isGoingStraightThrough = Math.PI - Math.Abs(Math.Abs(ytList[ytList.Count - 2].heading - ytList[1].heading) - Math.PI) < glm.PIBy2;
                    ytList2?.Clear();
                    isOutOfBounds = false;
                    youTurnPhase = 10;
                    turnTooCloseTrigger = false;
                    isTurnCreationTooClose = false;
                    return true;
            }

            // just in case
            return false;
        }

        private bool CreateABOmegaTurn()
        {
            //keep from making turns constantly - wait 1.5 seconds
            if (mf.makeUTurnCounter < 4)
            {
                youTurnPhase = 0;
                return true;
            }

            double head = mf.ABLine.abHeading;
            if (!mf.ABLine.isHeadingSameWay) head += Math.PI;
            if (head >= glm.twoPI) head -= glm.twoPI;

            //we are doing an omega turn
            switch (youTurnPhase)
            {
                case 0:
                    //how far are we from any turn boundary
                    FindABTurnPoint(new vec3(mf.ABLine.rEastAB, mf.ABLine.rNorthAB, head));

                    //or did we lose the turnLine - we are on the highway cuz we left the outer/inner turn boundary
                    if (closestTurnPt.turnLineIndex != -1)
                    {
                        //calculate the distance to the turnline
                        mf.distancePivotToTurnLine = glm.Distance(mf.pivotAxlePos, closestTurnPt.closePt);
                    }
                    else
                    {
                        //Full emergency stop code goes here, it thinks its auto turn, but its not!
                        FailCreate();
                        return false;
                    }
                    inClosestTurnPt = new CClose(closestTurnPt);

                    CDubins dubYouTurnPath = new CDubins();
                    CDubins.turningRadius = youTurnRadius;

                    //grab the vehicle widths and offsets
                    double turnOffset = (mf.tool.width - mf.tool.overlap) * rowSkipsWidth + (isTurnLeft ? -mf.tool.offset * 2.0 : mf.tool.offset * 2.0);

                    vec3 start = new vec3(inClosestTurnPt.closePt);
                    start.heading = head;

                    vec3 goal = new vec3(start);

                    //now we go the other way to turn round
                    double invertedHead = head - Math.PI;
                    if (invertedHead < 0) invertedHead += glm.twoPI;
                    if (invertedHead > glm.twoPI) invertedHead -= glm.twoPI;

                    if (isTurnLeft)
                    {
                        goal.easting = goal.easting + (Math.Cos(-invertedHead) * turnOffset);
                        goal.northing = goal.northing + (Math.Sin(-invertedHead) * turnOffset);
                    }
                    else
                    {
                        goal.easting = goal.easting - (Math.Cos(-invertedHead) * turnOffset);
                        goal.northing = goal.northing - (Math.Sin(-invertedHead) * turnOffset);
                    }

                    goal.heading = invertedHead;

                    //generate the turn points
                    ytList = dubYouTurnPath.GenerateDubins(start, goal);
                    isOutOfBounds = true;

                    if (ytList.Count == 0)
                    {
                        FailCreate();
                        return false;
                    }

                    youTurnPhase = 1;

                    double distance;

                    int cnt = ytList.Count;
                    for (int i = 1; i < cnt - 2; i++)
                    {
                        distance = glm.DistanceSquared(ytList[i], ytList[i + 1]);
                        if (distance < pointSpacing)
                        {
                            ytList.RemoveAt(i + 1);
                            i--;
                            cnt = ytList.Count;
                        }
                    }

                    return true;

                case 1:
                    //move out
                    ytList = MoveTurnInsideTurnLine(ytList, head, false, false);

                    if (ytList.Count == 0)
                    {
                        FailCreate();
                        return false;
                    }

                    isOutOfBounds = false;
                    youTurnPhase = 10;
                    turnTooCloseTrigger = false;
                    isTurnCreationTooClose = false;

                    if (!AddABSequenceLines()) return false;

                    return true;
            }

            return true;

        }

        private bool CreateABWideTurn()
        {
            //keep from making turns constantly - wait 1.5 seconds
            if (mf.makeUTurnCounter < 4)
            {
                youTurnPhase = 0;
                return true;
            }

            double head = mf.ABLine.abHeading;
            if (!mf.ABLine.isHeadingSameWay) head += Math.PI;
            if (head >= glm.twoPI) head -= glm.twoPI;


            //NV rigid U (sandbox port): fixed construction replaces the phased stock build
            if (mf.tnl.IsFeatureOn)
            {
                double rigidOffset = (mf.tool.width - mf.tool.overlap) * rowSkipsWidth
                    + (isTurnLeft ? -mf.tool.offset * 2.0 : mf.tool.offset * 2.0);
                return BuildRigidUTurn(new vec3(mf.ABLine.rEastAB, mf.ABLine.rNorthAB, head), head, rigidOffset);
            }

            //step 1 turn in to the turnline
            switch (youTurnPhase)
            {
                case 0:
                    //grab the pure pursuit point right on ABLine
                    vec3 onPurePoint = new vec3(mf.ABLine.rEastAB, mf.ABLine.rNorthAB, 0);

                    //how far are we from any turn boundary
                    FindABTurnPoint(onPurePoint);

                    //save a copy for first point
                    inClosestTurnPt = new CClose(closestTurnPt);
                    startOfTurnPt = new CClose(closestTurnPt);

                    //already no turnline
                    if (inClosestTurnPt.turnLineIndex == -1)
                    {
                        FailCreate();
                        return false;
                    }

                    //creates half a circle starting at the crossing point
                    ytList?.Clear();
                    vec3 currentPos = new vec3(inClosestTurnPt.closePt.easting, inClosestTurnPt.closePt.northing, head);
                    ytList.Add(currentPos);

                    //Taken from Dubbins
                    while (Math.Abs(head - currentPos.heading) < Math.PI)
                    {
                        //Update the position of the car
                        currentPos.easting += pointSpacing * Math.Sin(currentPos.heading);
                        currentPos.northing += pointSpacing * Math.Cos(currentPos.heading);

                        //Which way are we turning?
                        double turnParameter = isTurnLeft ? -1.0 : 1.0;

                        //Update the heading (U exit radius when turn limit lines on)
                        currentPos.heading += (pointSpacing / (mf.tnl.IsFeatureOn ? UOutRadius : youTurnRadius)) * turnParameter;

                        //Add the new coordinate to the path
                        ytList.Add(currentPos);
                    }

                    //move the half circle to tangent the turnline
                    isOutOfBounds = true;
                    ytList = MoveTurnInsideTurnLine(ytList, head, true, false);

                    //if it couldn't be done this will trigger
                    if (ytList.Count == 0)
                    {
                        FailCreate();
                        return false;
                    }

                    mf.distancePivotToTurnLine = glm.Distance(ytList[0], mf.pivotAxlePos);

                    youTurnPhase = 1;
                    return true;

                case 1:
                    //build the next line to add sequencelines
                    double widthMinusOverlap = mf.tool.width - mf.tool.overlap;

                    CTrk track = mf.trk.gArr[mf.trk.idx];

                    double distAway = widthMinusOverlap * (mf.ABLine.howManyPathsAway + ((isTurnLeft ^ mf.ABLine.isHeadingSameWay) ? rowSkipsWidth : -rowSkipsWidth)) + (mf.ABLine.isHeadingSameWay ? mf.tool.offset : -mf.tool.offset) + track.nudgeDistance;

                    distAway += (0.5 * widthMinusOverlap);

                    //NV custom U overshoot: aim the entry arc at a virtual line pushed outward by
                    //"overshoot distance", so a large entry radius overshoots and a short reverse
                    //(driver) then aligns onto the real row. 0 = tangent the real line (stock).
                    if (mf.tnl.IsFeatureOn)
                    {
                        double over = Properties.Settings.Default.set_turnU_overshootDist;
                        if (over > 0.01)
                            distAway += ((isTurnLeft ^ mf.ABLine.isHeadingSameWay) ? over : -over);
                    }

                    nextCurve = mf.curve.BuildNewOffsetList(distAway, track);

                    //going with or against boundary?
                    bool isTurnLineSameWay = true;
                    double headingDifference = Math.Abs(startOfTurnPt.closePt.heading - ytList[ytList.Count - 1].heading);
                    if (headingDifference > glm.PIBy2 && headingDifference < 3 * glm.PIBy2) isTurnLineSameWay = false;

                    if (!FindABOutTurnPoint(mf.ABLine, ref nextCurve, inClosestTurnPt, isTurnLineSameWay))
                    {
                        //error
                        FailCreate();
                        return false;
                    }
                    outClosestTurnPt = new CClose(closestTurnPt);

                    vec3 pointPos = new vec3(outClosestTurnPt.closePt.easting, outClosestTurnPt.closePt.northing, 0);
                    double headie;
                    if (!isOutSameCurve)
                    {
                        headie = head;
                    }
                    else
                    {
                        headie = head + Math.PI;
                        if (headie >= glm.twoPI) headie -= glm.twoPI;
                    }
                    pointPos.heading = headie;

                    //step 3 create half cirkle in new list
                    ytList2?.Clear();
                    ytList2.Add(pointPos);

                    //Taken from Dubbins
                    while (Math.Abs(headie - pointPos.heading) < Math.PI)
                    {
                        //Update the position of the car
                        pointPos.easting += pointSpacing * Math.Sin(pointPos.heading);
                        pointPos.northing += pointSpacing * Math.Cos(pointPos.heading);

                        //Which way are we turning?
                        double turnParameter = isTurnLeft ? 1.0 : -1.0;

                        //Update the heading (U entry radius when turn limit lines on)
                        pointPos.heading += (pointSpacing / (mf.tnl.IsFeatureOn ? UInRadius : youTurnRadius)) * turnParameter;

                        //Add the new coordinate to the path
                        ytList2.Add(pointPos);
                    }

                    //move the half circle to tangent the turnline
                    isOutOfBounds = true;
                    ytList2 = MoveTurnInsideTurnLine(ytList2, headie, true, true);

                    if (ytList2.Count == 0)
                    {
                        FailCreate();
                        return false;
                    }

                    youTurnPhase = 2;
                    return true;

                case 2:
                    int cnt1 = ytList.Count;
                    int cnt2 = ytList2.Count;

                    //Validate that both lists have elements to prevent crashes
                    if (cnt1 == 0 || cnt2 == 0)
                    {
                        FailCreate();
                        return false;
                    }

                    //Find if the turn goes same way as turnline heading
                    bool isFirstTurnLineSameWay = true;
                    double firstHeadingDifference = Math.Abs(inClosestTurnPt.turnLineHeading - ytList[ytList.Count - 1].heading);

                    if (firstHeadingDifference > glm.PIBy2 && firstHeadingDifference < 3 * glm.PIBy2) isFirstTurnLineSameWay = false;

                    //finds out start and goal point along the tunline
                    FindInnerTurnPoints(ytList[cnt1 - 1], ytList[0].heading, inClosestTurnPt, isFirstTurnLineSameWay);
                    CClose startClosestTurnPt = new CClose(closestTurnPt);

                    FindInnerTurnPoints(ytList2[cnt2 - 1], ytList2[0].heading + Math.PI, outClosestTurnPt, !isFirstTurnLineSameWay);
                    CClose goalClosestTurnPt = new CClose(closestTurnPt);

                    //we have 2 different turnLine crossings
                    if (startClosestTurnPt.turnLineNum != goalClosestTurnPt.turnLineNum)
                    {
                        FailCreate();
                        return false;
                    }

                    //segment index is the "A" of the segment. segmentIndex+1 would be the "B"
                    //is in and out on same segment? so only 1 segment
                    if (startClosestTurnPt.turnLineIndex == goalClosestTurnPt.turnLineIndex)
                    {
                        for (int a = 0; a < cnt2; a++)
                        {
                            ytList.Add(new vec3(ytList2[cnt2 - 1 - a]));
                        }

                    }
                    else
                    {
                        //multiple segments
                        vec3 tPoint = new vec3();
                        int turnCount = mf.bnd.bndList[startClosestTurnPt.turnLineNum].turnLine.Count;

                        //how many points from turnline do we add
                        int loops = Math.Abs(startClosestTurnPt.turnLineIndex - goalClosestTurnPt.turnLineIndex);

                        //TODO: Generates error if trying to go around more than half of something...... Do we ever?
                        //are we crossing a border?
                        if (loops > (mf.bnd.bndList[startClosestTurnPt.turnLineNum].turnLine.Count / 2))
                        {
                            if (startClosestTurnPt.turnLineIndex < goalClosestTurnPt.turnLineIndex)
                            {
                                loops = (turnCount - goalClosestTurnPt.turnLineIndex) + startClosestTurnPt.turnLineIndex;
                            }
                            else
                            {
                                loops = (turnCount - startClosestTurnPt.turnLineIndex) + goalClosestTurnPt.turnLineIndex;
                            }
                        }

                        //countExit up - start with B which is next A
                        if (isFirstTurnLineSameWay)
                        {
                            for (int i = 0; i < loops; i++)
                            {
                                if ((startClosestTurnPt.turnLineIndex + 1) >= turnCount) startClosestTurnPt.turnLineIndex = -1;

                                tPoint = mf.bnd.bndList[startClosestTurnPt.turnLineNum].turnLine[startClosestTurnPt.turnLineIndex + 1];
                                startClosestTurnPt.turnLineIndex++;
                                if (startClosestTurnPt.turnLineIndex >= turnCount)
                                    startClosestTurnPt.turnLineIndex = 0;
                                ytList.Add(tPoint);
                            }
                        }
                        else //countExit down = start with A
                        {
                            for (int i = 0; i < loops; i++)
                            {
                                tPoint = mf.bnd.bndList[startClosestTurnPt.turnLineNum].turnLine[startClosestTurnPt.turnLineIndex];
                                startClosestTurnPt.turnLineIndex--;
                                if (startClosestTurnPt.turnLineIndex == -1)
                                    startClosestTurnPt.turnLineIndex = turnCount - 1;
                                ytList.Add(tPoint);
                            }
                        }

                        //add the out from ytList2
                        for (int a = 0; a < cnt2; a++)
                        {
                            ytList.Add(new vec3(ytList2[cnt2 - 1 - a]));
                        }
                    }

                    //fill in the gaps
                    double distance;

                    int cnt = ytList.Count;
                    for (int i = 1; i < cnt - 2; i++)
                    {
                        int j = i + 1;
                        if (j == cnt - 1) continue;
                        distance = glm.DistanceSquared(ytList[i], ytList[j]);
                        if (distance > 1)
                        {
                            vec3 pointB = new vec3((ytList[i].easting + ytList[j].easting) / 2.0,
                                (ytList[i].northing + ytList[j].northing) / 2.0, ytList[i].heading);

                            ytList.Insert(j, pointB);
                            cnt = ytList.Count;
                            i--;
                        }
                    }

                    //calculate the new points headings based on fore and aft of point - smoother turns
                    cnt = ytList.Count;
                    vec3[] arr = new vec3[cnt];
                    cnt -= 2;
                    ytList.CopyTo(arr);
                    ytList.Clear();

                    for (int i = 2; i < cnt; i++)
                    {
                        vec3 pt3 = new vec3(arr[i]);
                        pt3.heading = Math.Atan2(arr[i + 1].easting - arr[i - 1].easting,
                            arr[i + 1].northing - arr[i - 1].northing);
                        if (pt3.heading < 0) pt3.heading += glm.twoPI;
                        ytList.Add(pt3);
                    }

                    //AddABSequenceLines
                    //check to close
                    if (glm.Distance(ytList[0], mf.pivotAxlePos) < 3)
                    {
                        FailCreate();
                        return false;
                    }

                    //are we continuing the same way?
                    isGoingStraightThrough = Math.PI - Math.Abs(Math.Abs(ytList[ytList.Count - 2].heading - ytList[1].heading) - Math.PI) < glm.PIBy2;

                    isOutOfBounds = false;
                    youTurnPhase = 10;
                    turnTooCloseTrigger = false;
                    isTurnCreationTooClose = false;
                    ytList2?.Clear();

                    if (!AddABSequenceLines()) return false;

                    return true;
            }

            //just in case
            return false;
        }

        #endregion


        #region KStyle turns
        public bool KStyleTurnCurve()
        {
            //grab the vehicle widths and offsets
            double turnOffset = (mf.tool.width - mf.tool.overlap) * rowSkipsWidth + (isTurnLeft ? -mf.tool.offset * 2.0 : mf.tool.offset * 2.0);
            double pointSpacing = youTurnRadius * 0.1;

            isHeadingSameWay = mf.curve.isHeadingSameWay;

            int turnIndex = mf.bnd.IsPointInsideTurnArea(mf.pivotAxlePos);
            if (mf.makeUTurnCounter < 4 || turnIndex != 0)
            {
                youTurnPhase = 0;
                return true;
            }

            mf.makeUTurnCounter = 0;

            if (!FindCurveTurnPoint(mf.curve, true))
            {
                FailCreate();
                return false;
            }

            //save a copy
            inClosestTurnPt = new CClose(closestTurnPt);

            ytList?.Clear();

            //NV rigid K (sandbox port): fixed construction, handles the tight case with a
            //split + guided reverse leg - replaces the old parallel-then-enter shape
            if (mf.tnl.IsFeatureOn)
            {
                int ci = inClosestTurnPt.curveIndex;
                if (ci < 0) ci = 0;
                if (ci >= mf.curve.curList.Count) ci = mf.curve.curList.Count - 1;
                double kHead = mf.curve.curList[ci].heading;
                if (!isHeadingSameWay) kHead += Math.PI;
                if (kHead >= glm.twoPI) kHead -= glm.twoPI;
                return BuildRigidKTurn(inClosestTurnPt.closePt, kHead, turnOffset);
            }

            int count = isHeadingSameWay ? -1 : 1;
            int curveIndex = inClosestTurnPt.curveIndex + count;

            bool pointOutOfBnd = true;
            int stopIfWayOut = 0;

            double head = 0;

            while (pointOutOfBnd)
            {
                stopIfWayOut++;
                pointOutOfBnd = false;

                //creates half a circle starting at the crossing point
                ytList.Clear();
                if (curveIndex >= mf.curve.curList.Count || curveIndex < 0)
                {
                    FailCreate();
                    return false;
                }
                vec3 currentPos = new vec3(mf.curve.curList[curveIndex]);

                curveIndex += count;

                if (!isHeadingSameWay) currentPos.heading += Math.PI;
                if (currentPos.heading >= glm.twoPI) currentPos.heading -= glm.twoPI;

                ytList.Add(currentPos);

                while (Math.Abs(ytList[0].heading - currentPos.heading) < 2.2)
                {
                    //Update the position of the car
                    currentPos.easting += pointSpacing * Math.Sin(currentPos.heading);
                    currentPos.northing += pointSpacing * Math.Cos(currentPos.heading);

                    //Which way are we turning?
                    double turnParameter = isTurnLeft ? -1.0 : 1.0;

                    //Update the heading (K exit radius when turn limit lines on)
                    currentPos.heading += (pointSpacing / (mf.tnl.IsFeatureOn ? KExitRadius : youTurnRadius)) * turnParameter;

                    //Add the new coordinate to the path
                    ytList.Add(currentPos);
                }

                for (int i = 0; i < ytList.Count; i++)
                {
                    if (!IsPointAllowedForTurn(ytList[i]))
                    {
                        pointOutOfBnd = true;
                        break;
                    }
                }
            }

            //move out
            head = ytList[0].heading;
            double cosHead = Math.Cos(head) * 0.1;
            double sinHead = Math.Sin(head) * 0.1;
            vec3[] arr2 = new vec3[ytList.Count];
            ytList.CopyTo(arr2);
            ytList.Clear();

            //step 2 move the turn inside with steps of 0.1 meter
            int j = 0;
            pointOutOfBnd = false;

            while (!pointOutOfBnd)
            {
                stopIfWayOut++;
                pointOutOfBnd = false;
                mf.distancePivotToTurnLine = glm.DistanceSquared(arr2[0], mf.pivotAxlePos);

                for (int i = 0; i < arr2.Length; i++)
                {
                    arr2[i].easting += (sinHead);
                    arr2[i].northing += (cosHead);
                }

                for (j = 0; j < arr2.Length; j++)
                {
                    if (!IsPointAllowedForTurn(arr2[j]))
                    {
                        pointOutOfBnd = true;
                        break;
                    }
                }

                if (stopIfWayOut == 300 || (mf.distancePivotToTurnLine < 6))
                {
                    //for some reason it doesn't go inside boundary, return empty list
                    return false;
                }
            }

            ytList.AddRange(arr2);

            //add start extension from curve points
            curveIndex -= count;

            //point used to set next guidance line

            //now we go the other way to turn round
            head = ytList[0].heading;
            head -= Math.PI;
            if (head < -Math.PI) head += glm.twoPI;
            if (head > Math.PI) head -= glm.twoPI;

            if (head >= glm.twoPI) head -= glm.twoPI;
            else if (head < 0) head += glm.twoPI;

            //add the tail to first turn
            head = ytList[ytList.Count - 1].heading;

            vec3 pt;
            for (int i = 1; i <= (int)(3 * turnOffset); i++)
            {
                pt.easting = ytList[ytList.Count - 1].easting + (Math.Sin(head) * 0.5);
                pt.northing = ytList[ytList.Count - 1].northing + (Math.Cos(head) * 0.5);
                pt.heading = 0;
                ytList.Add(pt);
            }

            //leading in line of turn
            for (int i = 0; i < 4; i++)
            {
                ytList.Insert(0, new vec3(mf.curve.curList[curveIndex + i * count]));
            }

            //fill in the gaps
            double distance;

            int cnt = ytList.Count;
            for (int i = 1; i < cnt - 2; i++)
            {
                j = i + 1;
                if (j == cnt - 1) continue;
                distance = glm.DistanceSquared(ytList[i], ytList[j]);
                if (distance > 1)
                {
                    vec3 pointB = new vec3((ytList[i].easting + ytList[j].easting) / 2.0,
                        (ytList[i].northing + ytList[j].northing) / 2.0, ytList[i].heading);

                    ytList.Insert(j, pointB);
                    cnt = ytList.Count;
                    i--;
                }
            }

            //calculate line headings
            vec3[] arr = new vec3[ytList.Count];
            ytList.CopyTo(arr);
            ytList.Clear();

            for (int i = 0; i < arr.Length - 1; i++)
            {
                arr[i].heading = Math.Atan2(arr[i + 1].easting - arr[i].easting, arr[i + 1].northing - arr[i].northing);
                if (arr[i].heading < 0) arr[i].heading += glm.twoPI;
                ytList.Add(arr[i]);
            }

            mf.distancePivotToTurnLine = glm.Distance(ytList[0], mf.pivotAxlePos);

            isOutOfBounds = false;
            youTurnPhase = 10;
            turnTooCloseTrigger = false;
            isTurnCreationTooClose = false;

            return true;
        }

        public bool KStyleTurnAB()
        {
            double pointSpacing = youTurnRadius * 0.1;

            int turnIndex = mf.bnd.IsPointInsideTurnArea(mf.pivotAxlePos);
            if (mf.makeUTurnCounter < 4 || turnIndex != 0)
            {
                youTurnPhase = 0;
                return true;
            }

            mf.makeUTurnCounter = 0;

            //step 1 turn in to the turnline
            if (youTurnPhase == 0)
            {
                isOutOfBounds = true;


                //timer.Start();
                //grab the pure pursuit point right on ABLine
                vec3 onPurePoint = new vec3(mf.ABLine.rEastAB, mf.ABLine.rNorthAB, 0);

                //how far are we from any turn boundary
                FindClosestTurnPoint(onPurePoint);

                //save a copy for first point
                inClosestTurnPt = new CClose(closestTurnPt);

                //already no turnline
                if (inClosestTurnPt.turnLineIndex == -1) return false;

                //calculate the distance to the turnline
                mf.distancePivotToTurnLine = glm.Distance(mf.pivotAxlePos, closestTurnPt.closePt);

                //point on AB line closest to pivot axle point from ABLine PurePursuit aka where we are
                rEastYT = mf.ABLine.rEastAB;
                rNorthYT = mf.ABLine.rNorthAB;
                isHeadingSameWay = mf.ABLine.isHeadingSameWay;
                double head = mf.ABLine.abHeading;

                if (!isHeadingSameWay) head += Math.PI;
                if (head >= glm.twoPI) head -= glm.twoPI;

                //NV rigid K (sandbox port): fixed construction, handles the tight case with
                //a split + guided reverse leg - replaces the old parallel-then-enter shape
                if (mf.tnl.IsFeatureOn)
                {
                    double kOffset = (mf.tool.width - mf.tool.overlap) * rowSkipsWidth
                        + (isTurnLeft ? mf.tool.offset : -mf.tool.offset);
                    return BuildRigidKTurn(inClosestTurnPt.closePt, head, kOffset);
                }

                //thistance to turnline from where we are
                double turnDiagDistance = mf.distancePivotToTurnLine;

                //moves the point to the crossing with the turnline
                rEastYT += (Math.Sin(head) * turnDiagDistance);
                rNorthYT += (Math.Cos(head) * turnDiagDistance);

                //creates half a circle starting at the crossing point
                ytList.Clear();
                vec3 currentPos = new vec3(rEastYT, rNorthYT, head);
                ytList.Add(currentPos);

                //make semi circle - not quite
                while (Math.Abs(head - currentPos.heading) < 2.2)
                {
                    //Update the position of the car
                    currentPos.easting += pointSpacing * Math.Sin(currentPos.heading);
                    currentPos.northing += pointSpacing * Math.Cos(currentPos.heading);

                    //Which way are we turning?
                    double turnParameter = 1.0;

                    if (isTurnLeft) turnParameter = -1.0;

                    //Update the heading (K exit radius when turn limit lines on)
                    currentPos.heading += (pointSpacing / (mf.tnl.IsFeatureOn ? KExitRadius : youTurnRadius)) * turnParameter;

                    //Add the new coordinate to the path
                    ytList.Add(currentPos);
                }

                //move the half circle to tangent the turnline
                ytList = MoveABTurnInsideTurnLine(ytList, head);

                //if it couldn't be done this will trigger
                if (ytList.Count < 5 || semiCircleIndex == -1)
                {
                    FailCreate();
                    return false;
                }

                //grab the vehicle widths and offsets
                double turnOffset = (mf.tool.width - mf.tool.overlap) * rowSkipsWidth + (isTurnLeft ? mf.tool.offset : -mf.tool.offset);

                //add the tail to first turn
                int count = ytList.Count;
                head = ytList[count - 1].heading;

                vec3 pt;
                for (int i = 1; i <= (int)(3 * turnOffset); i++)
                {
                    pt.easting = ytList[count - 1].easting + (Math.Sin(head) * i * 0.5);
                    pt.northing = ytList[count - 1].northing + (Math.Cos(head) * i * 0.5);
                    pt.heading = 0;
                    ytList.Add(pt);
                }

                //leading in line of turn
                head = mf.ABLine.abHeading;
                if (isHeadingSameWay) head += Math.PI;
                if (head >= glm.twoPI) head -= glm.twoPI;

                for (int a = 0; a < 8; a++)
                {
                    pt.easting = ytList[0].easting + (Math.Sin(head) * 0.511);
                    pt.northing = ytList[0].northing + (Math.Cos(head) * 0.511);
                    pt.heading = ytList[0].heading;
                    ytList.Insert(0, pt);
                }

                //calculate line headings
                vec3[] arr = new vec3[ytList.Count];
                ytList.CopyTo(arr);
                ytList.Clear();

                //headings of line one
                for (int i = 0; i < arr.Length - 1; i++)
                {
                    arr[i].heading = Math.Atan2(arr[i + 1].easting - arr[i].easting, arr[i + 1].northing - arr[i].northing);
                    if (arr[i].heading < 0) arr[i].heading += glm.twoPI;
                    ytList.Add(arr[i]);
                }

                mf.distancePivotToTurnLine = glm.Distance(ytList[0], mf.pivotAxlePos);

                isOutOfBounds = false;
                youTurnPhase = 10;
                turnTooCloseTrigger = false;
                isTurnCreationTooClose = false;
                return true;
            }

            return true;
        }

        // ── NV RIGID TURN BUILDERS (1:1 port of the turn_sim sandbox) ─────────────────
        // See features/05_turn-zone/PORT_REVIEW.md. Rigid construction, minimal freedom:
        //  U: exit circle R_out and entry circle R_in each slide up their own row until
        //     TANGENT to the limit line (2 DOF); joined by the common upper tangent line.
        //     Degenerate (stacked circles) -> level apexes (1 DOF). Too tight: shrink both
        //     radii max -30%, then automatic overshoot (never a folded spike). No tail.
        //  K: same construction, but NO entry radius - the entry is a straight slant at
        //     angle alpha from E0 (d before the new row, on the tangent line), crossing the
        //     row ~2 m (red) - the driver reverses, AB guidance aligns. Entry too close ->
        //     SPLIT into two forward parts joined by a GUIDED reverse leg along the line.
        // All math runs in a local frame: origin on the current row, +Y = travel heading,
        // +X = lateral toward the target row (so left/right turns share one code path).
        private static double AngleDiff(double a, double b)
        {
            double d = a - b;
            while (d > Math.PI) d -= glm.twoPI;
            while (d < -Math.PI) d += glm.twoPI;
            return d;
        }

        // K split state: pending part 2 + stage (0 = none, 1 = part 1 forward,
        // 2 = guided reverse back along part 1, 3 = part 2 forward)
        public List<vec3> kPart2List = new List<vec3>();
        public int kTurnStage = 0;
        private bool kRevReached = false;   // reverse leg has reached the stop point

        // field fixes 2026-07-10:
        // forward meters held after the reverse leg - a power-shuttle reverser can
        // flip direction without the speed ever dropping under the stop threshold
        private double kFwdHeldDist;
        // which side of the new row's line part 2 starts on (0 = not sampled yet);
        // the front axle crossing that line ends the turn
        private int kPart2EntrySide;
        public int ytRedIndex = -1;     // ytList index from which points draw red
        public int part2RedIndex = -1;  // red index inside the pending part 2

        // local frame
        private double frOx, frOy, frTx, frTy, frLx, frLy;

        private void SetTurnFrame(vec3 rowPt, double head)
        {
            frOx = rowPt.easting; frOy = rowPt.northing;
            frTx = Math.Sin(head); frTy = Math.Cos(head);
            frLx = isTurnLeft ? -frTy : frTy;   // lateral unit toward the target row
            frLy = isTurnLeft ? frTx : -frTx;
        }

        private vec2 ToLocal(double e, double n)
        {
            return new vec2((e - frOx) * frLx + (n - frOy) * frLy,
                            (e - frOx) * frTx + (n - frOy) * frTy);
        }

        private vec3 ToWorld(double x, double y)
        {
            return new vec3(frOx + frLx * x + frTx * y, frOy + frLy * x + frTy * y, 0);
        }

        // effective limit polyline in LOCAL coords: the extrapolated drawn line hit by the
        // travel ray, or the boundary turn line (closed ring) when no line is hit there
        private bool TryGetLocalEff(double head, out List<vec2> eff)
        {
            eff = null;
            List<vec2> world = mf.tnl.GetEffPolylineByRay(new vec2(frOx, frOy), head);
            if (world == null)
            {
                if (mf.bnd.bndList.Count == 0 || mf.bnd.bndList[0].turnLine.Count < 3) return false;
                var tl = mf.bnd.bndList[0].turnLine;
                world = new List<vec2>(tl.Count + 1);
                for (int i = 0; i < tl.Count; i++) world.Add(new vec2(tl[i].easting, tl[i].northing));
                world.Add(new vec2(tl[0].easting, tl[0].northing));
            }
            eff = new List<vec2>(world.Count);
            for (int i = 0; i < world.Count; i++) eff.Add(ToLocal(world[i].easting, world[i].northing));
            return true;
        }

        // TOP crossing (largest local y) of the vertical x = e with the polyline
        private static bool CrossTop(List<vec2> eff, double x, out double y)
        {
            y = double.MinValue; bool hit = false;
            for (int i = 0; i < eff.Count - 1; i++)
            {
                double ax = eff[i].easting, bx = eff[i + 1].easting;
                if ((ax - x) * (bx - x) <= 0 && Math.Abs(bx - ax) > 1e-9)
                {
                    double t = (x - ax) / (bx - ax);
                    double yy = eff[i].northing + t * (eff[i + 1].northing - eff[i].northing);
                    if (!hit || yy > y) { y = yy; hit = true; }
                }
            }
            return hit;
        }

        // highest center y so the whole circle (center x = cx, radius R) stays under eff
        private static double FitCircleY(List<vec2> eff, double cx, double R, double step)
        {
            double y = double.MaxValue;
            for (int i = 0; i < eff.Count - 1; i++)
            {
                double ax = eff[i].easting, ay = eff[i].northing;
                double dx = eff[i + 1].easting - ax, dy = eff[i + 1].northing - ay;
                double len = Math.Sqrt(dx * dx + dy * dy);
                int m = Math.Max(1, (int)Math.Ceiling(len / step));
                for (int j = 0; j <= m; j++)
                {
                    double x = ax + dx * j / m, ddx = x - cx;
                    if (Math.Abs(ddx) < R)
                    {
                        double cand = ay + dy * j / m - Math.Sqrt(R * R - ddx * ddx);
                        if (cand < y) y = cand;
                    }
                }
            }
            return y;   // double.MaxValue = line nowhere above the circle span
        }

        // Laplacian smoothing, endpoints fixed (hardcoded 15 passes at the call sites)
        private static void SmoothLocal(List<vec2> p, int passes)
        {
            if (passes <= 0 || p.Count < 5) return;
            var q = new vec2[p.Count];
            for (int k = 0; k < passes; k++)
            {
                p.CopyTo(q);
                for (int i = 1; i < p.Count - 1; i++)
                {
                    p[i] = new vec2((q[i - 1].easting + 2 * q[i].easting + q[i + 1].easting) / 4.0,
                                    (q[i - 1].northing + 2 * q[i].northing + q[i + 1].northing) / 4.0);
                }
            }
        }

        // rigid-ensemble shift toward the field when the limit line dips into the path (V)
        private static void ShiftUnderEff(List<vec2> p1, List<vec2> p2, List<vec2> eff)
        {
            double viol = 0;
            for (int i = 0; i < p1.Count; i++)
                if (CrossTop(eff, p1[i].easting, out double yy)) viol = Math.Min(viol, yy - p1[i].northing);
            if (p2 != null)
                for (int i = 0; i < p2.Count; i++)
                    if (CrossTop(eff, p2[i].easting, out double yy)) viol = Math.Min(viol, yy - p2[i].northing);
            if (viol < 0)
            {
                for (int i = 0; i < p1.Count; i++) p1[i] = new vec2(p1[i].easting, p1[i].northing + viol);
                if (p2 != null)
                    for (int i = 0; i < p2.Count; i++) p2[i] = new vec2(p2[i].easting, p2[i].northing + viol);
            }
        }

        private List<vec3> LocalToWorldPath(List<vec2> pl)
        {
            var outp = new List<vec3>(pl.Count);
            for (int i = 0; i < pl.Count; i++) outp.Add(ToWorld(pl[i].easting, pl[i].northing));
            for (int i = 0; i < outp.Count - 1; i++)
            {
                double hh = Math.Atan2(outp[i + 1].easting - outp[i].easting, outp[i + 1].northing - outp[i].northing);
                if (hh < 0) hh += glm.twoPI;
                outp[i] = new vec3(outp[i].easting, outp[i].northing, hh);
            }
            if (outp.Count > 1)
                outp[outp.Count - 1] = new vec3(outp[outp.Count - 1].easting, outp[outp.Count - 1].northing, outp[outp.Count - 2].heading);
            return outp;
        }

        private bool FinishRigidPath(List<vec2> p1, int redStart, List<vec2> p2, int p2Red)
        {
            if (p1.Count < 6) { FailCreate(); return false; }

            var w1 = LocalToWorldPath(p1);
            if (glm.Distance(w1[0], mf.pivotAxlePos) < 3) { FailCreate(); return false; }

            ytList.Clear();
            ytList.AddRange(w1);
            ytRedIndex = redStart;

            kPart2List.Clear();
            kTurnStage = 0;
            kFwdHeldDist = 0;
            kPart2EntrySide = 0;
            part2RedIndex = -1;
            if (p2 != null && p2.Count > 3)
            {
                kPart2List.AddRange(LocalToWorldPath(p2));
                part2RedIndex = p2Red;
                kTurnStage = 1;
            }

            mf.distancePivotToTurnLine = glm.Distance(ytList[0], mf.pivotAxlePos);
            isOutOfBounds = false;
            youTurnPhase = 10;
            turnTooCloseTrigger = false;
            isTurnCreationTooClose = false;
            return true;
        }

        /// <summary>
        /// Rigid U-turn: 2-DOF circle fit + common upper tangent; degenerate -> level
        /// apexes; too tight -> shrink both radii max -30%, then automatic overshoot.
        /// </summary>
        private bool BuildRigidUTurn(vec3 rowPt, double head, double turnOffset)
        {
            double o = Math.Abs(turnOffset);
            if (o < 1.0) { FailCreate(); return false; }

            SetTurnFrame(rowPt, head);
            if (!TryGetLocalEff(head, out List<vec2> eff)) { FailCreate(); return false; }

            double ps = Math.Max(0.1, youTurnRadius * 0.1);
            double Rout = UOutRadius, Rin = UInRadius;
            double over = Properties.Settings.Default.set_turnU_overshootDist;
            if (over < 0) over = 0;

            // auto-shrink both radii (max -30%), then automatic overshoot for the rest
            double room = o + over;
            if (Rout + Rin > room)
            {
                double shrink = Math.Max(0.7, room / (Rout + Rin));
                Rout *= shrink; Rin *= shrink;
            }
            if (Rout + Rin > o + over) over += Rout + Rin - (o + over);

            double eT = o + over;
            double cx1 = Rout, cx3 = eT - Rin;
            double y1 = FitCircleY(eff, cx1, Rout, ps);
            double y3 = FitCircleY(eff, cx3, Rin, ps);
            if (y1 == double.MaxValue || y3 == double.MaxValue) { FailCreate(); return false; }

            // common upper external tangent: unit normal n with n·d = R_out - R_in
            double dvx = cx3 - cx1, dvy = y3 - y1, D = Math.Sqrt(dvx * dvx + dvy * dvy);
            double T1x = 0, T1y = 0, T3x = 0, T3y = 0, htgt = 0;
            bool degen = true;
            if (D > Math.Abs(Rout - Rin) + 1e-6)
            {
                double k = (Rout - Rin) / D, s = Math.Sqrt(Math.Max(0, 1 - k * k));
                double dpx = -dvy / D, dpy = dvx / D;
                double nAx = dvx / D * k + dpx * s, nAy = dvy / D * k + dpy * s;
                double nBx = dvx / D * k - dpx * s, nBy = dvy / D * k - dpy * s;
                double nx = nAy > nBy ? nAx : nBx, ny = nAy > nBy ? nAy : nBy;
                T1x = cx1 + nx * Rout; T1y = y1 + ny * Rout;
                T3x = cx3 + nx * Rin; T3y = y3 + ny * Rin;
                htgt = Math.Atan2(T3x - T1x, T3y - T1y);
                // the connector must actually head toward the target row (stacked circles
                // at off ~ R_out+R_in give a near-vertical tangent -> the path would loop)
                degen = !(Math.Sin(htgt) > 0.1);
            }
            if (degen)
            {
                double t = Math.Min(y1 + Rout, y3 + Rin);
                y1 = t - Rout; y3 = t - Rin;
                T1x = cx1; T1y = t; T3x = cx3; T3y = t; htgt = glm.PIBy2;
            }

            // lead-in + exit arc (0 -> htgt) + tangent line + entry arc (htgt -> PI), no tail
            var pl = new List<vec2>(512);
            int nLead = Math.Max(3, (int)(2.5 / ps));
            for (int i = nLead; i >= 1; i--) pl.Add(new vec2(0, y1 - ps * i));

            double px = 0, py = y1, h = 0;
            int g = 0;
            while (Math.Abs(AngleDiff(h, htgt)) > ps / Rout && g++ < 4000)
            {
                px += ps * Math.Sin(h); py += ps * Math.Cos(h); h += ps / Rout;
                pl.Add(new vec2(px, py));
            }
            double L = Math.Sqrt((T3x - px) * (T3x - px) + (T3y - py) * (T3y - py));
            int m = Math.Max(1, (int)Math.Round(L / ps));
            for (int j = 1; j <= m; j++) pl.Add(new vec2(px + (T3x - px) * j / m, py + (T3y - py) * j / m));

            int redStart = over > 0.05 ? pl.Count : -1;   // overshoot -> entry arc draws red
            px = T3x; py = T3y; h = htgt; g = 0;
            while (Math.Abs(AngleDiff(h, Math.PI)) > ps / Rin && g++ < 4000)
            {
                px += ps * Math.Sin(h); py += ps * Math.Cos(h); h += ps / Rin;
                pl.Add(new vec2(px, py));
            }

            ShiftUnderEff(pl, null, eff);
            SmoothLocal(pl, 15);
            return FinishRigidPath(pl, redStart, null, -1);
        }

        /// <summary>
        /// Rigid K-turn: same construction, entry = straight slant (d, alpha), no entry
        /// radius. Entry too close -> split into two forward parts + guided reverse leg.
        /// </summary>
        private bool BuildRigidKTurn(vec3 rowPt, double head, double turnOffset)
        {
            double o = Math.Abs(turnOffset);
            if (o < 1.0) { FailCreate(); return false; }

            SetTurnFrame(rowPt, head);
            if (!TryGetLocalEff(head, out List<vec2> eff)) { FailCreate(); return false; }

            double ps = Math.Max(0.1, youTurnRadius * 0.1);
            double R = KExitRadius; if (R < 1.0) R = 1.0;
            double d = Properties.Settings.Default.set_turnK_entryStartDist;
            if (d < 0.1) d = 0.1;
            if (d > o - 0.2) d = Math.Max(0.1, o - 0.2);
            double alpha = Properties.Settings.Default.set_turnK_entryAngle * Math.PI / 180.0;
            if (alpha < 0.10) alpha = 0.10;
            if (alpha > 1.40) alpha = 1.40;

            double cx1 = R, cx3 = o - R;
            double y1 = FitCircleY(eff, cx1, R, ps);
            double y3 = FitCircleY(eff, cx3, R, ps);
            if (y1 == double.MaxValue || y3 == double.MaxValue) { FailCreate(); return false; }

            // equal radii -> the common upper tangent is parallel to the center line; the
            // (virtual) entry circle only sets the tangent-line slope
            double dvx = cx3 - cx1, dvy = y3 - y1, D = Math.Sqrt(dvx * dvx + dvy * dvy);
            double nx = 0, ny = 1;
            if (D > 1e-6)
            {
                double dpx = -dvy / D, dpy = dvx / D;
                if (dpy > 0) { nx = dpx; ny = dpy; } else { nx = -dpx; ny = -dpy; }
            }
            double tx = ny, ty = -nx;               // CW-consistent travel direction
            double htgt = Math.Atan2(tx, ty);
            double T1x = cx1 + nx * R, T1y = y1 + ny * R;

            // E0 = entry start point: on the tangent line, lateral d before the new row
            double E0x, E0y;
            if (Math.Abs(tx) > 0.05)
            {
                double sE = (o - d - T1x) / tx;
                E0x = T1x + tx * sE; E0y = T1y + ty * sE;
            }
            else { E0x = o - d; E0y = T1y; }
            double proj = (E0x - T1x) * tx + (E0y - T1y) * ty;   // < 0 -> E0 is BEHIND T1

            // lead-in + exit arc (0 -> htgt), ends at T1
            var pl = new List<vec2>(512);
            int nLead = Math.Max(3, (int)(2.5 / ps));
            for (int i = nLead; i >= 1; i--) pl.Add(new vec2(0, y1 - ps * i));

            double px = 0, py = y1, h = 0;
            int g = 0;
            while (Math.Abs(AngleDiff(h, htgt)) > ps / R && g++ < 4000)
            {
                px += ps * Math.Sin(h); py += ps * Math.Cos(h); h += ps / R;
                pl.Add(new vec2(px, py));
            }

            void Dens(List<vec2> lst, double ax, double ay, double bx, double by)
            {
                double len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                int mm = Math.Max(1, (int)Math.Round(len / ps));
                for (int j = 1; j <= mm; j++) lst.Add(new vec2(ax + (bx - ax) * j / mm, ay + (by - ay) * j / mm));
            }

            double hEnt = Math.PI - alpha;          // slant heading onto the row (local)
            List<vec2> p2 = null;
            int redStart = -1, p2Red = -1;

            if (proj >= -0.01)
            {
                // fits forward: tangent line to E0, then the slant crossing the row ~2 m
                Dens(pl, px, py, E0x, E0y);
                px = E0x; py = E0y; g = 0;
                while (px < o - 0.02 && g++ < 7000)
                {
                    px += ps * Math.Sin(hEnt); py += ps * Math.Cos(hEnt);
                    pl.Add(new vec2(px, py));
                }
                redStart = pl.Count;                 // crossing past the line draws red
                int nCross = (int)Math.Round(2.0 / ps);
                for (int i = 0; i < nCross; i++)
                {
                    px += ps * Math.Sin(hEnt); py += ps * Math.Cos(hEnt);
                    pl.Add(new vec2(px, py));
                }
            }
            else
            {
                // SPLIT: part 1 forward (+2 m past T1), guided reverse back along the
                // line, part 2 = forward line + slant (pending until reversed enough)
                const double fwdE = 15.0, revE = 1.5;  // 15 m safety tangent on the exit radius
                Dens(pl, px, py, px + tx * fwdE, py + ty * fwdE);

                double rsx = E0x - tx * revE, rsy = E0y - ty * revE;
                p2 = new List<vec2>(256) { new vec2(rsx, rsy) };
                Dens(p2, rsx, rsy, E0x, E0y);
                px = E0x; py = E0y; g = 0;
                while (px < o - 0.02 && g++ < 7000)
                {
                    px += ps * Math.Sin(hEnt); py += ps * Math.Cos(hEnt);
                    p2.Add(new vec2(px, py));
                }
                p2Red = p2.Count;
                int nCross = (int)Math.Round(2.0 / ps);
                for (int i = 0; i < nCross; i++)
                {
                    px += ps * Math.Sin(hEnt); py += ps * Math.Cos(hEnt);
                    p2.Add(new vec2(px, py));
                }
            }

            ShiftUnderEff(pl, p2, eff);
            SmoothLocal(pl, 15);
            if (p2 != null) SmoothLocal(p2, 15);
            return FinishRigidPath(pl, redStart, p2, p2Red);
        }

        private List<vec3> MoveABTurnInsideTurnLine(List<vec3> uTurnList, double head)
        {
            //step 1 make array out of the list so that we can modify the position
            double cosHead = Math.Cos(head);
            double sinHead = Math.Sin(head);
            int cnt = uTurnList.Count;
            vec3[] arr2 = new vec3[cnt];
            uTurnList.CopyTo(arr2);
            uTurnList.Clear();

            semiCircleIndex = -1;
            //step 2 move the turn inside with steps of 1 meter
            bool pointOutOfBnd = true;
            int j = 0;
            int stopIfWayOut = 0;
            while (pointOutOfBnd)
            {
                stopIfWayOut++;
                pointOutOfBnd = false;
                mf.distancePivotToTurnLine = glm.DistanceSquared(arr2[0], mf.pivotAxlePos);

                for (int i = 0; i < cnt; i++)
                {
                    arr2[i].easting -= (sinHead);
                    arr2[i].northing -= (cosHead);
                }

                for (; j < cnt; j += 1)
                {
                    if (!IsPointAllowedForTurn(arr2[j]))
                    {
                        pointOutOfBnd = true;
                        if (j > 0) j--;
                        break;
                    }
                }

                if (stopIfWayOut == 1000 || (mf.distancePivotToTurnLine < 6))
                {
                    //for some reason it doesn't go inside boundary, return empty list
                    return uTurnList;
                }
            }

            //step 5, we ar now inside turnfence by 0-0.1 meters, move the turn forward until it hits the turnfence in steps of 0.05 meters
            while (!pointOutOfBnd)
            {

                for (int i = 0; i < cnt; i++)
                {
                    arr2[i].easting += (sinHead * 0.1);
                    arr2[i].northing += (cosHead * 0.1);
                }

                for (int a = 0; a < cnt; a++)
                {
                    if (!IsPointAllowedForTurn(arr2[a]))
                    {
                        semiCircleIndex = a;
                        uTurnList.AddRange(arr2);
                        return uTurnList;
                    }
                }
            }

            //if empty - no creation.
            return uTurnList;

            //step 6, we have now placed the turn, so send it back in a list

        }

        public void FindClosestTurnPoint(vec3 fromPt)
        {
            double eP = fromPt.easting;
            double nP = fromPt.northing;
            double eAB, nAB;
            turnClosestList?.Clear();

            CClose cClose;

            if (mf.ABLine.isHeadingSameWay)
            {
                eAB = mf.ABLine.currentLinePtB.easting;
                nAB = mf.ABLine.currentLinePtB.northing;
            }
            else
            {
                eAB = mf.ABLine.currentLinePtA.easting;
                nAB = mf.ABLine.currentLinePtA.northing;
            }

            turnClosestList.Clear();

            for (int j = 0; j < mf.bnd.bndList.Count; j++)
            {
                for (int i = 0; i < mf.bnd.bndList[j].turnLine.Count - 1; i++)
                {
                    int res = GetLineIntersection(
                        mf.bnd.bndList[j].turnLine[i].easting,
                        mf.bnd.bndList[j].turnLine[i].northing,
                        mf.bnd.bndList[j].turnLine[i + 1].easting,
                        mf.bnd.bndList[j].turnLine[i + 1].northing,
                        eP, nP, eAB, nAB, ref iE, ref iN
                    );

                    if (res == 1)
                    {
                        cClose = new CClose();
                        cClose.closePt.easting = iE;
                        cClose.closePt.northing = iN;

                        double hed = Math.Atan2(mf.bnd.bndList[j].turnLine[i + 1].easting - mf.bnd.bndList[j].turnLine[i].easting,
                            mf.bnd.bndList[j].turnLine[i + 1].northing - mf.bnd.bndList[j].turnLine[i].northing);
                        if (hed < 0) hed += glm.twoPI;
                        cClose.closePt.heading = hed;
                        cClose.turnLineNum = j;
                        cClose.turnLineIndex = i;

                        turnClosestList.Add(new CClose(cClose));
                    }
                }
            }

            //determine closest point
            double minDistance = double.MaxValue;

            if (turnClosestList.Count > 0)
            {
                for (int i = 0; i < turnClosestList.Count; i++)
                {
                    double dist = (((fromPt.easting - turnClosestList[i].closePt.easting) * (fromPt.easting - turnClosestList[i].closePt.easting))
                                    + ((fromPt.northing - turnClosestList[i].closePt.northing) * (fromPt.northing - turnClosestList[i].closePt.northing)));

                    if (minDistance >= dist)
                    {

                        minDistance = dist;
                        closestTurnPt = new CClose(turnClosestList[i]);
                    }
                }
            }
        }
        #endregion

        #region FindTurnPoint

        public bool FindCurveOutTurnPoint(CABCurve thisCurve, ref List<vec3> nextCurve, CClose inPt, bool isTurnLineSameWay)
        {
            int a = isTurnLineSameWay ? 1 : -1;

            int turnLineIndex = inPt.turnLineIndex;
            int turnLineNum = inPt.turnLineNum;
            int stopTurnLineIndex = inPt.turnLineIndex - a;
            if (stopTurnLineIndex < 0) stopTurnLineIndex = mf.bnd.bndList[turnLineNum].turnLine.Count - 3;
            if (stopTurnLineIndex > mf.bnd.bndList[turnLineNum].turnLine.Count - 1) turnLineIndex = 3;


            for (; turnLineIndex != stopTurnLineIndex; turnLineIndex += a)
            {
                if (turnLineIndex < 0) turnLineIndex = mf.bnd.bndList[turnLineNum].turnLine.Count - 2; //AAA could be less than 0???
                if (turnLineIndex > mf.bnd.bndList[turnLineNum].turnLine.Count - 2) turnLineIndex = 0;

                for (int i = 0; i < nextCurve.Count - 2; i++)
                {
                    int res = GetLineIntersection(
                                    mf.bnd.bndList[turnLineNum].turnLine[turnLineIndex].easting,
                                    mf.bnd.bndList[turnLineNum].turnLine[turnLineIndex].northing,
                                    mf.bnd.bndList[turnLineNum].turnLine[turnLineIndex + 1].easting,
                                    mf.bnd.bndList[turnLineNum].turnLine[turnLineIndex + 1].northing,

                                    nextCurve[i].easting,
                                    nextCurve[i].northing,
                                    nextCurve[i + 1].easting,
                                    nextCurve[i + 1].northing,
                                     ref iE, ref iN);
                    if (res == 1)
                    {
                        closestTurnPt = new CClose();
                        closestTurnPt.closePt.easting = iE;
                        closestTurnPt.closePt.northing = iN;
                        closestTurnPt.closePt.heading = nextCurve[i].heading;
                        closestTurnPt.turnLineIndex = turnLineIndex;
                        closestTurnPt.curveIndex = i;
                        closestTurnPt.turnLineHeading = mf.bnd.bndList[turnLineNum].turnLine[turnLineIndex].heading;
                        closestTurnPt.turnLineNum = turnLineNum;
                        return true;
                    }
                }

                for (int i = 0; i < thisCurve.curList.Count - 2; i++)
                {
                    int res = GetLineIntersection(
                                    mf.bnd.bndList[turnLineNum].turnLine[turnLineIndex].easting,
                                    mf.bnd.bndList[turnLineNum].turnLine[turnLineIndex].northing,
                                    mf.bnd.bndList[turnLineNum].turnLine[turnLineIndex + 1].easting,
                                    mf.bnd.bndList[turnLineNum].turnLine[turnLineIndex + 1].northing,

                                    thisCurve.curList[i].easting,
                                    thisCurve.curList[i].northing,
                                    thisCurve.curList[i + 1].easting,
                                    thisCurve.curList[i + 1].northing,

                                     ref iE, ref iN);
                    if (res == 1)
                    {
                        if ((i < inPt.curveIndex && thisCurve.isHeadingSameWay) || (i > inPt.curveIndex && !thisCurve.isHeadingSameWay))
                        {
                            return false; //hitting the curve behind us
                        }
                        else if (i == inPt.curveIndex)
                        {
                            //do nothing hitting the curve at the same place as in
                        }
                        else
                        {
                            closestTurnPt = new CClose();
                            closestTurnPt.closePt.easting = iE;
                            closestTurnPt.closePt.northing = iN;
                            closestTurnPt.closePt.heading = thisCurve.curList[i].heading; //ändrad nyss till this curve
                            closestTurnPt.turnLineIndex = turnLineIndex;
                            closestTurnPt.curveIndex = i;
                            closestTurnPt.turnLineHeading = mf.bnd.bndList[turnLineNum].turnLine[turnLineIndex].heading;
                            closestTurnPt.turnLineNum = turnLineNum;
                            isOutSameCurve = true;
                            nextCurve = thisCurve.curList;//???? already created the line so why change it
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        public bool FindABOutTurnPoint(CABLine thisCurve, ref List<vec3> nextCurve, CClose inPt, bool isTurnLineSameWay)
        {
            int a = isTurnLineSameWay ? 1 : -1;

            int turnLineIndex = inPt.turnLineIndex;
            int turnLineNum = inPt.turnLineNum;
            int stopTurnLineIndex = inPt.turnLineIndex - a;
            if (stopTurnLineIndex < 0) stopTurnLineIndex = mf.bnd.bndList[turnLineNum].turnLine.Count - 3;
            if (stopTurnLineIndex > mf.bnd.bndList[turnLineNum].turnLine.Count - 1) turnLineIndex = 3;


            for (; turnLineIndex != stopTurnLineIndex; turnLineIndex += a)
            {
                if (turnLineIndex < 0) turnLineIndex = mf.bnd.bndList[turnLineNum].turnLine.Count - 2;
                if (turnLineIndex > mf.bnd.bndList[turnLineNum].turnLine.Count - 2) turnLineIndex = 0;

                if (nextCurve.Count > 1)
                {
                    int res = GetLineIntersection(
                                    mf.bnd.bndList[turnLineNum].turnLine[turnLineIndex].easting,
                                    mf.bnd.bndList[turnLineNum].turnLine[turnLineIndex].northing,
                                    mf.bnd.bndList[turnLineNum].turnLine[turnLineIndex + 1].easting,
                                    mf.bnd.bndList[turnLineNum].turnLine[turnLineIndex + 1].northing,

                                    nextCurve[0].easting,
                                    nextCurve[0].northing,
                                    nextCurve[1].easting,
                                    nextCurve[1].northing,
                                     ref iE, ref iN);
                    if (res == 1)
                    {
                        closestTurnPt = new CClose();
                        closestTurnPt.closePt.easting = iE;
                        closestTurnPt.closePt.northing = iN;
                        closestTurnPt.closePt.heading = thisCurve.abHeading;
                        closestTurnPt.turnLineIndex = turnLineIndex;
                        closestTurnPt.curveIndex = -1;
                        closestTurnPt.turnLineHeading = mf.bnd.bndList[turnLineNum].turnLine[turnLineIndex].heading;
                        closestTurnPt.turnLineNum = turnLineNum;
                        return true;
                    }
                }



                int res2 = GetLineIntersection(
                                mf.bnd.bndList[turnLineNum].turnLine[turnLineIndex].easting,
                                mf.bnd.bndList[turnLineNum].turnLine[turnLineIndex].northing,
                                mf.bnd.bndList[turnLineNum].turnLine[turnLineIndex + 1].easting,
                                mf.bnd.bndList[turnLineNum].turnLine[turnLineIndex + 1].northing,

                                thisCurve.currentLinePtA.easting,
                                thisCurve.currentLinePtA.northing,
                                thisCurve.currentLinePtB.easting,
                                thisCurve.currentLinePtB.northing,

                                 ref iE, ref iN);
                if (res2 == 1)
                {
                    double hed;
                    if (mf.ABLine.isHeadingSameWay) hed = Math.Atan2(mf.ABLine.rEastAB - iE, mf.ABLine.rNorthAB - iN);
                    else hed = Math.Atan2(iE - mf.ABLine.rEastAB, iN - mf.ABLine.rNorthAB);
                    if (hed < 0) hed += glm.twoPI;
                    hed = Math.Round(hed, 3);
                    double hedAB = Math.Round(thisCurve.abHeading, 3);

                    if (hed == hedAB)
                    {
                        return false; //hitting the curve behind us
                    }
                    else if (turnLineIndex == inPt.turnLineIndex)
                    {
                        //do nothing hitting the curve at the same place as in
                    }
                    else
                    {
                        closestTurnPt = new CClose();
                        closestTurnPt.closePt.easting = iE;
                        closestTurnPt.closePt.northing = iN;
                        closestTurnPt.closePt.heading = thisCurve.abHeading;
                        closestTurnPt.turnLineIndex = turnLineIndex;
                        closestTurnPt.curveIndex = -1;
                        closestTurnPt.turnLineHeading = mf.bnd.bndList[turnLineNum].turnLine[turnLineIndex].heading;
                        closestTurnPt.turnLineNum = turnLineNum;
                        isOutSameCurve = true;
                        return true;
                    }
                }

            }
            return false;
        }

        private bool FindInnerTurnPoints(vec3 fromPt, double inDirection, CClose refClosePt, bool isTurnLineSameWay)
        {
            double eP, nP;

            eP = fromPt.easting + Math.Sin(inDirection);
            nP = fromPt.northing + Math.Cos(inDirection);

            int a = isTurnLineSameWay ? 1 : -1;

            int turnLineIndex = refClosePt.turnLineIndex;
            int turnLineNum = refClosePt.turnLineNum;
            int stopTurnLineIndex = refClosePt.turnLineIndex - a;
            if (stopTurnLineIndex < 0) stopTurnLineIndex = mf.bnd.bndList[turnLineNum].turnLine.Count - 3;
            if (stopTurnLineIndex > mf.bnd.bndList[turnLineNum].turnLine.Count - 1) turnLineIndex = 3;


            for (; turnLineIndex != stopTurnLineIndex; turnLineIndex += a)
            {
                if (turnLineIndex < 0) turnLineIndex = mf.bnd.bndList[turnLineNum].turnLine.Count - 2;
                if (turnLineIndex > mf.bnd.bndList[turnLineNum].turnLine.Count - 2) turnLineIndex = 0;


                int res = GetLineIntersection(
                                mf.bnd.bndList[turnLineNum].turnLine[turnLineIndex].easting,
                                mf.bnd.bndList[turnLineNum].turnLine[turnLineIndex].northing,
                                mf.bnd.bndList[turnLineNum].turnLine[turnLineIndex + 1].easting,
                                mf.bnd.bndList[turnLineNum].turnLine[turnLineIndex + 1].northing,

                                fromPt.easting,
                                fromPt.northing, eP, nP, ref iE, ref iN);
                if (res == 1)
                {
                    closestTurnPt = new CClose();
                    closestTurnPt.closePt.easting = iE;
                    closestTurnPt.closePt.northing = iN;
                    closestTurnPt.closePt.heading = -1; //isnt needed but could be calculated
                    closestTurnPt.turnLineIndex = turnLineIndex;
                    closestTurnPt.curveIndex = -1;
                    closestTurnPt.turnLineHeading = mf.bnd.bndList[turnLineNum].turnLine[turnLineIndex].heading;
                    closestTurnPt.turnLineNum = turnLineNum;
                    return true;
                }

            }
            return false;
        }

        private bool FindCurveTurnPoint(CABCurve thisCurve, bool noIdea)
        {
            //AAA Is updated but not tested....
            //find closet AB Curve point that will cross and go out of bounds
            int Count = mf.curve.isHeadingSameWay ? 1 : -1;
            int turnNum = 99;
            int j;

            closestTurnPt = new CClose();

            if (mf.trk.idx < 0 || mf.trk.gArr.Count < mf.trk.idx) return true;
            CTrk track = mf.trk.gArr[mf.trk.idx];

            bool loop = track.mode == TrackMode.bndCurve || track.mode == TrackMode.waterPivot;

            for (j = thisCurve.currentLocationIndex; j > 0 && j < thisCurve.curList.Count; j += Count)
            {
                if (j < 0)
                {
                    if (loop)
                    {
                        loop = false;
                        j = thisCurve.curList.Count;
                        continue;
                    }
                    break;
                }
                else if (j >= thisCurve.curList.Count)
                {
                    if (loop)
                    {
                        loop = false;
                        j = -1;
                        continue;
                    }
                    break;
                }

                int turnIndex = mf.bnd.IsPointInsideTurnArea(thisCurve.curList[j]);
                if (turnIndex != 0)
                {
                    closestTurnPt.curveIndex = j - Count;
                    closestTurnPt.turnLineNum = turnIndex;
                    turnNum = turnIndex;
                    break;
                }
            }

            if (turnNum < 0) //uturn will be on outer boundary turn
            {
                closestTurnPt.turnLineNum = 0;
                turnNum = 0;
            }
            else if (turnNum == 99)
            {
                //curve does not cross a boundary - oops
                isTurnCreationNotCrossingError = true;
                return false;
            }

            if (closestTurnPt.curveIndex == -1)
            {
                isTurnCreationNotCrossingError = true;
                return false;
            }


            for (int i = 0; i < mf.bnd.bndList[turnNum].turnLine.Count - 1; i++)
            {
                int res = GetLineIntersection(
                        mf.bnd.bndList[turnNum].turnLine[i].easting,
                        mf.bnd.bndList[turnNum].turnLine[i].northing,
                        mf.bnd.bndList[turnNum].turnLine[i + 1].easting,
                        mf.bnd.bndList[turnNum].turnLine[i + 1].northing,

                        thisCurve.curList[closestTurnPt.curveIndex].easting,
                        thisCurve.curList[closestTurnPt.curveIndex].northing,
                        thisCurve.curList[closestTurnPt.curveIndex + Count].easting,
                        thisCurve.curList[closestTurnPt.curveIndex + Count].northing,

                         ref iE, ref iN);

                if (res == 1)
                {
                    closestTurnPt.closePt.easting = iE;
                    closestTurnPt.closePt.northing = iN;
                    if (noIdea)
                    {
                        double hed = Math.Atan2(mf.bnd.bndList[turnNum].turnLine[i + 1].easting - mf.bnd.bndList[turnNum].turnLine[i].easting,
                            mf.bnd.bndList[turnNum].turnLine[i + 1].northing - mf.bnd.bndList[turnNum].turnLine[i].northing);
                        if (hed < 0) hed += glm.twoPI;
                        closestTurnPt.closePt.heading = hed;
                        closestTurnPt.turnLineIndex = i;
                    }
                    else
                    {
                        closestTurnPt.closePt.heading = thisCurve.curList[closestTurnPt.curveIndex].heading;
                        closestTurnPt.turnLineIndex = i;
                        closestTurnPt.turnLineNum = turnNum;
                        closestTurnPt.turnLineHeading = mf.bnd.bndList[turnNum].turnLine[i].heading;
                        if (!thisCurve.isHeadingSameWay && closestTurnPt.curveIndex > 0) closestTurnPt.curveIndex--;
                    }
                    break;
                }
            }

            return closestTurnPt.turnLineIndex != -1 && closestTurnPt.curveIndex != -1;
        }

        private void FindABTurnPoint(vec3 fromPt)
        {
            double eP = fromPt.easting;
            double nP = fromPt.northing;
            double eAB, nAB;
            turnClosestList?.Clear();

            CClose cClose;

            if (mf.ABLine.isHeadingSameWay)
            {
                eAB = mf.ABLine.currentLinePtB.easting;
                nAB = mf.ABLine.currentLinePtB.northing;
            }
            else
            {
                eAB = mf.ABLine.currentLinePtA.easting;
                nAB = mf.ABLine.currentLinePtA.northing;
            }

            turnClosestList.Clear();

            for (int j = 0; j < mf.bnd.bndList.Count; j++)
            {
                for (int i = 0; i < mf.bnd.bndList[j].turnLine.Count - 1; i++)
                {
                    int res = GetLineIntersection(
                        mf.bnd.bndList[j].turnLine[i].easting,
                        mf.bnd.bndList[j].turnLine[i].northing,
                        mf.bnd.bndList[j].turnLine[i + 1].easting,
                        mf.bnd.bndList[j].turnLine[i + 1].northing,
                        eP, nP, eAB, nAB, ref iE, ref iN
                    );

                    if (res == 1)
                    {
                        cClose = new CClose();
                        cClose.closePt.easting = iE;
                        cClose.closePt.northing = iN;

                        double hed = Math.Atan2(mf.bnd.bndList[j].turnLine[i + 1].easting - mf.bnd.bndList[j].turnLine[i].easting,
                            mf.bnd.bndList[j].turnLine[i + 1].northing - mf.bnd.bndList[j].turnLine[i].northing);
                        if (hed < 0) hed += glm.twoPI;
                        cClose.closePt.heading = hed;
                        cClose.turnLineHeading = mf.bnd.bndList[j].turnLine[i].heading;
                        cClose.turnLineNum = j;
                        cClose.turnLineIndex = i;

                        turnClosestList.Add(new CClose(cClose));
                    }
                }
            }

            //determine closest point
            double minDistance = double.MaxValue;

            if (turnClosestList.Count > 0)
            {
                for (int i = 0; i < turnClosestList.Count; i++)
                {
                    double dist = (((fromPt.easting - turnClosestList[i].closePt.easting) * (fromPt.easting - turnClosestList[i].closePt.easting))
                                    + ((fromPt.northing - turnClosestList[i].closePt.northing) * (fromPt.northing - turnClosestList[i].closePt.northing)));

                    if (minDistance >= dist)
                    {

                        minDistance = dist;
                        closestTurnPt = new CClose(turnClosestList[i]);
                    }
                }
            }
        }

        #endregion

        #region SequenceLines

        private bool AddABSequenceLines()
        {
            double inhead = mf.ABLine.abHeading;
            if (!mf.ABLine.isHeadingSameWay) inhead += Math.PI;
            if (inhead > glm.twoPI) inhead -= glm.twoPI;

            double outhead = inhead;
            if (isOutSameCurve) outhead += Math.PI;
            if (outhead > glm.twoPI) outhead -= glm.twoPI;

            //how many points straight out - use youTurnStartOffset setting (value needs to be doubled for actual length)
            double lenny = youTurnStartOffset * 2.0;

            //turn limit lines: the LEAD-IN is where the maneuver "starts" (and where the turn
            //triggers). A long stock lead-in puts the start ~20 m back so you'd have to reverse
            //to it - shorten it to ~2 m of straight just before the arc, near the boundary.
            double lennyIn = mf.tnl.IsFeatureOn && pointSpacing > 0.001
                ? Math.Max(2.0, 2.0 / pointSpacing) : lenny;

            vec3 pt;
            for (int a = 0; a < lennyIn; a++)
            {
                pt.easting = ytList[0].easting - (Math.Sin(inhead) * pointSpacing);
                pt.northing = ytList[0].northing - (Math.Cos(inhead) * pointSpacing);
                pt.heading = inhead;
                ytList.Insert(0, pt);
            }

            int count = ytList.Count;

            //turn limit lines: short lead-OUT too, so the youturn ends ~2 m into the new row
            //(right at the line) and hands back to AB/curve guidance instead of driving a long
            //tail that keeps steering if the driver stops or backs up.
            double lennyOut = mf.tnl.IsFeatureOn && pointSpacing > 0.001
                ? Math.Max(2.0, 2.0 / pointSpacing) : lenny;

            for (int i = 0; i <= lennyOut; i++)
            {
                pt.easting = ytList[count - 1].easting - (Math.Sin(outhead) * i * pointSpacing);
                pt.northing = ytList[count - 1].northing - (Math.Cos(outhead) * i * pointSpacing);
                pt.heading = outhead;
                ytList.Add(pt);
            }

            double distancePivotToTurnLine;
            count = ytList.Count;
            for (int i = 0; i < count; i += 2)
            {
                distancePivotToTurnLine = glm.DistanceSquared(ytList[i], mf.pivotAxlePos);
                if (distancePivotToTurnLine > 3)
                {
                    isTurnCreationTooClose = false;
                }
                else
                {
                    //set the flag to Critical stop machine
                    FailCreate();
                    return false;
                }
            }

            return true;
        }

        //TODO: is for some reason making longer for omegaturn....
        private bool AddCurveSequenceLines()
        {
            //how many points striaght out - use youTurnStartOffset setting (value needs to be doubled for actual length)
            double lenny = youTurnStartOffset * 2.0;

            //turn limit lines: short lead-in so the turn starts just before the arc (see AddABSequenceLines)
            double lennyIn = mf.tnl.IsFeatureOn && pointSpacing > 0.001
                ? Math.Max(2.0, 2.0 / pointSpacing) : lenny;

            bool sameWay = mf.curve.isHeadingSameWay;
            int a = sameWay ? -1 : 1;

            for (int i = 0; i < lennyIn && i > -lennyIn; i += a)
            {
                ytList.Insert(0, new vec3(mf.curve.curList[inClosestTurnPt.curveIndex]));
                inClosestTurnPt.curveIndex += a;
                if (inClosestTurnPt.curveIndex < 2 || inClosestTurnPt.curveIndex > mf.curve.curList.Count - 3)
                {
                    FailCreate();
                    return false;
                }
            }
            if (isOutSameCurve) sameWay = !sameWay;
            a = sameWay ? -1 : 1;

            for (int i = 0; i < lennyIn && i > -lennyIn; i += a)
            {
                ytList.Add(new vec3(nextCurve[outClosestTurnPt.curveIndex]));
                outClosestTurnPt.curveIndex += a;
                if (outClosestTurnPt.curveIndex < 2 || outClosestTurnPt.curveIndex > mf.curve.curList.Count - 3)
                {
                    FailCreate();
                    return false;
                }
            }

            return true;
        }

        #endregion

        /// <summary>/// Calculates the crosing point between the two lines and returns the easting and northing in the reference vaiables
        /// returns 0 for no collision and 1 for collision /// </summary>
        public int GetLineIntersection(double p0x, double p0y, double p1x, double p1y,
                double p2x, double p2y, double p3x, double p3y, ref double iEast, ref double iNorth)
        {
            double s1x, s1y, s2x, s2y;
            s1x = p1x - p0x;
            s1y = p1y - p0y;

            s2x = p3x - p2x;
            s2y = p3y - p2y;

            double s, t;
            s = (-s1y * (p0x - p2x) + s1x * (p0y - p2y)) / (-s2x * s1y + s1x * s2y);

            if (s >= 0 && s <= 1)
            {
                //check oher side
                t = (s2x * (p0y - p2y) - s2y * (p0x - p2x)) / (-s2x * s1y + s1x * s2y);
                if (t >= 0 && t <= 1)
                {
                    // Collision detected
                    iEast = p0x + (t * s1x);
                    iNorth = p0y + (t * s1y);
                    return 1;
                }
            }

            return 0; // No collision
        }

        private List<vec3> MoveTurnInsideTurnLine(List<vec3> uTurnList, double head, bool deleteSecondHalf, bool invertHeading)
        {
            //step 1 make array out of the list so that we can modify the position
            double cosHead = Math.Cos(head);
            double sinHead = Math.Sin(head);
            int cnt = uTurnList.Count;
            vec3[] arr2 = new vec3[cnt];
            uTurnList.CopyTo(arr2);
            uTurnList.Clear();

            semiCircleIndex = -1;
            //step 2 move the turn inside with steps of 1 meter
            bool pointOutOfBnd = isOutOfBounds;
            int j = 0;
            int stopIfWayOut = 0;
            while (pointOutOfBnd)
            {
                stopIfWayOut++;
                pointOutOfBnd = false;
                mf.distancePivotToTurnLine = glm.Distance(arr2[0], mf.pivotAxlePos);

                for (int i = 0; i < cnt; i++)
                {
                    arr2[i].easting -= (sinHead);
                    arr2[i].northing -= (cosHead);
                }

                for (; j < cnt; j += 1)
                {
                    if (!IsPointAllowedForTurn(arr2[j]))
                    {
                        pointOutOfBnd = true;
                        if (j > 0) j--;
                        break;
                    }
                }

                if (stopIfWayOut == 1000 || (mf.distancePivotToTurnLine < 3))
                {
                    //for some reason it doesn't go inside boundary, return empty list
                    return uTurnList;
                }
            }

            //step 3, we ar now inside turnline, move the turn forward until it hits the turnfence in steps of 0.1 meters
            //safety cap on the forward travel: with a turn limit line the allowed region can
            //reach far (e.g. a line drawn near-parallel to travel), which would otherwise push
            //the turn far down the pass or loop forever. Stock hits the headland well within this.
            int fwdSteps = 0;
            while (!pointOutOfBnd)
            {
                if (++fwdSteps > 2000) return uTurnList; //~200 m: give up, upstream fails the turn
                for (int i = 0; i < cnt; i++)
                {
                    arr2[i].easting += (sinHead * 0.1);
                    arr2[i].northing += (cosHead * 0.1);
                }

                for (int a = 0; a < cnt; a++)
                {
                    if (!IsPointAllowedForTurn(arr2[a]))
                    {
                        semiCircleIndex = a;
                        pointOutOfBnd = true;
                        break;

                    }
                }
            }

            //step 4, Should we delete the points after the one that is outside? and where the points made in the wrong direction?
            for (int i = 0; i < cnt; i++)
            {
                if (i == semiCircleIndex && deleteSecondHalf)
                    break;
                if (invertHeading) arr2[i].heading += Math.PI;
                if (arr2[i].heading >= glm.twoPI) arr2[i].heading -= glm.twoPI;
                else if (arr2[i].heading < 0) arr2[i].heading += glm.twoPI;
                uTurnList.Add(arr2[i]);
            }

            //we have succesfully moved the turn inside
            isOutOfBounds = false;

            //if empty - no creation.
            return uTurnList;

        }

        public void SmoothYouTurn(int smPts)
        {
            //countExit the reference list of original curve
            int cnt = ytList.Count;

            //the temp array
            vec3[] arr = new vec3[cnt];

            //read the points before and after the setpoint
            for (int s = 0; s < smPts / 2; s++)
            {
                arr[s].easting = ytList[s].easting;
                arr[s].northing = ytList[s].northing;
                arr[s].heading = ytList[s].heading;
            }

            for (int s = cnt - (smPts / 2); s < cnt; s++)
            {
                arr[s].easting = ytList[s].easting;
                arr[s].northing = ytList[s].northing;
                arr[s].heading = ytList[s].heading;
            }

            //average them - center weighted average
            for (int i = smPts / 2; i < cnt - (smPts / 2); i++)
            {
                for (int j = -smPts / 2; j < smPts / 2; j++)
                {
                    arr[i].easting += ytList[j + i].easting;
                    arr[i].northing += ytList[j + i].northing;
                }
                arr[i].easting /= smPts;
                arr[i].northing /= smPts;
                arr[i].heading = ytList[i].heading;
            }

            ytList?.Clear();

            //calculate new headings on smoothed line
            for (int i = 1; i < cnt - 1; i++)
            {
                arr[i].heading = Math.Atan2(arr[i + 1].easting - arr[i].easting, arr[i + 1].northing - arr[i].northing);
                if (arr[i].heading < 0) arr[i].heading += glm.twoPI;
                ytList.Add(arr[i]);
            }
        }

        //called to initiate turn
        //NV: with the turn limit feature the line swap is DEFERRED - guidance stays on
        //the ORIGINAL row line until the vehicle has actually turned off the row
        //(heading delta > 15 deg); only then the new line becomes active
        private bool turnLineSwapPending = false;

        public void YouTurnTrigger()
        {
            //trigger pulled
            isYouTurnTriggered = true;

            if (!isGoingStraightThrough)
            {
                // Save state so we can restore if user cancels mid-turn
                savedCurvePathsAway = mf.curve.howManyPathsAway;
                savedABLinePathsAway = mf.ABLine.howManyPathsAway;
                savedCurveHeadingSameWay = mf.curve.isHeadingSameWay;
                savedABLineHeadingSameWay = mf.ABLine.isHeadingSameWay;
                savedIsTurnLeft = isTurnLeft;
                savedRowSkipsWidth = rowSkipsWidth;
                savedTurnSkips = turnSkips;
                savedPreviousBigSkip = previousBigSkip;

                if (mf.tnl.IsFeatureOn) turnLineSwapPending = true;
                else ApplyTriggerLineSwap();
            }
        }

        private void ApplyTriggerLineSwap()
        {
            mf.curve.howManyPathsAway += (isTurnLeft ^ mf.curve.isHeadingSameWay) ? rowSkipsWidth : -rowSkipsWidth;
            mf.curve.isHeadingSameWay = !mf.curve.isHeadingSameWay;

            mf.ABLine.howManyPathsAway += (isTurnLeft ^ mf.ABLine.isHeadingSameWay) ? rowSkipsWidth : -rowSkipsWidth;
            mf.ABLine.isHeadingSameWay = !mf.ABLine.isHeadingSameWay;

            if (skipMode == SkipMode.Alternative && rowSkipsWidth2 > 1)
            {
                if (--turnSkips == 0)
                {
                    isTurnLeft = !isTurnLeft;
                    turnSkips = rowSkipsWidth2 * 2 - 1;
                }
                else if (previousBigSkip = !previousBigSkip)
                    rowSkipsWidth = rowSkipsWidth2 - 1;
                else
                    rowSkipsWidth = rowSkipsWidth2;
            }
            else isTurnLeft = !isTurnLeft;
        }

        // Restores path state to before the trigger when user cancels a turn in progress
        public void RestorePreTriggerState()
        {
            if (!isYouTurnTriggered || isGoingStraightThrough) return;

            mf.curve.howManyPathsAway = savedCurvePathsAway;
            mf.curve.isHeadingSameWay = savedCurveHeadingSameWay;
            mf.ABLine.howManyPathsAway = savedABLinePathsAway;
            mf.ABLine.isHeadingSameWay = savedABLineHeadingSameWay;
            isTurnLeft = savedIsTurnLeft;
            rowSkipsWidth = savedRowSkipsWidth;
            turnSkips = savedTurnSkips;
            previousBigSkip = savedPreviousBigSkip;
        }

        //Normal copmpletion of youturn
        public void CompleteYouTurn()
        {
            //safety: a completed turn must always end up on the new line
            if (turnLineSwapPending) { turnLineSwapPending = false; ApplyTriggerLineSwap(); }
            isYouTurnTriggered = false;
            ResetCreatedYouTurn();
            mf.sounds.isBoundAlarming = false;
        }

        public void Set_Alternate_skips()
        {
            rowSkipsWidth2 = rowSkipsWidth;
            turnSkips = rowSkipsWidth2 * 2 - 1;
            previousBigSkip = false;
        }

        //something went seriously wrong so reset everything
        public void ResetYouTurn()
        {
            //fix you turn
            isYouTurnTriggered = false;
            mf.makeUTurnCounter = 0;
            ytList?.Clear();
            ResetCreatedYouTurn();
            mf.sounds.isBoundAlarming = false;
            isTurnCreationTooClose = false;
            isTurnCreationNotCrossingError = false;
            mf.p_239.pgn[mf.p_239.uturn] = 0;
        }

        public void ResetCreatedYouTurn()
        {
            youTurnPhase = 0;
            ytList?.Clear();
            mf.makeUTurnCounter = 0;
            mf.p_239.pgn[mf.p_239.uturn] = 0;
            isOutSameCurve = false;
            isGoingStraightThrough = false;
            maxProgressIndexReached = 0;
            afterStopStopped = false;
            turnOvershot = false;
            turnArmed = false;
            kPart2List?.Clear();
            kTurnStage = 0;
            kRevReached = false;
            kFwdHeldDist = 0;
            kPart2EntrySide = 0;
            ytRedIndex = -1;
            part2RedIndex = -1;
            turnLineSwapPending = false;
        }

        public void FailCreate()
        {
            //fail
            isOutOfBounds = true;
            isTurnCreationTooClose = true;
            mf.mc.isOutOfBounds = true;
            youTurnPhase = 11;
        }

        public void BuildManualYouLateral(bool isTurnLeft)
        {
            //point on AB line closest to pivot axle point from ABLine PurePursuit
            if (mf.trk.idx > -1 && mf.trk.gArr.Count > 0)
            {
                if (mf.trk.gArr[mf.trk.idx].mode == TrackMode.AB)
                {
                    isHeadingSameWay = mf.ABLine.isHeadingSameWay;
                }
                else
                {
                    isHeadingSameWay = mf.curve.isHeadingSameWay;
                }
                if (isHeadingSameWay == isTurnLeft)
                {
                    mf.curve.howManyPathsAway += 1;
                    mf.ABLine.howManyPathsAway += 1;
                }
                else
                {
                    mf.curve.howManyPathsAway -= 1;
                    mf.ABLine.howManyPathsAway -= 1;
                }
                return;
            }
            else return;
        }

        //build the points and path of youturn to be scaled and transformed
        public void BuildManualYouTurn(bool isTurnRight, bool isTurnButtonTriggered)
        {
            double head;
            //point on AB line closest to pivot axle point from ABLine PurePursuit
            if (mf.trk.idx > -1 && mf.trk.gArr.Count > 0)
            {
                if (mf.trk.gArr[mf.trk.idx].mode == TrackMode.AB)
                {
                    rEastYT = mf.ABLine.rEastAB;
                    rNorthYT = mf.ABLine.rNorthAB;
                    isHeadingSameWay = mf.ABLine.isHeadingSameWay;
                    head = mf.ABLine.abHeading;
                }

                else
                {
                    rEastYT = mf.curve.rEastCu;
                    rNorthYT = mf.curve.rNorthCu;
                    isHeadingSameWay = mf.curve.isHeadingSameWay;
                    head = mf.curve.manualUturnHeading;
                }
            }
            else return;

            //grab the vehicle widths and offsets
            double turnOffset = (mf.tool.width - mf.tool.overlap) * rowSkipsWidth + (isTurnRight ? mf.tool.offset * 2.0 : -mf.tool.offset * 2.0);

            CDubins dubYouTurnPath = new CDubins();
            CDubins.turningRadius = youTurnRadius;

            //if its straight across it makes 2 loops instead so goal is a little lower then start
            if (!isHeadingSameWay) head += 3.14;
            else head -= 0.01;

            //move the start forward 2 meters, this point is critical to formation of uturn
            rEastYT += (Math.Sin(head) * 4);
            rNorthYT += (Math.Cos(head) * 4);

            //now we have our start point
            vec3 start = new vec3(rEastYT, rNorthYT, head);
            vec3 goal = new vec3();

            //now we go the other way to turn round
            head -= Math.PI;
            if (head < 0) head += glm.twoPI;

            //set up the goal point for Dubins
            goal.heading = head;
            if (isTurnButtonTriggered)
            {
                if (isTurnRight)
                {
                    goal.easting = rEastYT - (Math.Cos(-head) * turnOffset);
                    goal.northing = rNorthYT - (Math.Sin(-head) * turnOffset);
                }
                else
                {
                    goal.easting = rEastYT + (Math.Cos(-head) * turnOffset);
                    goal.northing = rNorthYT + (Math.Sin(-head) * turnOffset);
                }
            }

            //generate the turn points
            ytList = dubYouTurnPath.GenerateDubins(start, goal);

            isTurnLeft = !isTurnRight;
            YouTurnTrigger();
        }

        public int onA;
        private int maxProgressIndexReached = 0;

        /// <summary>
        /// Meters left along the turn path from the point the vehicle is at - shown
        /// on the HUD during the maneuver instead of the raw path-point index (onA)
        /// the stock display leaked (HUD cleanup 2026-07-11).
        /// </summary>
        public double RemainingTurnDistance()
        {
            double d = 0;
            for (int i = Math.Max(0, onA); i < ytList.Count - 1; i++)
                d += glm.Distance(ytList[i], ytList[i + 1]);
            return d;
        }

        /// <summary>
        /// Field fix 2c (2026-07-10): which way the driver should drive during the
        /// split K maneuver. 1 = forward, -1 = reverse, 0 = no instruction (not a
        /// running K turn). Drawn big on the HUD with a beep on every change.
        /// </summary>
        public int GuidedDriveDir()
        {
            if (!isYouTurnTriggered || !mf.tnl.IsFeatureOn || activeTurnStyle != 1) return 0;
            if (kTurnStage == 1)
                return (ytList.Count > 3 && A >= ytList.Count - 3) ? -1 : 1;
            if (kTurnStage == 2) return kRevReached ? 1 : -1;
            return 1;
        }

        //determine distance from youTurn guidance line
        public bool DistanceFromYouTurnLine()
        {
            //grab a copy from main - the steer position
            double minDistA = 1000000, minDistB = 1000000;
            int ptCount = ytList.Count;

            if (ptCount > 0)
            {
                //NV: the maneuver has really begun once the vehicle has TURNED OFF the row
                //(heading delta to the row > 15 deg = into the exit radius). Before that the
                //pattern is forward-only: reversing or creeping (< 0.4 km/h) un-triggers the
                //turn (guidance returns to the ORIGINAL row line, the built turn stays armed;
                //driving forward re-triggers it). After turning off, reversing in the K stage
                //machine goes straight to the guided reverse leg instead.
                bool turnedOff = Math.Abs(AngleDiff(mf.fixHeading, ytList[0].heading)) > 0.26;
                if (mf.tnl.IsFeatureOn && (kTurnStage == 0 || kTurnStage == 1) && !turnedOff
                    && (mf.isReverse || Math.Abs(mf.avgSpeed) < 0.4))
                {
                    //nothing was swapped yet if the swap is still pending - just un-trigger
                    if (!turnLineSwapPending) RestorePreTriggerState();
                    turnLineSwapPending = false;
                    isYouTurnTriggered = false;
                    maxProgressIndexReached = 0;
                    return false;
                }

                //the vehicle has turned off the row -> NOW the new line becomes active
                if (mf.tnl.IsFeatureOn && turnLineSwapPending && turnedOff)
                {
                    turnLineSwapPending = false;
                    ApplyTriggerLineSwap();
                }

                //NV rigid K split: part 1 forward -> GUIDED reverse back along the tangent
                //line -> swap to part 2 -> forward again. Autosteer stays on the whole time.
                if (mf.tnl.IsFeatureOn && kTurnStage > 0)
                {
                    vec3 kPiv = mf.isStanleyUsed ? mf.steerAxlePos : mf.pivotAxlePos;

                    if (kTurnStage == 1 && mf.isReverse)
                    {
                        kTurnStage = 2;
                        //guidance for the reverse leg = the straight tangent line from the
                        //reverse stop point (start of part 2) to the end of part 1; ordered
                        //forward so the follower's reverse walk (backward) guides correctly
                        if (kPart2List.Count > 0 && ytList.Count > 1)
                        {
                            vec3 pEnd = ytList[ytList.Count - 1];
                            vec3 rStop = kPart2List[0];
                            double dxr = pEnd.easting - rStop.easting, dyr = pEnd.northing - rStop.northing;
                            double lenR = Math.Sqrt(dxr * dxr + dyr * dyr);
                            if (lenR > 1.0)
                            {
                                //extend the leg 15 m BEHIND the reverse stop point so
                                //reversing too far never loses guidance
                                double ux = dxr / lenR, uy = dyr / lenR;
                                double sx = rStop.easting - ux * 15.0, sy = rStop.northing - uy * 15.0;
                                double tot = lenR + 15.0;
                                double hR = Math.Atan2(dxr, dyr);
                                if (hR < 0) hR += glm.twoPI;
                                int nR = Math.Max(2, (int)(tot / 0.5));
                                ytList.Clear();
                                for (int i2 = 0; i2 <= nR; i2++)
                                    ytList.Add(new vec3(sx + ux * tot * i2 / nR, sy + uy * tot * i2 / nR, hR));
                                ytRedIndex = -1;
                                ptCount = ytList.Count;
                            }
                        }
                    }

                    if (kTurnStage == 2)
                    {
                        //full search each frame - the closest point walks BACK down the leg
                        maxProgressIndexReached = 0;

                        if (!kRevReached && kPart2List.Count > 3 && glm.Distance(kPiv, kPart2List[0]) < 2.0)
                            kRevReached = true;

                        //field fix 2a (2026-07-10): a power-shuttle reverser flips
                        //reverse->forward without the speed dropping under the stop
                        //threshold - forward motion held over 0.3 m counts as the
                        //direction change too, not just a full stop
                        if (kRevReached && !mf.isReverse)
                            kFwdHeldDist += Math.Abs(mf.distanceCurrentStepFix);
                        else
                            kFwdHeldDist = 0;

                        //the reverse guide line stays active while still reversing;
                        //part 2 (new-row entry) activates on a stop OR a held
                        //direction change (reverser)
                        if (kRevReached && (Math.Abs(mf.avgSpeed) < 0.3 || kFwdHeldDist > 0.3))
                        {
                            kRevReached = false;
                            kFwdHeldDist = 0;
                            kPart2EntrySide = 0;
                            ytList.Clear();
                            ytList.AddRange(kPart2List);
                            kPart2List.Clear();
                            ytRedIndex = part2RedIndex;
                            part2RedIndex = -1;
                            kTurnStage = 3;
                            A = 0; B = 1;
                            ptCount = ytList.Count;
                        }
                    }
                }
                bool kSplitHold = mf.tnl.IsFeatureOn && (kTurnStage == 1 || kTurnStage == 2);

                //field fix 2b (2026-07-10): part 2 used to complete only when the
                //follower reached the very end of the turn path - well past where the
                //driver expects. The final segment of part 2 lies ON the new row's
                //line, so end the turn the moment the FRONT axle reaches that line
                //(sign flip of its lateral offset) and hand the merge to guidance.
                if (mf.tnl.IsFeatureOn && kTurnStage == 3 && ptCount > 1)
                {
                    vec3 rowPt = ytList[ptCount - 1];
                    double rx = Math.Sin(rowPt.heading), ry = Math.Cos(rowPt.heading);
                    double side = (mf.steerAxlePos.easting - rowPt.easting) * ry
                                - (mf.steerAxlePos.northing - rowPt.northing) * rx;

                    if (kPart2EntrySide == 0)
                    {
                        //sample the approach side once clearly off the line; starting
                        //on the line keeps the stock end-of-path completion instead
                        if (Math.Abs(side) > 0.2) kPart2EntrySide = side > 0 ? 1 : -1;
                    }
                    else if (Math.Abs(side) < 0.05 || (side > 0 ? 1 : -1) != kPart2EntrySide)
                    {
                        CompleteYouTurn();
                        return false;
                    }
                }

                if (mf.isStanleyUsed)
                {
                    vec3 pivot = mf.steerAxlePos;

                    //find the closest 2 points to current fix
                    //Start search from max progress minus small lookback to prevent jumping back to start
                    //when start and end positions overlap
                    int searchStartIndex = Math.Max(0, maxProgressIndexReached - 5);
                    for (int t = searchStartIndex; t < ptCount; t++)
                    {
                        double dist = ((pivot.easting - ytList[t].easting) * (pivot.easting - ytList[t].easting))
                                        + ((pivot.northing - ytList[t].northing) * (pivot.northing - ytList[t].northing));
                        if (dist < minDistA)
                        {
                            minDistB = minDistA;
                            B = A;
                            minDistA = dist;
                            A = t;
                        }
                        else if (dist < minDistB)
                        {
                            minDistB = dist;
                            B = t;
                        }
                    }

                    if (minDistA > 16)
                    {
                        CompleteYouTurn();
                        return false;
                    }

                    //just need to make sure the points continue ascending or heading switches all over the place
                    if (A > B)
                    {
                        (B, A) = (A, B);
                    }

                    //minDistA = 100;
                    //int closestPt = 0;
                    //for (int i = 0; i < ptCount; i++)
                    //{
                    //    double distancePiv = glm.DistanceSquared(ytList[i], pivot);
                    //    if (distancePiv < minDistA)
                    //    {
                    //        minDistA = distancePiv;
                    //    }
                    //}

                    //feed backward to turn slower to keep pivot on
                    if (A < 0)
                    {
                        A = 0;
                    }
                    B = A + 1;

                    // Track maximum progress through the path
                    if (A > maxProgressIndexReached)
                        maxProgressIndexReached = A;

                    // Only complete if we've progressed through at least 50% of the path
                    // This prevents premature completion when start and end positions are the same
                    bool hasProgressedEnough = maxProgressIndexReached > (ptCount / 2);

                    //return and reset if too far away or end of the line
                    //(K split holds at the end of part 1 - the reverse leg follows)
                    if (B >= ptCount - 1 && hasProgressedEnough && !kSplitHold)
                    {
                        CompleteYouTurn();
                        return false;
                    }

                    //turn limit lines: a U-turn has no reverse phase, so backing up after the
                    //arc means the driver is on the new row - end the turn, hand back to the line
                    if (mf.tnl.IsFeatureOn && activeTurnStyle == 0 && mf.isReverse && hasProgressedEnough)
                    {
                        CompleteYouTurn();
                        return false;
                    }

                    if (activeTurnStyle == 1 && mf.isReverse)
                    {
                        if (!mf.tnl.IsFeatureOn)
                        {
                            CompleteYouTurn();
                            return true;
                        }
                        //rigid K: stage 1/2 reverse is GUIDED (keep steering along the path);
                        //stage 0/3 after the crossing -> hand the guided reverse to the AB line
                        if (kTurnStage != 1 && kTurnStage != 2 && hasProgressedEnough)
                        {
                            CompleteYouTurn();
                            return false;
                        }
                    }

                    //get the distance from currently active AB line, precalc the norm of line
                    double dx = ytList[B].easting - ytList[A].easting;
                    double dz = ytList[B].northing - ytList[A].northing;
                    if (Math.Abs(dx) < Double.Epsilon && Math.Abs(dz) < Double.Epsilon) return false;

                    double abHeading = ytList[A].heading;

                    //how far from current AB Line is steer point 90 degrees from steer position
                    distanceFromCurrentLine = ((dz * pivot.easting) - (dx * pivot.northing) + (ytList[B].easting
                                * ytList[A].northing) - (ytList[B].northing * ytList[A].easting))
                                    / Math.Sqrt((dz * dz) + (dx * dx));

                    //Calc point on ABLine closest to current position and 90 degrees to segment heading
                    double U = (((pivot.easting - ytList[A].easting) * dx)
                                + ((pivot.northing - ytList[A].northing) * dz))
                                / ((dx * dx) + (dz * dz));

                    //critical point used as start for the uturn path - critical
                    rEastYT = ytList[A].easting + (U * dx);
                    rNorthYT = ytList[A].northing + (U * dz);

                    //the first part of stanley is to extract heading error
                    double abFixHeadingDelta = (pivot.heading - abHeading);

                    //Fix the circular error - get it from -Pi/2 to Pi/2
                    if (abFixHeadingDelta > Math.PI) abFixHeadingDelta -= Math.PI;
                    else if (abFixHeadingDelta < Math.PI) abFixHeadingDelta += Math.PI;
                    if (abFixHeadingDelta > glm.PIBy2) abFixHeadingDelta -= Math.PI;
                    else if (abFixHeadingDelta < -glm.PIBy2) abFixHeadingDelta += Math.PI;

                    if (mf.isReverse) abFixHeadingDelta *= -1;
                    //normally set to 1, less then unity gives less heading error.
                    abFixHeadingDelta *= mf.vehicle.stanleyHeadingErrorGain;
                    if (abFixHeadingDelta > 0.74) abFixHeadingDelta = 0.74;
                    if (abFixHeadingDelta < -0.74) abFixHeadingDelta = -0.74;

                    //the non linear distance error part of stanley
                    steerAngleYT = Math.Atan((distanceFromCurrentLine * mf.vehicle.stanleyDistanceErrorGain) / ((mf.avgSpeed * 0.277777) + 1));

                    //clamp it to max 42 degrees
                    if (steerAngleYT > 0.74) steerAngleYT = 0.74;
                    if (steerAngleYT < -0.74) steerAngleYT = -0.74;

                    //add them up and clamp to max in vehicle settings
                    steerAngleYT = glm.toDegrees((steerAngleYT + abFixHeadingDelta * mf.vehicle.uturnCompensation) * -1.0);
                    if (steerAngleYT < -mf.vehicle.maxSteerAngle) steerAngleYT = -mf.vehicle.maxSteerAngle;
                    if (steerAngleYT > mf.vehicle.maxSteerAngle) steerAngleYT = mf.vehicle.maxSteerAngle;
                }
                else
                {
                    vec3 pivot = mf.pivotAxlePos;

                    //find the closest 2 points to current fix
                    //Start search from max progress minus small lookback to prevent jumping back to start
                    //when start and end positions overlap
                    int searchStartIndex = Math.Max(0, maxProgressIndexReached - 5);
                    for (int t = searchStartIndex; t < ptCount; t++)
                    {
                        double dist = ((pivot.easting - ytList[t].easting) * (pivot.easting - ytList[t].easting))
                                        + ((pivot.northing - ytList[t].northing) * (pivot.northing - ytList[t].northing));
                        if (dist < minDistA)
                        {
                            minDistB = minDistA;
                            B = A;
                            minDistA = dist;
                            A = t;
                        }
                        else if (dist < minDistB)
                        {
                            minDistB = dist;
                            B = t;
                        }
                    }

                    //just need to make sure the points continue ascending or heading switches all over the place
                    if (A > B)
                    {
                        (B, A) = (A, B);
                    }

                    onA = A;

                    // Track maximum progress through the path
                    if (A > maxProgressIndexReached)
                        maxProgressIndexReached = A;

                    double distancePiv = glm.Distance(ytList[A], pivot);

                    // Only complete if we've progressed through at least 50% of the path
                    // This prevents premature completion when start and end positions are the same
                    bool hasProgressedEnough = maxProgressIndexReached > (ptCount / 2);

                    //(K split holds at the end of part 1 - the reverse leg follows)
                    if (((A > 0 && distancePiv > 2) || (B >= ptCount - 1)) && hasProgressedEnough && !kSplitHold)
                    {
                        CompleteYouTurn();
                        return false;
                    }

                    //turn limit lines: U-turn has no reverse phase - backing up after the arc
                    //ends the turn and hands guidance back to the AB/curve line
                    if (mf.tnl.IsFeatureOn && activeTurnStyle == 0 && mf.isReverse && hasProgressedEnough)
                    {
                        CompleteYouTurn();
                        return false;
                    }

                    //rigid K: stage 1/2 reverse is GUIDED; stage 0/3 after the crossing ->
                    //hand the guided reverse to the AB line (stock case stays in the goal loop)
                    if (mf.tnl.IsFeatureOn && activeTurnStyle == 1 && mf.isReverse
                        && kTurnStage != 1 && kTurnStage != 2 && hasProgressedEnough)
                    {
                        CompleteYouTurn();
                        return false;
                    }

                    //get the distance from currently active AB line
                    double dx = ytList[B].easting - ytList[A].easting;
                    double dz = ytList[B].northing - ytList[A].northing;

                    if (Math.Abs(dx) < double.Epsilon && Math.Abs(dz) < double.Epsilon) return false;

                    //how far from current AB Line is fix
                    distanceFromCurrentLine = ((dz * pivot.easting) - (dx * pivot.northing) + (ytList[B].easting
                                * ytList[A].northing) - (ytList[B].northing * ytList[A].easting))
                                    / Math.Sqrt((dz * dz) + (dx * dx));

                    // ** Pure pursuit ** - calc point on ABLine closest to current position
                    double U = (((pivot.easting - ytList[A].easting) * dx)
                                + ((pivot.northing - ytList[A].northing) * dz))
                                / ((dx * dx) + (dz * dz));

                    rEastYT = ytList[A].easting + (U * dx);
                    rNorthYT = ytList[A].northing + (U * dz);

                    //sharp turns on you turn.
                    //update base on autosteer settings and distance from line
                    double goalPointDistance = mf.vehicle.UpdateGoalPointDistance();

                    //goalPointDistance *= mf.vehicle.uturnCompensation;

                    isHeadingSameWay = true;
                    bool ReverseHeading = !mf.isReverse;

                    int count = ReverseHeading ? 1 : -1;
                    vec3 start = new vec3(rEastYT, rNorthYT, 0);
                    double distSoFar = 0;

                    for (int i = ReverseHeading ? B : A; i < ptCount && i >= 0; i += count)
                    {
                        // used for calculating the length squared of next segment.
                        double tempDist = glm.Distance(start, ytList[i]);

                        //will we go too far?
                        if ((tempDist + distSoFar) > goalPointDistance)
                        {
                            double j = (goalPointDistance - distSoFar) / tempDist; // the remainder to yet travel

                            goalPointYT.easting = (((1 - j) * start.easting) + (j * ytList[i].easting));
                            goalPointYT.northing = (((1 - j) * start.northing) + (j * ytList[i].northing));
                            break;
                        }
                        else distSoFar += tempDist;

                        start = ytList[i];

                        if (i == ptCount - 1)//goalPointDistance is longer than remaining u-turn
                        {
                            if (kSplitHold)
                            {
                                //K split: hold at the end of part 1, aim at the last point
                                //until the driver reverses (guided) back toward part 2
                                goalPointYT.easting = ytList[ptCount - 1].easting;
                                goalPointYT.northing = ytList[ptCount - 1].northing;
                                break;
                            }
                            CompleteYouTurn();
                            return false;
                        }

                        if (!mf.tnl.IsFeatureOn && activeTurnStyle == 1 && mf.isReverse)
                        {
                            CompleteYouTurn();
                            return true;
                        }
                    }

                    //calc "D" the distance from pivot axle to lookahead point
                    double goalPointDistanceSquared = glm.DistanceSquared(goalPointYT.northing, goalPointYT.easting, pivot.northing, pivot.easting);

                    //calculate the the delta x in local coordinates and steering angle degrees based on wheelbase
                    double localHeading = glm.twoPI - mf.fixHeading;
                    ppRadiusYT = goalPointDistanceSquared / (2 * (((goalPointYT.easting - pivot.easting) * Math.Cos(localHeading)) + ((goalPointYT.northing - pivot.northing) * Math.Sin(localHeading))));

                    steerAngleYT = glm.toDegrees(Math.Atan(2 * (((goalPointYT.easting - pivot.easting) * Math.Cos(localHeading))
                        + ((goalPointYT.northing - pivot.northing) * Math.Sin(localHeading))) * mf.vehicle.VehicleConfig.Wheelbase / goalPointDistanceSquared));

                    steerAngleYT *= mf.vehicle.uturnCompensation;

                    if (steerAngleYT < -mf.vehicle.maxSteerAngle) steerAngleYT = -mf.vehicle.maxSteerAngle;
                    if (steerAngleYT > mf.vehicle.maxSteerAngle) steerAngleYT = mf.vehicle.maxSteerAngle;

                    if (ppRadiusYT < -500) ppRadiusYT = -500;
                    if (ppRadiusYT > 500) ppRadiusYT = 500;

                    radiusPointYT.easting = pivot.easting + (ppRadiusYT * Math.Cos(localHeading));
                    radiusPointYT.northing = pivot.northing + (ppRadiusYT * Math.Sin(localHeading));

                    //distance is negative if on left, positive if on right
                    if (!isHeadingSameWay)
                        distanceFromCurrentLine *= -1.0;
                }

                //used for smooth mode
                mf.vehicle.modeActualXTE = (distanceFromCurrentLine);

                //Convert to centimeters
                mf.guidanceLineDistanceOff = (short)Math.Round(distanceFromCurrentLine * 1000.0, MidpointRounding.AwayFromZero);
                mf.guidanceLineSteerAngle = (short)(steerAngleYT * 100);
                return true;
            }
            else
            {
                CompleteYouTurn();
                return false;
            }
        }

        //Duh.... What does this do....
        public void DrawYouTurn()
        {
            if (ytList.Count < 3) return;

            GL.PointSize(mf.ABLine.lineWidth + 2);

            if (isYouTurnTriggered)
                GL.Color3(0.95f, 0.5f, 0.95f);            //maneuver running
            else if (isOutOfBounds || turnOvershot)
                GL.Color3(0.9495f, 0.395f, 0.325f);      //red: overshot (reverse) or out of bounds
            else if (turnArmed)
                GL.Color3(1.0f, 0.55f, 0.0f);            //orange: in the trigger window - turn is next (even stopped)
            else
                GL.Color3(0.395f, 0.925f, 0.30f);        //green: approaching, turn will arm

            GL.Begin(PrimitiveType.Points);
            for (int i = 0; i < ytList.Count; i++)
            {
                GL.Vertex3(ytList[i].easting, ytList[i].northing, 0);
            }
            GL.End();

            //NV rigid turns: red tail = overshoot / crossing past the new row (reverse there)
            if (ytRedIndex > 0 && ytRedIndex < ytList.Count)
            {
                GL.Color3(0.9495f, 0.395f, 0.325f);
                GL.Begin(PrimitiveType.Points);
                for (int i = ytRedIndex; i < ytList.Count; i++)
                    GL.Vertex3(ytList[i].easting, ytList[i].northing, 0);
                GL.End();
            }

            //K split: pending part 2 drawn sparse/dim (dotted) until the reverse is driven
            if (kTurnStage > 0 && kTurnStage < 3 && kPart2List.Count > 0)
            {
                GL.PointSize(mf.ABLine.lineWidth);
                GL.Color3(0.75f, 0.75f, 0.75f);
                GL.Begin(PrimitiveType.Points);
                for (int i = 0; i < kPart2List.Count; i += 3)
                    GL.Vertex3(kPart2List[i].easting, kPart2List[i].northing, 0);
                GL.End();
            }

            //GL.PointSize(12.0f);
            //GL.Begin(PrimitiveType.Points);
            //GL.Color3(0.95f, 0.73f, 1.0f);
            //GL.Vertex3(inClosestTurnPt.closePt.easting, inClosestTurnPt.closePt.northing, 0);
            //GL.Color3(0.395f, 0.925f, 0.30f);
            //GL.Vertex3(outClosestTurnPt.closePt.easting, outClosestTurnPt.closePt.northing, 0);
            //GL.End();
            //GL.PointSize(1.0f);

            //if (nextCurve != null)
            //{
            //    GL.Begin(PrimitiveType.Points);
            //    GL.Color3(0.95f, 0.41f, 0.980f);
            //    for (int i = 0; i < nextCurve.curList.Count; i++)
            //    {
            //        GL.Vertex3(nextCurve.curList[i].easting, nextCurve.curList[i].northing, 0);
            //    }
            //    GL.End();
            //}

            //if (ytList2?.Count > 0)
            //{
            //    GL.PointSize(mf.ABLine.lineWidth + 2);
            //    GL.Color3(0.3f, 0.941f, 0.980f);
            //    GL.Begin(PrimitiveType.Points);
            //    for (int i = 0; i < ytList2.Count; i++)
            //    {
            //        GL.Vertex3(ytList2[i].easting, ytList2[i].northing, 0);
            //    }
            //    GL.End();
            //}
        }

        /// <summary>
        /// Rebuilds the uturn after a nudge operation to reflect the new track position.
        /// </summary>
        public void RebuildAfterNudge()
        {
            // Only rebuild if a uturn is currently active
            if (!isYouTurnBtnOn)
                return;

            // Clear existing uturn path
            ytList?.Clear();

            // Rebuild
            ResetCreatedYouTurn();
        }

        public class CClose
        {
            public vec3 closePt = new vec3();
            public int turnLineNum;
            public int turnLineIndex;
            public double turnLineHeading;
            public int curveIndex;

            public CClose()
            {
                closePt = new vec3();
                turnLineNum = -1;
                turnLineIndex = -1;
                turnLineHeading = -1;
                curveIndex = -1;
            }

            public CClose(CClose _clo)
            {
                closePt = new vec3(_clo.closePt);
                turnLineNum = _clo.turnLineNum;
                turnLineIndex = _clo.turnLineIndex;
                turnLineHeading = _clo.turnLineHeading;
                curveIndex = _clo.curveIndex;
            }
        }
    }
}