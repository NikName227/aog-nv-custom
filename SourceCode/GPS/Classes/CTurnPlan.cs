using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;

namespace AgOpenGPS
{
    /// <summary>
    /// One pass of the field plan. Identity is the guidance offset index
    /// (howManyPathsAway); order is the 1-based processing sequence.
    /// </summary>
    public class CPlanEntry
    {
        public int offsetIdx;
        public int order;
        public int zone;                 // 1-based zone number
        public bool isSameWay;           // drive along ref heading (visual, alternates)
        public bool isWorked;
        public bool crossesObstacle;     // split by an inner boundary (obstacle)

        // manually added closing pass: the track it hangs on ("" = the plan track).
        // Manual passes clip to the fence, never to the headland (they close the
        // part of the field the headland-clipped plan leaves out).
        public string trackName = "";
        public bool IsManual { get { return trackName.Length > 0; } }

        // display geometry: pass polyline clipped to the boundary minus obstacles;
        // rebuilt on demand, never persisted
        public List<List<vec3>> segments = new List<List<vec3>>();

        // length of the pass inside the field (m); 0 = not computed yet
        public double passLength;
    }

    /// <summary>
    /// Field pass plan (NV custom, F05 phase 3): systematic order of the passes.
    /// Mode 1 = sequential row by row, mode 3 = zone interleave (first half of a zone
    /// interleaved with its second half so turns stay wide); mode 2 (contour) comes later.
    /// The plan is generated from the active track + tool width + boundary and is
    /// saved per field in TurnPlan.txt.
    /// </summary>
    public class CTurnPlan
    {
        private readonly FormGPS mf;

        public const int ModeSequential = 1, ModeContour = 2, ModeZones = 3;

        // a pass must have at least this length inside the boundary to exist
        public const double minPassLength = 10.0;

        public bool isFollowPlan;        // master switch (runtime hookup = phase 3.2)
        public int mode = ModeZones;
        public int zoneCount = 2;
        public string trackName = "";    // "" = the active track
        public bool clipToHeadland = true;   // passes stop at the headland (when defined)
        public List<CPlanEntry> entries = new List<CPlanEntry>();

        public CTurnPlan(FormGPS _f)
        {
            mf = _f;
        }

        public bool HasPlan { get { return entries.Count > 0; } }

        public void Reset()
        {
            entries.Clear();
            isFollowPlan = false;
            mode = ModeZones;
            zoneCount = 2;
            trackName = "";
            clipToHeadland = true;
            runEntry = null;
            runDistance = 0;
            isFlexPause = false;
        }

        public void ApplyLoaded(AgOpenGPS.IO.TurnPlanData data)
        {
            Reset();
            if (data == null) return;
            isFollowPlan = data.followPlan;
            mode = data.mode;
            zoneCount = data.zoneCount;
            trackName = data.trackName ?? "";
            clipToHeadland = data.clipHeadland;
            entries.AddRange(data.entries);
        }

        // ── info for the PATH tab: zone width + passes per zone ──────────────

        /// <summary>
        /// Informative numbers from the current field + tool: how wide one zone is and
        /// how many passes fit in it. Returns false without a boundary/track/width.
        /// </summary>
        public bool TryZoneInfo(int zones, out double zoneWidth, out int passesPerZone)
        {
            zoneWidth = 0;
            passesPerZone = 0;
            double w = WorkWidth();
            if (w < 0.1 || zones < 1) return false;
            if (!TryOffsetRange(out int minIdx, out int maxIdx)) return false;

            int passes = maxIdx - minIdx + 1;
            zoneWidth = passes * w / zones;
            passesPerZone = (int)Math.Ceiling((double)passes / zones);
            return true;
        }

        // ── generator ─────────────────────────────────────────────────────────

        /// <summary>
        /// Build the plan from the chosen track, tool width and boundary. Worked flags
        /// of the generated passes are discarded (a new plan = a new job); manually
        /// added closing passes are kept and renumbered after the generated ones.
        /// </summary>
        public bool Generate(out string error)
        {
            error = null;

            var manual = new List<CPlanEntry>();
            foreach (var p in entries) if (p.IsManual) manual.Add(p);
            entries.Clear();

            double w = WorkWidth();
            if (w < 0.1) { error = "No tool width"; return false; }
            if (mf.bnd.bndList.Count == 0 || mf.bnd.bndList[0].fenceLine.Count < 3)
            { error = "No boundary"; return false; }
            if (!TryResolveTrack(out CTrk track))
            { error = "No active AB line or curve"; return false; }
            if (!TryOffsetRange(out int minIdx, out int maxIdx))
            { error = "No active AB line or curve"; return false; }

            // collect the passes that really exist (enough length inside the field)
            var passes = new List<CPlanEntry>();
            for (int k = minIdx; k <= maxIdx; k++)
            {
                if (!BuildPassGeometry(track, k, clipToHeadland, out List<List<vec3>> segs,
                        out double len, out bool split))
                    continue;
                if (len < minPassLength) continue;
                passes.Add(new CPlanEntry
                {
                    offsetIdx = k,
                    segments = segs,
                    crossesObstacle = split,
                    passLength = len
                });
            }

            if (passes.Count == 0)
            {
                entries.AddRange(manual);
                error = "No passes inside the boundary";
                return false;
            }

            // pass 1 / zone 1 sits NEXT TO the reference line: orient the spatial
            // sequence so it starts at the end closest to the track
            double firstDist = Math.Abs((passes[0].offsetIdx + 0.5) * w);
            double lastDist = Math.Abs((passes[passes.Count - 1].offsetIdx + 0.5) * w);
            if (lastDist < firstDist) passes.Reverse();

            // split into contiguous zones of near-equal size
            int zones = Math.Max(1, Math.Min(zoneCount, passes.Count));
            int baseCount = passes.Count / zones, rem = passes.Count % zones;
            int at = 0;
            var zoneLists = new List<List<CPlanEntry>>();
            for (int z = 0; z < zones; z++)
            {
                int take = baseCount + (z < rem ? 1 : 0);
                var lst = new List<CPlanEntry>();
                for (int i = 0; i < take; i++) lst.Add(passes[at++]);
                zoneLists.Add(lst);
            }

            // order the passes: zone after zone; inside a zone by mode
            int order = 0;
            for (int z = 0; z < zoneLists.Count; z++)
            {
                var lst = zoneLists[z];
                foreach (var p in OrderWithinZoneByMode(lst))
                {
                    p.zone = z + 1;
                    p.order = ++order;
                    p.isSameWay = (order % 2) == 1;   // alternates with every turn
                    entries.Add(p);
                }
            }

            // manual closing passes come after the generated plan
            foreach (var p in manual)
            {
                p.order = ++order;
                entries.Add(p);
            }
            isFlexPause = false;
            return true;
        }

        /// <summary>
        /// Add one manual closing pass beside the given track (each call adds the
        /// next one further out, on the side where the field is). These passes close
        /// the part of the field a headland-clipped plan leaves out, so they clip to
        /// the fence only. Appended to the plan after everything else.
        /// </summary>
        public bool AddManualPass(string tName, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(tName)) { error = "No line selected"; return false; }

            CTrk track = null;
            foreach (var t in mf.trk.gArr)
            {
                if (t.name == tName) { track = t; break; }
            }
            if (track == null) { error = "Line not found"; return false; }

            // continue outward from the last manual pass on this track
            bool any = false;
            int lastK = 0;
            foreach (var p in entries)
            {
                if (!p.IsManual || p.trackName != tName) continue;
                if (!any || Math.Abs(p.offsetIdx) > Math.Abs(lastK)) lastK = p.offsetIdx;
                any = true;
            }

            int k;
            if (any)
            {
                k = lastK + (lastK >= 0 ? 1 : -1);
            }
            else
            {
                // first pass: pick the side of the line where the field is
                BuildPassGeometry(track, 0, false, out _, out double lenPlus, out _);
                BuildPassGeometry(track, -1, false, out _, out double lenMinus, out _);
                k = lenPlus >= lenMinus ? 0 : -1;
            }

            if (!BuildPassGeometry(track, k, false, out List<List<vec3>> segs,
                    out double len, out bool split) || len < minPassLength)
            {
                error = "No more room beside this line";
                return false;
            }

            int maxOrder = 0, maxZone = 0;
            foreach (var p in entries)
            {
                if (p.order > maxOrder) maxOrder = p.order;
                if (p.zone > maxZone) maxZone = p.zone;
            }

            entries.Add(new CPlanEntry
            {
                offsetIdx = k,
                order = maxOrder + 1,
                zone = maxZone + (any ? 0 : 1),   // one zone (color) per manual track group
                isSameWay = (maxOrder + 1) % 2 == 1,
                trackName = tName,
                segments = segs,
                crossesObstacle = split
            });

            // keep one zone number for all manual passes of the same track
            if (any)
            {
                int grpZone = 0;
                foreach (var p in entries)
                {
                    if (p.IsManual && p.trackName == tName && p.zone > 0) { grpZone = p.zone; break; }
                }
                entries[entries.Count - 1].zone = grpZone;
            }
            return true;
        }

        /// <summary>
        /// Recompute the display geometry of already loaded entries (worked flags kept).
        /// Entries whose pass no longer exists keep an empty segment list.
        /// </summary>
        public void RebuildGeometry()
        {
            TryResolveTrack(out CTrk planTrack);

            foreach (var p in entries)
            {
                p.segments.Clear();

                CTrk track = planTrack;
                bool useHeadland = clipToHeadland;
                if (p.IsManual)
                {
                    track = null;
                    foreach (var t in mf.trk.gArr)
                    {
                        if (t.name == p.trackName) { track = t; break; }
                    }
                    useHeadland = false;
                }
                if (track == null) continue;

                if (BuildPassGeometry(track, p.offsetIdx, useHeadland,
                        out List<List<vec3>> segs, out double len, out bool split))
                {
                    p.segments = segs;
                    p.crossesObstacle = split;
                    p.passLength = len;
                }
            }
        }

        // mode 1: plain sequence; mode 3: first half interleaved with the second half
        // (1, m+1, 2, m+2, ... so every turn is ~half a zone wide)
        private static IEnumerable<CPlanEntry> OrderWithinZone(List<CPlanEntry> lst)
        {
            if (lst.Count < 3) { foreach (var p in lst) yield return p; yield break; }
            int m = (lst.Count + 1) / 2;
            for (int i = 0; i < m; i++)
            {
                yield return lst[i];
                if (m + i < lst.Count) yield return lst[m + i];
            }
        }

        // mode 2 (contour/spiral): start at the middle pass of the zone and spiral
        // outward to both edges (c, c+1, c-1, c+2, c-2, ...). Driving is the same as
        // modes 1/3 - only the pass order differs.
        private static IEnumerable<CPlanEntry> OrderFromMiddle(List<CPlanEntry> lst)
        {
            int n = lst.Count;
            if (n == 0) yield break;
            int c = (n - 1) / 2;
            yield return lst[c];
            for (int d = 1; d < n; d++)
            {
                if (c + d < n) yield return lst[c + d];
                if (c - d >= 0) yield return lst[c - d];
            }
        }

        private IEnumerable<CPlanEntry> OrderWithinZoneByMode(List<CPlanEntry> lst)
        {
            if (mode == ModeZones) return OrderWithinZone(lst);
            if (mode == ModeContour) return OrderFromMiddle(lst);
            return lst;
        }

        // ── runtime (phase 3.2: Follow plan) ──────────────────────────────────

        // pass the vehicle is currently driving + distance covered on it
        private CPlanEntry runEntry;
        private double runDistance;

        // manual-flex (wedge/triangle field end): automatic turns are paused and the
        // driver finishes the area by hand; the first not-worked manual pass is drawn
        // dashed as the resume point - driving onto it re-engages the plan
        public bool isFlexPause;

        // forward meters on the resume pass before the plan re-engages
        public const double flexResumeDistance = 20.0;

        public bool IsRuntimeOn { get { return isFollowPlan && entries.Count > 0; } }

        /// <summary>
        /// Forget the pass the vehicle is running so the next fix re-picks the first
        /// not-worked entry. Call after a manual worked-flag correction (phase 3.3).
        /// </summary>
        public void InvalidateRun()
        {
            runEntry = null;
            runDistance = 0;
        }

        /// <summary>
        /// First not-worked manual pass (by order) = the resume point of the
        /// manual-flex pause. Null when there is none.
        /// </summary>
        public CPlanEntry FlexResumePass()
        {
            CPlanEntry best = null;
            foreach (var p in entries)
            {
                if (!p.IsManual || p.isWorked) continue;
                if (best == null || p.order < best.order) best = p;
            }
            return best;
        }

        /// <summary>
        /// Pause the automatic turns (wedge/triangle field end: no reliable place for
        /// the maneuver) and let the driver finish the area by hand. Only pauses when
        /// a manual resume pass exists - without one there is nothing to come back to,
        /// so the caller keeps today's behavior (abort the turn, run out).
        /// </summary>
        public bool TryEnterFlexPause()
        {
            if (isFlexPause) return true;
            if (FlexResumePass() == null) return false;
            isFlexPause = true;
            runDistance = 0;
            return true;
        }

        /// <summary>
        /// Can a turn from the pass the vehicle is on reach the target pass? At a
        /// wedge-shaped field end the neighboring pass stops short of the current one,
        /// so the maneuver has no anchor: the pass end ahead of the vehicle must have
        /// the target geometry within the lateral offset plus the turn depth. True
        /// when there is not enough geometry to judge by (keeps stock behavior).
        /// </summary>
        public bool IsTargetReachable(int targetIdx, double turnRadius)
        {
            CPlanEntry cur = CurrentEntry();
            if (cur == null) return true;

            string curName = CurrentTrackName();
            CPlanEntry target = null;
            foreach (var p in entries)
            {
                if (p.offsetIdx == targetIdx && EntryTrackName(p) == curName)
                { target = p; break; }
            }
            if (target == null) return true;

            EnsureGeometry(cur);
            EnsureGeometry(target);
            if (cur.segments.Count == 0) return true;
            if (target.segments.Count == 0) return false;

            // pass end ahead of the vehicle = nearest segment endpoint forward
            double hx = Math.Sin(mf.fixHeading), hy = Math.Cos(mf.fixHeading);
            vec3 pivot = mf.pivotAxlePos;
            vec3 endPt = pivot;
            double bestT = double.MaxValue;
            foreach (var seg in cur.segments)
            {
                if (seg.Count == 0) continue;
                for (int e = 0; e < 2; e++)
                {
                    vec3 q = e == 0 ? seg[0] : seg[seg.Count - 1];
                    double t = (q.easting - pivot.easting) * hx
                             + (q.northing - pivot.northing) * hy;
                    if (t > 0 && t < bestT) { bestT = t; endPt = q; }
                }
            }

            double near = double.MaxValue;
            foreach (var seg in target.segments)
            {
                for (int i = 0; i < seg.Count; i++)
                {
                    double de = seg[i].easting - endPt.easting;
                    double dn = seg[i].northing - endPt.northing;
                    double d = de * de + dn * dn;
                    if (d < near) near = d;
                }
            }

            double lat = Math.Abs(targetIdx - cur.offsetIdx) * WorkWidth();
            double depth = 2.0 * turnRadius + WorkWidth();
            return near <= lat * lat + depth * depth;
        }

        /// <summary>
        /// Undo the last manually added pass (highest order among manual entries).
        /// For when the user tapped "Add path" one time too many.
        /// </summary>
        public bool DeleteLastManualPass(out string error)
        {
            error = null;
            CPlanEntry last = null;
            foreach (var p in entries)
            {
                if (!p.IsManual) continue;
                if (last == null || p.order > last.order) last = p;
            }

            if (last == null) { error = "No manual pass to delete"; return false; }

            entries.Remove(last);
            InvalidateRun();
            return true;
        }

        // dashed display lines reach this far past the clipped ends, so the marker
        // is visible outside the boundary too (test f05-3.3r-03)
        public const double displayOverhang = 15.0;

        /// <summary>
        /// Geometry of the currently selected reference line (offset 0), used to
        /// highlight which AB line the plan is built on. Display only - the outer
        /// ends run past the boundary so the line reads clearly. False = no usable track.
        /// </summary>
        public bool TryGetActiveLineGeometry(out List<List<vec3>> segs)
        {
            segs = null;
            if (!TryResolveTrack(out CTrk track)) return false;
            if (!BuildOffsetGeometry(track, 0.0, false, out segs, out _, out _)) return false;
            segs = ExtendForDisplay(segs, displayOverhang);
            return true;
        }

        /// <summary>
        /// Display-only copy of a clipped pass polyline with the two OUTER ends
        /// extended along their local direction (inner gaps - obstacles - are kept).
        /// Never mutates the cached segments.
        /// </summary>
        public static List<List<vec3>> ExtendForDisplay(List<List<vec3>> segs, double meters)
        {
            var outSegs = new List<List<vec3>>(segs.Count);
            foreach (var s in segs) outSegs.Add(new List<vec3>(s));
            if (outSegs.Count == 0) return outSegs;

            List<vec3> first = outSegs[0];
            if (first.Count > 1)
            {
                vec3 a = first[0], b = first[1];
                double dx = a.easting - b.easting, dy = a.northing - b.northing;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len > 0.01)
                    first.Insert(0, new vec3(a.easting + dx / len * meters,
                        a.northing + dy / len * meters, a.heading));
            }

            List<vec3> last = outSegs[outSegs.Count - 1];
            if (last.Count > 1)
            {
                vec3 a = last[last.Count - 1], b = last[last.Count - 2];
                double dx = a.easting - b.easting, dy = a.northing - b.northing;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len > 0.01)
                    last.Add(new vec3(a.easting + dx / len * meters,
                        a.northing + dy / len * meters, a.heading));
            }
            return outSegs;
        }

        private string CurrentTrackName()
        {
            return (mf.trk.idx >= 0 && mf.trk.idx < mf.trk.gArr.Count)
                ? mf.trk.gArr[mf.trk.idx].name : "";
        }

        // the guidance track this entry belongs to ("" = the plan's main track)
        private string EntryTrackName(CPlanEntry p)
        {
            return p.IsManual ? p.trackName : trackName;
        }

        /// <summary>
        /// Entry the vehicle is on right now (current track + current offset index),
        /// or null when the current line is not part of the plan.
        /// </summary>
        public CPlanEntry CurrentEntry()
        {
            if (entries.Count == 0) return null;
            if (mf.trk.idx < 0 || mf.trk.idx >= mf.trk.gArr.Count) return null;

            string cur = CurrentTrackName();
            int idx = mf.trk.gArr[mf.trk.idx].mode == TrackMode.AB
                ? mf.ABLine.howManyPathsAway
                : mf.curve.howManyPathsAway;

            foreach (var p in entries)
            {
                if (p.offsetIdx == idx && EntryTrackName(p) == cur) return p;
            }
            return null;
        }

        /// <summary>
        /// Next pass to drive = first not-worked entry (by order) on the current
        /// guidance track, excluding the pass we are on. False when nothing is left
        /// on this track - the caller falls back to stock behavior.
        /// </summary>
        public bool TryNextTarget(int currentIdx, out int targetIdx)
        {
            targetIdx = 0;
            if (!IsRuntimeOn) return false;

            string cur = CurrentTrackName();
            CPlanEntry best = null;
            foreach (var p in entries)
            {
                if (p.isWorked) continue;
                if (p.offsetIdx == currentIdx) continue;
                if (EntryTrackName(p) != cur) continue;
                if (best == null || p.order < best.order) best = p;
            }
            if (best == null) return false;
            targetIdx = best.offsetIdx;
            return true;
        }

        /// <summary>
        /// Offset index the guidance should lead to when Follow plan is ON = the first
        /// not-worked entry (by order) on the current track. Used to seed the active
        /// guidance line to the plan's first pass instead of the pass nearest the
        /// vehicle (spec 4.3: start pass = first by plan, not "where the vehicle sits").
        /// False when nothing is left on this track (caller keeps stock selection).
        /// </summary>
        public bool TryGuidanceOffset(out int idx)
        {
            idx = 0;
            if (!IsRuntimeOn) return false;

            string cur = CurrentTrackName();

            // manual-flex: guidance leads to the dashed resume pass and nowhere
            // else - the wedge passes are the driver's business now
            if (isFlexPause)
            {
                CPlanEntry resume = FlexResumePass();
                if (resume == null || EntryTrackName(resume) != cur) return false;
                idx = resume.offsetIdx;
                return true;
            }
            CPlanEntry best = null;
            foreach (var p in entries)
            {
                if (p.isWorked) continue;
                if (EntryTrackName(p) != cur) continue;
                if (best == null || p.order < best.order) best = p;
            }
            if (best == null) return false;
            idx = best.offsetIdx;
            return true;
        }

        /// <summary>
        /// The next not-worked entry over the whole plan (for HUD/next-pass display).
        /// </summary>
        public CPlanEntry NextEntry()
        {
            CPlanEntry best = null;
            foreach (var p in entries)
            {
                if (p.isWorked) continue;
                if (best == null || p.order < best.order) best = p;
            }
            return best;
        }

        public int WorkedCount()
        {
            int n = 0;
            foreach (var p in entries) if (p.isWorked) n++;
            return n;
        }

        /// <summary>
        /// Per-fix runtime: accumulate the distance driven while on a plan pass
        /// (laterally within half a width of its line); leaving the pass with more
        /// than 50 % of its length covered marks it worked and saves the plan.
        /// </summary>
        public void UpdateProgress()
        {
            if (!IsRuntimeOn || !mf.isJobStarted) return;

            CPlanEntry now = CurrentEntry();

            // left the previous pass?
            if (runEntry != null && now != runEntry)
            {
                EnsureGeometry(runEntry);
                if (!runEntry.isWorked && runEntry.passLength > 1.0
                    && runDistance > 0.5 * runEntry.passLength)
                {
                    runEntry.isWorked = true;
                    mf.FileSaveTurnPlan();

                    // last manual (closing) pass finished = the job is done:
                    // switch the auto turn button off (test f05-3.2-08)
                    if (runEntry.IsManual && FlexResumePass() == null
                        && mf.yt.isYouTurnBtnOn)
                        mf.btnAutoYouTurn.PerformClick();
                }
                runDistance = 0;
            }
            runEntry = now;
            if (now == null) return;

            // only count real driving on the line: forward, laterally on the strip
            double lateral = mf.trk.gArr[mf.trk.idx].mode == TrackMode.AB
                ? mf.ABLine.distanceFromCurrentLinePivot
                : mf.curve.distanceFromCurrentLinePivot;

            if (!mf.isReverse && Math.Abs(lateral) < 0.5 * WorkWidth())
                runDistance += mf.distanceCurrentStepFix;

            // manual-flex: forward driving on the dashed resume pass re-engages
            // the plan; with no manual pass left there is nothing to wait for
            if (isFlexPause)
            {
                CPlanEntry resume = FlexResumePass();
                if (resume == null) isFlexPause = false;
                else if (now == resume && runDistance > flexResumeDistance)
                    isFlexPause = false;
            }
        }

        private void EnsureGeometry(CPlanEntry p)
        {
            if (p.passLength > 0 && p.segments.Count > 0) return;

            CTrk track = null;
            string owner = EntryTrackName(p);
            foreach (var t in mf.trk.gArr)
            {
                if (t.name == owner) { track = t; break; }
            }
            if (track == null && !p.IsManual) TryResolveTrack(out track);

            if (BuildPassGeometry(track, p.offsetIdx, !p.IsManual && clipToHeadland,
                    out List<List<vec3>> segs, out double len, out bool split))
            {
                p.segments = segs;
                p.passLength = len;
                p.crossesObstacle = split;
            }
        }

        /// <summary>
        /// Main map: draw only the NEXT pass of the plan (dashed violet) so the
        /// driver sees where the plan goes after this row. During a manual-flex
        /// pause the resume pass is drawn instead (dashed orange) - it stays put
        /// until the driver picks it up. Everything else stays in the PATH tab.
        /// </summary>
        public void DrawNextPass()
        {
            if (!IsRuntimeOn || !mf.isJobStarted) return;

            CPlanEntry next = isFlexPause ? FlexResumePass() : NextEntry();
            if (next == null) return;
            EnsureGeometry(next);
            if (next.segments.Count == 0) return;

            // ends run past the boundary so the dashed pass reads clearly
            List<List<vec3>> drawSegs = ExtendForDisplay(next.segments, displayOverhang);

            GL.LineWidth(4);
            if (isFlexPause) GL.Color4(1.0f, 0.62f, 0.10f, 0.95f);
            else GL.Color4(0.75f, 0.30f, 0.95f, 0.92f);
            GL.Enable(EnableCap.LineStipple);
            GL.LineStipple(1, 0x07C7);

            foreach (var seg in drawSegs)
            {
                GL.Begin(PrimitiveType.LineStrip);
                for (int i = 0; i < seg.Count; i++)
                    GL.Vertex3(seg[i].easting, seg[i].northing, 0);
                GL.End();
            }

            GL.Disable(EnableCap.LineStipple);
        }

        // ── geometry ──────────────────────────────────────────────────────────

        private double WorkWidth()
        {
            return mf.tool.width - mf.tool.overlap;
        }

        /// <summary>
        /// Track the plan is built on: the one picked by name in the PATH tab,
        /// falling back to the active track when no (or an unknown) name is set.
        /// </summary>
        private bool TryResolveTrack(out CTrk track)
        {
            track = null;

            if (!string.IsNullOrEmpty(trackName))
            {
                foreach (var t in mf.trk.gArr)
                {
                    if (t.name == trackName) { track = t; break; }
                }
            }
            if (track == null && mf.trk.idx >= 0 && mf.trk.idx < mf.trk.gArr.Count)
                track = mf.trk.gArr[mf.trk.idx];

            if (track == null) return false;
            if (track.mode == TrackMode.AB) return true;
            return track.curvePts.Count > 1;
        }

        /// <summary>
        /// Range of offset indices whose pass strip [k*W .. (k+1)*W] can touch the
        /// boundary. AB: signed perpendicular distance of the fence points from the
        /// ref line. Curve: signed distance of the fence points from the ref polyline.
        /// </summary>
        private bool TryOffsetRange(out int minIdx, out int maxIdx)
        {
            minIdx = maxIdx = 0;
            double w = WorkWidth();
            if (w < 0.1) return false;
            if (!TryResolveTrack(out CTrk track)) return false;
            if (mf.bnd.bndList.Count == 0 || mf.bnd.bndList[0].fenceLine.Count < 3) return false;

            var fence = mf.bnd.bndList[0].fenceLine;
            double minS = double.MaxValue, maxS = double.MinValue;

            for (int i = 0; i < fence.Count; i++)
            {
                double s = SignedDistanceFromRef(track, fence[i].easting, fence[i].northing);
                if (s < minS) minS = s;
                if (s > maxS) maxS = s;
            }
            if (minS > maxS) return false;

            minIdx = (int)Math.Floor(minS / w) - 1;
            maxIdx = (int)Math.Floor(maxS / w) + 1;
            return true;
        }

        // signed distance to the ref line/polyline; positive = right of the heading
        // (matches the guidance convention: pass k center sits at (k + 0.5) * width)
        private static double SignedDistanceFromRef(CTrk track, double e, double n)
        {
            if (track.mode == TrackMode.AB)
            {
                double px = Math.Sin(track.heading + glm.PIBy2);
                double py = Math.Cos(track.heading + glm.PIBy2);
                return (e - track.ptA.easting) * px + (n - track.ptA.northing) * py;
            }

            // nearest curve point, then its heading defines the side
            double best = double.MaxValue;
            int bi = 0;
            var pts = track.curvePts;
            for (int i = 0; i < pts.Count; i++)
            {
                double de = pts[i].easting - e, dn = pts[i].northing - n;
                double d = de * de + dn * dn;
                if (d < best) { best = d; bi = i; }
            }
            double hx = Math.Sin(pts[bi].heading + glm.PIBy2);
            double hy = Math.Cos(pts[bi].heading + glm.PIBy2);
            return (e - pts[bi].easting) * hx + (n - pts[bi].northing) * hy;
        }

        /// <summary>
        /// Pass polyline of a track for offset index k, clipped to inside the field
        /// (headland line when requested and defined, else the fence) and outside
        /// every non-driveThru inner boundary (obstacle). Returns false when nothing
        /// of the pass lies inside; split reports an obstacle crossing.
        /// </summary>
        public bool BuildPassGeometry(CTrk track, int k, bool clipHeadland,
            out List<List<vec3>> segments, out double length, out bool split)
        {
            double w = WorkWidth();
            return BuildOffsetGeometry(track, w * k + 0.5 * w, clipHeadland,
                out segments, out length, out split);
        }

        /// <summary>
        /// Core of BuildPassGeometry: build the track polyline at a given lateral offset
        /// (meters across the driving direction), clipped to the field. distAway = 0 is
        /// the reference line itself (used to highlight the active AB line).
        /// </summary>
        private bool BuildOffsetGeometry(CTrk track, double distAway, bool clipHeadland,
            out List<List<vec3>> segments, out double length, out bool split)
        {
            segments = new List<List<vec3>>();
            length = 0;
            split = false;

            double w = WorkWidth();
            if (w < 0.1) return false;
            if (track == null) return false;
            if (track.mode != TrackMode.AB && track.curvePts.Count < 2) return false;
            if (mf.bnd.bndList.Count == 0 || mf.bnd.bndList[0].fenceLine.Count < 3) return false;

            // sample the offset pass as a dense polyline
            var pass = new List<vec3>();
            if (track.mode == TrackMode.AB)
            {
                double hx = Math.Sin(track.heading), hy = Math.Cos(track.heading);
                double px = Math.Sin(track.heading + glm.PIBy2), py = Math.Cos(track.heading + glm.PIBy2);

                // long enough to cross any field, stepped fine enough for clipping
                const double half = 4000, step = 2.0;
                double bx = track.ptA.easting + px * distAway;
                double by = track.ptA.northing + py * distAway;
                for (double t = -half; t <= half; t += step)
                    pass.Add(new vec3(bx + hx * t, by + hy * t, track.heading));
            }
            else
            {
                var pts = track.curvePts;
                for (int i = 0; i < pts.Count; i++)
                {
                    pass.Add(new vec3(
                        pts[i].easting + Math.Sin(pts[i].heading + glm.PIBy2) * distAway,
                        pts[i].northing + Math.Cos(pts[i].heading + glm.PIBy2) * distAway,
                        pts[i].heading));
                }
            }

            // clip: keep runs of points inside the field and outside the obstacles.
            // With a headland defined the passes stop at the headland line - the
            // headland itself is driven with its own (boundary) line to close the field.
            var fenceEar = mf.bnd.bndList[0].fenceLineEar;
            var headland = mf.bnd.bndList[0].hdLine;
            bool useHeadland = clipHeadland && headland.Count > 2;
            List<vec3> run = null;
            bool everClipped = false;

            for (int i = 0; i < pass.Count; i++)
            {
                var pt = new vec2(pass[i].easting, pass[i].northing);
                bool inside = useHeadland
                    ? headland.IsPointInPolygon(pt)
                    : fenceEar.IsPointInPolygon(pt);
                if (inside)
                {
                    for (int b = 1; b < mf.bnd.bndList.Count; b++)
                    {
                        if (mf.bnd.bndList[b].isDriveThru) continue;
                        if (mf.bnd.bndList[b].fenceLineEar.IsPointInPolygon(
                                new vec2(pass[i].easting, pass[i].northing)))
                        {
                            inside = false;
                            everClipped = true;
                            break;
                        }
                    }
                }

                if (inside)
                {
                    if (run == null) run = new List<vec3>();
                    run.Add(pass[i]);
                }
                else if (run != null)
                {
                    if (run.Count > 1) segments.Add(run);
                    run = null;
                }
            }
            if (run != null && run.Count > 1) segments.Add(run);

            foreach (var s in segments)
            {
                for (int i = 1; i < s.Count; i++)
                    length += glm.Distance(s[i - 1], s[i]);
            }

            split = everClipped && segments.Count > 1;
            return segments.Count > 0 && length > 0.1;
        }
    }
}
