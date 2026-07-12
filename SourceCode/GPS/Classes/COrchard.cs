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

    public enum RowSource { Manual = 0, KmlLine = 1, KmlPoints = 2, Lidar = 3 }

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

        // ── Planner (phase 2): passes per inter-row from the implement settings ──
        // Working mode: 0 = center (symmetric belt coverage), 1 = left, 2 = right
        // (passes start at that side's row). The working belt of an inter-row =
        // rowGap - 2×rowOffset (setting 6); pass count = ceil(belt / toolWidth),
        // onePass forces a single center pass (spraying). Hard limits: the tool edge
        // keeps treeSpace from either row line; in L/R mode the pass line keeps
        // safeSpace from the OPPOSITE row. Violations drop the pass + a warning.
        public static List<CTrk> BuildPasses(List<OrchRow> rows, double toolWidth,
            int workMode, double safeSpace, double treeSpace, double rowOffset,
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
                double halfLimit = gap * 0.5 - treeSpace - toolWidth * 0.5;
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
                    var pass = OffsetCurve(mid, offs[k] * bSign);
                    if (pass.Count < 2) continue;

                    double head = pass[pass.Count / 2].heading;
                    if (head < 0) head += glm.twoPI;

                    ExtendEnds(pass, ExtendMeters);
                    CABCurve.CalculateHeadings(ref pass);

                    var t = new CTrk { mode = TrackMode.Curve, heading = head, isVisible = true };
                    t.ptA = new vec2(pass[0].easting, pass[0].northing);
                    t.ptB = new vec2(pass[pass.Count - 1].easting, pass[pass.Count - 1].northing);
                    t.name = "Row " + interName + (offs.Count > 1 ? " p" + (k + 1) : "");
                    foreach (vec3 p in pass) t.curvePts.Add(p);
                    result.Add(t);
                }
            }
            return result;
        }

        // midline between two neighbouring rows + their minimal gap; null when degenerate
        private static List<vec3> BuildMidline(OrchRow a, OrchRow b, out double minGap)
        {
            minGap = double.MaxValue;
            var mid = new List<vec3>();

            // one midpoint per point of row A, projected to the nearest point of row B
            foreach (vec3 pa in a.curve)
            {
                vec2 nb = NearestOnCurve(b.curve, pa.easting, pa.northing, out double gap);
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
            return mid;
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

        // ── Planner: "sredina" MVP (kept for reference; BuildPasses supersedes it) ──
        // One center pass per inter-row = BuildPasses with onePass = true.
        public static List<CTrk> BuildMidlines(List<OrchRow> rows, double toolWidth, out int warnCount)
        {
            var tracks = BuildPasses(rows, toolWidth, 0, 0, 0, 0, true, true, out List<string> warns);
            warnCount = warns.Count;
            return tracks;
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
            double best = double.MaxValue; vec2 res = new vec2(e, n);
            for (int i = 0; i < curve.Count; i++)
            {
                double de = curve[i].easting - e, dn = curve[i].northing - n;
                double d = de * de + dn * dn;
                if (d < best) { best = d; res = new vec2(curve[i].easting, curve[i].northing); }
            }
            dist = Math.Sqrt(best);
            return res;
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

        public static void ExtendEnds(List<vec3> des, double meters)
        {
            if (des.Count < 2) return;
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
        public static void SaveRows(string fieldDir, List<OrchRow> rows, List<vec2> looseTrees = null)
        {
            if (string.IsNullOrEmpty(fieldDir)) return;
            string path = Path.Combine(fieldDir, "Orchard.txt");
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
        {
            var rows = new List<OrchRow>();
            if (string.IsNullOrEmpty(fieldDir)) return rows;
            string path = Path.Combine(fieldDir, "Orchard.txt");
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
    }
}
