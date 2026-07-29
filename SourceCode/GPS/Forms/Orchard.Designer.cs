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

            OrchardRebuildTracks("field opened");
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
        }
    }
}
