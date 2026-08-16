using AgLibrary.Logging;
using AgOpenGPS.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace AgOpenGPS
{
    // Feature 50 — Orchard Navigation, Phase 1 (MVP "sredina").
    // A Row is a fixed reference line of trees (drawn, or imported from KML). The planner turns
    // adjacent rows into ONE mid-inter-row guidance line each ("sredina" = one pass per inter-row),
    // output as ordinary AOG Curve tracks so the existing guidance + K-turn drives them.
    // Multi-pass / safe-space / tree-marking are later phases.

    public enum RowSource { Manual = 0, KmlLine = 1, KmlPoints = 2, Lidar = 3, Recorded = 4, Generated = 5 }

    /// <summary>One orchard row: its control points, the derived dense curve, and (optional) trees.</summary>
    public class OrchRow
    {
        public List<vec2> pts = new List<vec2>();      // control points (drawn or KML LineString)
        public List<vec3> curve = new List<vec3>();    // densified curve with headings (derived)
        public List<vec2> trees = new List<vec2>();    // optional tree positions (KML Point / LiDAR)
        public RowSource source = RowSource.Manual;
        public string name = "Row";

        public OrchRow() { }

        public OrchRow(OrchRow o)
        {
            pts = new List<vec2>(o.pts);
            curve = new List<vec3>(o.curve);
            trees = new List<vec2>(o.trees);
            source = o.source;
            name = o.name;
        }

        /// <summary>Rebuild the dense curve from the control points (A→B nearest-neighbour, densified).</summary>
        public void BuildCurve()
        {
            curve.Clear();
            if (pts.Count < 2) return;
            var ordered = COrchard.OrderEndToEnd(pts);
            var des = COrchard.Densify(ordered, 1.6);
            if (des.Count < 2) return;
            CABCurve.CalculateHeadings(ref des);
            curve = des;
        }

        /// <summary>Row direction (unit vector, easting/northing) from first to last curve point.</summary>
        public vec2 Dir()
        {
            if (curve.Count < 2) return new vec2(0, 1);
            double dx = curve[curve.Count - 1].easting - curve[0].easting;
            double dy = curve[curve.Count - 1].northing - curve[0].northing;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.001) return new vec2(0, 1);
            return new vec2(dx / len, dy / len);
        }

        public vec2 Centroid()
        {
            double e = 0, n = 0; int c = curve.Count;
            if (c == 0) return new vec2(0, 0);
            for (int i = 0; i < c; i++) { e += curve[i].easting; n += curve[i].northing; }
            return new vec2(e / c, n / c);
        }
    }

    /// <summary>Geometry + planner + KML/file IO for the orchard feature (all static, no UI).</summary>
    public static class COrchard
    {
        public const double ExtendMeters = 150.0; // guidance tail length each end (matches Smart AB)

        // ── Path modes (SIM feedback 2026-07-29) ──────────────────────────
        // How the AB lines are derived from the rows. The rows are the definition of the
        // orchard; these three say what the machine does with them.
        public const int PathOnePass = 0;    // one line down the middle of each inter-row
        public const int PathMultiPass = 1;  // several passes per inter-row (to be re-specced)
        public const int PathOnRow = 2;      // the line IS the row + planter offset (planting)
        public const int PathTwoPass = 3;    // exactly two passes, one hugging each tree line

        /// <summary>
        /// Every track that this class generates carries this name prefix. The path is a
        /// DERIVED object: it is rebuilt whenever the rows or the implement change, and it
        /// is not written to TrackLines.txt - the prefix is how the tracks the user made by
        /// hand (Smart AB, recorded curves) are told apart and left alone.
        /// </summary>
        public const string DerivedPrefix = "Orch ";

        /// <summary>Legacy prefix: paths committed by the planner's old "Make path" button.</summary>
        public const string LegacyDerivedPrefix = "Row Mid ";

        public static bool IsDerivedName(string name)
            => name != null && (name.StartsWith(DerivedPrefix) || name.StartsWith(LegacyDerivedPrefix));

        /// <summary>
        /// The inter-row (or row) number carried in a derived pass name - "Orch Mid 3 p2" -> 3,
        /// "Orch row 5" -> 5. Used as the plan's zone, which is what colours the passes.
        /// </summary>
        public static int ZoneFromDerivedName(string name)
        {
            if (name == null) return 1;
            string[] parts = name.Split(' ');
            return parts.Length > 2 && int.TryParse(parts[2], out int z) ? z : 1;
        }

        /// <summary>
        /// The saved path mode, migrating an install that only knows the old one-pass flag
        /// (checked = one pass, unchecked = multi pass). Runs once and writes the result back.
        /// </summary>
        public static int ResolvePathMode()
        {
            var ts = Properties.ToolSettings.Default;
            if (ts.setTool_orchardPathMode < 0 || ts.setTool_orchardPathMode > PathTwoPass)
            {
                ts.setTool_orchardPathMode = ts.setTool_orchardOnePass ? PathOnePass : PathMultiPass;
                try { ts.Save(); } catch { /* a read-only profile is not worth a crash */ }
            }
            return ts.setTool_orchardPathMode;
        }

        // ── Generator (planting): lay out a NEW orchard inside the boundary ──
        // Planting turns F50 around: there are no trees yet, so the rows and the seedling
        // positions are GENERATED from one drawn reference line (straight AB or a curve)
        // plus the spacing settings, and the tractor is then guided so the planter drops
        // each seedling on its planned spot.
        //
        // Layout rules:
        //   rowSpacing   - lateral distance between rows; the reference line is copied
        //                  sideways both ways until the field is covered
        //   headland     - turning strip: each row STOPS this far (along the row) before
        //                  the boundary
        //   treeSpacing  - distance between seedlings along a row
        //   treeSetback  - no seedling closer than this to the boundary (any direction)
        //   stagger      - every other row shifted half a tree spacing along the row
        //                  (triangular planting instead of a rectangular grid)
        //
        // Tree positions are taken at the SAME arc-length parameter of the reference line
        // on every row, so they line up across rows (radially on a curved reference; on a
        // curve the along-row spacing of the inner/outer rows therefore differs slightly).
        //
        // bounds[0] = outer fence, bounds[1..] = inner exclusions (islands).
        public static List<OrchRow> GenerateOrchard(List<vec2> refPts, List<List<vec2>> bounds,
            double rowSpacing, double treeSpacing, double headland, double treeSetback,
            bool stagger, out string err)
        {
            err = null;
            var rows = new List<OrchRow>();

            if (refPts == null || refPts.Count < 2) { err = "Draw a reference line first (2 points or more)"; return rows; }
            if (bounds == null || bounds.Count == 0 || bounds[0] == null || bounds[0].Count < 3)
            { err = "A field boundary is needed to lay out the orchard"; return rows; }
            if (rowSpacing < 0.5) { err = "Row spacing too small"; return rows; }
            if (treeSpacing < 0.3) { err = "Tree spacing too small"; return rows; }

            var outer = bounds[0];
            var ordered = OrderEndToEnd(refPts);
            bool refIsStraight = ordered.Count == 2;

            // reference curve, densified fine enough to interpolate tree spots on
            var refCurve = Densify(ordered, 0.5);
            if (refCurve.Count < 2) { err = "Reference line too short"; return rows; }
            CABCurve.CalculateHeadings(ref refCurve);

            // stretch it past the field so every row spans the whole boundary
            BoundsOf(outer, out double minE, out double minN, out double maxE, out double maxN);
            double diag = Math.Sqrt((maxE - minE) * (maxE - minE) + (maxN - minN) * (maxN - minN));
            ExtendEnds(refCurve, diag + rowSpacing);
            CABCurve.CalculateHeadings(ref refCurve);

            // arc length along the reference = the shared longitudinal ruler for all rows
            var sRef = new double[refCurve.Count];
            for (int i = 1; i < refCurve.Count; i++)
            {
                double de = refCurve[i].easting - refCurve[i - 1].easting;
                double dn = refCurve[i].northing - refCurve[i - 1].northing;
                sRef[i] = sRef[i - 1] + Math.Sqrt(de * de + dn * dn);
            }

            // how far sideways the boundary reaches from the reference line
            double latMin = double.MaxValue, latMax = -double.MaxValue;
            foreach (var b in outer)
            {
                double lat = SignedLateral(refCurve, b);
                if (lat < latMin) latMin = lat;
                if (lat > latMax) latMax = lat;
            }
            if (latMin > latMax) { err = "Reference line does not cross the field"; return rows; }

            int kMin = (int)Math.Floor(latMin / rowSpacing);
            int kMax = (int)Math.Ceiling(latMax / rowSpacing);
            if (kMax - kMin > 2000) { err = "Row spacing too small for this field"; return rows; }

            for (int k = kMin; k <= kMax; k++)
            {
                var rowCurve = OffsetCurve(refCurve, k * rowSpacing);

                // keep the longest run of points that lie inside the field
                if (!LongestInsideRun(rowCurve, bounds, out int lo, out int hi)) continue;

                // turning strip: pull both ends back by the headland width, along the row
                double sIn = sRef[lo] + headland, sOut = sRef[hi] - headland;
                while (lo <= hi && sRef[lo] < sIn) lo++;
                while (hi >= lo && sRef[hi] > sOut) hi--;
                if (hi - lo < 1) continue;

                var row = new OrchRow { source = RowSource.Generated, name = "Row " + (rows.Count + 1) };

                // control points: a straight reference needs only the two ends
                if (refIsStraight)
                {
                    row.pts.Add(new vec2(rowCurve[lo].easting, rowCurve[lo].northing));
                    row.pts.Add(new vec2(rowCurve[hi].easting, rowCurve[hi].northing));
                }
                else
                {
                    double sLast = double.MinValue;
                    for (int i = lo; i <= hi; i++)
                    {
                        if (i != lo && i != hi && sRef[i] - sLast < 4.0) continue;
                        row.pts.Add(new vec2(rowCurve[i].easting, rowCurve[i].northing));
                        sLast = sRef[i];
                    }
                }
                row.BuildCurve();
                if (row.curve.Count < 2) continue;

                // seedlings at shared multiples of the tree spacing -> aligned across rows
                double shift = (stagger && (k & 1) != 0) ? treeSpacing * 0.5 : 0.0;
                long m0 = (long)Math.Ceiling((sRef[lo] - shift) / treeSpacing);
                long m1 = (long)Math.Floor((sRef[hi] - shift) / treeSpacing);
                int cursor = lo;
                for (long m = m0; m <= m1; m++)
                {
                    double target = m * treeSpacing + shift;
                    while (cursor < hi && sRef[cursor + 1] < target) cursor++;
                    if (cursor >= hi) break;

                    double span = sRef[cursor + 1] - sRef[cursor];
                    double f = span > 0.0001 ? (target - sRef[cursor]) / span : 0.0;
                    var tree = new vec2(
                        rowCurve[cursor].easting + (rowCurve[cursor + 1].easting - rowCurve[cursor].easting) * f,
                        rowCurve[cursor].northing + (rowCurve[cursor + 1].northing - rowCurve[cursor].northing) * f);

                    if (!IsInsideField(tree, bounds)) continue;
                    if (treeSetback > 0 && DistToBounds(tree, bounds) < treeSetback) continue;
                    row.trees.Add(tree);
                }

                if (row.trees.Count == 0) continue;   // a row with no seedlings is not a row
                rows.Add(row);
            }

            if (rows.Count == 0) err = "Nothing generated - check the spacings and the headland width";
            return rows;
        }

        /// <summary>Re-place the seedlings of a row along its (possibly hand-edited) curve.</summary>
        public static void PlaceTreesOnRow(OrchRow row, List<List<vec2>> bounds,
            double treeSpacing, double treeSetback, double headland)
        {
            row.trees.Clear();
            if (row.curve.Count < 2 || treeSpacing < 0.3) return;

            // arc length along this row, ends pulled back by the headland
            var s = new double[row.curve.Count];
            for (int i = 1; i < row.curve.Count; i++)
            {
                double de = row.curve[i].easting - row.curve[i - 1].easting;
                double dn = row.curve[i].northing - row.curve[i - 1].northing;
                s[i] = s[i - 1] + Math.Sqrt(de * de + dn * dn);
            }
            double total = s[s.Length - 1];
            double from = headland, to = total - headland;
            if (to <= from) return;

            int cursor = 0;
            for (double target = from; target <= to + 0.0001; target += treeSpacing)
            {
                while (cursor < row.curve.Count - 2 && s[cursor + 1] < target) cursor++;
                double span = s[cursor + 1] - s[cursor];
                double f = span > 0.0001 ? (target - s[cursor]) / span : 0.0;
                var tree = new vec2(
                    row.curve[cursor].easting + (row.curve[cursor + 1].easting - row.curve[cursor].easting) * f,
                    row.curve[cursor].northing + (row.curve[cursor + 1].northing - row.curve[cursor].northing) * f);

                if (bounds != null && bounds.Count > 0 && bounds[0] != null && bounds[0].Count >= 3)
                {
                    if (!IsInsideField(tree, bounds)) continue;
                    if (treeSetback > 0 && DistToBounds(tree, bounds) < treeSetback) continue;
                }
                row.trees.Add(tree);
            }
        }

        // ── Generator helpers ─────────────────────────────────────────────
        private static void BoundsOf(List<vec2> poly, out double minE, out double minN,
            out double maxE, out double maxN)
        {
            minE = minN = double.MaxValue; maxE = maxN = -double.MaxValue;
            foreach (var p in poly)
            {
                if (p.easting < minE) minE = p.easting;
                if (p.easting > maxE) maxE = p.easting;
                if (p.northing < minN) minN = p.northing;
                if (p.northing > maxN) maxN = p.northing;
            }
        }

        // lateral offset of a point from the curve: + = right of the curve heading
        private static double SignedLateral(List<vec3> curve, vec2 p)
        {
            double best = double.MaxValue; int bi = 0;
            for (int i = 0; i < curve.Count; i++)
            {
                double de = curve[i].easting - p.easting, dn = curve[i].northing - p.northing;
                double d = de * de + dn * dn;
                if (d < best) { best = d; bi = i; }
            }
            double rx = Math.Sin(curve[bi].heading + glm.PIBy2), ry = Math.Cos(curve[bi].heading + glm.PIBy2);
            return (p.easting - curve[bi].easting) * rx + (p.northing - curve[bi].northing) * ry;
        }

        public static bool IsInsideField(vec2 p, List<List<vec2>> bounds)
        {
            if (bounds == null || bounds.Count == 0) return true;
            if (!bounds[0].IsPointInPolygon(p)) return false;
            for (int i = 1; i < bounds.Count; i++)
                if (bounds[i] != null && bounds[i].Count >= 3 && bounds[i].IsPointInPolygon(p)) return false;
            return true;
        }

        /// <summary>Shortest distance from a point to any boundary ring (outer or island).</summary>
        public static double DistToBounds(vec2 p, List<List<vec2>> bounds)
        {
            double best = double.MaxValue;
            if (bounds == null) return best;
            foreach (var ring in bounds)
            {
                if (ring == null || ring.Count < 2) continue;
                for (int i = 0; i < ring.Count; i++)
                {
                    double d = DistToSegment(p, ring[i], ring[(i + 1) % ring.Count]);
                    if (d < best) best = d;
                }
            }
            return best;
        }

        private static double DistToSegment(vec2 p, vec2 a, vec2 b)
        {
            double vx = b.easting - a.easting, vy = b.northing - a.northing;
            double wx = p.easting - a.easting, wy = p.northing - a.northing;
            double len2 = vx * vx + vy * vy;
            double t = len2 > 0.000001 ? (wx * vx + wy * vy) / len2 : 0.0;
            if (t < 0) t = 0; else if (t > 1) t = 1;
            double dx = wx - vx * t, dy = wy - vy * t;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // longest stretch of consecutive curve points that lies inside the field
        private static bool LongestInsideRun(List<vec3> curve, List<List<vec2>> bounds,
            out int lo, out int hi)
        {
            lo = hi = -1;
            int bestLen = 0, runStart = -1;
            for (int i = 0; i <= curve.Count; i++)
            {
                bool inside = i < curve.Count
                    && IsInsideField(new vec2(curve[i].easting, curve[i].northing), bounds);
                if (inside)
                {
                    if (runStart < 0) runStart = i;
                }
                else if (runStart >= 0)
                {
                    int len = i - runStart;
                    if (len > bestLen) { bestLen = len; lo = runStart; hi = i - 1; }
                    runStart = -1;
                }
            }
            return bestLen >= 2;
        }

        // ── Planner (phase 2): passes per inter-row from the implement settings ──
        // Working mode: 0 = center (symmetric belt coverage), 1 = left, 2 = right
        // (passes start at that side's row). The working belt of an inter-row =
        // rowGap - 2×rowOffset (setting 6); pass count = ceil(belt / toolWidth),
        // onePass forces a single center pass (spraying). Hard limits: the tool edge
        // keeps half of treeWidth from either row line (treeWidth = the whole unworked band
        // around a row, SIM 0730); in L/R mode the pass line keeps safeSpace from the OPPOSITE
        // row. Violations drop the pass + a warning.
        public static List<CTrk> BuildPasses(List<OrchRow> rows, double toolWidth,
            int workMode, double safeSpace, double treeWidth, double rowOffset,
            bool onePass, bool centerFirst, out List<string> warnings)
        {
            warnings = new List<string>();
            var result = new List<CTrk>();
            if (rows == null || rows.Count < 2) return result;
            if (toolWidth < 0.1) { warnings.Add("No tool width"); return result; }

            var sorted = SortRowsTransverse(rows);

            for (int i = 0; i < sorted.Count - 1; i++)
            {
                var a = sorted[i];
                var b = sorted[i + 1];
                if (a.curve.Count < 2 || b.curve.Count < 2) continue;

                List<vec3> mid = BuildMidline(a, b, out double gap);
                if (mid == null) continue;

                string interName = "Mid " + (i + 1);

                // usable half-width: the tool edge must not reach into the tree line
                //the tool edge must stay out of the unworked band: half of it per side
                double halfLimit = gap * 0.5 - treeWidth * 0.5 - toolWidth * 0.5;
                if (halfLimit < 0)
                {
                    warnings.Add(interName + ": inter-row too narrow for the tool (gap "
                        + gap.ToString("N1") + " m)");
                    continue;
                }

                // pass center offsets across the working belt (0 = midline)
                double belt = Math.Max(0.1, gap - 2.0 * rowOffset);
                int n = onePass ? 1 : Math.Max(1, (int)Math.Ceiling(belt / toolWidth - 0.001));
                double spacing = belt / n;

                var offs = new List<double>();
                for (int k = 0; k < n; k++)
                {
                    double off = -belt * 0.5 + spacing * (k + 0.5);
                    if (Math.Abs(off) > halfLimit + 0.001)
                    {
                        warnings.Add(interName + " pass " + (k + 1) + ": dropped (tree space)");
                        continue;
                    }
                    // L/R: the pass line itself keeps safeSpace from the OPPOSITE row
                    if (workMode == 1 && gap * 0.5 - off < safeSpace)
                    { warnings.Add(interName + " pass " + (k + 1) + ": dropped (safe space)"); continue; }
                    if (workMode == 2 && gap * 0.5 + off < safeSpace)
                    { warnings.Add(interName + " pass " + (k + 1) + ": dropped (safe space)"); continue; }
                    offs.Add(off);
                }
                if (offs.Count == 0) continue;

                // processing order within the inter-row
                if (workMode == 1) offs.Sort();                       // from row A outward
                else if (workMode == 2) { offs.Sort(); offs.Reverse(); } // from row B outward
                else if (centerFirst) offs.Sort((u, v) => Math.Abs(u).CompareTo(Math.Abs(v)));
                else offs.Sort((u, v) => Math.Abs(v).CompareTo(Math.Abs(u)));

                // offset sign: does +offset move the line toward row B?
                double bSign = OffsetSignTowardB(mid, b);

                for (int k = 0; k < offs.Count; k++)
                {
                    // AOG guidance always drives strip 0 at +0.5*width RIGHT of the
                    // track reference - shift the reference half a width LEFT so the
                    // driven strip lands exactly on the planned pass line
                    var pass = OffsetCurve(mid, offs[k] * bSign);
                    if (pass.Count < 2) continue;
                    CABCurve.CalculateHeadings(ref pass);
                    SanitizeLine(pass);
                    if (pass.Count < 2) continue;
                    SmoothHeadings(pass, HeadingSmoothWin);

                    pass = OffsetCurve(pass, -0.5 * toolWidth);
                    if (pass.Count < 2) continue;
                    CABCurve.CalculateHeadings(ref pass);
                    SanitizeLine(pass);
                    if (pass.Count < 2) continue;

                    double head = pass[pass.Count / 2].heading;
                    if (head < 0) head += glm.twoPI;

                    ExtendEnds(pass, ExtendMeters);
                    CABCurve.CalculateHeadings(ref pass);

                    var t = new CTrk { mode = TrackMode.Curve, heading = head, isVisible = true };
                    t.isOrchardDerived = true;
                    t.ptA = new vec2(pass[0].easting, pass[0].northing);
                    t.ptB = new vec2(pass[pass.Count - 1].easting, pass[pass.Count - 1].northing);
                    t.name = DerivedPrefix + interName + (offs.Count > 1 ? " p" + (k + 1) : "");
                    foreach (vec3 p in pass) t.curvePts.Add(p);
                    result.Add(t);
                }
            }
            return result;
        }

        // midline between two neighbouring rows + their minimal gap; null when degenerate
        /// <summary>
        /// F50 (2026-08-11) - midline between two free polylines, for the row recorder: it hands
        /// in the OUTER edges of the two painted passes and gets the row centre back. Same
        /// BuildMidline the pass generators use, so the 2026-08-10 kink fixes apply here too.
        /// </summary>
        public static List<vec3> MidlineOfEdges(List<vec2> edgeA, List<vec2> edgeB, out double minGap)
        {
            minGap = double.MaxValue;
            if (edgeA == null || edgeB == null || edgeA.Count < 2 || edgeB.Count < 2) return null;

            var a = new OrchRow { curve = EdgeToCurve(edgeA) };
            var b = new OrchRow { curve = EdgeToCurve(edgeB) };
            if (a.curve.Count < 2 || b.curve.Count < 2) return null;

            return BuildMidline(a, b, out minGap);
        }

        /// <summary>F50 recorder: plain point list -> dense curve with headings (single pass).</summary>
        public static List<vec3> CurveFromPoints(List<vec2> pts) => EdgeToCurve(pts);

        /// <summary>Recorded edge -> dense curve with headings. The points are already in
        /// driving order, so no end-to-end reordering (that would scramble a hairpin).</summary>
        private static List<vec3> EdgeToCurve(List<vec2> pts)
        {
            var des = Densify(pts, 1.0);
            if (des.Count < 2) return des;
            CABCurve.CalculateHeadings(ref des);
            return des;
        }

        private static List<vec3> BuildMidline(OrchRow a, OrchRow b, out double minGap)
        {
            minGap = double.MaxValue;
            var mid = new List<vec3>();

            // Row B may have been recorded in the opposite direction - walk a copy that runs
            // the same way as row A, otherwise a monotone correspondence is impossible.
            var bCurve = SameDirectionCopy(b.curve, a.curve);

            // One midpoint per point of row A, projected onto row B. The projection index may
            // only move FORWARD (test 2026-08-10): a free nearest-point search over the whole
            // row lets the match slide backwards where the rows run near-parallel, and every
            // such backward match became a kink in the finished guidance line.
            int fromIdx = 0;
            bool first = true;
            foreach (vec3 pa in a.curve)
            {
                vec2 nb = NearestOnCurveFrom(bCurve, first ? 0 : fromIdx,
                    pa.easting, pa.northing, out double gap, out int segIdx);
                first = false;
                if (segIdx > fromIdx) fromIdx = segIdx;

                if (gap < minGap) minGap = gap;
                double me = (pa.easting + nb.easting) * 0.5;
                double mn = (pa.northing + nb.northing) * 0.5;
                // dedupe: skip points within 0.5 m of the previous
                if (mid.Count > 0)
                {
                    double de = me - mid[mid.Count - 1].easting;
                    double dn = mn - mid[mid.Count - 1].northing;
                    if (de * de + dn * dn < 0.25) continue;
                }
                mid.Add(new vec3(me, mn, 0));
            }
            if (mid.Count < 2) return null;

            CABCurve.CalculateHeadings(ref mid);
            SanitizeLine(mid);
            if (mid.Count < 2) return null;

            return PrepareForOffset(mid);
        }

        /// <summary>
        /// "other" running the same way as "reference" - reversed copy when the two were
        /// recorded in opposite directions (driving up one row and back down the next is the
        /// normal way to record them).
        /// </summary>
        private static List<vec3> SameDirectionCopy(List<vec3> other, List<vec3> reference)
        {
            if (other == null || other.Count < 2 || reference == null || reference.Count < 2)
                return other;

            double rx = reference[reference.Count - 1].easting - reference[0].easting;
            double ry = reference[reference.Count - 1].northing - reference[0].northing;
            double ox = other[other.Count - 1].easting - other[0].easting;
            double oy = other[other.Count - 1].northing - other[0].northing;

            if (rx * ox + ry * oy >= 0) return other;

            var rev = new List<vec3>(other);
            rev.Reverse();
            return rev;
        }

        // laterally offset a headed polyline (positive = right of the heading)
        private static List<vec3> OffsetCurve(List<vec3> mid, double off)
        {
            var outPts = new List<vec3>(mid.Count);
            foreach (vec3 p in mid)
            {
                outPts.Add(new vec3(
                    p.easting + Math.Sin(p.heading + glm.PIBy2) * off,
                    p.northing + Math.Cos(p.heading + glm.PIBy2) * off,
                    p.heading));
            }
            return outPts;
        }

        // +1 when a positive (right-of-heading) offset moves the midline toward row b, else -1
        private static double OffsetSignTowardB(List<vec3> mid, OrchRow b)
        {
            vec3 m = mid[mid.Count / 2];
            vec2 bc = b.Centroid();
            double rx = Math.Sin(m.heading + glm.PIBy2), ry = Math.Cos(m.heading + glm.PIBy2);
            return ((bc.easting - m.easting) * rx + (bc.northing - m.northing) * ry) >= 0 ? 1 : -1;
        }

        // ── Path from rows + implement (SIM 0729 t3/t5) ───────────────────
        // One entry point for all three modes. Everything it returns is a DERIVED track.
        // bounds (t10 E) let the OUTER strips - between the boundary and the first / last row -
        // get their pass too; without a boundary they are simply skipped.
        public static List<CTrk> BuildTracks(List<OrchRow> rows, int pathMode, double toolWidth,
            double treeWidth, bool centerFirst, int passCount,
            out List<string> warnings, List<List<vec2>> bounds = null,
            Dictionary<string, List<OrchOffsetPoint>> offsets = null)
        {
            if (pathMode == PathOnRow)
                return BuildOnRowTracks(rows, toolWidth, out warnings);

            //two pass and multi pass are the same layout, only the pass count differs
            //(SIM 0730). centerFirst = do the middle before the two row passes.
            if (pathMode == PathTwoPass || pathMode == PathMultiPass)
            {
                var along = BuildRowPassTracks(rows, toolWidth, treeWidth,
                    pathMode == PathMultiPass, centerFirst, passCount, out warnings, offsets);
                //the outer rows get their one strip along the boundary side as well
                AddOuterStripPasses(rows, bounds, toolWidth, treeWidth, true, along, warnings);
                return along;
            }

            //one pass: a single line down the middle of each inter-row. The old belt settings
            //(row offset, safe space, working mode) are gone - they shaped no line any more.
            var passes = BuildPasses(rows, toolWidth, 0, 0, treeWidth, 0,
                true, centerFirst, out warnings);

            AddOuterStripPasses(rows, bounds, toolWidth, treeWidth,
                pathMode == PathOnePass, passes, warnings);

            return passes;
        }

        // ── Outer strips: boundary <-> first / last row (t10 E) ───────────
        // The inter-row loop only works between two rows, so the ground between the boundary and
        // the outermost rows had no pass at all. Decision 2026-07-30: the pass runs PARALLEL to
        // the outer row (constant offset, same clearance rules), and where the machine does not
        // fit there is simply no pass plus a warning - a skewed boundary is worked with the
        // driver's own Smart AB lines, not with a converging line nobody can follow.
        //
        // The first row's strip is put in FRONT of the inter-row passes and the last row's strip
        // behind them, so the sequence still sweeps across the field in one direction.
        private static void AddOuterStripPasses(List<OrchRow> rows, List<List<vec2>> bounds,
            double toolWidth, double treeWidth, bool onePass,
            List<CTrk> passes, List<string> warnings)
        {
            if (rows == null || rows.Count < 2 || bounds == null || bounds.Count == 0) return;
            if (toolWidth < 0.1) return;

            var sorted = SortRowsTransverse(rows);

            for (int side = 0; side < 2; side++)
            {
                OrchRow row = side == 0 ? sorted[0] : sorted[sorted.Count - 1];
                OrchRow inner = side == 0 ? sorted[1] : sorted[sorted.Count - 2];
                if (row.curve.Count < 2 || inner.curve.Count < 2) continue;

                //outward = away from the neighbouring row
                double outward = -OffsetSignTowardB(row.curve, inner);

                double free = OutwardFreeWidth(row.curve, bounds, outward,
                    treeWidth * 0.5 + toolWidth * 3.0 + 1.0);
                string name = "edge " + (side + 1);

                //how many passes fit between the tree line and the boundary
                int n = 0;
                var offs = new List<double>();
                while (true)
                {
                    double d = treeWidth * 0.5 + toolWidth * (n + 0.5);
                    if (d + toolWidth * 0.5 > free) break;
                    offs.Add(d);
                    n++;
                    if (onePass || n > 20) break;
                }

                if (offs.Count == 0)
                {
                    warnings.Add("Orch " + name + ": strip too narrow for the tool ("
                        + free.ToString("N1") + " m)");
                    continue;
                }

                var made = new List<CTrk>();
                for (int k = 0; k < offs.Count; k++)
                {
                    var line = OffsetCurve(row.curve, outward * offs[k]);
                    if (line.Count < 2) continue;
                    CABCurve.CalculateHeadings(ref line);
                    line = OffsetCurve(line, -0.5 * toolWidth);

                    double head = line[line.Count / 2].heading;
                    if (head < 0) head += glm.twoPI;

                    ExtendEnds(line, ExtendMeters);
                    CABCurve.CalculateHeadings(ref line);

                    var t = new CTrk { mode = TrackMode.Curve, heading = head, isVisible = true };
                    t.isOrchardDerived = true;
                    t.ptA = new vec2(line[0].easting, line[0].northing);
                    t.ptB = new vec2(line[line.Count - 1].easting, line[line.Count - 1].northing);
                    t.name = DerivedPrefix + name + (offs.Count > 1 ? " p" + (k + 1) : "");
                    foreach (vec3 p in line) t.curvePts.Add(p);
                    made.Add(t);
                }

                if (side == 0) passes.InsertRange(0, made);
                else passes.AddRange(made);
            }
        }

        // How far the field reaches outward from a row before the boundary, in metres: march out
        // perpendicular from points along the row and keep the WORST (narrowest) place. The ends
        // are left out of the sample - there the nearest boundary is the headland, not the strip.
        private static double OutwardFreeWidth(List<vec3> curve, List<List<vec2>> bounds,
            double outward, double maxLook)
        {
            const double step = 0.25;
            int from = curve.Count / 10, to = curve.Count - curve.Count / 10;
            if (to - from < 2) { from = 0; to = curve.Count; }

            double worst = maxLook;
            for (int i = from; i < to; i += 3)
            {
                double rx = Math.Sin(curve[i].heading + glm.PIBy2) * outward;
                double ry = Math.Cos(curve[i].heading + glm.PIBy2) * outward;

                double d = 0;
                while (d + step <= worst)
                {
                    var p = new vec2(curve[i].easting + rx * (d + step),
                                     curve[i].northing + ry * (d + step));
                    if (!IsInsideField(p, bounds)) break;
                    d += step;
                }
                if (d < worst) worst = d;
                if (worst < step) return 0;
            }
            return worst;
        }

        // ── Passes along the rows: two pass and multi pass are ONE layout ─
        // A side-mounted implement (mulcher, mower, trimmer) goes down each inter-row twice,
        // working along one tree line each time. A centred implement does the same two passes and
        // then fills what is left in the middle. Same geometry, one function (user 2026-07-30:
        // "logika je jednaka pa da iskoristimo kod za obje opcije").
        //
        //   workable width   Wc = gap - treeWidth        (between the unworked bands)
        //   pass count       n  = 2                      (two pass)
        //                    n  = max(2, ceil(Wc / W))   (multi pass)
        //   outer centres    at treeWidth/2 + W/2 from each row - hugging the tree lines
        //   spacing          s  = (gap - treeWidth - W) / (n - 1)
        //   overlap          o  = W - s, THE SAME at every junction
        //
        // The surplus when n passes are wider than Wc is spread evenly over all junctions, and by
        // symmetry an odd n puts one pass exactly down the middle of the inter-row - both of the
        // things the layout was asked for. n = 2 leaves the middle unworked on purpose.
        //
        // Only the two OUTER passes belong to a tree line; that side is recorded on the track
        // (orchardRowSide) so a side implement can be kept to the passes it can actually work.
        // The middle passes have no side and are drivable either way.
        //
        // middleFirst = do the middle before the two row passes instead of after.
        //
        // passCount (SIM 0730, multi pass only): 0 = work the count out from the tool width as
        // above; >= 3 = take exactly that many passes whatever the width and the overlap - the
        // two outer ones still hug the tree lines by the safe tree width, the rest are spread
        // evenly between them. 1 and 2 are refused in the UI (they are the one-pass and two-pass
        // modes), so anything below 3 is treated as 0 here.
        public static List<CTrk> BuildRowPassTracks(List<OrchRow> rows, double toolWidth,
            double treeWidth, bool multiPass, bool middleFirst, int passCount,
            out List<string> warnings, Dictionary<string, List<OrchOffsetPoint>> offsets = null)
        {
            warnings = new List<string>();
            var result = new List<CTrk>();
            if (rows == null || rows.Count < 2) return result;
            if (toolWidth < 0.1) { warnings.Add("No tool width"); return result; }

            var sorted = SortRowsTransverse(rows);

            for (int i = 0; i < sorted.Count - 1; i++)
            {
                var a = sorted[i];
                var b = sorted[i + 1];
                if (a.curve.Count < 2 || b.curve.Count < 2) continue;

                var mid = BuildMidline(a, b, out double gap);
                if (mid == null) continue;

                string interName = "Mid " + (i + 1);

                //the machine has to fit beside a row at all:
                //row spacing >= tool width + safe tree width
                if (gap - treeWidth - toolWidth < -0.001)
                {
                    warnings.Add("Orch " + interName + ": inter-row too narrow for the tool (gap "
                        + gap.ToString("N1") + " m)");
                    continue;
                }

                double outer = treeWidth * 0.5 + toolWidth * 0.5;   // row line -> outer centre

                int n = 2;
                if (multiPass)
                {
                    if (passCount >= 3) n = Math.Min(passCount, 40);
                    else
                    {
                        double workable = gap - treeWidth;
                        n = (int)Math.Ceiling(workable / toolWidth - 0.001);
                        if (n < 2) n = 2;
                        if (n > 40) n = 40;
                    }
                }

                //order: the two row passes first, then the middle - or the other way round
                var order = new List<int>();
                if (middleFirst)
                {
                    for (int k = 1; k < n - 1; k++) order.Add(k);
                    order.Add(0);
                    if (n > 1) order.Add(n - 1);
                }
                else
                {
                    order.Add(0);
                    if (n > 1) order.Add(n - 1);
                    for (int k = 1; k < n - 1; k++) order.Add(k);
                }

                //even step + smoothed headings before anything is pushed sideways: these
                //offsets reach half an inter-row, so heading noise is amplified by it
                var aPrep = PrepareForOffset(a.curve);
                if (aPrep.Count < 2) continue;
                var bPrep = PrepareForOffset(b.curve);
                if (bPrep.Count < 2) continue;

                double signAB = OffsetSignTowardB(aPrep, b);                       // + = toward b, off row a's heading
                double signBA = OffsetSignTowardB(bPrep, new OrchRow { curve = a.curve }); // + = toward a, off row b's heading

                //F50 2026-08-16: each hugging pass is offset from ITS OWN row by "outer" only,
                //not from row a by a distance stretched to the far end of a fixed global span -
                //that made the pass "hugging" row b just a parallel copy of row a's shape, which
                //drifted off row b wherever the two rows were not perfectly parallel (drift up
                //to the local gap - the whole inter-row's minimum gap).
                var hugA = BuildHugPass(aPrep, signAB * outer, toolWidth);
                var hugB = BuildHugPass(bPrep, signBA * outer, toolWidth);
                if (hugA == null || hugB == null) continue;

                int passNo = 0;
                foreach (int k in order)
                {
                    passNo++;

                    //own copy per pass: ExtendEnds below mutates its list in place, and hugA/hugB
                    //are reused by every middle-pass blend, so they must stay untouched
                    List<vec3> line;
                    if (k == 0) line = new List<vec3>(hugA);
                    else if (k == n - 1) line = new List<vec3>(hugB);
                    else
                    {
                        //multi-pass middle tracks: blend between the two hugging lines instead
                        //of a fixed offset from row a, so they follow local geometry too.
                        line = BlendHugPasses(hugA, hugB, (double)k / (n - 1));
                        if (line == null) continue;
                    }

                    string passName = DerivedPrefix + interName + " p" + passNo;

                    //F50 2026-08-16: manual clearance offset only applies to a pass that hugs a
                    //specific row (k = 0 or n-1) - a middle multi-pass track has no tree line of
                    //its own to be "closer to" or "further from". clearanceSign is signAB/signBA,
                    //the same sign the hug pass was originally offset by - "more offset in that
                    //direction" is what moved it away from its own row in the first place, so a
                    //manual "+clearance" delta must be pushed the same way, not assumed positive.
                    double clearanceSign = k == 0 ? signAB : k == n - 1 ? signBA : 0;
                    if (clearanceSign != 0 && offsets != null
                        && offsets.TryGetValue(passName, out List<OrchOffsetPoint> profile))
                        ApplyOffsetProfile(line, profile, clearanceSign);

                    double head = line[line.Count / 2].heading;
                    if (head < 0) head += glm.twoPI;

                    double frontExtension = ExtendEnds(line, ExtendMeters);
                    CABCurve.CalculateHeadings(ref line);

                    var t = new CTrk { mode = TrackMode.Curve, heading = head, isVisible = true };
                    t.isOrchardDerived = true;
                    t.orchardClearanceSign = clearanceSign;
                    t.orchardFrontExtension = frontExtension;

                    //which tree line this pass belongs to, as a side of ITS OWN heading:
                    //k = 0 hugs row a (row is on the -signAB side), k = n-1 hugs row b
                    if (k == 0) t.orchardRowSide = signAB > 0 ? TrackSide.Left : TrackSide.Right;
                    else if (k == n - 1) t.orchardRowSide = signAB > 0 ? TrackSide.Right : TrackSide.Left;
                    else t.orchardRowSide = TrackSide.None;

                    t.ptA = new vec2(line[0].easting, line[0].northing);
                    t.ptB = new vec2(line[line.Count - 1].easting, line[line.Count - 1].northing);
                    t.name = passName;
                    foreach (vec3 p in line) t.curvePts.Add(p);
                    result.Add(t);
                }
            }
            return result;
        }

        /// <summary>
        /// One hugging pass: offset a prepared row curve sideways to the tool-centre distance,
        /// then in again by half the tool width, same pipeline for row a and row b (F50
        /// 2026-08-16). Returns null if any step collapses the line below 2 points.
        /// </summary>
        private static List<vec3> BuildHugPass(List<vec3> basePrep, double offset, double toolWidth)
        {
            var line = OffsetCurve(basePrep, offset);
            if (line.Count < 2) return null;
            CABCurve.CalculateHeadings(ref line);
            SanitizeLine(line);
            if (line.Count < 2) return null;
            SmoothHeadings(line, HeadingSmoothWin);

            line = OffsetCurve(line, -0.5 * toolWidth);
            if (line.Count < 2) return null;
            CABCurve.CalculateHeadings(ref line);
            SanitizeLine(line);
            if (line.Count < 2) return null;
            return line;
        }

        /// <summary>
        /// Middle multi-pass track: blend two hugging lines point-by-point at matching
        /// index-fraction (both are resampled at the same step, so index fraction tracks
        /// arc-length fraction closely enough here), t=0 -> lo, t=1 -> hi.
        /// </summary>
        private static List<vec3> BlendHugPasses(List<vec3> lo, List<vec3> hi, double t)
        {
            int n = Math.Max(lo.Count, hi.Count);
            if (n < 2) return null;

            var outPts = new List<vec3>(n);
            for (int i = 0; i < n; i++)
            {
                double frac = (double)i / (n - 1);
                vec3 pl = SampleByFraction(lo, frac);
                vec3 ph = SampleByFraction(hi, frac);
                outPts.Add(new vec3(
                    pl.easting + (ph.easting - pl.easting) * t,
                    pl.northing + (ph.northing - pl.northing) * t,
                    0));
            }

            CABCurve.CalculateHeadings(ref outPts);
            SanitizeLine(outPts);
            return outPts.Count < 2 ? null : outPts;
        }

        /// <summary>Point at fraction 0..1 along a polyline, linearly interpolated by index.</summary>
        private static vec3 SampleByFraction(List<vec3> pts, double frac)
        {
            double idx = frac * (pts.Count - 1);
            int i0 = (int)Math.Floor(idx);
            if (i0 < 0) i0 = 0;
            if (i0 >= pts.Count - 1) return pts[pts.Count - 1];
            int i1 = i0 + 1;
            double lf = idx - i0;
            return new vec3(
                pts[i0].easting + (pts[i1].easting - pts[i0].easting) * lf,
                pts[i0].northing + (pts[i1].northing - pts[i0].northing) * lf,
                0);
        }

        // ── On row (planting): the line IS the row ────────────────────────
        // Planting a new orchard means driving ALONG the defined rows so the planter drops each
        // seedling exactly where the row says. The planter IS the implement (its working width,
        // and its lateral mounting as AOG's tool offset), so the geometry is the plain AOG one,
        // right of the track heading positive:
        //   pivot path  = reference + 0.5*width - tool.offset   (AOG's convention: the reference
        //                 is the EDGE of pass 0, see CABCurve distAway)
        //   implement   = pivot + tool.offset = reference + 0.5*width
        // The implement therefore lands on reference + half a width whatever the offset is - AOG
        // compensates it, and flips that compensation when the row is driven the other way.
        // So the track is simply the row shifted half a width, and nothing here has to know about
        // the mounting side (SIM 0730: an F50 planter-offset of its own would double-count it and
        // would not survive driving the row in reverse).
        public static List<CTrk> BuildOnRowTracks(List<OrchRow> rows, double toolWidth,
            out List<string> warnings)
        {
            warnings = new List<string>();
            var result = new List<CTrk>();
            if (rows == null || rows.Count == 0) return result;
            if (toolWidth < 0.1) { warnings.Add("No tool width"); return result; }

            double off = -0.5 * toolWidth;   //the planter sits on the row, AOG does the rest

            var sorted = SortRowsTransverse(rows);
            for (int i = 0; i < sorted.Count; i++)
            {
                var r = sorted[i];
                if (r.curve.Count < 2) continue;

                var rPrep = PrepareForOffset(r.curve);
                if (rPrep.Count < 2) continue;

                var line = OffsetCurve(rPrep, off);
                if (line.Count < 2) continue;
                CABCurve.CalculateHeadings(ref line);
                SanitizeLine(line);
                if (line.Count < 2) continue;

                double head = line[line.Count / 2].heading;
                if (head < 0) head += glm.twoPI;

                ExtendEnds(line, ExtendMeters);
                CABCurve.CalculateHeadings(ref line);

                var t = new CTrk { mode = TrackMode.Curve, heading = head, isVisible = true };
                t.isOrchardDerived = true;
                t.ptA = new vec2(line[0].easting, line[0].northing);
                t.ptB = new vec2(line[line.Count - 1].easting, line[line.Count - 1].northing);
                t.name = DerivedPrefix + "row " + (i + 1);
                foreach (vec3 p in line) t.curvePts.Add(p);
                result.Add(t);
            }
            return result;
        }

        // ── Tool-fits check (SIM 0729 t8) ─────────────────────────────────
        // Safety check against driving into a tree row: the machine has to fit into the
        // inter-row once the offsets are taken off it.
        //     width > gap - treeWidth
        // SIM 0730: this is the ONE rule now - the same one every pass is placed by, so the alert
        // can no longer fire over settings that shape no line.
        // Returns how many metres are missing in the WORST inter-row (0 = it fits). Planting
        // (on row) is not checked: the trees are not in the ground yet, and the machine
        // straddles the row instead of passing between two of them.
        public static double ToolTooWideBy(List<OrchRow> rows, double toolWidth,
            double treeWidth, out int worstInterRow)
        {
            worstInterRow = -1;
            double worst = 0;
            if (rows == null || rows.Count < 2 || toolWidth < 0.1) return 0;

            var sorted = SortRowsTransverse(rows);
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                if (sorted[i].curve.Count < 2 || sorted[i + 1].curve.Count < 2) continue;
                if (BuildMidline(sorted[i], sorted[i + 1], out double gap) == null) continue;

                double missing = toolWidth - (gap - treeWidth);
                if (missing > worst) { worst = missing; worstInterRow = i + 1; }
            }
            return worst;
        }

        // ── Planner: "sredina" MVP (kept for reference; BuildPasses supersedes it) ──
        // One center pass per inter-row = BuildPasses with onePass = true.
        public static List<CTrk> BuildMidlines(List<OrchRow> rows, double toolWidth, out int warnCount)
        {
            var tracks = BuildPasses(rows, toolWidth, 0, 0, 0, 0, true, true, out List<string> warns);
            warnCount = warns.Count;
            return tracks;
        }

        // ── Steer limit polygon (2026-08-01) ──────────────────────────────
        // Between the trees the machine must never answer a GPS wobble with a big steering angle,
        // but where it turns it needs full authority. Inside this polygon the commanded angle is
        // capped; outside it, it is not.
        //
        // Three attempts at deriving that outline from the rows all failed, each in its own way,
        // and all for the same reason: the shape depends on how the block is actually worked, and
        // no amount of geometry knows that. A line threaded through per-row points wandered once it
        // was smoothed. Moving the field boundary inward all round dropped the outermost rows out.
        // Cutting the boundary square across the rows collapsed to nothing the moment one short row
        // appeared - silently, taking the cap with it.
        //
        // So this one is DRAWN BY THE DRIVER. It starts as a rectangle with the whole field inside
        // it, which means the cap is in force everywhere until someone decides otherwise, and the
        // planner lets the points be dragged, added and deleted. Being the driver's own data it is
        // saved beside the field and nothing regenerates it behind their back. To switch the cap
        // off entirely the angle parameter goes to 0 - the polygon is not the off switch.
        public const string LimitFile = "SteerLimit.txt";

        /// <summary>A polygon needs four corners before it is worth calling one.</summary>
        public const int LimitMinPoints = 4;

        /// <summary>
        /// The starting polygon: an upright rectangle with the whole field comfortably inside it,
        /// so the cap covers everything until the driver pulls the corners in. Falls back to the
        /// rows when there is no boundary yet. null when there is nothing to measure at all.
        /// </summary>
        public static List<vec2> DefaultLimitPoly(List<vec3> fence, List<OrchRow> rows)
        {
            double minE = double.MaxValue, maxE = double.MinValue;
            double minN = double.MaxValue, maxN = double.MinValue;
            bool any = false;

            if (fence != null)
                foreach (vec3 p in fence)
                {
                    if (p.easting < minE) minE = p.easting;
                    if (p.easting > maxE) maxE = p.easting;
                    if (p.northing < minN) minN = p.northing;
                    if (p.northing > maxN) maxN = p.northing;
                    any = true;
                }

            if (!any && rows != null)
                foreach (OrchRow r in rows)
                    foreach (vec3 p in r.curve)
                    {
                        if (p.easting < minE) minE = p.easting;
                        if (p.easting > maxE) maxE = p.easting;
                        if (p.northing < minN) minN = p.northing;
                        if (p.northing > maxN) maxN = p.northing;
                        any = true;
                    }

            if (!any) return null;

            //a little air around it, so the corners are grabbable outside the field drawing
            const double pad = 5.0;
            minE -= pad; maxE += pad; minN -= pad; maxN += pad;

            return new List<vec2>
            {
                new vec2(minE, minN),
                new vec2(maxE, minN),
                new vec2(maxE, maxN),
                new vec2(minE, maxN),
            };
        }

        public static void SaveLimitPoly(string fieldDir, List<vec2> poly)
        {
            if (string.IsNullOrEmpty(fieldDir)) return;
            string path = Path.Combine(fieldDir, LimitFile);

            try
            {
                if (poly == null || poly.Count < LimitMinPoints)
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }

                using (var w = new StreamWriter(path, false))
                {
                    w.WriteLine("$SteerLimit");
                    w.WriteLine(poly.Count.ToString(CultureInfo.InvariantCulture));
                    foreach (vec2 p in poly)
                        w.WriteLine(p.easting.ToString("N3", CultureInfo.InvariantCulture) + ","
                                  + p.northing.ToString("N3", CultureInfo.InvariantCulture));
                }
            }
            catch (Exception ex) { Log.EventWriter("Orchard SaveLimitPoly: " + ex); }
        }

        /// <summary>The saved polygon, or null when the field has none yet.</summary>
        public static List<vec2> LoadLimitPoly(string fieldDir)
        {
            if (string.IsNullOrEmpty(fieldDir)) return null;
            string path = Path.Combine(fieldDir, LimitFile);
            if (!File.Exists(path)) return null;

            try
            {
                string[] L = File.ReadAllLines(path);
                int i = 0;
                if (i < L.Length && L[i].StartsWith("$SteerLimit")) i++;
                if (i >= L.Length) return null;
                if (!int.TryParse(L[i++], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return null;

                var poly = new List<vec2>(n);
                for (int k = 0; k < n && i < L.Length; k++, i++)
                {
                    string[] w = L[i].Split(',');
                    if (w.Length < 2) continue;
                    if (double.TryParse(w[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double e)
                        && double.TryParse(w[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double nn))
                        poly.Add(new vec2(e, nn));
                }
                return poly.Count >= LimitMinPoints ? poly : null;
            }
            catch (Exception ex) { Log.EventWriter("Orchard LoadLimitPoly: " + ex); return null; }
        }

        // Sort rows across the field: project each row centroid onto the axis perpendicular to the
        // orchard's main direction. Consecutive rows in this order are neighbours.
        public static List<OrchRow> SortRowsTransverse(List<OrchRow> rows)
        {
            // main direction = sum of row directions, each aligned to the first row's direction
            vec2 refDir = rows[0].Dir();
            double sx = 0, sy = 0;
            foreach (var r in rows)
            {
                vec2 d = r.Dir();
                if (d.easting * refDir.easting + d.northing * refDir.northing < 0) { d.easting = -d.easting; d.northing = -d.northing; }
                sx += d.easting; sy += d.northing;
            }
            double len = Math.Sqrt(sx * sx + sy * sy);
            if (len < 0.001) { sx = 0; sy = 1; len = 1; }
            sx /= len; sy /= len;
            // perpendicular axis (rotate main dir by 90°)
            double px = -sy, py = sx;

            var keyed = new List<KeyValuePair<double, OrchRow>>();
            foreach (var r in rows)
            {
                vec2 c = r.Centroid();
                double s = c.easting * px + c.northing * py;
                keyed.Add(new KeyValuePair<double, OrchRow>(s, r));
            }
            keyed.Sort((u, v) => u.Key.CompareTo(v.Key));

            var outList = new List<OrchRow>(keyed.Count);
            foreach (var kv in keyed) outList.Add(kv.Value);
            return outList;
        }

        private static vec2 NearestOnCurve(List<vec3> curve, double e, double n, out double dist)
        {
            return NearestOnCurveFrom(curve, 0, e, n, out dist, out _);
        }

        /// <summary>
        /// Nearest point on the curve as a POLYLINE - every segment is projected onto and the
        /// result clamped to it - not the nearest vertex (test 2026-08-10). Snapping to vertices
        /// made the midline climb a staircase: while the source point advanced the chosen vertex
        /// stayed put and then jumped a whole vertex spacing, and on near-parallel rows the
        /// distance minimum is so flat that the pick could even move BACKWARDS. Both landed in
        /// the guidance line as kinks and reversals.
        /// Searching starts at fromIdx so a caller can keep the correspondence monotone.
        /// </summary>
        private static vec2 NearestOnCurveFrom(List<vec3> curve, int fromIdx,
            double e, double n, out double dist, out int segIdx)
        {
            double best = double.MaxValue;
            vec2 res = new vec2(e, n);
            segIdx = fromIdx;
            if (curve == null || curve.Count == 0) { dist = 0; return res; }
            if (curve.Count == 1)
            {
                res = new vec2(curve[0].easting, curve[0].northing);
                dist = glm.Distance(res.easting, res.northing, e, n);
                return res;
            }

            if (fromIdx < 0) fromIdx = 0;
            if (fromIdx > curve.Count - 2) fromIdx = curve.Count - 2;

            for (int i = fromIdx; i < curve.Count - 1; i++)
            {
                double ax = curve[i].easting, ay = curve[i].northing;
                double bx = curve[i + 1].easting, by = curve[i + 1].northing;
                double vx = bx - ax, vy = by - ay;
                double len2 = vx * vx + vy * vy;

                double px, py;
                if (len2 < 1e-9) { px = ax; py = ay; }
                else
                {
                    double t = ((e - ax) * vx + (n - ay) * vy) / len2;
                    if (t < 0) t = 0; else if (t > 1) t = 1;
                    px = ax + vx * t; py = ay + vy * t;
                }

                double de = px - e, dn = py - n;
                double d = de * de + dn * dn;
                if (d < best) { best = d; res = new vec2(px, py); segIdx = i; }
            }

            dist = Math.Sqrt(best);
            return res;
        }

        // ── Line conditioning (test 2026-08-10) ───────────────────────────
        // A pass is the source line pushed sideways by "off", so the lateral error of the
        // result is roughly off * (heading error). In a wide inter-row off reaches tens of
        // metres and a couple of degrees of heading noise already throws the line a metre
        // sideways. Resampling to an even step and smoothing the headings kills that at the
        // source; SanitizeLine is the net that catches whatever still gets through, because
        // a guidance line must never step back on itself.
        private const double LineStep = 1.0;      // resample step (m)
        private const int HeadingSmoothWin = 2;   // +/- points averaged (≈5 m at LineStep)

        /// <summary>Resample a polyline to an even step, keeping both ends.</summary>
        private static List<vec3> Resample(List<vec3> src, double step)
        {
            var outPts = new List<vec3>();
            if (src == null || src.Count < 2 || step < 0.05) return outPts;

            outPts.Add(new vec3(src[0].easting, src[0].northing, 0));
            double carry = 0;

            for (int i = 1; i < src.Count; i++)
            {
                double ax = src[i - 1].easting, ay = src[i - 1].northing;
                double bx = src[i].easting, by = src[i].northing;
                double segLen = glm.Distance(ax, ay, bx, by);
                if (segLen < 1e-6) continue;

                double pos = step - carry;
                while (pos <= segLen)
                {
                    double t = pos / segLen;
                    outPts.Add(new vec3(ax + (bx - ax) * t, ay + (by - ay) * t, 0));
                    pos += step;
                }
                carry = segLen - (pos - step);
            }

            vec3 last = src[src.Count - 1];
            if (outPts.Count > 0)
            {
                double d = glm.Distance(outPts[outPts.Count - 1].easting,
                    outPts[outPts.Count - 1].northing, last.easting, last.northing);
                if (d > step * 0.25) outPts.Add(new vec3(last.easting, last.northing, 0));
            }
            return outPts;
        }

        /// <summary>Moving average of the heading, done on the unit vectors so the 0/2pi wrap
        /// cannot fold a heading onto its opposite.</summary>
        private static void SmoothHeadings(List<vec3> pts, int win)
        {
            int cnt = pts.Count;
            if (cnt < 3 || win < 1) return;

            var arr = new vec3[cnt];
            pts.CopyTo(arr);

            for (int i = 0; i < cnt; i++)
            {
                double sx = 0, sy = 0;
                int lo = Math.Max(0, i - win), hi = Math.Min(cnt - 1, i + win);
                for (int k = lo; k <= hi; k++)
                {
                    sx += Math.Sin(arr[k].heading);
                    sy += Math.Cos(arr[k].heading);
                }
                if (Math.Abs(sx) < 1e-9 && Math.Abs(sy) < 1e-9) continue;

                double h = Math.Atan2(sx, sy);
                if (h < 0) h += glm.twoPI;

                vec3 p = pts[i];
                p.heading = h;
                pts[i] = p;
            }
        }

        /// <summary>Even step + smoothed headings: what every line is offset FROM.</summary>
        private static List<vec3> PrepareForOffset(List<vec3> src)
        {
            var line = Resample(src, LineStep);
            if (line.Count < 2) return new List<vec3>(src);
            CABCurve.CalculateHeadings(ref line);
            SmoothHeadings(line, HeadingSmoothWin);
            return line;
        }

        /// <summary>
        /// Drop every point that does not carry the line FORWARD. Offsetting a polyline by more
        /// than its radius of curvature folds it into a loop, and a single reversed point flips
        /// the heading by ~180 degrees and throws the offset point to the other side - which is
        /// exactly the jump seen in the field. Keeping only monotone progress removes both.
        /// </summary>
        private static void SanitizeLine(List<vec3> pts)
        {
            if (pts.Count < 3) return;

            var kept = new List<vec3>(pts.Count) { pts[0] };
            for (int i = 1; i < pts.Count; i++)
            {
                vec3 prev = kept[kept.Count - 1];
                double de = pts[i].easting - prev.easting;
                double dn = pts[i].northing - prev.northing;
                if (de * de + dn * dn < 0.0025) continue;     // < 5 cm apart

                //forward = along the heading the line already had at the kept point
                if (de * Math.Sin(prev.heading) + dn * Math.Cos(prev.heading) <= 0) continue;

                kept.Add(pts[i]);
            }

            if (kept.Count < 2) return;
            pts.Clear();
            pts.AddRange(kept);
            CABCurve.CalculateHeadings(ref pts);
        }

        /// <summary>
        /// First free "Row N" name (SIM 0730). Deliberately NOT translated: row names are written
        /// to Orchard.txt and the recorded-rows archive is matched by them, so switching the app
        /// language must not break that match.
        /// </summary>
        public static string NextRowName(List<OrchRow> rows)
        {
            int n = 0;
            if (rows != null)
            {
                foreach (var r in rows)
                {
                    if (r.name == null || !r.name.StartsWith("Row ")) continue;
                    if (int.TryParse(r.name.Substring(4).Trim(), out int k) && k > n) n = k;
                }
            }
            return "Row " + (n + 1);
        }

        /// <summary>
        /// Pull every tree of a row onto its line, moving them LATERALLY only (SIM 0730): a
        /// recorded tree cannot be placed sideways with any accuracy, and a tree belongs on the
        /// row - so filtering the line takes the trees with it, keeping their position along the
        /// row and zeroing the cross-track offset.
        /// </summary>
        public static void ProjectTreesOntoRow(OrchRow row)
        {
            if (row == null || row.curve.Count < 2 || row.trees.Count == 0) return;

            for (int t = 0; t < row.trees.Count; t++)
            {
                vec2 p = row.trees[t];
                vec2 best = new vec2(row.curve[0].easting, row.curve[0].northing);
                double bestD = double.MaxValue;

                for (int i = 0; i < row.curve.Count - 1; i++)
                {
                    double ax = row.curve[i].easting, ay = row.curve[i].northing;
                    double bx = row.curve[i + 1].easting, by = row.curve[i + 1].northing;
                    double dx = bx - ax, dy = by - ay;
                    double len2 = dx * dx + dy * dy;
                    if (len2 < 1e-9) continue;

                    //where the tree falls on this segment, clamped to it
                    double u = ((p.easting - ax) * dx + (p.northing - ay) * dy) / len2;
                    if (u < 0) u = 0; else if (u > 1) u = 1;

                    double qx = ax + dx * u, qy = ay + dy * u;
                    double de = p.easting - qx, dn = p.northing - qy;
                    double d = de * de + dn * dn;
                    if (d < bestD) { bestD = d; best = new vec2(qx, qy); }
                }

                row.trees[t] = best;
            }
        }

        // ── Row filters: thin + smooth (t9) ───────────────────────────────
        // A row driven with the recorder is sampled every metre or so, GPS noise and all, so it
        // arrives with coarse steps. Both filters work on the CONTROL points, never on the dense
        // curve: Orchard.txt stores only the control points and rebuilds the curve on load, so a
        // filter applied to the curve would quietly vanish the next time the field is opened.

        /// <summary>
        /// Drop points closer together than minDist. The first and the LAST point always stay -
        /// the ends of a row are its anchors (headland, row length). This is what actually removes
        /// the coarse steps of a 1 m recording; the average alone only rounds them off.
        /// </summary>
        public static List<vec2> ThinPoints(List<vec2> pts, double minDist)
        {
            var outPts = new List<vec2>();
            if (pts == null || pts.Count == 0) return outPts;
            if (minDist <= 0.01 || pts.Count < 3) { outPts.AddRange(pts); return outPts; }

            double sq = minDist * minDist;
            outPts.Add(pts[0]);
            for (int i = 1; i < pts.Count - 1; i++)
            {
                var last = outPts[outPts.Count - 1];
                double de = pts[i].easting - last.easting, dn = pts[i].northing - last.northing;
                if (de * de + dn * dn >= sq) outPts.Add(pts[i]);
            }
            outPts.Add(pts[pts.Count - 1]);
            return outPts;
        }

        /// <summary>
        /// Moving average over the row's control points. ONLY the first and the last point are
        /// anchored (call 2026-08-11) - every point in between moves, with a window that narrows
        /// towards the ends, so the ends cannot creep and nothing sits untouched behind them.
        ///
        /// The old version kept the whole first and last half-window in place, and skipped the
        /// filtering altogether when the window was wider than the row had points - which is why
        /// SMALLER values appeared to smooth more: the big ones quietly did nothing. The window is
        /// now clamped to what the row can carry, so the largest value is the strongest smooth.
        ///
        /// Smart AB has its own smoothing over the dense curve and is deliberately left alone.
        /// </summary>
        public static List<vec2> SmoothPoints(List<vec2> pts, int window)
        {
            var outPts = new List<vec2>();
            if (pts == null) return outPts;
            outPts.AddRange(pts);
            if (window < 2 || pts.Count < 3) return outPts;

            int half = window / 2;
            int maxHalf = (pts.Count - 1) / 2;
            if (half > maxHalf) half = maxHalf;
            if (half < 1) return outPts;

            int last = pts.Count - 1;
            for (int i = 1; i < last; i++)
            {
                // how many neighbours this point actually has on BOTH sides
                int w = Math.Min(half, Math.Min(i, last - i));
                if (w < 1) continue;

                double e = 0, n = 0;
                for (int j = -w; j <= w; j++) { e += pts[i + j].easting; n += pts[i + j].northing; }
                int cnt = 2 * w + 1;
                outPts[i] = new vec2(e / cnt, n / cnt);
            }
            return outPts;
        }

        /// <summary>Thin then smooth, as ONE step - the order the values are read in.</summary>
        public static List<vec2> FilterRowPoints(List<vec2> pts, double thinMeters, int smoothWindow)
        {
            var res = ThinPoints(pts, thinMeters);
            if (smoothWindow >= 2) res = SmoothPoints(res, smoothWindow);
            return res;
        }

        // ── Raw recorded rows: Orchard_recorded.txt (t9) ──────────────────
        // Written lazily, the first time a recorded row is filtered, and never overwritten for a
        // row that is already in it - otherwise the second filtering would archive the already
        // smoothed line and the measurement would be gone. Rows are matched BY NAME.
        public static void ArchiveRecordedRow(string fieldDir, OrchRow row)
        {
            if (string.IsNullOrEmpty(fieldDir) || row == null) return;
            if (row.source != RowSource.Recorded) return;   // KML has its own original

            try
            {
                var have = LoadRowsFrom(fieldDir, RecordedFile);
                foreach (var r in have) if (r.name == row.name) return;   // already measured

                have.Add(new OrchRow(row));
                SaveRowsTo(fieldDir, RecordedFile, have);
            }
            catch (Exception ex)
            {
                Log.EventWriter("Orchard ArchiveRecordedRow: " + ex);
            }
        }

        // ── Shared geometry helpers (mirror Smart AB) ─────────────────────
        // Order points into a path: farthest-apart pair are the ends; nearest-neighbour through the rest.
        public static List<vec2> OrderEndToEnd(List<vec2> pts)
        {
            if (pts.Count <= 2) return new List<vec2>(pts);
            int aI = 0, bI = 1; double best = -1;
            for (int i = 0; i < pts.Count; i++)
                for (int j = i + 1; j < pts.Count; j++)
                {
                    double dx = pts[i].easting - pts[j].easting, dy = pts[i].northing - pts[j].northing;
                    double d = dx * dx + dy * dy;
                    if (d > best) { best = d; aI = i; bI = j; }
                }

            var remaining = new List<int>();
            for (int i = 0; i < pts.Count; i++) if (i != aI && i != bI) remaining.Add(i);

            var ordered = new List<vec2> { pts[aI] };
            int cur = aI;
            while (remaining.Count > 0)
            {
                int bk = 0; double bd = double.MaxValue;
                for (int k = 0; k < remaining.Count; k++)
                {
                    double dx = pts[remaining[k]].easting - pts[cur].easting;
                    double dy = pts[remaining[k]].northing - pts[cur].northing;
                    double d = dx * dx + dy * dy;
                    if (d < bd) { bd = d; bk = k; }
                }
                cur = remaining[bk];
                ordered.Add(pts[cur]);
                remaining.RemoveAt(bk);
            }
            ordered.Add(pts[bI]);
            return ordered;
        }

        public static List<vec3> Densify(List<vec2> p, double spacing)
        {
            var list = new List<vec3>();
            if (p.Count == 0) return list;
            for (int i = 0; i < p.Count - 1; i++)
            {
                double dx = p[i + 1].easting - p[i].easting, dy = p[i + 1].northing - p[i].northing;
                double len = Math.Sqrt(dx * dx + dy * dy);
                int steps = Math.Max(1, (int)(len / spacing));
                for (int s = 0; s < steps; s++)
                {
                    double f = (double)s / steps;
                    list.Add(new vec3(p[i].easting + dx * f, p[i].northing + dy * f, 0));
                }
            }
            list.Add(new vec3(p[p.Count - 1].easting, p[p.Count - 1].northing, 0));
            return list;
        }

        /// <summary>Prepends/appends a straight guidance tail at each end. Returns the actual
        /// length added at the front (n*step - "meters" rounds down to whole steps), so callers
        /// that measure along-track distance from the finished curvePts can subtract it back out
        /// to get distance from the real (pre-extension) start of the line.</summary>
        public static double ExtendEnds(List<vec3> des, double meters)
        {
            if (des.Count < 2) return 0;
            const double step = 1.6;
            int n = Math.Max(1, (int)(meters / step));

            double h0 = des[0].heading;
            var front = new List<vec3>();
            for (int i = n; i >= 1; i--)
            {
                double d = i * step;
                front.Add(new vec3(des[0].easting - Math.Sin(h0) * d, des[0].northing - Math.Cos(h0) * d, h0));
            }

            int last = des.Count - 1;
            double hl = des[last].heading;
            var back = new List<vec3>();
            for (int i = 1; i <= n; i++)
            {
                double d = i * step;
                back.Add(new vec3(des[last].easting + Math.Sin(hl) * d, des[last].northing + Math.Cos(hl) * d, hl));
            }

            des.InsertRange(0, front);
            des.AddRange(back);
            return n * step;
        }

        // ── KML import (reuse the FormFieldKML coordinate pipeline) ───────
        // LineString → one Row (control points). Point → a tree position (unassigned).
        // Returns rows via out param; trees appended to the given list. Uses the field's local plane.
        public static List<OrchRow> ImportKml(string path, LocalPlane plane, List<vec2> treesOut, out string err)
        {
            err = null;
            var rows = new List<OrchRow>();
            try
            {
                string[] lines = File.ReadAllLines(path);
                bool inLine = false, inPoint = false;
                string coordBuf = null;

                foreach (string raw in lines)
                {
                    string line = raw;
                    if (line.IndexOf("<LineString", StringComparison.OrdinalIgnoreCase) >= 0) { inLine = true; coordBuf = null; }
                    else if (line.IndexOf("<Point", StringComparison.OrdinalIgnoreCase) >= 0) { inPoint = true; coordBuf = null; }

                    int cs = line.IndexOf("<coordinates>", StringComparison.OrdinalIgnoreCase);
                    if (cs >= 0)
                    {
                        int ce = line.IndexOf("</coordinates>", StringComparison.OrdinalIgnoreCase);
                        if (ce >= 0) coordBuf = line.Substring(cs + 13, ce - (cs + 13));
                        else coordBuf = line.Substring(cs + 13); // multi-line coordinates
                    }
                    else if (coordBuf != null && line.IndexOf("</coordinates>", StringComparison.OrdinalIgnoreCase) < 0
                             && (inLine || inPoint) && line.Trim().Length > 0
                             && line.IndexOf('<') < 0)
                    {
                        coordBuf += " " + line.Trim(); // accumulate wrapped coordinate lines
                    }

                    int cEnd = line.IndexOf("</coordinates>", StringComparison.OrdinalIgnoreCase);
                    if (cEnd >= 0 && coordBuf != null)
                    {
                        if (line.IndexOf("<coordinates>", StringComparison.OrdinalIgnoreCase) < 0)
                            coordBuf += " " + line.Substring(0, cEnd);

                        var pts = ParseCoords(coordBuf, plane);
                        if (inPoint) { foreach (var p in pts) treesOut.Add(p); }
                        else if (inLine && pts.Count >= 2)
                        {
                            var r = new OrchRow { source = RowSource.KmlLine, name = "KML row " + (rows.Count + 1) };
                            r.pts = pts;
                            r.BuildCurve();
                            rows.Add(r);
                        }
                        coordBuf = null; inLine = false; inPoint = false;
                    }
                }
                if (rows.Count == 0 && treesOut.Count == 0) err = "No LineString/Point found in KML";
            }
            catch (Exception ex)
            {
                err = ex.Message;
                Log.EventWriter("Orchard KML import: " + ex);
            }
            return rows;
        }

        private static List<vec2> ParseCoords(string blob, LocalPlane plane)
        {
            var pts = new List<vec2>();
            string[] tokens = blob.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string tok in tokens)
            {
                string[] f = tok.Split(',');
                if (f.Length < 2) continue;
                if (!double.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon)) continue;
                if (!double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat)) continue;
                GeoCoord g = plane.ConvertWgs84ToGeoCoord(new Wgs84(lat, lon));
                pts.Add(new vec2(g.Easting, g.Northing));
            }
            return pts;
        }

        // ── Per-field persistence (rows saved "under field", fixed) ───────
        // Simple text format: one row block per row, control points only (curve is derived on load).
        /// <summary>The working rows of the field.</summary>
        public const string RowsFile = "Orchard.txt";

        /// <summary>
        /// The raw recorded rows, kept beside the field the first time a recorded row is filtered
        /// (t9). Orchard.txt holds the rows that are worked with; this one is the measurement, so
        /// a filter can always be judged against - or undone back to - what was actually driven.
        /// </summary>
        public const string RecordedFile = "Orchard_recorded.txt";

        public static void SaveRows(string fieldDir, List<OrchRow> rows, List<vec2> looseTrees = null)
            => SaveRowsTo(fieldDir, RowsFile, rows, looseTrees);

        public static void SaveRowsTo(string fieldDir, string fileName, List<OrchRow> rows, List<vec2> looseTrees = null)
        {
            if (string.IsNullOrEmpty(fieldDir)) return;
            string path = Path.Combine(fieldDir, fileName);
            using (var w = new StreamWriter(path, false))
            {
                w.WriteLine("$Orchard");
                w.WriteLine(rows.Count.ToString(CultureInfo.InvariantCulture));
                foreach (var r in rows)
                {
                    w.WriteLine("row," + (int)r.source + "," + (r.name ?? "Row").Replace(',', ' '));
                    w.WriteLine(r.pts.Count.ToString(CultureInfo.InvariantCulture));
                    foreach (var p in r.pts)
                        w.WriteLine(p.easting.ToString(CultureInfo.InvariantCulture) + "," + p.northing.ToString(CultureInfo.InvariantCulture));
                    w.WriteLine(r.trees.Count.ToString(CultureInfo.InvariantCulture));
                    foreach (var p in r.trees)
                        w.WriteLine(p.easting.ToString(CultureInfo.InvariantCulture) + "," + p.northing.ToString(CultureInfo.InvariantCulture));
                }

                // trees not assigned to any row (KML points) - optional tail section,
                // old files without it still load
                if (looseTrees != null && looseTrees.Count > 0)
                {
                    w.WriteLine("$LooseTrees");
                    w.WriteLine(looseTrees.Count.ToString(CultureInfo.InvariantCulture));
                    foreach (var p in looseTrees)
                        w.WriteLine(p.easting.ToString(CultureInfo.InvariantCulture) + "," + p.northing.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        public static List<OrchRow> LoadRows(string fieldDir)
        {
            return LoadRows(fieldDir, null);
        }

        public static List<OrchRow> LoadRows(string fieldDir, List<vec2> looseTreesOut)
            => LoadRowsFrom(fieldDir, RowsFile, looseTreesOut);

        public static List<OrchRow> LoadRowsFrom(string fieldDir, string fileName, List<vec2> looseTreesOut = null)
        {
            var rows = new List<OrchRow>();
            if (string.IsNullOrEmpty(fieldDir)) return rows;
            string path = Path.Combine(fieldDir, fileName);
            if (!File.Exists(path)) return rows;
            try
            {
                string[] L = File.ReadAllLines(path);
                int i = 0;
                if (i < L.Length && L[i].StartsWith("$Orchard")) i++;
                if (i >= L.Length) return rows;
                int.TryParse(L[i++], out int rowCount);
                for (int r = 0; r < rowCount && i < L.Length; r++)
                {
                    string[] hdr = L[i++].Split(',');
                    var row = new OrchRow();
                    if (hdr.Length >= 2 && int.TryParse(hdr[1], out int src)) row.source = (RowSource)src;
                    if (hdr.Length >= 3) row.name = hdr[2];
                    if (i >= L.Length) break;
                    int.TryParse(L[i++], out int nP);
                    for (int k = 0; k < nP && i < L.Length; k++, i++)
                    {
                        string[] c = L[i].Split(',');
                        if (c.Length >= 2 &&
                            double.TryParse(c[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double e) &&
                            double.TryParse(c[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
                            row.pts.Add(new vec2(e, n));
                    }
                    if (i < L.Length && int.TryParse(L[i], out int nT))
                    {
                        i++;
                        for (int k = 0; k < nT && i < L.Length; k++, i++)
                        {
                            string[] c = L[i].Split(',');
                            if (c.Length >= 2 &&
                                double.TryParse(c[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double e) &&
                                double.TryParse(c[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
                                row.trees.Add(new vec2(e, n));
                        }
                    }
                    row.BuildCurve();
                    rows.Add(row);
                }

                // optional tail: trees not assigned to any row
                if (looseTreesOut != null)
                {
                    while (i < L.Length && !L[i].StartsWith("$LooseTrees")) i++;
                    if (i < L.Length && ++i < L.Length && int.TryParse(L[i++], out int nLoose))
                    {
                        for (int k = 0; k < nLoose && i < L.Length; k++, i++)
                        {
                            string[] c = L[i].Split(',');
                            if (c.Length >= 2 &&
                                double.TryParse(c[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double e) &&
                                double.TryParse(c[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
                                looseTreesOut.Add(new vec2(e, n));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("Orchard LoadRows: " + ex);
            }
            return rows;
        }

        // ── Manual row offset profile (F50 2026-08-16) ─────────────────────
        // A dedicated file, not a tail section of Orchard.txt: independent of row geometry,
        // written on every HUD button press (small file, cheap), and keeps SaveRows/LoadRows
        // signatures - already called from several forms - untouched.
        /// <summary>Manual offset profile, kept beside the field.</summary>
        public const string OffsetsFile = "RowOffsets.txt";

        public static void SaveOffsets(string fieldDir, Dictionary<string, List<OrchOffsetPoint>> offsets)
        {
            if (string.IsNullOrEmpty(fieldDir) || offsets == null) return;
            string path = Path.Combine(fieldDir, OffsetsFile);
            using (var w = new StreamWriter(path, false))
            {
                w.WriteLine("$OrchOffsets");
                var keys = new List<string>(offsets.Keys);
                w.WriteLine(keys.Count.ToString(CultureInfo.InvariantCulture));
                foreach (string name in keys)
                {
                    var pts = offsets[name];
                    w.WriteLine(name);
                    w.WriteLine(pts.Count.ToString(CultureInfo.InvariantCulture));
                    foreach (var p in pts)
                        w.WriteLine(p.dist.ToString(CultureInfo.InvariantCulture) + "," + p.offset.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        public static Dictionary<string, List<OrchOffsetPoint>> LoadOffsets(string fieldDir)
        {
            var result = new Dictionary<string, List<OrchOffsetPoint>>();
            if (string.IsNullOrEmpty(fieldDir)) return result;
            string path = Path.Combine(fieldDir, OffsetsFile);
            if (!File.Exists(path)) return result;
            try
            {
                string[] L = File.ReadAllLines(path);
                int i = 0;
                if (i < L.Length && L[i].StartsWith("$OrchOffsets")) i++;
                if (i >= L.Length) return result;
                int.TryParse(L[i++], out int trackCount);
                for (int t = 0; t < trackCount && i < L.Length; t++)
                {
                    string name = L[i++];
                    if (i >= L.Length) break;
                    int.TryParse(L[i++], out int nP);
                    var pts = new List<OrchOffsetPoint>(nP);
                    for (int k = 0; k < nP && i < L.Length; k++, i++)
                    {
                        string[] c = L[i].Split(',');
                        if (c.Length >= 2
                            && double.TryParse(c[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                            && double.TryParse(c[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double off))
                            pts.Add(new OrchOffsetPoint(d, off));
                    }
                    result[name] = pts;
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("Orchard LoadOffsets: " + ex);
            }
            return result;
        }

        /// <summary>
        /// One manual breakpoint in a pass's offset profile: from "dist" (along-track metres from
        /// the start of that pass) onward, "offset" extra clearance applies - + = further from the
        /// row this pass hugs (away from the tree line), - = closer. Ramped in over the 2 m before
        /// each breakpoint (ApplyOffsetProfile) so the transition is not a jerk in the wheel.
        /// </summary>
        public struct OrchOffsetPoint
        {
            public double dist;
            public double offset;
            public OrchOffsetPoint(double dist, double offset) { this.dist = dist; this.offset = offset; }
        }

        /// <summary>How far into a breakpoint's approach the offset ramps in, in metres.</summary>
        public const double OffsetRampMeters = 2.0;

        /// <summary>
        /// Apply a manual offset profile to an already-built pass line, in place. Points are
        /// pushed sideways along their own heading's normal by the effective offset at their
        /// along-track distance from the start of the line - constant between breakpoints, ramped
        /// linearly over the OffsetRampMeters before each one (and into the first one, off 0).
        /// clearanceSign is signAB/signBA from BuildRowPassTracks: "+clearance" in the profile
        /// always means "away from the row this pass hugs", but which literal direction that is
        /// (Math.Sin(heading+PIBy2) positive or negative) depends on which side the pass was
        /// built on, same as the original hug offset.
        /// </summary>
        private static void ApplyOffsetProfile(List<vec3> line, List<OrchOffsetPoint> pts, double clearanceSign)
        {
            if (pts == null || pts.Count == 0 || line.Count < 2) return;

            var sorted = new List<OrchOffsetPoint>(pts);
            sorted.Sort((a, b) => a.dist.CompareTo(b.dist));

            double along = 0;
            for (int i = 0; i < line.Count; i++)
            {
                if (i > 0)
                {
                    double de = line[i].easting - line[i - 1].easting;
                    double dn = line[i].northing - line[i - 1].northing;
                    along += Math.Sqrt(de * de + dn * dn);
                }

                double off = EffectiveOffsetAt(sorted, along) * clearanceSign;
                if (Math.Abs(off) < 1e-6) continue;

                double h = line[i].heading;
                line[i] = new vec3(
                    line[i].easting + Math.Sin(h + glm.PIBy2) * off,
                    line[i].northing + Math.Cos(h + glm.PIBy2) * off,
                    h);
            }
        }

        /// <summary>Current clearance value of a profile at a given along-track distance (same
        /// ramped piecewise rule ApplyOffsetProfile uses) - for the HUD to read "what is it now"
        /// before adding a step.</summary>
        public static double CurrentOffset(List<OrchOffsetPoint> profile, double alongTrackDist)
        {
            if (profile == null || profile.Count == 0) return 0;
            var sorted = new List<OrchOffsetPoint>(profile);
            sorted.Sort((a, b) => a.dist.CompareTo(b.dist));
            return EffectiveOffsetAt(sorted, alongTrackDist);
        }

        private static double EffectiveOffsetAt(List<OrchOffsetPoint> sorted, double d)
        {
            double prevValue = 0;
            for (int k = 0; k < sorted.Count; k++)
            {
                double rampStart = sorted[k].dist - OffsetRampMeters;
                if (d < rampStart) return prevValue;
                if (d < sorted[k].dist)
                {
                    double t = OffsetRampMeters < 1e-6 ? 1.0 : (d - rampStart) / OffsetRampMeters;
                    return prevValue + (sorted[k].offset - prevValue) * t;
                }
                prevValue = sorted[k].offset;
            }
            return prevValue;
        }
    }
}
