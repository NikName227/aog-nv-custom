using AgLibrary.Logging;
using AgOpenGPS.Core.Translations;
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
        /// inter-rows. SIM 0730: raised from 0.5 to 1.0 m - half a metre still reacted to
        /// ordinary steering corrections inside a row.
        /// </summary>
        private const double OrchardSwitchHysteresis = 1.0;

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

            //implement side: 1 = left, 2 = right, 0 = centred -> everything goes
            int side = Properties.ToolSettings.Default.setTool_orchardWorkMode;
            if (side != 1 && side != 2) return true;

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

            List<CTrk> made = new List<CTrk>();
            if (orchRows.Count > 0)
            {
                try
                {
                    made = COrchard.BuildTracks(orchRows, pathMode, toolW,
                        ts.setTool_orchardTreeWidth, ts.setTool_orchardCenterFirst,
                        ts.setTool_orchardPassCount, out List<string> warns,
                        OrchardFieldBounds());

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
