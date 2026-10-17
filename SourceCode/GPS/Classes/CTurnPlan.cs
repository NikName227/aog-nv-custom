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
        // REQ 2026-10-12-1: where the field boundary cuts the pass in two or more, each part
        // is an entry of its own; piece = which part, counted A->B over ALL parts of the pass
        // (short ones too), so the same number finds the same part again after a rebuild
        public int piece;
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
        public int mode = ModeMinUTurn;   // default for field and orchard (2026-10-13)
        public int zoneCount = 2;
        // processing order of the zones, laid out spatially ("3 | 2 | 1 || 4 | 5").
        // Empty = use the default order. Kept as text because that is what the driver
        // edits in the PATH tab; parsing ignores everything that is not a digit.
        public string zoneOrder = "";
        public string trackName = "";    // "" = the active track
        public bool clipToHeadland = true;   // passes stop at the headland (when defined)
        public List<CPlanEntry> entries = new List<CPlanEntry>();

        // REQ 2026-10-11-2: zones follow the shape of the field. autoZones on = a zone border
        // wherever the end of the field jumps (maxEndStep or more between neighbour passes)
        // and the 10 m rule below; off = the driver cuts the zones alone with zoneCuts.
        public bool autoZones = true;
        // driver's zone borders, local plane points; each one cuts between the two passes
        // either side of it
        public List<vec2> zoneCuts = new List<vec2>();
        // zones the 10 m rule had to add on top of the asked count in the last generation
        public int zonesAddedByRule;

        // a neighbour pass whose end sits this far further along (or back) starts a new zone,
        // and no turn in a zone may step the end along by more than this (m). REQ 2026-10-12-1:
        // per field, set in the last wizard step ("Najveći pomak kraja"), 20 m by default
        public double maxEndStep = defaultMaxEndStep;
        public const double defaultMaxEndStep = 20.0, minMaxEndStep = 5.0, maxMaxEndStep = 100.0;

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
            mode = ModeMinUTurn;
            zoneCount = 2;
            zoneOrder = "";
            trackName = "";
            clipToHeadland = true;
            autoZones = true;
            zoneCuts.Clear();
            zonesAddedByRule = 0;
            maxEndStep = defaultMaxEndStep;
            builtWidth = 0;
            orchardStartZone = 1;
            orchardSeqFallback = false;
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
            orchardStartZone = data.startZone > 0 ? data.startZone : 1;
            autoZones = data.autoZones;
            zoneCuts.AddRange(data.zoneCuts);
            maxEndStep = Math.Max(minMaxEndStep, Math.Min(maxMaxEndStep, data.maxEndStep));
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
            return BuildZones(new List<List<CPlanEntry>> { passes }, zones, null,
                out leftZones, out _, out _);
        }

        /// <summary>
        /// REQ 2026-10-11-2. Zone borders that come before any zone count: where the field end
        /// jumps (autoZones) and where the driver put a zone cut. A border is stored as the
        /// LOWER offset index of the two passes it separates.
        /// </summary>
        private HashSet<int> ZoneBorders(List<CPlanEntry> passes)
        {
            var cuts = new HashSet<int>();
            if (mode == ModeSequential) return cuts;      // row by row has no zones

            if (autoZones)
            {
                for (int i = 0; i < passes.Count - 1; i++)
                {
                    var a = passes[i];
                    var b = passes[i + 1];
                    if (EndStep(a, b, true) >= maxEndStep || EndStep(a, b, false) >= maxEndStep)
                        cuts.Add(a.offsetIdx);
                }
            }

            double w = WorkWidth();
            if (zoneCuts.Count > 0 && w > 0.1 && TryResolveTrack(out CTrk track))
            {
                foreach (var c in zoneCuts)
                {
                    // pass k spans [k*w, (k+1)*w] across the line: the nearest border wins
                    double sd = SignedDistanceFromRef(track, c.easting, c.northing);
                    cuts.Add((int)Math.Round(sd / w) - 1);
                }
            }
            return cuts;
        }

        /// <summary>
        /// How far the end of pass b sits along/back from the end of pass a, at the B end
        /// (atB) or the A end: the leg along the pass of the right triangle whose hypotenuse
        /// joins the two ends (REQ 2026-10-11-2). -1 when either pass has no geometry.
        /// </summary>
        private static double EndStep(CPlanEntry a, CPlanEntry b, bool atB)
        {
            if (!TryPassEnd(a, atB, out vec3 ea) || !TryPassEnd(b, atB, out vec3 eb)) return -1;
            double ux = Math.Sin(ea.heading), uy = Math.Cos(ea.heading);
            return Math.Abs((eb.easting - ea.easting) * ux + (eb.northing - ea.northing) * uy);
        }

        // segments run in the reference direction (A to B), so the first point of the first
        // segment is the A end and the last point of the last one the B end
        private static bool TryPassEnd(CPlanEntry p, bool atB, out vec3 end)
        {
            end = new vec3();
            if (p.segments.Count == 0) return false;
            if (atB)
            {
                var seg = p.segments[p.segments.Count - 1];
                if (seg.Count == 0) return false;
                end = seg[seg.Count - 1];
            }
            else
            {
                var seg = p.segments[0];
                if (seg.Count == 0) return false;
                end = seg[0];
            }
            return true;
        }

        /// <summary>
        /// REQ 2026-10-17-3 §4.2: the zone list turned so it starts at the edge whose entry
        /// is nearer the exit of prev (the last pass of the previous zone, at plan position
        /// already). Both are on the same field end - the one prev turns at. Without pass
        /// geometry the pass index decides. prev == null -> the list as it is.
        /// beside = the new zone lies next to prev at that field end (end step within
        /// maxEndStep), so the way into it is a real turn the optimizer may price; a zone
        /// further along the line (a cut-off part) is only reached by driving there.
        /// </summary>
        private List<CPlanEntry> FacePrevious(List<CPlanEntry> lst, CPlanEntry prev, int already,
            out bool beside)
        {
            beside = false;
            var res = new List<CPlanEntry>(lst);
            if (prev == null || res.Count == 0) return res;
            if (res.Count < 2)
            {
                double one = EndStep(prev, res[0], TurnsAtB(already));
                beside = one >= 0 && one <= maxEndStep;
                return res;
            }
            CPlanEntry first = res[0], last = res[res.Count - 1];
            bool atB = TurnsAtB(already);
            double dFirst, dLast;
            if (TryPassEnd(prev, atB, out vec3 x) && TryPassEnd(first, atB, out vec3 f)
                && TryPassEnd(last, atB, out vec3 l))
            {
                dFirst = (f.easting - x.easting) * (f.easting - x.easting) + (f.northing - x.northing) * (f.northing - x.northing);
                dLast = (l.easting - x.easting) * (l.easting - x.easting) + (l.northing - x.northing) * (l.northing - x.northing);
            }
            else
            {
                dFirst = Math.Abs(first.offsetIdx - prev.offsetIdx);
                dLast = Math.Abs(last.offsetIdx - prev.offsetIdx);
            }
            if (dLast < dFirst - 1e-6) res.Reverse();
            double st = EndStep(prev, res[0], atB);
            beside = st >= 0 && st <= maxEndStep;
            return res;
        }

        // true when a border lies between two passes of a walk (they may skip indices)
        private static bool CutBetween(HashSet<int> cuts, CPlanEntry a, CPlanEntry b)
        {
            int lo = Math.Min(a.offsetIdx, b.offsetIdx), hi = Math.Max(a.offsetIdx, b.offsetIdx);
            foreach (int c in cuts) if (c >= lo && c < hi) return true;
            return false;
        }

        private static List<List<CPlanEntry>> SplitAtCuts(List<CPlanEntry> seq, HashSet<int> cuts)
        {
            var segs = new List<List<CPlanEntry>>();
            List<CPlanEntry> cur = null;
            for (int i = 0; i < seq.Count; i++)
            {
                if (cur == null || CutBetween(cuts, seq[i - 1], seq[i]))
                {
                    cur = new List<CPlanEntry>();
                    segs.Add(cur);
                }
                cur.Add(seq[i]);
            }
            return segs;
        }

        /// <summary>
        /// The zone builder. The field is first cut where it has to be (borders, and the
        /// reference line when there is work on both sides), then the asked zone count is
        /// handed out one zone at a time to the piece whose zones are currently the widest -
        /// sides of 60 m and 50 m over 4 zones give 2x30 and 2x25 rather than 1x50 against
        /// 3x20. extra (per piece, may be null) adds zones on top, for the 10 m rule.
        /// Without borders this is exactly the old left/right split.
        /// REQ 2026-10-12-1: cells[0] is the main part of the field and gets the old
        /// treatment; every further cell (a part the boundary cuts off - BuildCells) is a piece
        /// of its own with at least one zone, and its zones come after the main ones (mainZones).
        /// </summary>
        private List<List<CPlanEntry>> BuildZones(List<List<CPlanEntry>> cells, int zones,
            int[] extra, out int leftZones, out List<int> pieceOfZone, out int mainZones)
        {
            leftZones = 0;
            mainZones = 0;
            pieceOfZone = new List<int>();
            var passes = cells.Count > 0 ? cells[0] : new List<CPlanEntry>();
            var cuts = ZoneBorders(passes);

            var left = new List<CPlanEntry>();
            var right = new List<CPlanEntry>();
            foreach (var p in passes)
            {
                if (p.offsetIdx < 0) left.Add(p); else right.Add(p);
            }

            // pieces in hand-out order; left ones run from the line outwards (descending
            // index) so their zones are worked from the line out, as before
            var pieces = new List<List<CPlanEntry>>();
            int leftPieces = 0;
            bool sides = zones >= 2 && left.Count >= minSidePasses && right.Count >= minSidePasses;
            if (sides)
            {
                var leftFromLine = new List<CPlanEntry>(left);
                leftFromLine.Reverse();                                  // -1, -2, -3 ...
                pieces.AddRange(SplitAtCuts(leftFromLine, cuts));
                leftPieces = pieces.Count;
                pieces.AddRange(SplitAtCuts(right, cuts));               // 0, 1, 2 ...
            }
            else
            {
                // one list, and zone 1 is the end that lies NEXT TO the reference line
                var single = new List<CPlanEntry>(passes);
                if (single.Count > 0)
                {
                    double firstDist = Math.Abs(single[0].offsetIdx + 0.5);
                    double lastDist = Math.Abs(single[single.Count - 1].offsetIdx + 0.5);
                    if (lastDist < firstDist) single.Reverse();
                }
                pieces.AddRange(SplitAtCuts(single, cuts));
            }
            int mainPieces = pieces.Count;
            for (int c = 1; c < cells.Count; c++)
                pieces.AddRange(SplitAtCuts(cells[c], ZoneBorders(cells[c])));

            var nz = new int[pieces.Count];
            for (int i = 0; i < nz.Length; i++) nz[i] = 1;
            for (int z = pieces.Count + 1; z <= zones; z++)
            {
                int best = -1;
                double bestW = -1;
                for (int i = 0; i < pieces.Count; i++)
                {
                    if (nz[i] >= pieces[i].Count) continue;          // one pass per zone already
                    double wdt = (double)pieces[i].Count / nz[i];
                    if (wdt > bestW) { bestW = wdt; best = i; }      // tie: the earlier piece
                }
                if (best < 0) break;
                nz[best]++;
            }
            if (extra != null)
                for (int i = 0; i < nz.Length && i < extra.Length; i++)
                    nz[i] = Math.Min(pieces[i].Count, nz[i] + extra[i]);

            // spatial order = outermost left ... innermost left | innermost right ... outermost right
            var spatial = new List<List<CPlanEntry>>();
            for (int i = leftPieces - 1; i >= 0; i--)
            {
                var cut = BuildContiguousZones(pieces[i], nz[i]);        // [0] nearest the line
                for (int k = cut.Count - 1; k >= 0; k--) { spatial.Add(cut[k]); pieceOfZone.Add(i); }
            }
            leftZones = spatial.Count;
            if (!sides) leftZones = 0;
            for (int i = leftPieces; i < mainPieces; i++)
            {
                foreach (var z in BuildContiguousZones(pieces[i], nz[i])) { spatial.Add(z); pieceOfZone.Add(i); }
            }
            mainZones = spatial.Count;
            for (int i = mainPieces; i < pieces.Count; i++)
            {
                foreach (var z in BuildContiguousZones(pieces[i], nz[i])) { spatial.Add(z); pieceOfZone.Add(i); }
            }
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
        /// mainCount (REQ 2026-10-12-1): zones from that index on lie in parts the boundary
        /// cuts off - they always come last, in their spatial order. -1 = all are main zones.
        /// </summary>
        public static int[] DefaultZoneOrder(int count, int leftZones, int mainCount = -1)
        {
            var order = new int[count];
            if (mainCount < 0 || mainCount > count) mainCount = count;
            for (int i = mainCount; i < count; i++) order[i] = i + 1;
            count = mainCount;                    // the rest below works on the main zones only
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

            // collect the passes that really exist (enough length inside the field). Where the
            // boundary cuts a pass, every part is a pass of its own (REQ 2026-10-12-1) - the
            // tractor cannot drive across the neighbour's field or the canal to the next part
            var passes = new List<CPlanEntry>();
            for (int k = minIdx; k <= maxIdx; k++)
            {
                if (!BuildPassGeometry(track, k, clipToHeadland, out List<List<vec3>> segs,
                        out double len, out bool split))
                    continue;
                var parts = SplitAtFenceGaps(segs);
                for (int g = 0; g < parts.Count; g++)
                {
                    double plen = SegmentsLength(parts[g]);
                    if (plen < minPassLength) continue;
                    passes.Add(new CPlanEntry
                    {
                        offsetIdx = k,
                        piece = g,
                        segments = parts[g],
                        crossesObstacle = split && parts[g].Count > 1,
                        passLength = plen
                    });
                }
            }

            if (passes.Count == 0)
            {
                entries.AddRange(manual);
                error = "No passes inside the boundary";
                return false;
            }

            // REQ 2026-10-11-2: the zone count asked for depends on the mode - row by row has
            // none, min turn takes only what the field shape dictates
            int asked = mode == ModeZones || mode == ModeContour ? zoneCount : 1;
            var cells = BuildCells(passes);
            int[] extra = null;
            List<List<CPlanEntry>> zoneLists;
            int[] zoneOrderById;
            var ordered = new List<CPlanEntry>();
            zonesAddedByRule = 0;
            string keptOrder = zoneOrder;

            // the 10 m rule: no turn inside a zone may step the field end along by more than
            // maxEndStep. Where one does, that piece gets one more (narrower) zone and the
            // plan is laid out again - until it holds or a zone is down to one pass.
            for (int round = 0; ; round++)
            {
                zoneLists = BuildZones(cells, asked, extra, out int leftZones,
                    out List<int> pieceOfZone, out int mainZones);

                // processing order per SPATIAL zone; the user may have reordered it
                zoneOrderById = DefaultZoneOrder(zoneLists.Count, leftZones, mainZones);
                if (TryParseZoneOrder(keptOrder, zoneLists.Count, out int[] wanted, out _))
                    zoneOrderById = wanted;

                // order the passes: zone after zone in the requested sequence, inside a zone by mode
                ordered.Clear();
                int badZone = -1;
                for (int step = 1; step <= zoneLists.Count; step++)
                {
                    int z = Array.IndexOf(zoneOrderById, step);
                    if (z < 0) continue;
                    int startCount = ordered.Count;
                    // REQ 2026-10-17-3 §4.2: a zone starts at the edge next to where the
                    // previous one ended; min turn also prices the turn out of that pass
                    CPlanEntry prev = ordered.Count > 0 ? ordered[ordered.Count - 1] : null;
                    bool beside = false;
                    var zl = mode == ModeContour ? zoneLists[z]
                        : FacePrevious(zoneLists[z], prev, ordered.Count, out beside);
                    var inZone = mode == ModeMinUTurn
                        ? OrderMinTurn(zl, ordered.Count, beside ? prev : null)
                        : new List<CPlanEntry>(OrderWithinZoneByMode(zl));
                    foreach (var p in inZone)
                    {
                        // zone number = SPATIAL position, so the map colours stay put no
                        // matter how the driver reorders the processing sequence
                        p.zone = z + 1;
                        ordered.Add(p);
                    }
                    if (badZone < 0 && autoZones && (mode == ModeZones || mode == ModeContour)
                        && zoneLists[z].Count > 1 && !TransitionsHold(ordered, startCount))
                        badZone = z;
                }

                if (badZone < 0 || round > 200) break;
                int piece = pieceOfZone[badZone];
                if (extra == null) extra = new int[zoneLists.Count + 1];
                if (piece >= extra.Length) Array.Resize(ref extra, piece + 1);
                extra[piece]++;
                zonesAddedByRule++;
            }
            zoneOrder = FormatZoneOrder(zoneOrderById);

            int order = 0;
            foreach (var p in ordered)
            {
                p.order = ++order;
                p.isSameWay = (order % 2) == 1;   // alternates with every turn
                entries.Add(p);
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
                        out List<List<vec3>> segs, out double len, out bool split)
                    && TakePiece(p, ref segs, ref len))
                {
                    p.segments = segs;
                    p.crossesObstacle = split && segs.Count > 1;
                    p.passLength = len;
                }
            }
        }

        // ── parts of a pass the boundary cuts apart (REQ 2026-10-12-1) ──────────

        /// <summary>
        /// Group the clipped segments of one pass into parts. A new part starts where the gap
        /// between two segments leaves the field (outside the fence): an obstacle or the
        /// headland leave the gap inside and keep the segments together, as before.
        /// </summary>
        private List<List<List<vec3>>> SplitAtFenceGaps(List<List<vec3>> segs)
        {
            var parts = new List<List<List<vec3>>>();
            if (segs == null || segs.Count == 0) return parts;
            bool haveFence = mf.bnd.bndList.Count > 0 && mf.bnd.bndList[0].fenceLineEar.Count > 2;

            var cur = new List<List<vec3>> { segs[0] };
            parts.Add(cur);
            for (int i = 1; i < segs.Count; i++)
            {
                var prev = segs[i - 1];
                if (haveFence && prev.Count > 0 && segs[i].Count > 0
                    && GapLeavesField(prev[prev.Count - 1], segs[i][0]))
                {
                    cur = new List<List<vec3>>();
                    parts.Add(cur);
                }
                cur.Add(segs[i]);
            }
            return parts;
        }

        // any point of the straight gap a..b (every metre) outside the outer fence
        private bool GapLeavesField(vec3 a, vec3 b)
        {
            var fence = mf.bnd.bndList[0].fenceLineEar;
            double dx = b.easting - a.easting, dy = b.northing - a.northing;
            int n = Math.Max(2, (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy)));
            for (int j = 1; j < n; j++)
            {
                double f = (double)j / n;
                if (!fence.IsPointInPolygon(new vec2(a.easting + dx * f, a.northing + dy * f)))
                    return true;
            }
            return false;
        }

        private static double SegmentsLength(List<List<vec3>> segs)
        {
            double len = 0;
            foreach (var s in segs)
                for (int i = 1; i < s.Count; i++) len += glm.Distance(s[i - 1], s[i]);
            return len;
        }

        // keep only this entry's part of a freshly built pass; false = the part is gone
        private bool TakePiece(CPlanEntry p, ref List<List<vec3>> segs, ref double len)
        {
            if (p.IsManual) return true;              // closing passes stay whole
            var parts = SplitAtFenceGaps(segs);
            if (parts.Count <= 1 && p.piece == 0) return true;
            if (p.piece < 0 || p.piece >= parts.Count) return false;
            segs = parts[p.piece];
            len = SegmentsLength(segs);
            return true;
        }

        /// <summary>
        /// Cells of the field (boustrophedon decomposition): neighbour passes stay in one cell
        /// only while their parts pair up one to one. Where one pass has a part the next one
        /// has two of (a pocket the boundary cuts off begins) every side starts a cell of its
        /// own - this is what keeps two pockets apart even when both touch the main field.
        /// Each cell is sorted by offsetIdx and holds one part per pass; the cell with the most
        /// passes comes first (the main field), the rest by position.
        /// </summary>
        private List<List<CPlanEntry>> BuildCells(List<CPlanEntry> passes)
        {
            var byIdx = new SortedDictionary<int, List<CPlanEntry>>();
            foreach (var p in passes)
            {
                if (!byIdx.TryGetValue(p.offsetIdx, out var l)) byIdx[p.offsetIdx] = l = new List<CPlanEntry>();
                l.Add(p);
            }

            var parent = new Dictionary<CPlanEntry, CPlanEntry>();
            foreach (var p in passes) parent[p] = p;
            CPlanEntry Find(CPlanEntry x)
            {
                while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
                return x;
            }

            double w = WorkWidth();
            List<CPlanEntry> prev = null;
            int prevIdx = 0;
            foreach (var kv in byIdx)
            {
                if (prev != null)
                {
                    double reach = Math.Abs(kv.Key - prevIdx) * w * 1.25 + 3.0;
                    var touch = new bool[prev.Count, kv.Value.Count];
                    var na = new int[prev.Count];
                    var nb = new int[kv.Value.Count];
                    for (int i = 0; i < prev.Count; i++)
                        for (int j = 0; j < kv.Value.Count; j++)
                            if (PartsTouch(prev[i], kv.Value[j], reach))
                            {
                                touch[i, j] = true; na[i]++; nb[j]++;
                            }
                    for (int i = 0; i < prev.Count; i++)
                        for (int j = 0; j < kv.Value.Count; j++)
                            if (touch[i, j] && na[i] == 1 && nb[j] == 1)
                                parent[Find(kv.Value[j])] = Find(prev[i]);
                }
                prev = kv.Value;
                prevIdx = kv.Key;
            }

            var groups = new Dictionary<CPlanEntry, List<CPlanEntry>>();
            var cells = new List<List<CPlanEntry>>();
            foreach (var kv in byIdx)
                foreach (var p in kv.Value)
                {
                    var r = Find(p);
                    if (!groups.TryGetValue(r, out var c)) { groups[r] = c = new List<CPlanEntry>(); cells.Add(c); }
                    c.Add(p);                         // byIdx order = sorted by offsetIdx
                }

            // main field first; the rest left to right, then A->B
            int main = 0;
            for (int i = 1; i < cells.Count; i++)
                if (cells[i].Count > cells[main].Count) main = i;
            var first = cells[main];
            cells.RemoveAt(main);
            cells.Sort((a, b) => a[0].offsetIdx != b[0].offsetIdx
                ? a[0].offsetIdx.CompareTo(b[0].offsetIdx) : a[0].piece.CompareTo(b[0].piece));
            cells.Insert(0, first);
            return cells;
        }

        // two parts of neighbour passes lie side by side somewhere (points within reach)
        private static bool PartsTouch(CPlanEntry a, CPlanEntry b, double reach)
        {
            double r2 = reach * reach;
            foreach (var sa in a.segments)
                for (int i = 0; i < sa.Count; i += 2)
                    foreach (var sb in b.segments)
                        for (int j = 0; j < sb.Count; j += 2)
                        {
                            double dx = sa[i].easting - sb[j].easting;
                            if (dx > reach || dx < -reach) continue;
                            double dy = sa[i].northing - sb[j].northing;
                            if (dx * dx + dy * dy <= r2) return true;
                        }
            return false;
        }

        // squared distance from a point to the nearest point of an entry's geometry
        private static double DistToEntry2(CPlanEntry p, double e, double n)
        {
            double best = double.MaxValue;
            foreach (var s in p.segments)
                foreach (var q in s)
                {
                    double dx = q.easting - e, dy = q.northing - n;
                    double d = dx * dx + dy * dy;
                    if (d < best) best = d;
                }
            return best;
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

        // pass at 1-based plan position pos drives A->B (and turns at the B end) when pos is
        // odd - the same alternation Generate writes into isSameWay
        private static bool TurnsAtB(int pos) { return pos % 2 == 1; }

        /// <summary>
        /// 10 m rule check for the passes of one zone, which start at ordered[start]: no turn
        /// between two of them may step the field end along by more than maxEndStep.
        /// </summary>
        private bool TransitionsHold(List<CPlanEntry> ordered, int start)
        {
            for (int i = start; i < ordered.Count - 1; i++)
            {
                double step = EndStep(ordered[i], ordered[i + 1], TurnsAtB(i + 1));
                if (step > maxEndStep) return false;
            }
            return true;
        }

        /// <summary>
        /// REQ 2026-10-17-3 - min U-turn for the field = the SHORTEST way through a zone in
        /// which every turn is a clean U. Track sequencing as a small travelling-salesman
        /// problem: the price of a turn a -> b is the lateral distance plus how far the field
        /// end steps along (a slanted end); a turn the U does not fit (closer than
        /// UTurnSpanNeeded + minTurnReserve) or that steps more than maxEndStep costs a K-turn
        /// penalty. Two starts are laid - the old walk (earliest workable pass) and the
        /// driver's n+1 / n zigzag with its zone end searched exhaustively - both are improved
        /// by local moves (shift one to three passes, reversed or not; reverse a stretch) as
        /// long as the total gets shorter, and the cheaper one is kept.
        /// already = passes ordered before this zone (keeps the A/B alternation). from = the
        /// pass the vehicle is on when only the rest of a plan is re-laid (REQ 2026-10-17-2):
        /// the first pass is then free, and the turn into it is priced too.
        /// </summary>
        private List<CPlanEntry> OrderMinTurn(List<CPlanEntry> lst, int already, CPlanEntry from = null)
        {
            int m = lst.Count;
            if (m == 0) return new List<CPlanEntry>();
            if (m < 3 && from == null) return new List<CPlanEntry>(lst);

            var tc = new TurnCosts(this, lst, already, from);
            int start = 0;
            if (from != null)
                for (int j = 1; j < m; j++)
                    if (tc.Start(j) < tc.Start(start) - 1e-9) start = j;

            int[] a = tc.Improve(tc.WalkEarliest(start));
            int[] b = tc.Improve(tc.TailSearch(tc.WalkZigzag(start)));
            int[] best = tc.Total(b) <= tc.Total(a) + 1e-9 ? b : a;

            var seq = new List<CPlanEntry>(m);
            foreach (int i in best) seq.Add(lst[i]);
            return seq;
        }

        // REQ 2026-10-17-3: room kept on top of the U itself (R_out + R_in), instead of +20 %
        public const double minTurnReserve = 0.5;
        private const double kTurnPenalty = 1000.0;
        private const double jumpRuleFactor = 2.0;    // × width per pass beyond n+1

        /// <summary>
        /// REQ 2026-10-17-3: rough length the generated plan drives on the headland, in plan
        /// order - per turn the lateral step, the field-end step and what the arcs add (a
        /// U = the quarter circles in place of the straight; too close = about one span more).
        /// </summary>
        public double HeadlandEstimate()
        {
            var list = new List<CPlanEntry>();
            foreach (var p in entries) if (!p.IsManual) list.Add(p);
            list.Sort((a, b) => a.order.CompareTo(b.order));

            double w = WorkWidth();
            double span = mf.yt.UTurnSpanNeeded;
            double sum = 0;
            for (int i = 0; i + 1 < list.Count; i++)
            {
                double lat = Math.Abs(list[i].offsetIdx - list[i + 1].offsetIdx) * w;
                double st = EndStep(list[i], list[i + 1], TurnsAtB(list[i].order));
                sum += lat + Math.Max(0, st) + (lat >= span ? (Math.PI / 2 - 1) * span : span);
            }
            return sum;
        }

        /// <summary>
        /// Turn prices of one zone and the moves on a pass sequence (indices into the zone
        /// list). A sequence s puts s[t] at plan position already + t + 1; the turn after it
        /// is at the B end when that position is odd (TurnsAtB).
        /// </summary>
        private sealed class TurnCosts
        {
            private readonly int m, already, n, window;
            private readonly bool hasFrom;
            private readonly double span;
            private readonly double[,] costA, costB;
            private readonly double[] startA, startB;

            public TurnCosts(CTurnPlan plan, List<CPlanEntry> lst, int already, CPlanEntry from)
            {
                m = lst.Count;
                this.already = already;
                hasFrom = from != null;
                double w = plan.WorkWidth();
                span = plan.mf.yt.UTurnSpanNeeded + minTurnReserve;
                n = Math.Max(1, (int)Math.Ceiling(span / Math.Max(0.1, w) - 1e-9));
                window = Math.Max(12, 3 * (n + 1));

                costA = new double[m, m];
                costB = new double[m, m];
                for (int i = 0; i < m; i++)
                    for (int j = 0; j < m; j++)
                    {
                        if (i == j) continue;
                        costA[i, j] = Price(plan, lst[i], lst[j], false, w);
                        costB[i, j] = Price(plan, lst[i], lst[j], true, w);
                    }
                startA = new double[m];
                startB = new double[m];
                if (hasFrom)
                    for (int j = 0; j < m; j++)
                    {
                        startA[j] = Price(plan, from, lst[j], false, w);
                        startB[j] = Price(plan, from, lst[j], true, w);
                    }
            }

            private double Price(CTurnPlan plan, CPlanEntry a, CPlanEntry b, bool atB, double w)
            {
                double lat = Math.Abs(a.offsetIdx - b.offsetIdx) * w;
                double st = EndStep(a, b, atB);
                if (st < 0) st = 0;
                double c = lat + st;
                if (lat < span - 1e-6 || st > plan.maxEndStep) c += kTurnPenalty;

                // jump rule (field 2026-10-17): n and n+1 only - a longer jump pays extra per
                // pass beyond n+1, so it is taken only where it must be (end step, zone end)
                int extra = Math.Abs(a.offsetIdx - b.offsetIdx) - (n + 1);
                if (extra > 0) c += jumpRuleFactor * extra * w;
                return c;
            }

            public double Start(int j) { return !hasFrom ? 0 : (TurnsAtB(already) ? startB[j] : startA[j]); }

            private double Step(int a, int b, int t)
            {
                return TurnsAtB(already + t + 1) ? costB[a, b] : costA[a, b];
            }

            private bool Fits(int a, int b, int t) { return Step(a, b, t) < kTurnPenalty; }

            // turns t in [lo, hi): t = -1 is the turn into s[0], t = i the one after s[i]
            private double Range(int[] s, int lo, int hi)
            {
                double c = 0;
                if (lo < 0) { c += Start(s[0]); lo = 0; }
                if (hi > m - 1) hi = m - 1;
                for (int t = lo; t < hi; t++) c += Step(s[t], s[t + 1], t);
                return c;
            }

            public double Total(int[] s) { return Range(s, -1, m - 1); }

            /// <summary>The F50 walk: the earliest still-to-do pass that fits, one look ahead.</summary>
            public int[] WalkEarliest(int start)
            {
                var used = new bool[m];
                var s = new int[m];
                s[0] = start;
                used[start] = true;
                int cur = start;
                for (int t = 1; t < m; t++)
                {
                    int next = -1;
                    for (int k = 0; k < m && next < 0; k++)
                    {
                        if (used[k] || !Fits(cur, k, t - 1)) continue;
                        bool onward = t == m - 1;
                        for (int y = 0; !onward && y < m; y++)
                            if (!used[y] && y != k && Fits(k, y, t)) onward = true;
                        if (onward) next = k;
                    }
                    for (int k = 0; k < m && next < 0; k++)
                        if (!used[k] && Fits(cur, k, t - 1)) next = k;
                    if (next < 0) next = Cheapest(used, cur, t - 1);
                    used[next] = true;
                    s[t] = next;
                    cur = next;
                }
                return s;
            }

            /// <summary>The driver's zigzag: n+1 on, n back; taken -> the cheapest that is left.</summary>
            public int[] WalkZigzag(int start)
            {
                var used = new bool[m];
                var s = new int[m];
                s[0] = start;
                used[start] = true;
                int cur = start;
                bool up = true;
                for (int t = 1; t < m; t++)
                {
                    int p1 = up ? cur + n + 1 : cur - n;
                    int p2 = up ? cur - n : cur + n + 1;
                    int next = -1;
                    if (p1 >= 0 && p1 < m && !used[p1] && Fits(cur, p1, t - 1)) next = p1;
                    else if (p2 >= 0 && p2 < m && !used[p2] && Fits(cur, p2, t - 1)) next = p2;
                    if (next < 0) next = Cheapest(used, cur, t - 1);
                    up = next < cur;
                    used[next] = true;
                    s[t] = next;
                    cur = next;
                }
                return s;
            }

            private int Cheapest(bool[] used, int cur, int t)
            {
                int best = -1;
                double bc = double.MaxValue;
                for (int k = 0; k < m; k++)
                {
                    if (used[k]) continue;
                    double c = Step(cur, k, t);
                    if (c < bc - 1e-9) { bc = c; best = k; }
                }
                return best;
            }

            /// <summary>
            /// The zone end, where a walk strands its last passes side by side: the last few
            /// are laid out exhaustively (branch and bound, with a node budget).
            /// </summary>
            public int[] TailSearch(int[] s)
            {
                int tail = Math.Min(Math.Min(2 * (n + 1) + 1, 10), m - 1);
                if (tail < 2) return s;
                int head = m - tail;
                var rest = new List<int>();
                for (int i = head; i < m; i++) rest.Add(s[i]);

                double bestCost = Range(s, head - 1, m - 1);
                int[] best = null;
                var path = new int[tail];
                var used = new bool[tail];
                long budget = 2000000;

                void Dfs(int prev, int depth, double cost)
                {
                    if (--budget < 0 || cost >= bestCost - 1e-9) return;
                    if (depth == tail)
                    {
                        bestCost = cost;
                        best = (int[])path.Clone();
                        return;
                    }
                    for (int r = 0; r < tail; r++)
                    {
                        if (used[r]) continue;
                        used[r] = true;
                        path[depth] = rest[r];
                        Dfs(rest[r], depth + 1, cost + Step(prev, rest[r], head + depth - 1));
                        used[r] = false;
                    }
                }
                Dfs(s[head - 1], 0, 0);

                if (best == null) return s;
                var o = (int[])s.Clone();
                Array.Copy(best, 0, o, head, tail);
                return o;
            }

            /// <summary>Local moves while the total gets shorter (first pass stays unless free).</summary>
            public int[] Improve(int[] s)
            {
                s = (int[])s.Clone();
                int first = hasFrom ? 0 : 1;
                var trial = new int[m];
                for (int sweep = 0; sweep < 50; sweep++)
                {
                    bool better = false;

                    // shift a stretch of 1..3 passes (as it is or reversed) to another place
                    for (int len = 1; len <= 3; len++)
                        for (int i = first; i + len <= m; i++)
                            for (int j = Math.Max(first, i - window); j <= Math.Min(m - len, i + window); j++)
                            {
                                if (j == i) continue;
                                int lo = Math.Min(i, j) - 1, hi = Math.Max(i, j) + len;
                                double before = Range(s, lo, hi);
                                for (int rev = 0; rev < 2; rev++)
                                {
                                    Shift(s, trial, i, len, j, rev == 1);
                                    if (Range(trial, lo, hi) < before - 1e-9)
                                    {
                                        Array.Copy(trial, s, m);
                                        better = true;
                                        break;
                                    }
                                }
                            }

                    // reverse a stretch
                    for (int i = first; i < m - 1; i++)
                        for (int j = i + 1; j <= Math.Min(m - 1, i + window); j++)
                        {
                            double before = Range(s, i - 1, j + 1);
                            Array.Copy(s, trial, m);
                            Array.Reverse(trial, i, j - i + 1);
                            if (Range(trial, i - 1, j + 1) < before - 1e-9)
                            {
                                Array.Copy(trial, s, m);
                                better = true;
                            }
                        }

                    if (!better) break;
                }
                return s;
            }

            // o = s with s[i .. i+len) taken out and put back so that it starts at index j
            private void Shift(int[] s, int[] o, int i, int len, int j, bool reversed)
            {
                int w = 0;
                for (int k = 0; k < m; k++)
                {
                    if (w == j) { Put(s, o, ref w, i, len, reversed); }
                    if (k >= i && k < i + len) continue;
                    o[w++] = s[k];
                }
                if (w == j) Put(s, o, ref w, i, len, reversed);
            }

            private static void Put(int[] s, int[] o, ref int w, int i, int len, bool reversed)
            {
                for (int q = 0; q < len; q++) o[w++] = s[reversed ? i + len - 1 - q : i + q];
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
                //EARLIEST still-to-do pass that is far enough, not the nearest one: the point
                //is to close the worked block up as soon as the spacing allows, instead of
                //running to the far end and wandering back (user, 2026-08-16). For 10 rows at a
                //3-row minimum that is 1,4,7,2,5,8,3,6,9,10 - after seven passes the field is
                //covered in one piece up to row 5, where "nearest" was still at row 1.
                var fits = new List<int>();
                for (int k = 0; k < m; k++)
                    if (!used[k] && gap[cur, k] >= span) fits.Add(k);
                fits.Sort();

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
            //no zones in an orchard - the entry's zone is the inter-row number and it only
            //colours the passes. Left at the field default of 2 the count showed up in the
            //texts as if the orchard had zones.
            zoneCount = 1;
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
            // the part the plan goes to next (a cut pass has several on this offset)
            CPlanEntry target = null;
            foreach (var p in entries)
            {
                if (p.offsetIdx != targetIdx || EntryTrackName(p) != curName) continue;
                bool open = !p.isWorked && !p.isSkipped;
                bool tOpen = target != null && !target.isWorked && !target.isSkipped;
                if (target == null || (open && (!tOpen || p.order < target.order))) target = p;
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
        /// Not clipped to the boundary (field 2026-10-13): the first line is usually driven
        /// along the field edge, and clipping left nothing of it. An AB line runs across the
        /// field's extent along it, a curve is drawn whole.
        /// </summary>
        public bool TryGetActiveLineGeometry(out List<List<vec3>> segs)
        {
            segs = null;
            if (!TryResolveTrack(out CTrk track)) return false;

            if (track.mode != TrackMode.AB)
            {
                if (track.curvePts.Count < 2) return false;
                segs = new List<List<vec3>> { new List<vec3>(track.curvePts) };
                return true;
            }

            if (mf.bnd.bndList.Count == 0 || mf.bnd.bndList[0].fenceLine.Count < 3) return false;
            double hx = Math.Sin(track.heading), hy = Math.Cos(track.heading);
            double tMin = double.MaxValue, tMax = double.MinValue;
            foreach (var f in mf.bnd.bndList[0].fenceLine)
            {
                double t = (f.easting - track.ptA.easting) * hx + (f.northing - track.ptA.northing) * hy;
                if (t < tMin) tMin = t;
                if (t > tMax) tMax = t;
            }
            tMin -= displayOverhang;
            tMax += displayOverhang;
            segs = new List<List<vec3>>
            {
                new List<vec3>
                {
                    new vec3(track.ptA.easting + hx * tMin, track.ptA.northing + hy * tMin, track.heading),
                    new vec3(track.ptA.easting + hx * tMax, track.ptA.northing + hy * tMax, track.heading)
                }
            };
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

            // REQ 2026-10-12-1: a pass the boundary cuts apart has one entry per part - the
            // one the vehicle is on is the nearest
            CPlanEntry found = null;
            double best = double.MaxValue;
            int matches = 0;
            foreach (var p in entries)
            {
                if (p.offsetIdx != idx || EntryTrackName(p) != cur) continue;
                matches++;
                if (found == null) found = p;
                if (matches == 1) continue;
                if (matches == 2)
                {
                    EnsureGeometry(found);
                    best = DistToEntry2(found, mf.pivotAxlePos.easting, mf.pivotAxlePos.northing);
                }
                EnsureGeometry(p);
                double d = DistToEntry2(p, mf.pivotAxlePos.easting, mf.pivotAxlePos.northing);
                if (d < best) { best = d; found = p; }
            }
            return found;
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

        // ── REQ 2026-10-17-2: the plan catches up with what was worked without it ──
        // Passes driven with auto turn off are on the map as section coverage, but the plan
        // never saw them. When auto turn / Follow plan is switched on, every open pass is
        // checked against the coverage: more than half of it painted (points every 2 m along
        // it) = worked, the same 50 % rule as the runtime. All coverage counts - it is cleared
        // before every new operation. What is still open is then laid out again from where
        // the vehicle is: the open pass it stands on comes first, otherwise the cheapest turn
        // from the pass it is on (min U-turn) - the current zone first, the others after.

        private const double syncSampleStep = 2.0;    // m between coverage samples on a pass
        private const double syncCellSize = 5.0;      // m, coverage grid cell

        /// <summary>Returns how many passes were newly marked worked (field plans only).</summary>
        public int SyncWithCoverage()
        {
            if (entries.Count == 0 || mf.tool.isToolOrchard) return 0;

            var grid = BuildCoverageGrid();
            int marked = 0;
            if (grid.Count > 0)
            {
                foreach (var p in entries)
                {
                    if (p.isWorked || p.isSkipped) continue;
                    EnsureGeometry(p);
                    if (p.passLength <= 1.0) continue;

                    int total = 0, covered = 0;
                    foreach (var seg in p.segments)
                        for (int i = 0; i + 1 < seg.Count; i++)
                        {
                            double dx = seg[i + 1].easting - seg[i].easting;
                            double dn = seg[i + 1].northing - seg[i].northing;
                            double len = Math.Sqrt(dx * dx + dn * dn);
                            int steps = Math.Max(1, (int)(len / syncSampleStep));
                            for (int k = 0; k < steps; k++)
                            {
                                double f = (k + 0.5) / steps;
                                total++;
                                if (IsCovered(grid, seg[i].easting + dx * f, seg[i].northing + dn * f)) covered++;
                            }
                        }
                    if (total > 0 && covered > 0.5 * total)
                    {
                        p.isWorked = true;
                        marked++;
                    }
                }
            }

            bool relaid = RelayRemaining(marked > 0);
            if (marked > 0 || relaid)
            {
                InvalidateRun();
                mf.FileSaveTurnPlan();
                Log.EventWriter("TurnPlan: synced with coverage, " + marked + " pass(es) marked worked"
                    + (relaid ? ", rest re-laid from the vehicle" : ""));
            }
            return marked;
        }

        // coverage quads (two triangles each) bucketed on a grid; a triangle strip list is
        // [colour, L0, R0, L1, R1, ...] and quad k = L_k, R_k, L_k+1, R_k+1
        private Dictionary<long, List<vec3[]>> BuildCoverageGrid()
        {
            var grid = new Dictionary<long, List<vec3[]>>();
            if (mf.triStrip == null) return grid;
            foreach (var strip in mf.triStrip)
            {
                if (strip?.patchList == null) continue;
                foreach (var tri in strip.patchList)
                {
                    for (int k = 1; k + 3 < tri.Count; k += 2)
                    {
                        var q = new[] { tri[k], tri[k + 1], tri[k + 2], tri[k + 3] };
                        double minE = q[0].easting, maxE = minE, minN = q[0].northing, maxN = minN;
                        for (int i = 1; i < 4; i++)
                        {
                            minE = Math.Min(minE, q[i].easting); maxE = Math.Max(maxE, q[i].easting);
                            minN = Math.Min(minN, q[i].northing); maxN = Math.Max(maxN, q[i].northing);
                        }
                        int e0 = (int)Math.Floor(minE / syncCellSize), e1 = (int)Math.Floor(maxE / syncCellSize);
                        int n0 = (int)Math.Floor(minN / syncCellSize), n1 = (int)Math.Floor(maxN / syncCellSize);
                        if ((e1 - e0) > 20 || (n1 - n0) > 20) continue;    // a broken strip, not a quad
                        for (int ce = e0; ce <= e1; ce++)
                            for (int cn = n0; cn <= n1; cn++)
                            {
                                long key = CellKey(ce, cn);
                                if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<vec3[]>();
                                list.Add(q);
                            }
                    }
                }
            }
            return grid;
        }

        private static long CellKey(int e, int n) { return ((long)e << 32) ^ (uint)n; }

        private static bool IsCovered(Dictionary<long, List<vec3[]>> grid, double e, double n)
        {
            long key = CellKey((int)Math.Floor(e / syncCellSize), (int)Math.Floor(n / syncCellSize));
            if (!grid.TryGetValue(key, out var list)) return false;
            foreach (var q in list)
                if (InTriangle(q[0], q[1], q[2], e, n) || InTriangle(q[1], q[2], q[3], e, n)) return true;
            return false;
        }

        private static bool InTriangle(vec3 a, vec3 b, vec3 c, double e, double n)
        {
            double d1 = (e - b.easting) * (a.northing - b.northing) - (a.easting - b.easting) * (n - b.northing);
            double d2 = (e - c.easting) * (b.northing - c.northing) - (b.easting - c.easting) * (n - c.northing);
            double d3 = (e - a.easting) * (c.northing - a.northing) - (c.easting - a.easting) * (n - a.northing);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        // distance (squared) from a point to the pass polyline - segments, not just vertices
        private static double DistToPass2(CPlanEntry p, double e, double n)
        {
            double best = double.MaxValue;
            foreach (var s in p.segments)
                for (int i = 0; i < s.Count; i++)
                {
                    double d;
                    if (i + 1 < s.Count)
                    {
                        double dx = s[i + 1].easting - s[i].easting, dn = s[i + 1].northing - s[i].northing;
                        double l2 = dx * dx + dn * dn;
                        double t = l2 < 1e-9 ? 0 : ((e - s[i].easting) * dx + (n - s[i].northing) * dn) / l2;
                        t = Math.Max(0, Math.Min(1, t));
                        double qe = s[i].easting + dx * t - e, qn = s[i].northing + dn * t - n;
                        d = qe * qe + qn * qn;
                    }
                    else
                    {
                        double qe = s[i].easting - e, qn = s[i].northing - n;
                        d = qe * qe + qn * qn;
                    }
                    if (d < best) best = d;
                }
            return best;
        }

        /// <summary>
        /// Lay the open passes out again from the vehicle. Done ones keep their place at the
        /// front (in their old order). Without changes (nothing new worked, the vehicle not on
        /// an open pass out of turn) the plan stays exactly as it is.
        /// </summary>
        private bool RelayRemaining(bool changed)
        {
            var main = new List<CPlanEntry>();
            foreach (var p in entries) if (!p.IsManual) main.Add(p);
            if (main.Count == 0) return false;
            main.Sort((a, b) => a.order.CompareTo(b.order));

            var done = new List<CPlanEntry>();
            var open = new List<CPlanEntry>();
            foreach (var p in main) (p.isWorked || p.isSkipped ? done : open).Add(p);
            if (open.Count == 0) return false;

            // the pass the vehicle is on (within 3/4 of a width of it), else the nearest one
            CPlanEntry near = null;
            double nearD = double.MaxValue;
            foreach (var p in main)
            {
                EnsureGeometry(p);
                double d = DistToPass2(p, mf.pivotAxlePos.easting, mf.pivotAxlePos.northing);
                if (d < nearD) { nearD = d; near = p; }
            }
            double w = WorkWidth();
            bool onOpen = near != null && open.Contains(near) && nearD <= 0.5625 * w * w;

            if (!changed && (!onOpen || near == open[0])) return false;

            // zones: the one the vehicle is in first, the rest in their planned sequence
            var zoneSeq = new List<int>();
            int curZone = near != null ? near.zone : open[0].zone;
            bool curHasOpen = false;
            foreach (var p in open) if (p.zone == curZone) { curHasOpen = true; break; }
            if (curHasOpen) zoneSeq.Add(curZone);
            foreach (var p in open) if (!zoneSeq.Contains(p.zone)) zoneSeq.Add(p.zone);

            var seq = new List<CPlanEntry>(done);
            foreach (int z in zoneSeq)
            {
                var lst = new List<CPlanEntry>();
                foreach (var p in open) if (p.zone == z) lst.Add(p);

                CPlanEntry from = null;
                if (z == curZone && near != null)
                {
                    if (onOpen) { lst.Remove(near); lst.Insert(0, near); }   // stand on it = next
                    else from = near;                                         // turn from it
                }
                else
                {
                    // REQ 2026-10-17-3 §4.2: a later zone starts at the edge next to where
                    // the one before ended (the vehicle's pass when its own zone is done)
                    CPlanEntry prev = seq.Count > 0 && seq.Count > done.Count ? seq[seq.Count - 1] : near;
                    bool beside;
                    if (mode == ModeContour) beside = false;
                    else lst = FacePrevious(lst, prev, seq.Count, out beside);
                    if (beside) from = prev;
                }

                if (mode == ModeMinUTurn) seq.AddRange(OrderMinTurn(lst, seq.Count, from));
                else seq.AddRange(lst);
            }

            int order = 0;
            foreach (var p in seq)
            {
                p.order = ++order;
                p.isSameWay = (order % 2) == 1;
            }
            foreach (var p in entries) if (p.IsManual) p.order = ++order;
            entries.Sort((a, b) => a.order.CompareTo(b.order));
            return true;
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
                    out List<List<vec3>> segs, out double len, out bool split)
                && TakePiece(p, ref segs, ref len))
            {
                p.segments = segs;
                p.passLength = len;
                p.crossesObstacle = split && segs.Count > 1;
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

            bool Inside(double e, double n, ref bool clippedByObstacle)
            {
                var pt = new vec2(e, n);
                bool ins = useHeadland ? headland.IsPointInPolygon(pt) : fenceEar.IsPointInPolygon(pt);
                if (!ins) return false;
                for (int b = 1; b < mf.bnd.bndList.Count; b++)
                {
                    if (mf.bnd.bndList[b].isDriveThru) continue;
                    if (mf.bnd.bndList[b].fenceLineEar.IsPointInPolygon(pt))
                    {
                        clippedByObstacle = true;
                        return false;
                    }
                }
                return true;
            }

            // REQ 2026-10-17-3: the samples are 2 m apart, so a run used to end on the last
            // sample inside - pass ends on a 2 m grid, and the min-turn order counted that
            // grid as end steps. The crossing is found between the last sample inside and the
            // first one outside (bisection, ~1 mm) and becomes the end point.
            vec3 Crossing(vec3 inPt, vec3 outPt)
            {
                bool dummy = false;
                for (int it = 0; it < 12; it++)
                {
                    var mid = new vec3((inPt.easting + outPt.easting) * 0.5,
                        (inPt.northing + outPt.northing) * 0.5, inPt.heading);
                    if (Inside(mid.easting, mid.northing, ref dummy)) inPt = mid; else outPt = mid;
                }
                return inPt;
            }

            for (int i = 0; i < pass.Count; i++)
            {
                bool inside = Inside(pass[i].easting, pass[i].northing, ref everClipped);

                if (inside)
                {
                    if (run == null)
                    {
                        run = new List<vec3>();
                        if (i > 0) run.Add(Crossing(pass[i], pass[i - 1]));
                    }
                    run.Add(pass[i]);
                }
                else if (run != null)
                {
                    run.Add(Crossing(run[run.Count - 1], pass[i]));
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
