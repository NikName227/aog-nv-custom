using AgLibrary.Logging;
using System;
using System.Collections.Generic;

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

            //t10 G: a field with orchard rows opens in orchard mode - the driver should not have
            //to pick anything to get the passes that belong to this orchard
            trk.isOrchardAuto = orchRows.Count > 0;

            OrchardRebuildTracks("field opened");

            if (trk.isOrchardAuto) OrchardPickNearestPass(true);
        }

        /// <summary>
        /// How much closer another pass has to be before the active one is given up (m).
        /// Without it the choice would flicker while driving along the edge between two
        /// inter-rows.
        /// </summary>
        private const double OrchardSwitchHysteresis = 0.5;

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

            List<CTrk> made = new List<CTrk>();
            if (orchRows.Count > 0)
            {
                try
                {
                    made = COrchard.BuildTracks(orchRows, pathMode, toolW,
                        ts.setTool_orchardWorkMode, ts.setTool_orchardSafeSpace,
                        ts.setTool_orchardTreeSpace, ts.setTool_orchardRowOffset,
                        ts.setTool_orchardCenterFirst,
                        tool.plantLateralOffset, tool.offset, out List<string> warns);

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
                    ts.setTool_orchardRowOffset, ts.setTool_orchardSafeSpace,
                    ts.setTool_orchardTreeSpace, out int worst);
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

            //t10 D: an orchard always has a plan - it is what defines the work
            OrchardSyncPlan(false);
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
