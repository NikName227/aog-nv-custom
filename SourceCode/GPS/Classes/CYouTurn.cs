using AgLibrary.Logging;
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

        /// <summary>
        /// REQ 2026-08-15-6: while OUR maneuver is running the active pass belongs to the
        /// turn - it is the turn that swaps the line (ApplyTriggerLineSwap). Guidance must
        /// not re-pick it from the vehicle position meanwhile, or the swap is overwritten
        /// within half a second and the line follows the vehicle instead of the plan.
        /// REQ 2026-08-15-7: the hold outlives an approach - the path ends AT the row, so at
        /// speed the vehicle is still settling onto it and the nearest pass is briefly the one
        /// NEXT to it (field 2026-08-15: "skoci na sljedecu ab liniju"). The pass is released
        /// once the vehicle really is on it, or once it has clearly gone somewhere else.
        /// </summary>
        public bool IsPassPinned
        {
            get
            {
                if (!mf.tnl.IsFeatureOn) return false;
                if (isYouTurnTriggered) return true;
                if (!approachHoldPass) return false;

                //REQ 2026-08-15-7 (field 2026-08-15): the hold used to end on geometry - close
                //enough to the pass, or so far off that the driver had "clearly gone elsewhere".
                //That second escape is exactly what let the target line drift away while
                //manoeuvring in reverse. The driver's rule instead: once the target is set it
                //STAYS set until he drives it (the plan marks it worked at 50 %) or skips it.
                if (!mf.plan.IsRuntimeOn)
                {
                    approachHoldPass = false;
                    heldEntry = null;
                    return false;
                }

                if (heldEntry == null) heldEntry = mf.plan.CurrentEntry();

                if (heldEntry != null && (heldEntry.isWorked || heldEntry.isSkipped))
                {
                    approachHoldPass = false;
                    heldEntry = null;
                    Log.EventWriter("LineApproach: target pass done or skipped - pass released");
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// The DIRECTION of travel along the line is pinned only while the maneuver itself
        /// runs. After it, the direction must be re-derived at once (that is what turned a
        /// finished approach into an instant U-turn), while the PASS stays held above.
        /// </summary>
        public bool IsDirPinned { get { return mf.tnl.IsFeatureOn && isYouTurnTriggered; } }

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

        /// <summary>
        /// What the tractor can physically do: bicycle model, R = wheelbase / tan(max steer).
        /// The radius travels with the implement now, and an implement can ask for a tighter turn
        /// than the tractor can drive - that would plan a turn nobody can follow. So this is the
        /// floor. No safety margin on top (decision 2026-08-12): the margin is that one does not
        /// drive at full lock anyway, and a second silent add-on would only hide the real number.
        /// </summary>
        public double MinTurnRadius()
        {
            double wb = Math.Abs(Properties.VehicleSettings.Default.setVehicle_wheelbase);
            double maxDeg = Math.Abs(Properties.VehicleSettings.Default.setVehicle_maxSteerAngle);
            if (wb < 0.1 || maxDeg < 1.0) return 0;      // profile not filled in - no opinion

            double r = wb / Math.Tan(maxDeg * Math.PI / 180.0);
            return r > 0 ? r : 0;
        }

        /// <summary>Implement's wish, never below what the tractor can drive.</summary>
        public double TurnRadius(double wanted)
        {
            double min = MinTurnRadius();
            return (min > 0 && wanted < min) ? min : wanted;
        }

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
        // the NEXT thing that will happen (even while stopped) — drawn green so the driver
        // knows this is the moment to stop or to shuttle forward.
        public bool turnArmed = false;

        // turn-after-stop, field feedback 2026-07-27: reverse -> forward edge detector.
        // On some setups isReverse comes from an IMU/heading delta and chatters, so a
        // reverse only counts once it has been held for reverseDebounceSeconds.
        private bool reverseHeld;
        private DateTime reverseRawSince = DateTime.MaxValue;
        private const double reverseDebounceSeconds = 0.5;

        /// <summary>
        /// Feed the raw reverse flag once per position update. Returns true on the single
        /// update where a DEBOUNCED reverse has just been released into forward travel.
        /// Must be called every cycle (also when turn-after-stop is off) so the detector
        /// never sees a stale gap and reports a phantom edge.
        /// </summary>
        public bool UpdateReverseEdge(bool isReverseRaw)
        {
            if (isReverseRaw)
            {
                if (reverseRawSince == DateTime.MaxValue) reverseRawSince = DateTime.UtcNow;
                if (!reverseHeld
                    && (DateTime.UtcNow - reverseRawSince).TotalSeconds >= reverseDebounceSeconds)
                    reverseHeld = true;
                return false;
            }

            reverseRawSince = DateTime.MaxValue;
            if (!reverseHeld) return false;
            reverseHeld = false;
            return true;      // held reverse -> forward = the driver shuttled
        }

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

            ytList.Capacity = 128;
            ReloadTurnSettings();
            Set_Alternate_skips();
        }

        /// <summary>
        /// Re-read everything the turn geometry is built from. Called on start AND after a
        /// profile switch (F51): the turn radii now live in the VEHICLE profile, so changing
        /// tractor has to change them here too - otherwise the new tractor keeps turning with
        /// the old one's radius.
        /// </summary>
        public void ReloadTurnSettings()
        {
            uturnDistanceFromBoundary = Properties.Settings.Default.set_youTurnDistanceFromBoundary;

            //how far before or after boundary line should turn happen
            youTurnStartOffset = Properties.Settings.Default.set_youTurnExtensionLength;

            rowSkipsWidth = Properties.Settings.Default.set_youSkipWidth;

            youTurnRadius = TurnRadius(Properties.ToolSettings.Default.setTurn_youTurnRadius);

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
            get { double v = Properties.ToolSettings.Default.setTurn_K_exitRadius; return v > 0.5 ? TurnRadius(v) : youTurnRadius; }
        }
        private double UOutRadius
        {
            get { double v = Properties.ToolSettings.Default.setTurn_U_exitRadius; return v > 0.5 ? TurnRadius(v) : youTurnRadius; }
        }
        private double UInRadius
        {
            get { double v = Properties.ToolSettings.Default.setTurn_U_entryRadius; return v > 0.5 ? TurnRadius(v) : youTurnRadius; }
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
                    crossSwitchTrack = null;
                    crossTurnOffset = 0;
                }
                //F50 orchard: the next pass lives on ANOTHER track - aim the same
                //U/K maneuver at its strip and switch tracks when the turn completes
                else if (mf.plan.TryNextTargetCross(out CTrk crossTrk, out double dRight)
                    && Math.Abs(dRight) > 1.0 && Math.Abs(dRight) < 40.0)
                {
                    isTurnLeft = dRight < 0;
                    crossTurnOffset = Math.Abs(dRight);
                    crossSwitchTrack = crossTrk;
                    rowSkipsWidth = 1;
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

            //orchard cross-track target: the real lateral distance to the next pass
            if (crossSwitchTrack != null && crossTurnOffset > 0.1) turnOffset = crossTurnOffset;

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
                    //field 2026-07-16 diag: this abort loop is the suspect for "the
                    //first pass never counts down" - record which gate refused
                    if ((DateTime.UtcNow - lastPlanDiagLog).TotalSeconds > 15)
                    {
                        lastPlanDiagLog = DateTime.UtcNow;
                        bool hasT = mf.plan.TryNextTarget(mf.ABLine.howManyPathsAway, out int t);
                        Log.EventWriter("TurnPlan diag: AB turn abort at offset "
                            + mf.ABLine.howManyPathsAway
                            + (hasT ? ", target " + t + " NOT reachable" : ", NO target"));
                    }
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
                        double over = Properties.ToolSettings.Default.setTurn_U_overshootDist;
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
        // reverse distance driven past the stop point (field 2026-07-16: part 2 and
        // the FORWARD cue wait for a full vehicle length of it)
        private double kRevExtraDist;
        //field 2026-07-16 diag: throttle for the plan-abort log
        private DateTime lastPlanDiagLog = DateTime.MinValue;
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

        // last effective-line pick, logged with every build result (field 2026-07-28
        // diag: says WHICH line the forward ray chose for the wrong-end reports)
        public string lastRayDiag = "";

        // meters the ray origin is backed up when the first cast misses - the row
        // point can already sit past the margin-inset effective line, which used to
        // silently fall through to the boundary ring
        private const double rayBackup = 15.0;

        // effective limit polyline in LOCAL coords: the extrapolated drawn line hit by the
        // travel ray, or the boundary turn line (closed ring) when no line is hit there
        private bool TryGetLocalEff(double head, out List<vec2> eff)
        {
            eff = null;
            List<vec2> world = mf.tnl.GetEffPolylineByRay(new vec2(frOx, frOy), head,
                out int rayLine, out double rayDist);
            if (world == null)
            {
                vec2 backed = new vec2(frOx - Math.Sin(head) * rayBackup,
                                       frOy - Math.Cos(head) * rayBackup);
                world = mf.tnl.GetEffPolylineByRay(backed, head, out rayLine, out rayDist);
                if (world != null) rayDist -= rayBackup;
            }

            if (world != null)
            {
                lastRayDiag = "eff line " + rayLine + " at " + rayDist.ToString("F1") + " m";
            }
            else
            {
                lastRayDiag = "no eff line hit, boundary ring fallback";
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

        //field 2026-07-28 diag: throttle for the wrong-end rejection log below
        private DateTime lastWrongEndLog = DateTime.MinValue;

        private bool FinishRigidPath(List<vec2> p1, int redStart, List<vec2> p2, int p2Red)
        {
            if (p1.Count < 6) { FailCreate(); return false; }

            var w1 = LocalToWorldPath(p1);
            if (glm.Distance(w1[0], mf.pivotAxlePos) < 3) { FailCreate(); return false; }

            //field 2026-07-28: a start behind the vehicle = wrong-end build; committed,
            //it would park at phase 10 waiting for a trigger that never comes. No
            //FailCreate latch - the next fix retries with fresher geometry.
            double startFwd = (w1[0].easting - mf.pivotAxlePos.easting) * frTx
                            + (w1[0].northing - mf.pivotAxlePos.northing) * frTy;
            if (startFwd < -2.0)
            {
                if ((DateTime.UtcNow - lastWrongEndLog).TotalSeconds > 15)
                {
                    lastWrongEndLog = DateTime.UtcNow;
                    Log.EventWriter("Rigid turn rejected: start " + (-startFwd).ToString("F1")
                        + " m behind vehicle, " + lastRayDiag);
                }
                return false;
            }

            ytList.Clear();
            ytList.AddRange(w1);
            ytRedIndex = redStart;

            kPart2List.Clear();
            kTurnStage = 0;
            kFwdHeldDist = 0;
            kRevExtraDist = 0;
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

            //field 2026-07-28 diag: one line per created turn - enough to see at which
            //end and off which limit line every maneuver was anchored
            Log.EventWriter("Rigid turn built: start " + mf.distancePivotToTurnLine.ToString("F1")
                + " m ahead, " + (isTurnLeft ? "left" : "right") + " skip " + rowSkipsWidth
                + ", " + lastRayDiag);
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
            double over = Properties.ToolSettings.Default.setTurn_U_overshootDist;
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
            double d = Properties.ToolSettings.Default.setTurn_K_entryStartDist;
            if (d < 0.1) d = 0.1;
            if (d > o - 0.2) d = Math.Max(0.1, o - 0.2);
            double alpha = Properties.ToolSettings.Default.setTurn_K_entryAngle * Math.PI / 180.0;
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
                //field 2026-07-16: tail past the line cut to the 0.1 m minimum (user)
                //so even the end-of-path fallback completes AT the row, AB takes over
                int nCross = Math.Max(1, (int)Math.Round(0.1 / ps));
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
                //field 2026-07-16: same 0.1 m minimum tail on the split part 2
                int nCross = Math.Max(1, (int)Math.Round(0.1 / ps));
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
        //F50 orchard: a turn built toward a pass on ANOTHER track - the maneuver uses
        //this lateral distance instead of rowSkips*width, and guidance switches to
        //the target track the moment the turn completes
        private double crossTurnOffset;
        private CTrk crossSwitchTrack;

        public void CompleteYouTurn()
        {
            //safety: a completed turn must always end up on the new line
            if (turnLineSwapPending) { turnLineSwapPending = false; ApplyTriggerLineSwap(); }
            isYouTurnTriggered = false;

            //orchard: land on the next pass's track
            if (crossSwitchTrack != null)
            {
                for (int i = 0; i < mf.trk.gArr.Count; i++)
                {
                    if (mf.trk.gArr[i] != crossSwitchTrack) continue;
                    mf.trk.idx = i;
                    mf.ABLine.isABValid = false;
                    mf.curve.isCurveValid = false;
                    break;
                }
                crossSwitchTrack = null;
                crossTurnOffset = 0;
            }

            //REQ 2026-08-15-7: an approach turns the vehicle by most of a right angle while
            //the pass is pinned (REQ-6), so isHeadingSameWay still holds the direction from
            //BEFORE the maneuver. Handing guidance back with that stale direction made the
            //builder look for the turn line the way we CAME from - it found it a metre or two
            //away and fired a U-turn on the spot (field 2026-08-15 21:04: "Rigid turn built:
            //start 6,6 m ahead ... eff line 1 at 1,7 m"). The line is invalidated so both the
            //direction and the pass are re-derived from where the vehicle actually is.
            bool wasApproach = isLineApproach;

            ResetCreatedYouTurn();

            if (wasApproach)
            {
                //the DIRECTION is re-derived at once; the PASS stays held until the plan moves
                //on from it (driven to 50 % or skipped) - at speed the vehicle is still swinging
                //in when the path ends, and the nearest pass is then the one next to it
                approachHoldPass = true;
                heldEntry = null;
                mf.ABLine.isABValid = false;
                mf.curve.isCurveValid = false;
                Log.EventWriter("LineApproach: done, direction re-derived, pass held until settled");
            }

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
            crossSwitchTrack = null;
            crossTurnOffset = 0;
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
            //drop any half-seen reverse so the next turn cannot be armed by a stale edge
            reverseHeld = false;
            reverseRawSince = DateTime.MaxValue;
            kPart2List?.Clear();
            kTurnStage = 0;
            kRevReached = false;
            kFwdHeldDist = 0;
            kRevExtraDist = 0;
            ytRedIndex = -1;
            part2RedIndex = -1;
            turnLineSwapPending = false;
            isLineApproach = false;
            approachReverse = false;
        }

        #region line approach (REQ 2026-08-15-7)

        // The way back onto a pass is always the same simple shape: a straight run ALONG
        // the effective turn line, then ONE entry arc of the U entry radius into the row.
        // No free Dubins and no half turns - the straight is made long enough to reach back
        // past the vehicle, so wherever it stands the follower pulls it onto the parallel
        // first and the row is entered exactly the way a normal turn enters it.
        // Phase A only: if the arc does not fit, or the entry lies behind the vehicle,
        // nothing is built (the reverse case is phase B).
        public bool isLineApproach;

        // asked for (autosteer engaged) but not built yet: retargeting the pass needs one
        // fix to pass before the line's own reference point is on the NEW pass
        private bool approachPending;

        // the built approach is the phase B shape: straight only, waiting for room
        private bool approachReverse;

        // the pass stays held after the approach until the PLAN moves on from it
        private bool approachHoldPass;
        private CPlanEntry heldEntry;

        // the straight part reaches this far back from the entry, or past the vehicle,
        // whichever is longer. Past the arc the path stops AT the row (field 2026-08-15:
        // "nakon radijusa mora zavrsiti") - only a stub is kept so the follower has a
        // segment to hand guidance over on.
        private const double approachParallelLen = 20.0, approachRunIn = 0.1;

        // a row crossing further ahead than this is the FAR headland, not our entry -
        // there the driver leads himself back onto the line (field 2026-08-15)
        private const double approachEntryMax = 30.0;

        // outside this band the "approach" would be a turn (or a nudge), not an entry
        private const double approachTurnMax = 2.09, approachTurnMin = 0.087;

        /// <summary>
        /// Unit direction of the effective turn line at (near) the crossing point.
        /// </summary>
        private static bool TryTurnLineDir(List<vec2> eff, vec2 at, out double tx, out double ty)
        {
            tx = ty = 0;
            double best = -1;
            for (int i = 0; i < eff.Count - 1; i++)
            {
                double dx = eff[i + 1].easting - eff[i].easting;
                double dy = eff[i + 1].northing - eff[i].northing;
                double len2 = dx * dx + dy * dy;
                if (len2 < 0.0001) continue;

                double u = ((at.easting - eff[i].easting) * dx + (at.northing - eff[i].northing) * dy) / len2;
                if (u < 0) u = 0; else if (u > 1) u = 1;

                double ex = eff[i].easting + u * dx - at.easting;
                double ey = eff[i].northing + u * dy - at.northing;
                double dist = ex * ex + ey * ey;

                if (best < 0 || dist < best)
                {
                    best = dist;
                    double len = Math.Sqrt(len2);
                    tx = dx / len; ty = dy / len;
                }
            }
            return best >= 0;
        }

        /// <summary>
        /// Does the path END inside the field? Only the part from fromIndex on is judged -
        /// the straight and the arc live in the headland, which on some fields lies OUTSIDE
        /// the fence entirely (field 2026-08-15: the turn area is regularly reported outside
        /// it). What must be true is that the maneuver leads INTO the field, not out of it.
        /// </summary>
        private bool PathEndsInsideField(List<vec3> path, int fromIndex)
        {
            if (mf.bnd.bndList.Count == 0) return true;   //no boundary drawn: nothing to judge

            for (int i = Math.Max(0, fromIndex); i < path.Count; i++)
                if (!mf.bnd.IsPointInsideFenceArea(path[i])) return false;

            return true;
        }

        /// <summary>
        /// Build the way back onto the ACTIVE pass: parallel along the turn line + entry arc.
        /// Called when autosteer is engaged with the vehicle off the pass.
        /// </summary>
        // REQ 2026-08-15-8: what the approach WOULD be if autosteer were engaged now. Display
        // only - built by the pure geometry, never by the real builder, so looking at the
        // screen can never change what happens when the driver actually engages.
        public List<vec3> previewList = new List<vec3>();
        private double previewLastSecond;
        private string previewLastWhy;

        /// <summary>
        /// Once a second while autosteer is OFF, work out the approach that engaging it would
        /// build. Nothing to show = empty list = nothing on screen (the driver asked for it
        /// that way); the reason goes to the log, and only when it CHANGES - a per-second
        /// line would bury the log, and the distances in it move with every fix.
        /// </summary>
        public void UpdateApproachPreview()
        {
            if (!mf.tnl.IsFeatureOn || !isYouTurnBtnOn || mf.isBtnAutoSteerOn
                || isYouTurnTriggered || !mf.plan.IsRuntimeOn)
            {
                previewList.Clear();
                previewLastWhy = null;
                return;
            }

            if (mf.secondsSinceStart - previewLastSecond < 1.0) return;
            previewLastSecond = mf.secondsSinceStart;

            bool ok = TryBuildApproachPath(out List<vec3> path, out string why);
            previewList = ok ? path : new List<vec3>();

            //compare without the numbers, so "only 3,4 m off the pass" and "only 3,6 m off
            //the pass" count as the same thing to say
            string key = WithoutNumbers(why);
            if (key != previewLastWhy)
            {
                previewLastWhy = key;
                Log.EventWriter("LineApproach preview: " + why);
            }
        }

        private static string WithoutNumbers(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s) if (!char.IsDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        /// <summary>
        /// The preview, drawn the way the pending K part is drawn: grey, sparse, thin - never
        /// mistakable for a maneuver that is actually armed.
        /// </summary>
        public void DrawApproachPreview()
        {
            if (previewList.Count < 3) return;

            //bigger than the real path's dots: only every third point is drawn, so they need
            //the size to read as a line at all (field 2026-08-15)
            GL.PointSize(mf.ABLine.lineWidth + 3);
            GL.Color3(0.75f, 0.75f, 0.75f);
            GL.Begin(PrimitiveType.Points);
            for (int i = 0; i < previewList.Count; i += 3)
                GL.Vertex3(previewList[i].easting, previewList[i].northing, 0);
            GL.End();
        }

        /// <summary>
        /// Autosteer was engaged: ask for the way back onto the pass. The build itself waits
        /// for the next fix, so the guidance line's own reference point is current.
        /// </summary>
        public void RequestLineApproach()
        {
            approachPending = true;
        }

        /// <summary>
        /// Per fix: build the requested approach as soon as it can be built.
        /// </summary>
        public void UpdateLineApproach()
        {
            //PHASE B -> A: while the driver backs along the straight, ask every fix whether the
            //entry radius fits yet. The moment it does, the tail of the path becomes the arc -
            //the straight underneath him does not move, so the follower keeps its reference and
            //the maneuver simply carries on forward.
            if (isLineApproach && approachReverse && isYouTurnTriggered)
            {
                if (TryBuildApproachPath(out List<vec3> full, out string fwhy, out bool stillReverse)
                    && !stillReverse)
                {
                    ytList = full;
                    approachReverse = false;
                    maxProgressIndexReached = 0;
                    ytRedIndex = -1;
                    Log.EventWriter("LineApproach: room for the entry radius - " + fwhy);
                }
                return;
            }

            if (!approachPending) return;
            if (isYouTurnTriggered || !isYouTurnBtnOn || !mf.tnl.IsFeatureOn)
            {
                approachPending = false;
                return;
            }
            BuildLineApproach();
        }

        public void BuildLineApproach()
        {
            approachPending = false;
            if (!mf.tnl.IsFeatureOn || !isYouTurnBtnOn || isYouTurnTriggered) return;
            if (mf.trk.idx < 0 || mf.trk.idx >= mf.trk.gArr.Count) return;

            //The pass to go back to is the one the PLAN calls next, not the one guidance
            //happens to sit on. Field 2026-08-15: finishing a pass with autosteer off does
            //not always swap the line, so the approach led back to the pass just driven -
            //and only switching autosteer off and on again fixed it, because the seeding in
            //CABLine runs on autosteer OFF only. Retarget here, then build on the next fix:
            //the line's reference point (rEastAB / rEastCu) is still on the OLD pass now.
            if (mf.plan.TryGuidanceOffset(out int planIdx))
            {
                bool isAB = mf.trk.gArr[mf.trk.idx].mode == TrackMode.AB;
                int curIdx = isAB ? mf.ABLine.howManyPathsAway : mf.curve.howManyPathsAway;
                if (curIdx != planIdx)
                {
                    if (isAB) { mf.ABLine.howManyPathsAway = planIdx; mf.ABLine.isABValid = false; }
                    else { mf.curve.howManyPathsAway = planIdx; mf.curve.isCurveValid = false; }

                    //hold it against the position re-pick until the plan moves on from it
                    approachHoldPass = true;
                    heldEntry = null;
                    approachPending = true;

                    Log.EventWriter("LineApproach: target pass " + curIdx + " -> " + planIdx
                        + " (plan), building on the next fix");
                    return;
                }
            }

            if (!TryBuildApproachPath(out List<vec3> path, out string why, out bool reverseOnly))
            {
                Log.EventWriter("LineApproach: " + why + ", no maneuver");
                return;
            }

            ResetCreatedYouTurn();
            ytList = path;
            isGoingStraightThrough = true;   //target = the pass we are already on: no line swap
            isLineApproach = true;
            approachReverse = reverseOnly;
            activeTurnStyle = 0;
            youTurnPhase = 10;

            Log.EventWriter("LineApproach: " + why);

            //the driver just asked for it by engaging autosteer - no trigger window to wait for
            YouTurnTrigger();
        }

        /// <summary>
        /// The approach geometry, with NO side effects - the caller decides what to do with
        /// the result, and gets a plain-language reason when there is nothing to build. This
        /// is what lets the preview ask the same question once a second without changing
        /// anything (REQ 2026-08-15-8).
        /// </summary>
        public bool TryBuildApproachPath(out List<vec3> path, out string why)
        {
            return TryBuildApproachPath(out path, out why, out _);
        }

        /// <summary>
        /// Same, plus whether what came back is the PHASE B shape: just the straight along the
        /// turn line, with no entry arc - the driver backs along it until there is room.
        /// </summary>
        public bool TryBuildApproachPath(out List<vec3> path, out string why, out bool isReverseOnly)
        {
            path = null;
            why = null;
            isReverseOnly = false;

            if (mf.trk.idx < 0 || mf.trk.idx >= mf.trk.gArr.Count)
            {
                why = "no guidance line";
                return false;
            }

            //WHICH WAY the pass is driven comes from the plan. Never from the vehicle
            //heading: the approach is built exactly when the vehicle sits square to the row,
            //where a 90 deg test flips on a couple of degrees of yaw and the target jumps
            //180 deg - that is how an earlier version aimed out of the field (f05tl-14).
            CPlanEntry entry = mf.plan.IsRuntimeOn ? mf.plan.CurrentEntry() : null;
            if (entry == null)
            {
                why = "no plan pass to lead back to";
                return false;
            }

            double rE, rN, lineHead;
            if (mf.trk.gArr[mf.trk.idx].mode == TrackMode.AB)
            {
                rE = mf.ABLine.rEastAB; rN = mf.ABLine.rNorthAB;
                lineHead = mf.ABLine.abHeading;
            }
            else
            {
                rE = mf.curve.rEastCu; rN = mf.curve.rNorthCu;
                lineHead = mf.curve.manualUturnHeading;
            }

            if (!entry.isSameWay) lineHead += Math.PI;
            if (lineHead >= glm.twoPI) lineHead -= glm.twoPI;
            else if (lineHead < 0) lineHead += glm.twoPI;

            vec3 piv = mf.pivotAxlePos;
            double rowX = Math.Sin(lineHead), rowY = Math.Cos(lineHead);
            double y0 = Math.Abs((piv.easting - rE) * rowY - (piv.northing - rN) * rowX);
            double alpha = AngleDiff(mf.fixHeading, lineHead);

            //on the pass and pointing along it: there is nothing to lead back to
            if (y0 < 0.5 * (mf.tool.width - mf.tool.overlap) && Math.Abs(alpha) < 0.1745)
            {
                why = "already on the pass";
                return false;
            }

            //where the row crosses the effective turn line - THAT is the entry point.
            //Field 2026-08-15 (log 20:51): probing only ALONG the drive direction found the
            //crossing 172 m away - the OPPOSITE headland. Standing at a headland about to
            //drive into the field, the crossing we want lies behind that projection, so both
            //directions are probed and the one nearer the vehicle wins.
            //Only DRAWN turn lines count: since the turn line is built with zero inset the
            //boundary ring lies ON the fence, and a parallel taken from it would run the
            //wheels along the boundary.
            vec2 rowRef = new vec2(rE, rN);
            double backHead = lineHead + Math.PI;
            if (backHead >= glm.twoPI) backHead -= glm.twoPI;

            List<vec2> effAhead = mf.tnl.GetEffPolylineByRay(rowRef, lineHead, out _, out double dAhead);
            List<vec2> effBack = mf.tnl.GetEffPolylineByRay(rowRef, backHead, out _, out double dBack);

            List<vec2> eff; double entryAt;
            if (effAhead != null && dAhead >= 0 && (effBack == null || dBack < 0 || dAhead <= dBack))
            { eff = effAhead; entryAt = dAhead; }
            else if (effBack != null && dBack >= 0)
            { eff = effBack; entryAt = -dBack; }
            else { eff = null; entryAt = -1; }

            if (eff == null || Math.Abs(entryAt) > approachEntryMax)
            {
                why = "no turn line within " + approachEntryMax.ToString("N0") + " m along the pass"
                    + (eff != null ? " (nearest " + Math.Abs(entryAt).ToString("N0") + " m)" : "");
                return false;
            }

            vec2 cross = new vec2(rE + rowX * entryAt, rN + rowY * entryAt);
            if (!TryTurnLineDir(eff, cross, out double tx, out double ty))
            {
                why = "turn line has no usable segment";
                return false;
            }

            //the parallel is driven TOWARD the entry point
            if (tx * (cross.easting - piv.easting) + ty * (cross.northing - piv.northing) < 0)
            { tx = -tx; ty = -ty; }

            double parHead = Math.Atan2(tx, ty);
            if (parHead < 0) parHead += glm.twoPI;

            //the fillet that joins the parallel to the row: tangent points sit
            //d = R * tan(delta/2) either side of the crossing (delta = the turn itself,
            //so a row square to the headland gives the familiar d = R)
            double delta = AngleDiff(lineHead, parHead);
            double absDelta = Math.Abs(delta);
            if (absDelta > approachTurnMax || absDelta < approachTurnMin)
            {
                why = "row meets the turn line at " + ((int)glm.toDegrees(absDelta)) + " deg";
                return false;
            }

            double R = Math.Max(2.0, UInRadius);
            double d = R * Math.Tan(0.5 * absDelta);

            //room for the entry arc: the fillet eats d*sin(delta) of lateral offset, plus
            //half a machine width the driver asked for (2026-08-15) - that margin doubles
            //as the hysteresis that keeps this from flapping right at the threshold
            double need = d * Math.Sin(absDelta) + 0.5 * mf.tool.width;

            vec2 tanPar = new vec2(cross.easting - tx * d, cross.northing - ty * d);
            vec2 tanRow = new vec2(cross.easting + rowX * d, cross.northing + rowY * d);

            double alongToEntry = (tanPar.easting - piv.easting) * tx + (tanPar.northing - piv.northing) * ty;

            //PHASE B (REQ 2026-08-15-7): too close to the row for the arc to fit, the entry is
            //already behind us, or the vehicle FACES THE WRONG WAY. Then the maneuver is JUST
            //the straight along the turn line: the driver backs along it (guided - the follower
            //reads the path backwards when reversing) until there is room, and the moment there
            //is, the tail becomes the entry arc. Only these two shapes exist, by the driver's
            //own decision.
            //The heading test earns its place from the field (2026-08-15): standing with the
            //target line BEHIND the vehicle, both position tests passed, so the arc was built -
            //and reaching it meant turning almost 180 deg, which the follower drove as a half
            //circle. Hysteresis, because the shape of the path must not flip back and forth
            //while the driver manoeuvres around the threshold: enter B past 90 deg, leave it
            //only under 70.
            double headErr = Math.Abs(AngleDiff(mf.fixHeading, parHead));
            bool facingWrongWay = approachReverse ? headErr > 1.22 : headErr > glm.PIBy2;

            isReverseOnly = y0 < need || alongToEntry < 0 || facingWrongWay;
            string bReason = y0 < need
                ? "only " + y0.ToString("N1") + " m off the pass, " + need.ToString("N1") + " m needed"
                : alongToEntry < 0
                    ? "entry " + (-alongToEntry).ToString("N1") + " m behind the vehicle"
                    : "facing " + ((int)glm.toDegrees(headErr)) + " deg off the straight";

            //the straight reaches back PAST the vehicle - that is what lets the follower pull
            //it onto the parallel from wherever it stands. It is NOT tested against the fence:
            //it runs along the turn line, through the headland, and on this field the headland
            //is regularly outside the fence (log 2026-08-15 21:03, "only 0,0 m of straight fits"
            //on one side of the field - the maneuver was never built there).
            double parLen = Math.Max(approachParallelLen, alongToEntry + approachParallelLen * 0.25);
            double ps = Math.Max(0.2, R * 0.1);

            //reverse-only: the straight must also cover the ground the vehicle is standing on,
            //ahead of the tangent point, or the driver would have nothing under him to back along
            double aheadOfEntry = isReverseOnly ? Math.Max(0, -alongToEntry) + approachRunIn + 5.0 : 0;

            path = new List<vec3>();
            for (double s = parLen; s > -aheadOfEntry; s -= ps)
                path.Add(new vec3(tanPar.easting - tx * s, tanPar.northing - ty * s, parHead));

            if (isReverseOnly)
            {
                if (path.Count < 6)
                {
                    path = null;
                    why = "no room for a straight to back along";
                    return false;
                }

                why = "reverse leg only (" + bReason + "), straight " + parLen.ToString("N0") + " m";
                return true;
            }

            //the entry arc itself
            double turnSign = delta > 0 ? 1.0 : -1.0;
            double head = parHead, arcE = tanPar.easting, arcN = tanPar.northing;
            for (double run = 0; run < absDelta * R; run += ps)
            {
                arcE += Math.Sin(head) * ps;
                arcN += Math.Cos(head) * ps;
                head += turnSign * ps / R;
                if (head >= glm.twoPI) head -= glm.twoPI;
                else if (head < 0) head += glm.twoPI;
                path.Add(new vec3(arcE, arcN, head));
            }

            //and the stub in the row - taken from the tangent point, so the arc's rounding
            //cannot walk the end of the path off the pass
            int rowFrom = path.Count;
            for (double s = 0; s <= approachRunIn; s += ps)
                path.Add(new vec3(tanRow.easting + rowX * s, tanRow.northing + rowY * s, lineHead));

            if (path.Count < 6)
            {
                path = null;
                why = "path too short to follow";
                return false;
            }

            if (!PathEndsInsideField(path, rowFrom))
            {
                path = null;
                why = "the entry into the row lies outside the field";
                return false;
            }

            why = "built, " + y0.ToString("N1") + " m off the pass, entry "
                + entryAt.ToString("N1") + " m along it, turn " + ((int)glm.toDegrees(absDelta))
                + " deg, R " + R.ToString("N1") + " m, straight " + parLen.ToString("N0") + " m";
            return true;
        }

        #endregion

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
            {
                //field 2026-07-16: REVERSE is allowed from the moment the pivot
                //leaves the exit radius onto the straight tangent - the vehicle
                //heading matching the tangent (path end) heading marks that spot
                //(during the lead-in and the arc the headings differ)
                if (ytList.Count > 1 && Math.Abs(AngleDiff(mf.fixHeading,
                        ytList[ytList.Count - 1].heading)) < 0.14) return -1;
                return 1;
            }
            //field 2026-07-16: FORWARD only after a full vehicle length of reverse
            //past the stop point - flipping right at it proved too early on the field
            if (kTurnStage == 2)
                return (kRevReached && kRevExtraDist > mf.vehicle.VehicleConfig.Wheelbase) ? 1 : -1;
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
                //REQ 2026-08-15-7: an approach is exempt - the driver engages autosteer while
                //standing still, so this would un-trigger it on the very next fix and nothing
                //would ever bring it back (the vehicle is far past the path start, so the
                //trigger window cannot fire again).
                bool turnedOff = Math.Abs(AngleDiff(mf.fixHeading, ytList[0].heading)) > 0.26;
                if (mf.tnl.IsFeatureOn && !isLineApproach && (kTurnStage == 0 || kTurnStage == 1) && !turnedOff
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
                        {
                            kRevReached = true;
                            kRevExtraDist = 0;
                        }

                        //field 2026-07-16: the stop point alone proved too shallow -
                        //keep reversing a full vehicle length PAST it before part 2
                        //(and the FORWARD cue) are allowed
                        if (kRevReached && mf.isReverse)
                            kRevExtraDist += Math.Abs(mf.distanceCurrentStepFix);
                        bool kRevFar = kRevReached
                            && kRevExtraDist > mf.vehicle.VehicleConfig.Wheelbase;

                        //field fix 2a (2026-07-10): a power-shuttle reverser flips
                        //reverse->forward without the speed dropping under the stop
                        //threshold - forward motion held over 0.3 m counts as the
                        //direction change too, not just a full stop
                        if (kRevFar && !mf.isReverse)
                            kFwdHeldDist += Math.Abs(mf.distanceCurrentStepFix);
                        else
                            kFwdHeldDist = 0;

                        //the reverse guide line stays active while still reversing;
                        //part 2 (new-row entry) activates on a stop OR a held
                        //direction change (reverser)
                        if (kRevFar && (Math.Abs(mf.avgSpeed) < 0.3 || kFwdHeldDist > 0.3))
                        {
                            kRevReached = false;
                            kFwdHeldDist = 0;
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

                //field fix 2b v2 (2026-07-15): v1 measured the front axle against the
                //heading of the path's LAST segment - that is the diagonal entry leg,
                //not the row line, so the side never flipped and the turn still ran to
                //the end of the path. The red index is where the entry leg CROSSES the
                //new row's line, so the turn now ends when the follower reaches it
                //(pure pursuit: the lookahead point, stanley: the front axle) - see
                //the checks in the two follower branches below.

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

                    //REQ 2026-08-15-7: 4 m (16 = squared) says "the vehicle is not on this
                    //path any more, the turn is over" - true for a turn, which always starts
                    //ON the row. An approach is the opposite by design: the straight lies on
                    //the turn line and the vehicle is 10-30 m away, waiting to be picked up.
                    //Field 2026-08-15: this dropped the approach on the very first fix, the
                    //normal builder took over, swapped the line and drove a half circle.
                    double giveUpDist = isLineApproach ? 2500.0 : 16.0;   //50 m vs 4 m
                    if (minDistA > giveUpDist)
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

                    //field fix 2b v2: stanley steers the FRONT axle - it crossing the
                    //new row's line (the red index) ends the K turn, the line takes over
                    if (mf.tnl.IsFeatureOn && kTurnStage == 3 && !mf.isReverse
                        && ytRedIndex > 0 && B >= ytRedIndex)
                    {
                        CompleteYouTurn();
                        return false;
                    }

                    //return and reset if too far away or end of the line
                    //(K split holds at the end of part 1 - the reverse leg follows)
                    if (B >= ptCount - 1 && hasProgressedEnough && !kSplitHold)
                    {
                        CompleteYouTurn();
                        return false;
                    }

                    //turn limit lines: a U-turn has no reverse phase, so backing up after the
                    //arc means the driver is on the new row - end the turn, hand back to the line.
                    //An APPROACH is exempt (REQ 2026-08-15-7): reversing is part of it, phase B is
                    //driven backwards on purpose, and ending it here made the target line jump.
                    if (mf.tnl.IsFeatureOn && !isLineApproach
                        && activeTurnStyle == 0 && mf.isReverse && hasProgressedEnough)
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
                    //ends the turn and hands guidance back to the AB/curve line. An APPROACH is
                    //exempt (REQ 2026-08-15-7) - reversing is part of it, phase B is driven
                    //backwards on purpose.
                    if (mf.tnl.IsFeatureOn && !isLineApproach
                        && activeTurnStyle == 0 && mf.isReverse && hasProgressedEnough)
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
                    int goalIdx = ReverseHeading ? B : A;   //path index the goal point lands on

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
                            goalIdx = i;
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

                    //field fix 2b v2 (2026-07-15): the K turn ends the moment the
                    //LOOKAHEAD point passes the new row's line (the red index is where
                    //the entry leg crosses it) - the AB line takes over for the merge
                    if (mf.tnl.IsFeatureOn && kTurnStage == 3 && !mf.isReverse
                        && ytRedIndex > 0 && goalIdx >= ytRedIndex)
                    {
                        CompleteYouTurn();
                        return false;
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

            //field feedback 2026-07-27: green is an ACTION cue, not a status. Blue while the
            //maneuver is ready but still far away, green only inside the trigger tolerance
            //(= stop now / shuttle forward now). Same scheme as the HUD text and arrow.
            //With the turn limit lines off, turnArmed is never fed - keep the stock green.
            if (isYouTurnTriggered)
                GL.Color3(0.95f, 0.5f, 0.95f);            //maneuver running
            else if (isOutOfBounds || turnOvershot)
                GL.Color3(0.9495f, 0.395f, 0.325f);      //red: overshot (reverse) or out of bounds
            else if (!mf.tnl.IsFeatureOn || turnArmed)
                GL.Color3(0.395f, 0.925f, 0.30f);        //green: inside the tolerance - act now
            else
                GL.Color3(0.35f, 0.65f, 0.95f);          //light blue: ready, still approaching

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