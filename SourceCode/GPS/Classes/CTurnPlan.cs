using AgLibrary.Logging;
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
        public bool isSkipped;           // written off by the driver (HUD skip - obstacle)
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
        // processing order of the zones, laid out spatially ("3 | 2 | 1 || 4 | 5").
        // Empty = use the default order. Kept as text because that is what the driver
        // edits in the PATH tab; parsing ignores everything that is not a digit.
        public string zoneOrder = "";
        public string trackName = "";    // "" = the active track
        public bool clipToHeadland = true;   // passes stop at the headland (when defined)
        public List<CPlanEntry> entries = new List<CPlanEntry>();

        // Working width the passes were laid out for. The plan file is per width and its
        // signature is checked on load, but only THEN: change the implement while the field
        // is open and the plan in memory silently keeps the old raster. This is what the
        // auto-turn button checks before engaging (field 2026-08-15).
        public double builtWidth;

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
            zoneOrder = "";
            trackName = "";
            clipToHeadland = true;
            builtWidth = 0;
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
            zoneOrder = data.zoneOrder ?? "";
            trackName = data.trackName ?? "";
            clipToHeadland = data.clipHeadland;
            //old files carry no width: the file was picked BY the current width, so that is it
            builtWidth = data.builtWidth > 0.01 ? data.builtWidth : WorkWidth();
            entries.AddRange(data.entries);
        }

        // ── zones: left and right of the reference line stay apart ───────────
        // Field feedback 2026-07-27: the plan used to cut ONE list running from minIdx to
        // maxIdx into equal contiguous zones, so a zone could straddle the AB line and the
        // passes of the two sides got mixed. With work on both sides the sides are now
        // planned as separate zones. offsetIdx < 0 is left of the reference heading,
        // >= 0 is right (pass k is centred at (k + 0.5) * width), so the cut lands exactly
        // on the line.

        // a side has to carry at least this many passes to count as a side of its own
        private const int minSidePasses = 2;

        /// <summary>
        /// Zones in SPATIAL order, leftmost first. leftZones tells how many of them are
        /// left of the reference line (0 = no side split, i.e. the old behaviour).
        /// Passes must arrive sorted by offsetIdx ascending.
        /// </summary>
        private List<List<CPlanEntry>> BuildSpatialZones(List<CPlanEntry> passes, int zones,
            out int leftZones)
        {
            leftZones = 0;
            var left = new List<CPlanEntry>();
            var right = new List<CPlanEntry>();
            foreach (var p in passes)
            {
                if (p.offsetIdx < 0) left.Add(p); else right.Add(p);
            }

            // zoneCount 1, or the reference line sits at/near the field edge => unchanged:
            // one list, and zone 1 is the end that lies NEXT TO the reference line
            if (zones < 2 || left.Count < minSidePasses || right.Count < minSidePasses)
            {
                var single = new List<CPlanEntry>(passes);
                double firstDist = Math.Abs(single[0].offsetIdx + 0.5);
                double lastDist = Math.Abs(single[single.Count - 1].offsetIdx + 0.5);
                if (lastDist < firstDist) single.Reverse();
                return BuildContiguousZones(single, zones);
            }

            // Hand out the zones one at a time, always to the side whose zones are
            // currently the widest. Sides of 60 m and 50 m over 4 zones give 2x30 and
            // 2x25 rather than 1x50 against 3x20 - "no zone much smaller than the rest"
            // falls out of this on its own, it does not need a rule of its own.
            int nL = 1, nR = 1;
            for (int z = 3; z <= zones; z++)
            {
                if (nL >= left.Count && nR >= right.Count) break;   // one pass per zone already
                double wl = nL >= left.Count ? -1 : (double)left.Count / nL;
                double wr = nR >= right.Count ? -1 : (double)right.Count / nR;
                if (wl >= wr) nL++; else nR++;
            }

            // cut each side starting AT the reference line and working outwards
            var leftFromLine = new List<CPlanEntry>(left);
            leftFromLine.Reverse();                                  // -1, -2, -3 ...
            var leftCut = BuildContiguousZones(leftFromLine, nL);    // [0] touches the line
            var rightCut = BuildContiguousZones(right, nR);          // 0, 1, 2 ...

            // spatial order = outermost left ... innermost left | innermost right ... outermost right
            var spatial = new List<List<CPlanEntry>>();
            for (int i = leftCut.Count - 1; i >= 0; i--) spatial.Add(leftCut[i]);
            spatial.AddRange(rightCut);
            leftZones = leftCut.Count;
            return spatial;
        }

        /// <summary>The original split: one list into contiguous zones of near-equal size.</summary>
        private static List<List<CPlanEntry>> BuildContiguousZones(List<CPlanEntry> passes, int zones)
        {
            int n = Math.Max(1, Math.Min(zones, passes.Count));
            int baseCount = passes.Count / n, rem = passes.Count % n;
            int at = 0;
            var lists = new List<List<CPlanEntry>>();
            for (int z = 0; z < n; z++)
            {
                int take = baseCount + (z < rem ? 1 : 0);
                var lst = new List<CPlanEntry>();
                for (int i = 0; i < take; i++) lst.Add(passes[at++]);
                lists.Add(lst);
            }
            return lists;
        }

        /// <summary>
        /// Processing order per spatial zone. The WIDER side is worked first (deterministic -
        /// it does not depend on where the vehicle happens to stand); on a tie the left side
        /// goes first. Inside a side the zone touching the reference line is worked first.
        /// Without a side split it is simply left to right, as before.
        /// </summary>
        public static int[] DefaultZoneOrder(int count, int leftZones)
        {
            var order = new int[count];
            if (leftZones <= 0 || leftZones >= count)
            {
                for (int i = 0; i < count; i++) order[i] = i + 1;
                return order;
            }

            int rightZones = count - leftZones;
            int o = 1;
            if (leftZones >= rightZones)
            {
                for (int i = leftZones - 1; i >= 0; i--) order[i] = o++;         // left, line outwards
                for (int i = leftZones; i < count; i++) order[i] = o++;          // then right
            }
            else
            {
                for (int i = leftZones; i < count; i++) order[i] = o++;          // right, line outwards
                for (int i = leftZones - 1; i >= 0; i--) order[i] = o++;         // then left
            }
            return order;
        }

        /// <summary>
        /// Human readable order, laid out the way the zones sit in the field, leftmost
        /// first: "3 | 2 | 1 | 4 | 5". Where the reference line falls is shown by the zone
        /// badges on the map, so it is not marked here.
        /// </summary>
        public static string FormatZoneOrder(int[] order)
        {
            if (order == null || order.Length == 0) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < order.Length; i++)
            {
                if (i > 0) sb.Append(" | ");
                sb.Append(order[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Read a processing order out of free text: every run of digits is a number, any
        /// other character is a separator, so "3 | 2 | 1 || 4 | 5" and "3 2 1 4 5" are the
        /// same thing. Valid only as a complete permutation of 1..count.
        /// </summary>
        public static bool TryParseZoneOrder(string text, int count, out int[] order, out string error)
        {
            order = null;
            error = null;
            if (count <= 0) { error = "no zones"; return false; }

            var found = new List<int>();
            int i = 0;
            while (text != null && i < text.Length)
            {
                if (!char.IsDigit(text[i])) { i++; continue; }
                int v = 0;
                while (i < text.Length && char.IsDigit(text[i]))
                {
                    v = v * 10 + (text[i] - '0');
                    if (v > 9999) v = 9999;      // stop silly input from overflowing
                    i++;
                }
                found.Add(v);
            }

            if (found.Count != count)
            {
                error = found.Count + " of " + count + " zones";
                return false;
            }

            var seen = new bool[count + 1];
            foreach (int v in found)
            {
                if (v < 1 || v > count) { error = "zone " + v + " is out of range"; return false; }
                if (seen[v]) { error = "zone " + v + " twice"; return false; }
                seen[v] = true;
            }
            for (int z = 1; z <= count; z++)
                if (!seen[z]) { error = "zone " + z + " missing"; return false; }

            order = found.ToArray();
            return true;
        }

        /// <summary>
        /// Zone layout for the PATH tab preview, without building a plan: pass count per
        /// spatial zone plus how many of those zones are left of the line.
        /// </summary>
        public bool TryZoneLayout(int zones, out List<int> zonePasses, out int leftZones)
        {
            zonePasses = null;
            leftZones = 0;
            double w = WorkWidth();
            if (w < 0.1 || zones < 1) return false;
            if (!TryOffsetRange(out int minIdx, out int maxIdx)) return false;

            // the preview cannot know which passes survive the boundary clip, so it works
            // off the raw offset range - same as TryZoneInfo has always done
            // without the candidate padding (see TryZoneInfo)
            var stand = new List<CPlanEntry>();
            for (int k = minIdx + 1; k <= maxIdx - 1; k++) stand.Add(new CPlanEntry { offsetIdx = k });
            if (stand.Count == 0) return false;

            var lists = BuildSpatialZones(stand, zones, out leftZones);
            zonePasses = new List<int>();
            foreach (var l in lists) zonePasses.Add(l.Count);
            return true;
        }

        /// <summary>Default order text for the current field and zone count ("" if unknown).</summary>
        public string DefaultZoneOrderText(int zones)
        {
            if (!TryZoneLayout(zones, out List<int> zonePasses, out int leftZones)) return "";
            return FormatZoneOrder(DefaultZoneOrder(zonePasses.Count, leftZones));
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

            // TryOffsetRange pads the range by one pass on each side so the generator has
            // candidates to test - those two never exist in the field, and counting them made
            // every zone read wider than it is (field call 2026-08-12). Strip them here.
            int passes = maxIdx - minIdx - 1;
            if (passes < 1) return false;

            zoneWidth = passes * w / zones;
            passesPerZone = (int)Math.Ceiling((double)passes / zones);
            return true;
        }

        /// <summary>
        /// Zone sizes read from the PLAN itself, not from the geometry: passes that never made
        /// it in (too short, cut by the headland) must not be counted into a zone's width.
        /// Manual closing passes are left out - they belong to no zone.
        /// </summary>
        public bool TryZoneStatsFromPlan(out List<int> zonePasses, out int leftZones)
        {
            zonePasses = null;
            leftZones = 0;
            if (entries.Count == 0) return false;

            int maxZone = 0;
            foreach (CPlanEntry p in entries)
                if (!p.IsManual && p.zone > maxZone) maxZone = p.zone;
            if (maxZone < 1) return false;

            var counts = new int[maxZone];
            var lefts = new int[maxZone];
            foreach (CPlanEntry p in entries)
            {
                if (p.IsManual || p.zone < 1 || p.zone > maxZone) continue;
                counts[p.zone - 1]++;
                if (p.offsetIdx < 0) lefts[p.zone - 1]++;
            }

            zonePasses = new List<int>();
            for (int i = 0; i < maxZone; i++)
            {
                if (counts[i] == 0) continue;          // an empty zone is not a zone
                zonePasses.Add(counts[i]);
                // a zone counts as "left" when its passes sit left of the reference line
                if (lefts[i] * 2 > counts[i]) leftZones++;
            }
            return zonePasses.Count > 0;
        }

        /// <summary>Passes in one zone, in driving order, closing passes excluded.</summary>
        public List<CPlanEntry> ZonePasses(int zone)
        {
            var list = new List<CPlanEntry>();
            foreach (CPlanEntry p in entries)
                if (!p.IsManual && p.zone == zone) list.Add(p);
            list.Sort((a, b) => a.order.CompareTo(b.order));
            return list;
        }

        /// <summary>
        /// Resume the plan at zone/pass (1-based): everything before it is written off as
        /// SKIPPED - nobody drove it - and everything from it on is cleared, so the jump works
        /// backwards too. Manual closing passes are untouched; they come last anyway.
        /// </summary>
        public bool StartFrom(int zone, int pass, out string err)
        {
            err = "";
            if (!HasPlan) { err = "no plan"; return false; }

            var zoneList = ZonePasses(zone);
            if (zoneList.Count == 0) { err = "zone " + zone + " has no passes"; return false; }
            if (pass < 1 || pass > zoneList.Count)
            {
                err = "zone " + zone + " has " + zoneList.Count + " passes";
                return false;
            }

            int targetOrder = zoneList[pass - 1].order;

            foreach (CPlanEntry p in entries)
            {
                if (p.IsManual) continue;
                if (p.order < targetOrder) { p.isSkipped = true; p.isWorked = false; }
                else { p.isSkipped = false; p.isWorked = false; }
            }

            InvalidateRun();
            mf.FileSaveTurnPlan();
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

            // zones, left and right of the reference line kept apart (see BuildSpatialZones)
            var zoneLists = BuildSpatialZones(passes, zoneCount, out int leftZones);

            // processing order per SPATIAL zone; the user may have reordered it
            int[] zoneOrderById = DefaultZoneOrder(zoneLists.Count, leftZones);
            if (TryParseZoneOrder(zoneOrder, zoneLists.Count, out int[] wanted, out _))
                zoneOrderById = wanted;
            zoneOrder = FormatZoneOrder(zoneOrderById);

            // order the passes: zone after zone in the requested sequence, inside a zone by mode
            int order = 0;
            for (int step = 1; step <= zoneLists.Count; step++)
            {
                int z = Array.IndexOf(zoneOrderById, step);
                if (z < 0) continue;
                foreach (var p in OrderWithinZoneByMode(zoneLists[z]))
                {
                    // zone number = SPATIAL position, so the map colours stay put no
                    // matter how the driver reorders the processing sequence
                    p.zone = z + 1;
                    p.order = ++order;
                    p.isSameWay = (order % 2) == 1;   // alternates with every turn
                    entries.Add(p);
                }
            }

            // manual closing passes come after the generated plan; a new plan is a
            // new job, so their skip write-offs are forgiven along with worked flags
            foreach (var p in manual)
            {
                p.order = ++order;
                p.isSkipped = false;
                entries.Add(p);
            }
            isFlexPause = false;
            lastUndoEntry = null;
            builtWidth = WorkWidth();      // the raster these passes sit on
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

        // pass lock (field 2026-07-28): meters driven continuously on the strip of
        // ONE plan pass (tight lateral tolerance, forward, inside the fence). At
        // passLockDistance the vehicle is certainly on that pass - a leftover stuck
        // turn state (failed-creation latch, wrong-end build) is then discarded once
        // so the builder retries clean. This is what lets manual and automatic turns
        // mix: however the driver got here, 20 m on a pass re-arms the automatics.
        private CPlanEntry lockEntry;
        private double lockDistance;
        public const double passLockDistance = 20.0;

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
                if (!p.IsManual || p.isWorked || p.isSkipped) continue;
                if (best == null || p.order < best.order) best = p;
            }
            return best;
        }

        // ── Orchard (F50 phase 3): the SAME plan runtime, fed by orchard passes ──
        // Every orchard pass is its own curve track; a plan entry hangs on it by
        // name (like a manual closing pass, offsetIdx 0 = the driven strip). All
        // of Follow plan - HUD, dashed next pass, skip/undo, worked >50 % + exit,
        // flex pause, resume - then works unchanged. One brain for both modes.

        // ── orchard pass order: row by row, or skip for a minimum U-turn (F50) ──
        // Row by row is what GenerateOrchard always did. Min U-turn skips passes so every
        // turn has room for a clean U: the room a U needs comes from the turn builder itself
        // (UTurnSpanNeeded) plus 20 % for the machine to straighten out and line up for the
        // next pass. The unit is ONE PASS, not one inter-row - a multi-pass inter-row does
        // not have to be finished in one go, jumping to another inter-row is allowed
        // (decision 2026-08-14).
        public const int ModeMinUTurn = 4;
        public const double minTurnSafety = 1.20;

        // first inter-row the plan starts from; earlier ones are left out of the plan entirely
        public int orchardStartZone = 1;

        // true when min U-turn was asked for but nothing in this orchard is far enough apart
        public bool orchardSeqFallback;

        /// <summary>
        /// Lateral spacing between two passes, in meters: three points along one pass, each
        /// matched to the nearest point of the other, averaged. Curved rows keep their real
        /// spacing this way instead of an assumed constant.
        /// </summary>
        private static double PassSpacing(CTrk a, CTrk b)
        {
            if (a?.curvePts == null || b?.curvePts == null) return -1;
            if (a.curvePts.Count < 4 || b.curvePts.Count < 2) return -1;

            double sum = 0;
            int taken = 0;
            int[] at = { a.curvePts.Count / 4, a.curvePts.Count / 2, 3 * a.curvePts.Count / 4 };
            foreach (int i in at)
            {
                double best = double.MaxValue;
                for (int j = 0; j < b.curvePts.Count; j++)
                {
                    double de = b.curvePts[j].easting - a.curvePts[i].easting;
                    double dn = b.curvePts[j].northing - a.curvePts[i].northing;
                    double d = de * de + dn * dn;
                    if (d < best) best = d;
                }
                if (best == double.MaxValue) continue;
                sum += Math.Sqrt(best);
                taken++;
            }
            return taken == 0 ? -1 : sum / taken;
        }

        /// <summary>
        /// The order the orchard passes get driven in. Row by row = the derivation order,
        /// untouched. Min U-turn = always the SMALLEST allowed jump onward, but never one
        /// that strands the sequence: a candidate is only taken when something workable is
        /// still reachable from it (that one look ahead is what produces the driver's
        /// "sometimes it has to be min plus one row"). Passes of inter-rows before
        /// orchardStartZone are dropped here and never enter the plan.
        /// </summary>
        public List<CTrk> OrderOrchardPasses(List<CTrk> derived)
        {
            orchardSeqFallback = false;

            var pool = new List<CTrk>();
            foreach (var t in derived)
                if (COrchard.ZoneFromDerivedName(t.name) >= orchardStartZone) pool.Add(t);

            if (mode != ModeMinUTurn || pool.Count < 3) return pool;

            double span = mf.yt.UTurnSpanNeeded * minTurnSafety;
            int m = pool.Count;

            //spacing matrix, symmetric (each pair measured from both sides, closer wins)
            var gap = new double[m, m];
            bool anyFits = false;
            for (int i = 0; i < m; i++)
            {
                for (int j = i + 1; j < m; j++)
                {
                    double a = PassSpacing(pool[i], pool[j]);
                    double b = PassSpacing(pool[j], pool[i]);
                    double d = a < 0 ? b : (b < 0 ? a : Math.Min(a, b));
                    gap[i, j] = gap[j, i] = d;
                    if (d >= span) anyFits = true;
                }
            }

            //nothing in this orchard is far enough apart: row by row, and say so. The mode
            //stays as the driver set it - the field is what does not allow it, not the choice.
            if (!anyFits)
            {
                orchardSeqFallback = true;
                Log.EventWriter("Orchard plan: no pass pair reaches " + span.ToString("N1")
                    + " m - min U-turn falls back to row by row");
                return pool;
            }

            var used = new bool[m];
            var seq = new List<CTrk>(m);
            int cur = 0;
            used[0] = true;
            seq.Add(pool[0]);

            for (int step = 1; step < m; step++)
            {
                //candidates that leave room for a U, nearest first
                var fits = new List<int>();
                for (int k = 0; k < m; k++)
                    if (!used[k] && gap[cur, k] >= span) fits.Add(k);
                fits.Sort((x, y) => gap[cur, x] != gap[cur, y]
                    ? gap[cur, x].CompareTo(gap[cur, y]) : x.CompareTo(y));

                int next = -1;
                if (fits.Count == 0)
                {
                    //end of the run: take the nearest one left and let the K-turn cover it
                    double best = double.MaxValue;
                    for (int k = 0; k < m; k++)
                    {
                        if (used[k] || gap[cur, k] >= best) continue;
                        best = gap[cur, k];
                        next = k;
                    }
                }
                else
                {
                    //one look ahead: do not step onto a pass that leaves nothing workable
                    foreach (int k in fits)
                    {
                        bool onward = step == m - 1;
                        for (int y = 0; !onward && y < m; y++)
                            if (!used[y] && y != k && gap[k, y] >= span) onward = true;
                        if (!onward) continue;
                        next = k;
                        break;
                    }
                    if (next < 0) next = fits[0];
                }

                if (next < 0) break;
                used[next] = true;
                seq.Add(pool[next]);
                cur = next;
            }

            //safety net: anything the walk missed keeps its derivation order at the end
            for (int k = 0; k < m; k++) if (!used[k]) seq.Add(pool[k]);
            return seq;
        }

        /// <summary>Rebuild the plan as the ordered orchard pass sequence.</summary>
        public void GenerateOrchard(List<CTrk> passes, List<int> zones)
        {
            builtWidth = WorkWidth();
            entries.Clear();
            runEntry = null;
            runDistance = 0;
            isFlexPause = false;
            lastUndoEntry = null;
            trackName = "";

            for (int i = 0; i < passes.Count; i++)
            {
                entries.Add(new CPlanEntry
                {
                    offsetIdx = 0,
                    order = i + 1,
                    zone = zones != null && i < zones.Count ? zones[i] : 1,
                    isSameWay = (i % 2) == 0,
                    trackName = passes[i].name
                });
            }
        }

        /// <summary>
        /// Cross-track target (orchard): the first not-worked entry that lives on a
        /// DIFFERENT track than the vehicle. Returns that track plus where its driven
        /// strip lies relative to the vehicle (+ = right of the heading, meters) so
        /// the turn builder can aim the maneuver at it.
        /// </summary>
        public bool TryNextTargetCross(out CTrk track, out double distRightOfVehicle)
        {
            track = null;
            distRightOfVehicle = 0;
            if (!IsRuntimeOn) return false;

            CPlanEntry cur = CurrentEntry();
            string curName = CurrentTrackName();

            //candidates in plan order; the first one that can actually be worked wins.
            //F50 (SIM 0730): a turn reverses the driving direction, so for a side-mounted
            //implement the workable pass of an inter-row is the other one after the turn - aiming
            //the maneuver at a pass we could not work on arrival is what made it "catch every AB
            //line". A pass in the middle of an inter-row has no side and always qualifies.
            var candidates = new List<CPlanEntry>();
            foreach (var p in entries)
            {
                if (p.isWorked || p.isSkipped || p == cur) continue;
                if (EntryTrackName(p) == curName) continue;   // same track = stock path
                candidates.Add(p);
            }
            candidates.Sort((u, v) => u.order.CompareTo(v.order));

            foreach (var p in candidates)
            {
                string cName = EntryTrackName(p);
                CTrk cand = null;
                foreach (var t in mf.trk.gArr)
                {
                    if (t.name == cName) { cand = t; break; }
                }
                if (cand == null || cand.curvePts.Count < 2) continue;
                if (!mf.OrchardPassAllowed(cand, true)) continue;

                track = cand;
                break;
            }
            if (track == null) return false;

            // driven strip of that pass = +0.5 working width right of its reference
            vec3 piv = mf.pivotAxlePos;
            int bi = 0; double bd = double.MaxValue;
            var pts = track.curvePts;
            for (int i = 0; i < pts.Count; i++)
            {
                double de = pts[i].easting - piv.easting, dn = pts[i].northing - piv.northing;
                double d = de * de + dn * dn;
                if (d < bd) { bd = d; bi = i; }
            }
            double w = WorkWidth();
            double le = pts[bi].easting + Math.Sin(pts[bi].heading + glm.PIBy2) * 0.5 * w;
            double ln = pts[bi].northing + Math.Cos(pts[bi].heading + glm.PIBy2) * 0.5 * w;

            double vx = Math.Sin(mf.fixHeading + glm.PIBy2), vy = Math.Cos(mf.fixHeading + glm.PIBy2);
            distRightOfVehicle = (le - piv.easting) * vx + (ln - piv.northing) * vy;
            return true;
        }

        // ── HUD skip (2026-07-11): write off the targeted pass ────────────────

        // window in which the last skip can be taken back (slow processes in the
        // field; the first seconds go on "what just happened" - user decision)
        public const double undoSeconds = 10.0;

        // band either side of the turn line in which stepping off ends the pass
        // (REQ 2026-08-15-9, driver's figure)
        public const double passDoneBand = 6.0;

        // and how far the driver must step off to mean it: the working width, but never
        // less than this - with a narrow implement the width alone trips too easily
        public const double passDoneStepMin = 3.0;

        // the last plan action that can still be taken back - a skipped pass, or (REQ
        // 2026-08-15-9) a pass written off by stepping aside in the headland
        private CPlanEntry lastUndoEntry;
        private DateTime lastUndoAt;
        private bool lastUndoWasWorked;

        /// <summary>
        /// Write off the pass the plan currently targets (the dashed one): it is
        /// never targeted again (obstacle etc.). Saved immediately. Returns the
        /// skipped entry, null when there was nothing to skip.
        /// </summary>
        public CPlanEntry SkipCurrentTarget()
        {
            CPlanEntry t = isFlexPause ? FlexResumePass() : NextEntry();
            if (t == null) return null;

            t.isSkipped = true;
            lastUndoEntry = t;
            lastUndoAt = DateTime.UtcNow;
            lastUndoWasWorked = false;
            InvalidateRun();
            mf.FileSaveTurnPlan();
            return t;
        }

        /// <summary>True when the HUD skip button has something to write off.</summary>
        public bool HasSkipTarget()
        {
            return IsRuntimeOn && (isFlexPause ? FlexResumePass() : NextEntry()) != null;
        }

        /// <summary>Seconds left in which the last skip / write-off can be undone (0 = none).</summary>
        public double UndoSecondsLeft()
        {
            if (lastUndoEntry == null) return 0;
            double left = undoSeconds - (DateTime.UtcNow - lastUndoAt).TotalSeconds;
            if (left <= 0) { lastUndoEntry = null; return 0; }
            return left;
        }

        /// <summary>Take back the last skip or write-off (within the undo window).</summary>
        public bool UndoLast()
        {
            if (UndoSecondsLeft() <= 0) return false;

            if (lastUndoWasWorked) lastUndoEntry.isWorked = false;
            else lastUndoEntry.isSkipped = false;

            lastUndoEntry = null;
            InvalidateRun();
            mf.FileSaveTurnPlan();
            return true;
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
                if (p.isWorked || p.isSkipped) continue;
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
                if (p.isWorked || p.isSkipped) continue;
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
                if (p.isWorked || p.isSkipped) continue;
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
        //field 2026-07-16 diag: throttle for the no-matching-entry log below
        private DateTime lastNoEntryLog = DateTime.MinValue;

        /// <summary>
        /// Mark a pass worked (the 50 % rule and the headland step-off both land here) and
        /// save the plan. undoable = arm the HUD undo window, so the driver can take it back;
        /// only the step-off does that - after a normal turn the button must stay SKIP.
        /// </summary>
        private void MarkRunEntryWorked(CPlanEntry e, bool undoable)
        {
            EnsureGeometry(e);
            if (e.isWorked || e.passLength <= 1.0 || runDistance <= 0.5 * e.passLength) return;

            e.isWorked = true;

            if (undoable)
            {
                lastUndoEntry = e;
                lastUndoAt = DateTime.UtcNow;
                lastUndoWasWorked = true;
            }

            mf.FileSaveTurnPlan();

            // last manual (closing) pass finished = the job is done:
            // switch the auto turn button off (test f05-3.2-08)
            if (e.IsManual && FlexResumePass() == null && mf.yt.isYouTurnBtnOn)
                mf.btnAutoYouTurn.PerformClick();
        }

        public void UpdateProgress()
        {
            if (!IsRuntimeOn || !mf.isJobStarted) return;

            CPlanEntry now = CurrentEntry();

            //field 2026-07-16 diag: the FIRST pass sometimes never counts down nor
            //marks worked - log when the active line matches NO plan entry so the
            //next field test says exactly what mismatched (offset vs track name)
            if (now == null && (DateTime.UtcNow - lastNoEntryLog).TotalSeconds > 15)
            {
                lastNoEntryLog = DateTime.UtcNow;
                int idx = mf.trk.idx >= 0 && mf.trk.idx < mf.trk.gArr.Count
                    ? (mf.trk.gArr[mf.trk.idx].mode == TrackMode.AB
                        ? mf.ABLine.howManyPathsAway : mf.curve.howManyPathsAway)
                    : -999;
                Log.EventWriter("TurnPlan diag: no entry matches track '"
                    + CurrentTrackName() + "' offset " + idx);
            }

            // left the previous pass?
            if (runEntry != null && now != runEntry)
            {
                MarkRunEntryWorked(runEntry, false);
                runDistance = 0;
            }
            runEntry = now;
            if (now == null) { lockEntry = null; lockDistance = 0; return; }

            // only count real driving on the line: forward, laterally on the strip
            double lateral = mf.trk.gArr[mf.trk.idx].mode == TrackMode.AB
                ? mf.ABLine.distanceFromCurrentLinePivot
                : mf.curve.distanceFromCurrentLinePivot;

            if (!mf.isReverse && Math.Abs(lateral) < 0.5 * WorkWidth())
                runDistance += mf.distanceCurrentStepFix;

            UpdatePassLock(now, lateral);

            // REQ 2026-08-15-9: autosteer switched off before the turn fired, and the driver
            // steps a FULL working width off the pass - that is how he says "done with this
            // one" (drive away to refill the seeder, come back to the next pass). Without it
            // the plan deadlocks: worked is only set when guidance LEAVES the pass, and while
            // autosteer is off the seeding holds guidance on the first not-worked pass, which
            // is this one. The band around the turn line is what makes it a deliberate move -
            // no side test, because where the boundary runs differs from field to field.
            // The 50 % rule still decides (it lives in MarkRunEntryWorked): a pass that was
            // not really driven is never written off, and the pass we switch TO cannot be
            // written off in turn, because its own distance starts at zero.
            // Undoable for a few seconds through the HUD button, like a skip.
            double bandDist = mf.tnl.DistanceToEffectiveLines(mf.pivotAxlePos.easting,
                                                              mf.pivotAxlePos.northing);
            if (mf.tnl.IsFeatureOn && !mf.isBtnAutoSteerOn && !mf.yt.isYouTurnTriggered
                && now != null && !now.isWorked
                && Math.Abs(lateral) >= Math.Max(passDoneStepMin, WorkWidth())
                && bandDist >= 0 && bandDist <= passDoneBand)
            {
                bool wasWorked = now.isWorked;
                MarkRunEntryWorked(now, true);
                if (!wasWorked && now.isWorked)
                {
                    runDistance = 0;
                    Log.EventWriter("TurnPlan: stepped " + Math.Abs(lateral).ToString("N1")
                        + " m off the pass, " + bandDist.ToString("N1") + " m from the turn line"
                        + " - pass marked worked (undoable " + undoSeconds.ToString("N0") + " s)");
                }
            }

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

        // accumulate the continuous on-strip distance; fires OnPassLocked ONCE when
        // passLockDistance is reached (leaving the strip or reversing starts over)
        private void UpdatePassLock(CPlanEntry now, double lateral)
        {
            bool onStrip = !mf.isReverse
                && Math.Abs(lateral) < 0.25 * WorkWidth()
                && mf.bnd.IsPointInsideFenceArea(mf.pivotAxlePos);

            if (!onStrip || now != lockEntry)
            {
                lockEntry = onStrip ? now : null;
                lockDistance = 0;
                return;
            }

            double before = lockDistance;
            lockDistance += mf.distanceCurrentStepFix;
            if (before < passLockDistance && lockDistance >= passLockDistance)
                OnPassLocked();
        }

        // certainly on a plan pass now: a turn state stuck from before this pass can
        // never resolve itself - drop it so the builder retries from a known-good spot
        private void OnPassLocked()
        {
            var yt = mf.yt;
            if (!yt.isYouTurnBtnOn || yt.isYouTurnTriggered) return;

            bool creationLatch = yt.isTurnCreationTooClose;   // FailCreate parked at phase 11
            bool startBehind = false;
            if (!creationLatch && yt.youTurnPhase == 10 && yt.ytList.Count > 2)
            {
                // a built turn whose start lies behind the vehicle can never trigger
                double fwd = (yt.ytList[2].easting - mf.pivotAxlePos.easting) * Math.Sin(mf.fixHeading)
                           + (yt.ytList[2].northing - mf.pivotAxlePos.northing) * Math.Cos(mf.fixHeading);
                startBehind = fwd < 0;
            }
            if (!creationLatch && !startBehind) return;

            Log.EventWriter("TurnPlan: pass lock reset of stuck turn, phase " + yt.youTurnPhase
                + (creationLatch ? " (creation latch)" : " (start behind vehicle)"));
            yt.isTurnCreationTooClose = false;
            yt.turnTooCloseTrigger = false;
            yt.ResetCreatedYouTurn();
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

        public double WorkWidth()
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
        /// True if the exact AB line/curve this plan was built on still exists. Unlike
        /// TryResolveTrack (which falls back to the active track), this is STRICT: a plan
        /// bound to a since-deleted line is stale and must be rebuilt, not silently
        /// re-pointed at whatever track happens to be active.
        /// </summary>
        public bool ReferenceTrackExists()
        {
            if (entries.Count == 0) return true;   // no plan = nothing to validate
            if (string.IsNullOrEmpty(trackName))   // "" = built on the active track
                return mf.trk.idx >= 0 && mf.trk.idx < mf.trk.gArr.Count;
            foreach (var t in mf.trk.gArr)
                if (t.name == trackName) return true;
            return false;
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

            // orchard fields may have no boundary at all - the pass polyline is
            // then used unclipped (curve tracks only; an AB line would be endless)
            bool noBoundary = mf.bnd.bndList.Count == 0 || mf.bnd.bndList[0].fenceLine.Count < 3;
            if (noBoundary && track.mode == TrackMode.AB) return false;

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

            // no boundary (orchard): the whole polyline is one segment, unclipped
            if (noBoundary)
            {
                if (pass.Count < 2) return false;
                segments.Add(pass);
                for (int i = 1; i < pass.Count; i++)
                    length += glm.Distance(pass[i - 1], pass[i]);
                return length > 0.1;
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
