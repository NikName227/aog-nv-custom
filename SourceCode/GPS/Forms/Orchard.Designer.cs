using AgLibrary.Logging;
using AgOpenGPS.Core.Translations;
using System;
using System.Collections.Generic;
using System.IO;

namespace AgOpenGPS
{
    // Feature 50 — orchard runtime (SIM feedback 2026-07-29).
    //
    // The ROWS are the definition of the orchard: they are drawn/imported/recorded in the
    // planner and saved under the field (Orchard.txt). Everything the tractor drives is
    // DERIVED from those rows plus the implement settings - the same idea as field mode,
    // where one AB line plus the tool width gives every other pass.
    public partial class FormGPS
    {
        /// <summary>
        /// Read the orchard rows of the open field. F06: a locked install stays blind to
        /// them (no rows, no derived path, nothing drawn).
        /// </summary>
        public void OrchardLoadRows(string fieldDir)
        {
            orchRows.Clear();
            orchardWarn = "";

            if (!CFeatureLock.IsOn(CFeatureLock.FeatureOrchard)) return;
            if (string.IsNullOrEmpty(fieldDir)) return;

            try { orchRows.AddRange(COrchard.LoadRows(fieldDir)); }
            catch (Exception ex) { Log.EventWriter("Orchard LoadRows: " + ex); }

            orchOffsets.Clear();
            try
            {
                foreach (var kv in COrchard.LoadOffsets(fieldDir)) orchOffsets[kv.Key] = kv.Value;
            }
            catch (Exception ex) { Log.EventWriter("Orchard LoadOffsets: " + ex); }

            //t10 G: a field with orchard rows opens in orchard mode - the driver should not have
            //to pick anything to get the passes that belong to this orchard
            trk.isOrchardAuto = orchRows.Count > 0;

            OrchardModeFollowField("field opened");

            OrchardRebuildTracks("field opened");

            if (trk.isOrchardAuto) OrchardPickNearestPass(true);
        }

        /// <summary>
        /// NV custom F50 (2026-08-02): the orchard MODE belongs to the field, its settings belong
        /// to the implement. A mulcher works an orchard on Monday and open ground on Tuesday, and
        /// the guidance has to follow the field it is given - so the checkbox is set from whether
        /// the open field has rows, rather than being something the driver must remember to flip.
        ///
        /// Called on opening a field and on accepting rows in the planner, deliberately NOT when
        /// settings change: the driver can still untick it to work an orchard field as plain
        /// ground, and re-deriving it on every settings edit would undo that behind their back.
        /// With no field open nothing touches it, so the last value stands and the orchard
        /// parameters can be opened and read.
        /// </summary>
        public void OrchardModeFollowField(string why)
        {
            if (!CFeatureLock.IsOn(CFeatureLock.FeatureOrchard)) return;

            bool isOrchardField = orchRows.Count > 0;
            if (Properties.ToolSettings.Default.setTool_isToolOrchard == isOrchardField) return;

            Properties.ToolSettings.Default.setTool_isToolOrchard = isOrchardField;
            Properties.ToolSettings.Default.Save();
            tool.isToolOrchard = isOrchardField;

            Log.EventWriter("Orchard mode " + (isOrchardField ? "ON" : "OFF") + " (" + why + ")");
        }

        /// <summary>
        /// How much closer another pass has to be before the active one is given up (m).
        /// Without it the choice would flicker while driving along the edge between two
        /// inter-rows. SIM 0730: raised from 0.5 to 1.0 m - half a metre still reacted to
        /// ordinary steering corrections inside a row.
        /// </summary>
        /// F50 2026-08-01: this was a flat 1 m, set when an inter-row had ONE pass and the next
        /// line was a whole row gap away. In multi pass the neighbouring pass sits at
        /// (gap - treeWidth - toolWidth) / (n-1) - for a 4 m gap, 1 m tree band and a 1.5 m tool
        /// that is 0.75 m at three passes, well inside a metre, so the selection flipped between
        /// the middle and the side pass on the smallest movement. The passes are spaced by the
        /// TOOL, so the hysteresis is too, at a fraction of the tool width.
        /// F50 2026-08-16: 0.5 -> 0.35, field feedback that a pass held on too long swaying
        /// inside a row; kept relative to tool width rather than a flat number for the same
        /// reason as above.
        private double OrchardSwitchHysteresis
            => (tool.width > 0.1 ? tool.width : 3.0) * 0.35;

        /// <summary>
        /// NV custom F50 (2026-08-01): how long the steer angle is held after the RTK fix goes,
        /// before autosteer is dropped. Long enough that a fix flickering under a tree crown does
        /// not hand the wheel back for nothing, short enough that the machine has not travelled far
        /// enough for the returning fix to arrive as a jerk.
        /// </summary>
        private const int OrchardFixFreezeMs = 500;

        /// <summary>Orchard mode is on and the feature is unlocked.</summary>
        private bool OrchardModeOn
            => tool.isToolOrchard && CFeatureLock.IsOn(CFeatureLock.FeatureOrchard);

        /// <summary>
        /// The RTK gate applies. AOG's own simulator is excluded because it cycles fixQuality 2..6
        /// once a second, so a fix is "lost" four seconds in five and the gate would fire on every
        /// simulated run. A real fix - a receiver, or the external simulator feeding real GGA - is
        /// judged on its merits. Note this exempts the FIX rules only: the steering cap has nothing
        /// to do with fix quality and stays live in the simulator, which is where it gets tested.
        /// </summary>
        private bool OrchardSteerGateOn => OrchardModeOn && !timerSim.Enabled;

        /// <summary>
        /// NV custom F50 (2026-08-01): RTK fix gate for orchard work. In a field a lost fix is a
        /// nuisance; between two rows of trees it is a hazard, because the position jumps, guidance
        /// answers with a hard correction and the machine swerves into a trunk. So the wheel is
        /// never allowed to follow a bad fix: the angle FREEZES at what it was the moment before,
        /// and if the fix has not come back within OrchardFixFreezeMs autosteer is dropped with an
        /// alarm. RTK float (5) counts as lost, same as the rest of AOG treats it.
        ///
        /// This is unconditional in orchard mode - it does not ask the stock "Is RTK" / "RTK kill
        /// autosteer" checkboxes, which are off by default and only ever fired once, on the edge.
        ///
        /// Runs per GPS sentence, deliberately NOT from the draw loop like the stock alarm: a
        /// safety check that stops when rendering stops is not a safety check.
        /// </summary>
        private void OrchardFixGate()
        {
            if (!OrchardSteerGateOn) { orchardFixLostUtc = DateTime.MinValue; return; }

            if (pn.fixQuality == 4)
            {
                if (orchardFixLostUtc != DateTime.MinValue)
                {
                    Log.EventWriter("Orchard: RTK fix back inside the freeze window, steering resumed");
                    orchardFixLostUtc = DateTime.MinValue;
                }
                return;
            }

            //nothing to hold if the driver is steering
            if (!isBtnAutoSteerOn) { orchardFixLostUtc = DateTime.MinValue; return; }

            if (orchardFixLostUtc == DateTime.MinValue)
            {
                orchardFixLostUtc = DateTime.UtcNow;
                orchardFrozenSteerAngle = guidanceLineSteerAngle;
                Log.EventWriter("Orchard: RTK fix lost (quality " + pn.fixQuality + "), steer frozen at "
                    + (orchardFrozenSteerAngle * 0.01).ToString("N1") + " deg");
            }

            if ((DateTime.UtcNow - orchardFixLostUtc).TotalMilliseconds < OrchardFixFreezeMs)
            {
                //hold the last good angle - do NOT steer to a position we no longer trust
                guidanceLineSteerAngle = orchardFrozenSteerAngle;
                return;
            }

            //the window is up and the fix is still gone - give the wheel back, loudly
            btnAutoSteer.PerformClick();
            sounds.sndRTKAlarm.Play();
            Log.EventWriter("Orchard: autosteer OFF, no RTK fix after " + OrchardFixFreezeMs + " ms");
            TimedMessageBox(3000, gStr.gsOrchard, gStr.gsOrchNoFixSteerOff);
            orchardFixLostUtc = DateTime.MinValue;
        }

        /// <summary>
        /// NV custom F50 (2026-08-01): the steer-limit polygon. Inside it the commanded steer angle
        /// is capped; outside it - the headland, wherever the driver decided that is - it is not.
        /// Unlike everything else in orchard mode this is NOT derived: three attempts at deriving
        /// it from the rows each broke on a different real field, because the shape depends on how
        /// the block is worked and geometry cannot know that. The driver draws it in the planner
        /// and it is saved beside the field. It starts as a rectangle around the whole field, so
        /// the cap is in force everywhere until someone narrows it.
        /// </summary>
        public List<vec2> orchardLimitPoly;

        /// <summary>Planner has the polygon open for editing - the runtime leaves it alone.</summary>
        public bool isOrchardLimitEditing;

        //clamp episode, summarised into one log line per pass instead of one per tick - the
        //event log is trimmed to 100 lines at startup and a chatty check would eat all of it
        private string orchardClampPass = "";
        private int orchardClampHits;
        private double orchardClampPeakDeg;
        private DateTime orchardClampSince = DateTime.MinValue;
        private double orchardClampSeconds;

        /// <summary>
        /// Is the machine inside the steer-limit polygon? The same point-in-polygon call the rest
        /// of AOG uses for "am I in the field".
        /// </summary>
        /// <summary>Debug HUD: the cap is in force right now (mode on, inside the polygon).</summary>
        public bool OrchardLimitArmed
            => OrchardModeOn && orchardInLimitLatch
               && Properties.ToolSettings.Default.setTool_orchardMaxSteerAngle >= 0.5;

        private bool orchardInLimitLatch;

        private bool OrchardInLimitZoneNow()
        {
            var z = orchardLimitPoly;
            orchardInLimitLatch = z != null && z.Count >= COrchard.LimitMinPoints
                                  && z.IsPointInPolygon(pivotAxlePos);
            return orchardInLimitLatch;
        }

        /// <summary>
        /// Load the steer-limit polygon with the field, making the default one - the whole field
        /// inside it - when the field has none yet. Not regenerated afterwards: once it exists it
        /// is the driver's, and rebuilding rows must never quietly move it.
        /// </summary>
        public void OrchardLoadLimitPoly(string fieldDir)
        {
            orchardLimitPoly = null;
            isOrchardLimitEditing = false;
            if (!CFeatureLock.IsOn(CFeatureLock.FeatureOrchard)) return;

            try
            {
                orchardLimitPoly = COrchard.LoadLimitPoly(fieldDir);
                if (orchardLimitPoly != null) return;

                orchardLimitPoly = COrchard.DefaultLimitPoly(
                    bnd?.bndList?.Count > 0 ? bnd.bndList[0].fenceLine : null, orchRows);

                if (orchardLimitPoly != null)
                    Log.EventWriter("Orchard: steer limit polygon defaulted to the whole field");
            }
            catch (Exception ex) { Log.EventWriter("Orchard LoadLimitPoly: " + ex); }
        }

        public void OrchardSaveLimitPoly()
        {
            if (!isJobStarted) return;
            COrchard.SaveLimitPoly(currentFieldDirectory.Length > 0
                ? Path.Combine(RegistrySettings.fieldsDirectory, currentFieldDirectory) : "",
                orchardLimitPoly);
        }

        /// <summary>
        /// NV custom F50 (2026-08-01): cap the commanded steer angle while the machine is between
        /// the trees. A GPS wobble there must not turn into a big wheel angle - the row is only as
        /// wide as the gap between two trunks. Outside the zone - the headland along every field
        /// edge, where the machine lines itself up and turns - there is no cap at all.
        ///
        /// Applied at the single point where the angle reaches the PGN, so it also covers a turn
        /// that YouTurn is steering. Setting the parameter to 0 turns the cap off.
        /// </summary>
        private void OrchardClampSteerAngle()
        {
            if (!OrchardModeOn || !isBtnAutoSteerOn || !trk.IsActiveOrchardPass
                || !OrchardInLimitZoneNow())
            {
                OrchardFlushClampLog();
                return;
            }

            double cap = Properties.ToolSettings.Default.setTool_orchardMaxSteerAngle;
            if (cap < 0.5) return;      // 0 = no cap

            string pass = trk.gArr[trk.idx].name;
            if (pass != orchardClampPass) OrchardFlushClampLog();
            orchardClampPass = pass;

            short lim = (short)(cap * 100.0);
            if (guidanceLineSteerAngle <= lim && guidanceLineSteerAngle >= -lim)
            {
                //back inside the cap - the episode, if any, has ended
                if (orchardClampSince != DateTime.MinValue)
                {
                    orchardClampSeconds += (DateTime.UtcNow - orchardClampSince).TotalSeconds;
                    orchardClampSince = DateTime.MinValue;
                }
                return;
            }

            double asked = Math.Abs(guidanceLineSteerAngle) * 0.01;
            if (asked > orchardClampPeakDeg) orchardClampPeakDeg = asked;

            if (orchardClampSince == DateTime.MinValue)
            {
                orchardClampSince = DateTime.UtcNow;
                orchardClampHits++;
            }

            guidanceLineSteerAngle = guidanceLineSteerAngle > 0 ? lim : (short)-lim;
        }

        /// <summary>One line per pass, written when the pass or the zone is left.</summary>
        private void OrchardFlushClampLog()
        {
            if (orchardClampSince != DateTime.MinValue)
            {
                orchardClampSeconds += (DateTime.UtcNow - orchardClampSince).TotalSeconds;
                orchardClampSince = DateTime.MinValue;
            }

            if (orchardClampHits > 0)
            {
                Log.EventWriter("Orchard limit on " + orchardClampPass + ": capped "
                    + orchardClampHits + "x, " + orchardClampSeconds.ToString("N1")
                    + " s, guidance asked up to " + orchardClampPeakDeg.ToString("N1") + " deg");
            }

            orchardClampPass = "";
            orchardClampHits = 0;
            orchardClampPeakDeg = 0;
            orchardClampSeconds = 0;
        }

        /// <summary>
        /// Step the guidance line selection the way the driver sees it (SIM 0730): the whole
        /// orchard is ONE stop, so the cycle buttons move between "Orchard" and the hand-made
        /// lines instead of walking through every inter-row pass. Landing on the orchard turns
        /// the automatics on and takes the NEAREST pass, exactly like picking it in the list.
        ///
        /// Returns false when this field has no derived path - then the stock cycling runs.
        /// </summary>
        public bool OrchardCycleTracks(int dir, out string label)
        {
            label = null;
            if (trk?.gArr == null || !trk.HasOrchardDerived) return false;

            //what the driver can pick: -1 = the orchard, otherwise a hand-made line
            var items = new List<int>();
            bool anyDerivedVisible = false;
            for (int i = 0; i < trk.gArr.Count; i++)
                if (trk.gArr[i].isOrchardDerived && trk.gArr[i].isVisible) { anyDerivedVisible = true; break; }

            if (anyDerivedVisible) items.Add(-1);
            for (int i = 0; i < trk.gArr.Count; i++)
                if (!trk.gArr[i].isOrchardDerived && trk.gArr[i].isVisible) items.Add(i);

            if (items.Count == 0) return true;   //handled: nothing to step onto

            int cur = (trk.isOrchardAuto && anyDerivedVisible) ? 0 : items.IndexOf(trk.idx);
            if (cur < 0) cur = 0;

            int next = ((cur + dir) % items.Count + items.Count) % items.Count;

            if (items[next] == -1)
            {
                trk.isOrchardAuto = true;
                OrchardPickNearestPass(true);
                label = gStr.gsOrchard;
            }
            else
            {
                trk.isOrchardAuto = false;
                trk.idx = items[next];
                label = trk.gArr[trk.idx].name;
            }

            curve.isCurveValid = false;
            ABLine.isABValid = false;
            return true;
        }

        /// <summary>
        /// Field boundary rings (outer first, then the islands) for the path geometry - the outer
        /// strip pass needs to know where the field ends. null when no boundary is defined.
        /// </summary>
        private List<List<vec2>> OrchardFieldBounds()
        {
            if (bnd?.bndList == null || bnd.bndList.Count == 0) return null;

            var b = new List<List<vec2>>();
            for (int i = 0; i < bnd.bndList.Count; i++)
            {
                var ring = bnd.bndList[i].fenceLineEar;
                if (ring != null && ring.Count >= 3) b.Add(new List<vec2>(ring));
            }
            return b.Count == 0 ? null : b;
        }

        /// <summary>
        /// Can this pass be worked right now (SIM 0730)? A side-mounted implement only reaches the
        /// tree line on its own side, so of the two passes in an inter-row only one is workable in
        /// a given driving direction - and which one flips when the tractor turns around.
        ///
        /// The pass carries the side its row is on relative to ITS OWN heading; driving the pass
        /// against that heading mirrors it. Middle passes (TrackSide.None) and a centred implement
        /// have no restriction. reversed = judge it for the direction AFTER a turn.
        /// </summary>
        public bool OrchardPassAllowed(CTrk t, bool reversed = false)
        {
            if (t == null || !t.isOrchardDerived) return true;
            if (t.orchardRowSide == TrackSide.None) return true;

            //F50 fix 2026-08-01: the side is READ FROM THE TOOL OFFSET, it is not a setting of its
            //own any more. The orchard panel used to carry a second "working mode" combo saying the
            //same thing, and nothing kept the two in sync - offset +1.2 m with the combo on "left"
            //drew the coverage right and allowed the passes left. Same reading as the stock Tool
            //Offset icons: > 0 right, < 0 left.
            //F50 2026-08-16: offset 0 used to mean "centred, no restriction" and let
            //OrchardPickNearestPass choose either side of a 2-pass inter-row with no rule beyond
            //"whichever is closer right now" - a free choice on every entry, field feedback that
            //this was undesirable. Offset 0 now defaults to the right pass; driving the left one
            //on a centred implement needs an explicit (even 1 cm) left offset.
            int side = tool.offset < 0 ? 1 : 2;

            //driving with or against the pass heading
            var pts = t.curvePts;
            if (pts.Count < 2) return true;

            int bi = 0; double bd = double.MaxValue;
            for (int i = 0; i < pts.Count; i++)
            {
                double de = pts[i].easting - pivotAxlePos.easting;
                double dn = pts[i].northing - pivotAxlePos.northing;
                double d = de * de + dn * dn;
                if (d < bd) { bd = d; bi = i; }
            }

            double diff = Math.PI - Math.Abs(Math.Abs(pivotAxlePos.heading - pts[bi].heading) - Math.PI);
            bool sameWay = diff < glm.PIBy2;
            if (reversed) sameWay = !sameWay;

            TrackSide rowSide = sameWay
                ? t.orchardRowSide
                : (t.orchardRowSide == TrackSide.Left ? TrackSide.Right : TrackSide.Left);

            return (rowSide == TrackSide.Left && side == 1)
                || (rowSide == TrackSide.Right && side == 2);
        }

        /// <summary>
        /// Where the driver is, as "pass/of - inter-row/of" (SIM 0730: "5/6 - 21/56", no words).
        /// Everything is read from the derived pass name, which already carries it: "Orch Mid 21 p5".
        /// A planting row gives "7/56", an outer strip "E1". "" when no derived pass is active.
        /// </summary>
        public string OrchardPositionText()
        {
            if (!trk.IsActiveOrchardPass) return "";

            string[] w = trk.gArr[trk.idx].name.Split(' ');   // Orch | Mid | 21 | p5
            if (w.Length < 3) return "";

            if (w[1] == "edge") return "E" + w[2];

            if (w[1] == "row")
            {
                int rows = 0;
                for (int i = 0; i < trk.gArr.Count; i++)
                    if (trk.gArr[i].isOrchardDerived && trk.gArr[i].name.StartsWith(COrchard.DerivedPrefix + "row ")) rows++;
                return w[2] + "/" + rows;
            }

            //inter-row passes: how many passes this inter-row has, and how many inter-rows exist
            string mine = COrchard.DerivedPrefix + "Mid " + w[2] + " ";
            int passes = 0, mids = 0;
            int lastMid = -1;
            for (int i = 0; i < trk.gArr.Count; i++)
            {
                var t = trk.gArr[i];
                if (!t.isOrchardDerived) continue;
                if (t.name.StartsWith(mine)) passes++;

                string[] p = t.name.Split(' ');
                if (p.Length > 2 && p[1] == "Mid" && int.TryParse(p[2], out int m) && m != lastMid)
                { mids++; lastMid = m; }
            }

            string pass = w.Length > 3 && w[3].StartsWith("p") ? w[3].Substring(1) : "1";
            return pass + "/" + passes + " - " + w[2] + "/" + mids;
        }

        /// <summary>Shortest distance from the pivot to a track's line, in metres.</summary>
        private double OrchardPassDistance(CTrk t)
        {
            double best = double.MaxValue;
            var pts = t.curvePts;
            for (int i = 0; i < pts.Count; i++)
            {
                double de = pts[i].easting - pivotAxlePos.easting;
                double dn = pts[i].northing - pivotAxlePos.northing;
                double d = de * de + dn * dn;
                if (d < best) best = d;
            }
            return best == double.MaxValue ? best : Math.Sqrt(best);
        }

        /// <summary>
        /// Cumulative along-track distance (m) from the REAL start of a hug pass - before
        /// COrchard.ExtendEnds's straight guidance tail - to the point of t.curvePts nearest the
        /// pivot. This is the same coordinate the manual offset profile's breakpoints are stored
        /// in (COrchard.BuildRowPassTracks applies the profile before extending the ends).
        /// </summary>
        private double OrchardAlongTrackDistance(CTrk t)
        {
            var pts = t.curvePts;
            if (pts.Count < 2) return 0;

            int nearest = 0;
            double best = double.MaxValue;
            for (int i = 0; i < pts.Count; i++)
            {
                double de = pts[i].easting - pivotAxlePos.easting;
                double dn = pts[i].northing - pivotAxlePos.northing;
                double d = de * de + dn * dn;
                if (d < best) { best = d; nearest = i; }
            }

            double along = 0;
            for (int i = 1; i <= nearest; i++)
            {
                double de = pts[i].easting - pts[i - 1].easting;
                double dn = pts[i].northing - pts[i - 1].northing;
                along += Math.Sqrt(de * de + dn * dn);
            }
            return along - t.orchardFrontExtension;
        }

        /// <summary>
        /// NV custom F50 (2026-08-16): HUD minus/plus for the row offset feature - add a signed
        /// clearance step to the active orchard pass's manual offset profile at the vehicle's
        /// current position, rebuild the derived tracks so the new (variable) profile is baked
        /// into the actual geometry, and persist it. "step" is already signed in clearance terms
        /// (+ = further from the row this pass hugs) - the HUD button handlers work out that sign
        /// from which physical button was pressed and which side the tool is mounted on.
        /// No-op for passes with no row of their own (TrackSide.None: on-row/planting mode, or a
        /// multi-pass middle track) - "nemamo od cega odmaknuti".
        /// </summary>
        public void OrchardApplyOffsetStep(double step)
        {
            if (!trk.IsActiveOrchardPass) return;
            CTrk t = trk.gArr[trk.idx];
            if (t.orchardRowSide == TrackSide.None) return;

            double along = Math.Max(0, OrchardAlongTrackDistance(t));

            if (!orchOffsets.TryGetValue(t.name, out List<COrchard.OrchOffsetPoint> profile))
            {
                profile = new List<COrchard.OrchOffsetPoint>();
                orchOffsets[t.name] = profile;
            }

            double newValue = COrchard.CurrentOffset(profile, along) + step;

            //update the nearest existing breakpoint instead of piling up near-duplicates when
            //the driver taps the button repeatedly at roughly the same spot
            int existing = -1;
            for (int i = 0; i < profile.Count; i++)
                if (Math.Abs(profile[i].dist - along) < 0.5) { existing = i; break; }
            if (existing >= 0) profile[existing] = new COrchard.OrchOffsetPoint(along, newValue);
            else profile.Add(new COrchard.OrchOffsetPoint(along, newValue));

            //F50 2026-08-16 fix: a full rebuild, not a flat nudgeDistance shift. nudgeDistance can
            //only move the WHOLE line by one constant - it looked right for a moment right after
            //the click, but then the SAME constant (the latest step) applied to the entire pass
            //the next time through it, wiping out the variation the profile is supposed to have
            //(0 at the start, +10 further on, say - not +10 for the whole row).
            OrchardRebuildTracks("orchard offset step");
            PanelUpdateRightAndBottom();

            if (currentFieldDirectory.Length > 0)
                COrchard.SaveOffsets(Path.Combine(RegistrySettings.fieldsDirectory, currentFieldDirectory), orchOffsets);
        }

        /// <summary>
        /// NV custom F50 (2026-08-16): long-press (5s) on btnSnapToPivot in orchard mode - clears
        /// the WHOLE manual offset profile of the active pass (every breakpoint along the row, not
        /// just from the current position on), unlike the minus button which only walks the value
        /// at the current spot back down. For fixing a row entirely, e.g. after deciding a tree
        /// recording error correction was wrong for the whole row.
        /// </summary>
        public void OrchardResetActiveRowOffset()
        {
            if (!trk.IsActiveOrchardPass) return;
            CTrk t = trk.gArr[trk.idx];
            if (t.orchardRowSide == TrackSide.None) return;

            bool hadAny = orchOffsets.TryGetValue(t.name, out List<COrchard.OrchOffsetPoint> profile)
                && profile.Count > 0;
            if (hadAny) profile.Clear();

            if (hadAny)
            {
                OrchardRebuildTracks("orchard offset reset");
                PanelUpdateRightAndBottom();
                Log.EventWriter("Orchard offset reset (long press): " + t.name);
                if (currentFieldDirectory.Length > 0)
                    COrchard.SaveOffsets(Path.Combine(RegistrySettings.fieldsDirectory, currentFieldDirectory), orchOffsets);
            }
        }

        /// <summary>
        /// Make the derived pass nearest to the vehicle the active one (t10 B). This is how the
        /// next inter-row is reached in orchard mode - the pass itself is a fixed single line,
        /// so driving sideways selects the next line instead of stepping this one over.
        /// force = take the nearest one no matter what (used when the driver picks "Orchard"
        /// in the track list).
        /// </summary>
        public bool OrchardPickNearestPass(bool force)
        {
            if (!trk.isOrchardAuto) return false;

            int best = -1;
            double bestDist = double.MaxValue;
            for (int i = 0; i < trk.gArr.Count; i++)
            {
                if (!trk.gArr[i].isOrchardDerived || !trk.gArr[i].isVisible) continue;
                if (trk.gArr[i].curvePts.Count < 2) continue;
                //SIM 0730: never hand the driver a pass the implement cannot work from here
                if (!OrchardPassAllowed(trk.gArr[i])) continue;

                double d = OrchardPassDistance(trk.gArr[i]);
                if (d < bestDist) { bestDist = d; best = i; }
            }

            if (best < 0 || best == trk.idx) return false;

            //hold the current line unless another is clearly closer
            if (!force && trk.IsActiveOrchardPass)
            {
                double cur = OrchardPassDistance(trk.gArr[trk.idx]);
                if (bestDist + OrchardSwitchHysteresis > cur) return false;
            }

            trk.idx = best;
            curve.isCurveValid = false;
            ABLine.isABValid = false;
            return true;
        }

        /// <summary>
        /// (Re)derive the driving path from the orchard rows and the implement settings.
        /// Called when the field is opened, when the implement or one of its settings changes
        /// (LoadSettings) and when the planner accepts an edit - there is no manual trigger.
        ///
        /// Only DERIVED tracks are touched: whatever the user drew by hand (Smart AB,
        /// recorded curves, imported lines) is left exactly as it is, and the active track
        /// survives the rebuild whenever it still exists.
        /// </summary>
        public void OrchardRebuildTracks(string why)
        {
            if (!CFeatureLock.IsOn(CFeatureLock.FeatureOrchard)) return;
            if (trk?.gArr == null) return;

            // what was active before, so the driver is not dropped onto another line
            CTrk wasActive = (trk.idx > -1 && trk.idx < trk.gArr.Count) ? trk.gArr[trk.idx] : null;
            string wasActiveName = wasActive?.name;
            bool wasActiveDerived = wasActive != null && wasActive.isOrchardDerived;

            // drop the previous derivation (including paths committed by the old
            // "Make path" button, which FileLoadTracks marks as derived)
            bool droppedFromFile = false;
            for (int i = trk.gArr.Count - 1; i >= 0; i--)
            {
                if (!trk.gArr[i].isOrchardDerived) continue;
                if (COrchard.IsDerivedName(trk.gArr[i].name)
                    && trk.gArr[i].name.StartsWith(COrchard.LegacyDerivedPrefix)) droppedFromFile = true;
                trk.gArr.RemoveAt(i);
            }

            var ts = Properties.ToolSettings.Default;
            int pathMode = COrchard.ResolvePathMode();
            double toolW = tool.width > 0.1 ? tool.width : 3.0;

            //F50 (2026-08-01): the steer-limit polygon is deliberately NOT rebuilt here. It is the
            //driver's own drawing, loaded with the field once - regenerating it on a row change
            //would move a safety boundary behind their back.

            List<CTrk> made = new List<CTrk>();
            if (orchRows.Count > 0)
            {
                try
                {
                    made = COrchard.BuildTracks(orchRows, pathMode, toolW,
                        ts.setTool_orchardTreeWidth, ts.setTool_orchardCenterFirst,
                        ts.setTool_orchardPassCount, out List<string> warns,
                        OrchardFieldBounds(), orchOffsets);

                    if (warns.Count > 0)
                        Log.EventWriter("Orchard path (" + why + "): " + warns.Count
                            + " warning(s), first: " + warns[0]);
                }
                catch (Exception ex)
                {
                    Log.EventWriter("Orchard BuildTracks: " + ex);
                    made = new List<CTrk>();
                }
            }

            trk.gArr.AddRange(made);

            // the machine cannot fit between two rows -> HUD alert (planting is not checked:
            // no trees in the ground yet, and the machine drives ON the row)
            if (pathMode == COrchard.PathOnRow || orchRows.Count < 2) orchardWarn = "";
            else
            {
                double missing = COrchard.ToolTooWideBy(orchRows, toolW,
                    ts.setTool_orchardTreeWidth, out int worst);
                orchardWarn = missing > 0.01
                    ? "Tool too wide: -" + missing.ToString("N1") + " m"
                        + (worst > 0 ? " (inter-row " + worst + ")" : "")
                    : "";
            }

            // keep the driver on the same line: by object for a hand-made track (its index
            // moved), by name for a derived one (it was rebuilt from scratch)
            trk.idx = -1;
            if (wasActive != null)
            {
                for (int i = 0; i < trk.gArr.Count; i++)
                {
                    bool same = wasActiveDerived
                        ? (trk.gArr[i].isOrchardDerived && trk.gArr[i].name == wasActiveName)
                        : ReferenceEquals(trk.gArr[i], wasActive);
                    if (same) { trk.idx = i; break; }
                }
            }

            curve.isCurveValid = false;
            ABLine.isABValid = false;

            // an older field still has the old "Make path" tracks in its file - now that they
            // are gone from the list, write the file so they do not come back
            if (droppedFromFile && isJobStarted) FileSaveTracks();

            //SIM 0730: hand the tree positions to the runtime on EVERY change, not only when
            //the field is opened - rows mapped or edited in the planner have to show up in the
            //readout (and on the map) straight away, without closing the field
            OrchardSyncTrees();

            //t10 D: an orchard always has a plan - it is what defines the work
            OrchardSyncPlan(false);
        }

        /// <summary>Tree positions of the rows, for the readout and for the map.</summary>
        public void OrchardSyncTrees()
        {
            planting.rowTrees.Clear();
            for (int i = 0; i < orchRows.Count; i++) planting.rowTrees.AddRange(orchRows[i].trees);
        }

        /// <summary>
        /// Does the plan already describe exactly these derived passes, in this order? Then it
        /// is left alone - regenerating on every rebuild would throw away which passes are
        /// already worked.
        /// </summary>
        private bool OrchardPlanCovers(List<CTrk> derived)
        {
            int i = 0;
            foreach (var p in plan.entries)
            {
                if (!COrchard.IsDerivedName(p.trackName)) continue;
                if (i >= derived.Count || p.trackName != derived[i].name) return false;
                i++;
            }
            return i == derived.Count;
        }

        /// <summary>
        /// Keep the field plan in step with the derived passes (t10 D).
        ///
        /// Two levels of rule, as agreed: the LOCAL one (which pass inside an inter-row comes
        /// first - centre then the sides, or the other way round) is already carried by the
        /// order COrchard.BuildPasses produces from the implement settings; this method hands
        /// that order to F05, which turns it into the complete field sequence with the turns and
        /// the record of what is done. So the local plan shapes the passes, the field plan
        /// drives them.
        ///
        /// The plan is saved per working width (TurnPlan_W&lt;width&gt;.txt), so going back to the
        /// same machine brings its own plan back. If the saved plan no longer matches the passes
        /// - another width, another path mode - it is REGENERATED rather than mapped onto lines
        /// it was never made for. Manual passes (a skewed boundary, the ground around the
        /// orchard) are carried over.
        /// </summary>
        public void OrchardSyncPlan(bool force)
        {
            if (!CFeatureLock.IsOn(CFeatureLock.FeatureOrchard)) return;
            if (trk?.gArr == null) return;

            var derived = new List<CTrk>();
            for (int i = 0; i < trk.gArr.Count; i++)
                if (trk.gArr[i].isOrchardDerived) derived.Add(trk.gArr[i]);

            if (derived.Count == 0) return;
            if (!force && OrchardPlanCovers(derived)) return;

            //the driver's own passes stay in the plan
            var manual = new List<CPlanEntry>();
            foreach (var p in plan.entries)
                if (p.IsManual && !COrchard.IsDerivedName(p.trackName)) manual.Add(p);

            var zones = new List<int>();
            for (int i = 0; i < derived.Count; i++)
                zones.Add(COrchard.ZoneFromDerivedName(derived[i].name));

            plan.GenerateOrchard(derived, zones);

            int order = plan.entries.Count;
            foreach (var p in manual) { p.order = ++order; plan.entries.Add(p); }

            plan.RebuildGeometry();
            plan.InvalidateRun();

            if (isJobStarted) FileSaveTurnPlan();
        }
    }
}
