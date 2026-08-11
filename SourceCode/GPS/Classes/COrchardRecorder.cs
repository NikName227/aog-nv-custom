// COrchardRecorder.cs - orchard row recording from the painted sections (F50, 2026-08-11).
//
// The row is no longer typed in as a lateral/longitudinal offset of a reference point. The
// implement is parked far out to one side, the driver goes down BOTH sides of the row, and the
// two painted strips overlap over it. The row is the middle between the OUTER edges of those
// two strips.
//
// The edges are not reconstructed from the drawn coverage: CalculateSectionLookAhead already
// keeps section[].leftPoint / rightPoint in world space, and the outer edge of the working
// group is the left point of the first section that is painting and the right point of the
// last one. This class samples exactly those two points.
//
// Spec: features/50_orchard-navigation/RECORD_ROW.md
using AgOpenGPS.Core.Translations;
using System;
using System.Collections.Generic;

namespace AgOpenGPS
{
    /// <summary>One drive-by along the row: the two outer edges of the painted strip.</summary>
    public class OrchPass
    {
        public List<vec2> left = new List<vec2>();
        public List<vec2> right = new List<vec2>();

        /// <summary>Strip centre, used for direction and for pairing the two passes.</summary>
        public List<vec2> Mid()
        {
            var m = new List<vec2>(left.Count);
            int n = Math.Min(left.Count, right.Count);
            for (int i = 0; i < n; i++)
                m.Add(new vec2((left[i].easting + right[i].easting) * 0.5,
                               (left[i].northing + right[i].northing) * 0.5));
            return m;
        }

        public double Length()
        {
            var m = Mid();
            double d = 0;
            for (int i = 1; i < m.Count; i++) d += glm.Distance(m[i], m[i - 1]);
            return d;
        }
    }

    /// <summary>A tree the driver marked while recording: where the mark was dropped.</summary>
    public class OrchTreeMark
    {
        public vec2 pos;
        public int passIdx;
    }

    public class COrchardRecorder
    {
        private readonly FormGPS mf;

        // A short accidental switch-off must not split the pass (field call 2026-08-10 20:22).
        // Constants, not settings - they get nudged after the first real orchard, not by the user.
        private const double MergeSeconds = 10.0;
        private const double MergeMetres = 15.0;
        private const double SampleStep = 1.0;    // metres between samples along the pass

        /// <summary>The small recorder window is open - only then is anything sampled.</summary>
        public bool isArmed;

        public readonly List<OrchPass> passes = new List<OrchPass>();
        public readonly List<OrchTreeMark> treeMarks = new List<OrchTreeMark>();

        private OrchPass current;
        private bool wasOn;
        private double lastE, lastN;
        private DateTime offAt;
        private vec2 offPos, offDir;

        // where the painted coverage stood when this row started, so "discard" can take it back
        private readonly List<int> paintStart = new List<int>();
        private int saveStart;

        public COrchardRecorder(FormGPS _f) { mf = _f; }

        public bool HasData => passes.Count > 0 || current != null;
        public int PassCount => passes.Count + (current != null ? 1 : 0);

        /// <summary>Current pass length in metres - shown in the recorder window status line.</summary>
        public double CurrentLength() => current == null ? 0 : current.Length();

        /// <summary>Start a fresh row: drop everything and remember where the paint stands.</summary>
        public void StartRow()
        {
            passes.Clear();
            treeMarks.Clear();
            current = null;
            wasOn = false;
            lastE = double.MaxValue;

            paintStart.Clear();
            if (mf.triStrip != null)
                for (int j = 0; j < mf.triStrip.Count; j++)
                    paintStart.Add(mf.triStrip[j].patchList == null ? 0 : mf.triStrip[j].patchList.Count);
            saveStart = mf.patchSaveList == null ? 0 : mf.patchSaveList.Count;
        }

        /// <summary>Drop the row and take back the coverage it painted (Discard row).</summary>
        public void DiscardRow()
        {
            RemovePaintSinceStart();
            StartRow();
        }

        /// <summary>Row is built - the strips have done their job, wipe them off the map.</summary>
        public void ClearPaint() => RemovePaintSinceStart();

        private void RemovePaintSinceStart()
        {
            if (mf.triStrip != null)
            {
                for (int j = 0; j < mf.triStrip.Count && j < paintStart.Count; j++)
                {
                    var pl = mf.triStrip[j].patchList;
                    if (pl == null) continue;
                    while (pl.Count > paintStart[j]) pl.RemoveAt(pl.Count - 1);
                }
            }
            if (mf.patchSaveList != null)
                while (mf.patchSaveList.Count > saveStart) mf.patchSaveList.RemoveAt(mf.patchSaveList.Count - 1);
        }

        /// <summary>
        /// Called from the position update. Samples the outer edges of whatever the tool is
        /// painting and splits the drive into passes on section ON/OFF.
        /// </summary>
        public void Sample()
        {
            if (!isArmed || mf == null || !mf.isJobStarted) return;

            int first = -1, last = -1;
            for (int j = 0; j < mf.tool.numOfSections; j++)
            {
                if (!mf.section[j].isMappingOn) continue;
                if (first < 0) first = j;
                last = j;
            }

            bool on = first >= 0;

            if (!on)
            {
                if (wasOn)
                {
                    // pass paused - it only really ended if the driver stays off long enough
                    offAt = DateTime.Now;
                    offPos = new vec2(mf.pivotAxlePos.easting, mf.pivotAxlePos.northing);
                    offDir = HeadingVec();
                    wasOn = false;
                }
                return;
            }

            if (!wasOn)
            {
                if (current != null && !ContinuesSamePass()) CloseCurrentPass();
                if (current == null) current = new OrchPass();
                wasOn = true;
                lastE = double.MaxValue;   // do not draw a straight line across the gap
            }

            vec2 l = mf.section[first].leftPoint;
            vec2 r = mf.section[last].rightPoint;
            double ce = (l.easting + r.easting) * 0.5;
            double cn = (l.northing + r.northing) * 0.5;

            if (lastE != double.MaxValue)
            {
                double de = ce - lastE, dn = cn - lastN;
                if (de * de + dn * dn < SampleStep * SampleStep) return;
            }
            lastE = ce; lastN = cn;

            current.left.Add(l);
            current.right.Add(r);
        }

        /// <summary>A short stop in the same direction is the same pass, not a new one.</summary>
        private bool ContinuesSamePass()
        {
            if (current == null) return false;
            if ((DateTime.Now - offAt).TotalSeconds > MergeSeconds) return false;

            vec2 now = new vec2(mf.pivotAxlePos.easting, mf.pivotAxlePos.northing);
            if (glm.Distance(now, offPos) > MergeMetres) return false;

            vec2 d = HeadingVec();
            return (d.easting * offDir.easting + d.northing * offDir.northing) > 0;   // no U-turn
        }

        private vec2 HeadingVec()
        {
            double h = mf.fixHeading;
            return new vec2(Math.Sin(h), Math.Cos(h));
        }

        private void CloseCurrentPass()
        {
            if (current != null && current.left.Count >= 2) passes.Add(current);
            current = null;
        }

        /// <summary>Everything recorded so far, with the pass in progress closed off.</summary>
        public List<OrchPass> FinishedPasses()
        {
            CloseCurrentPass();
            wasOn = false;
            return passes;
        }

        /// <summary>Drop a tree mark at the centre of the painted strip.</summary>
        public void MarkTree()
        {
            if (!isArmed || mf == null || !mf.isJobStarted) return;

            int first = -1, last = -1;
            for (int j = 0; j < mf.tool.numOfSections; j++)
            {
                if (!mf.section[j].isMappingOn) continue;
                if (first < 0) first = j;
                last = j;
            }
            if (first < 0) return;      // nothing is painting - no reference point to mark from

            vec2 l = mf.section[first].leftPoint;
            vec2 r = mf.section[last].rightPoint;
            treeMarks.Add(new OrchTreeMark
            {
                pos = new vec2((l.easting + r.easting) * 0.5, (l.northing + r.northing) * 0.5),
                passIdx = passes.Count
            });
        }

        public void DeleteLastTree()
        {
            if (treeMarks.Count > 0) treeMarks.RemoveAt(treeMarks.Count - 1);
        }

        // ── Row build ─────────────────────────────────────────────────────
        // Exactly two passes, driven up one side of the row and back down the other. Their
        // strips overlap over the row, so the row centre is the middle between the two OUTER
        // edges - the edge of each strip that faces AWAY from the other pass.

        /// <summary>
        /// Turn what was recorded into a row. Returns null and fills <paramref name="err"/>
        /// when the drive does not describe a row (wrong number of passes, no overlap).
        /// </summary>
        public OrchRow BuildRow(string name, double treeMinDist, out string err)
        {
            err = "";
            var ps = FinishedPasses();

            if (ps.Count == 0) { err = gStr.gsOrchRecNeedTwo; return null; }
            if (ps.Count > 2) { err = gStr.gsOrchRecTooMany; return null; }

            List<vec3> mid;

            if (ps.Count == 1)
            {
                // One pass per row: the quick way to survey an orchard (decision 2026-08-11).
                // With a single strip there are no two outer edges, so the row is the centre of
                // that strip - which is where the row is IF the implement is set to straddle it.
                var only = ps[0].Mid();
                if (only.Count < 2) { err = gStr.gsOrchRecNoLine; return null; }
                mid = COrchard.CurveFromPoints(only);
            }
            else
            {
                List<vec2> outerA = OuterEdge(ps[0], ps[1]);
                List<vec2> outerB = OuterEdge(ps[1], ps[0]);
                if (outerA == null || outerB == null) { err = gStr.gsOrchRecNeedTwo; return null; }

                double ov = OverlapFraction(ps[0], ps[1]);
                if (ov < 0.10) { err = gStr.gsOrchRecNoOverlap; return null; }

                mid = COrchard.MidlineOfEdges(outerA, outerB, out _);
            }
            if (mid == null || mid.Count < 2) { err = gStr.gsOrchRecNoLine; return null; }

            var row = new OrchRow { source = RowSource.Recorded, name = name };

            // control points every ~2.5 m: dense enough for the shape, sparse enough to edit
            double step = 2.5;
            vec2 last = new vec2(double.MaxValue, double.MaxValue);
            foreach (vec3 p in mid)
            {
                if (last.easting != double.MaxValue && glm.Distance(new vec2(p.easting, p.northing), last) < step)
                    continue;
                last = new vec2(p.easting, p.northing);
                row.pts.Add(last);
            }
            if (row.pts.Count < 2) { err = gStr.gsOrchRecNoLine; return null; }

            row.curve = new List<vec3>(mid);
            row.trees = BuildTrees(row, treeMinDist);
            return row;
        }

        /// <summary>The edge of <paramref name="p"/> that faces away from <paramref name="other"/>.</summary>
        private static List<vec2> OuterEdge(OrchPass p, OrchPass other)
        {
            if (p == null || p.left.Count < 2 || p.right.Count < 2) return null;

            vec2 c = Centroid(other.Mid());
            double dl = MeanDistance(p.left, c);
            double dr = MeanDistance(p.right, c);
            return dl >= dr ? p.left : p.right;
        }

        private static vec2 Centroid(List<vec2> pts)
        {
            double e = 0, n = 0;
            if (pts.Count == 0) return new vec2(0, 0);
            foreach (vec2 p in pts) { e += p.easting; n += p.northing; }
            return new vec2(e / pts.Count, n / pts.Count);
        }

        private static double MeanDistance(List<vec2> line, vec2 c)
        {
            if (line.Count == 0) return 0;
            double d = 0;
            foreach (vec2 p in line) d += glm.Distance(p, c);
            return d / line.Count;
        }

        /// <summary>
        /// How much of the shorter pass runs alongside the other one, measured along the first
        /// pass' direction. Two passes that barely meet do not describe a row.
        /// </summary>
        private static double OverlapFraction(OrchPass a, OrchPass b)
        {
            var ma = a.Mid();
            var mb = b.Mid();
            if (ma.Count < 2 || mb.Count < 2) return 0;

            vec2 dir = new vec2(ma[ma.Count - 1].easting - ma[0].easting,
                                ma[ma.Count - 1].northing - ma[0].northing);
            double len = Math.Sqrt(dir.easting * dir.easting + dir.northing * dir.northing);
            if (len < 1.0) return 0;
            dir = new vec2(dir.easting / len, dir.northing / len);

            Span(ma, ma[0], dir, out double a0, out double a1);
            Span(mb, ma[0], dir, out double b0, out double b1);

            double lo = Math.Max(a0, b0), hi = Math.Min(a1, b1);
            double shared = hi - lo;
            if (shared <= 0) return 0;

            double shorter = Math.Min(a1 - a0, b1 - b0);
            return shorter < 1.0 ? 0 : shared / shorter;
        }

        private static void Span(List<vec2> line, vec2 origin, vec2 dir, out double lo, out double hi)
        {
            lo = double.MaxValue; hi = double.MinValue;
            foreach (vec2 p in line)
            {
                double t = (p.easting - origin.easting) * dir.easting + (p.northing - origin.northing) * dir.northing;
                if (t < lo) lo = t;
                if (t > hi) hi = t;
            }
        }

        // ── Trees ─────────────────────────────────────────────────────────
        // The same tree gets marked once per pass and never at the same spot, so marks that sit
        // closer together than HALF the minimum spacing are one tree, averaged ALONG the row.
        // A tree marked in only one pass keeps its single mark.

        private List<vec2> BuildTrees(OrchRow row, double treeMinDist)
        {
            var trees = new List<vec2>();
            if (treeMarks.Count == 0 || row.curve.Count < 2) return trees;

            var byAlong = new List<KeyValuePair<double, vec2>>();
            foreach (OrchTreeMark m in treeMarks)
                byAlong.Add(new KeyValuePair<double, vec2>(AlongRow(row, m.pos), m.pos));
            byAlong.Sort((x, y) => x.Key.CompareTo(y.Key));

            double half = Math.Max(0.2, treeMinDist * 0.5);

            int i = 0;
            while (i < byAlong.Count)
            {
                double sum = byAlong[i].Key;
                double e = byAlong[i].Value.easting, n = byAlong[i].Value.northing;
                int cnt = 1;
                int j = i + 1;
                while (j < byAlong.Count && (byAlong[j].Key - byAlong[j - 1].Key) < half)
                {
                    sum += byAlong[j].Key;
                    e += byAlong[j].Value.easting;
                    n += byAlong[j].Value.northing;
                    cnt++; j++;
                }
                trees.Add(new vec2(e / cnt, n / cnt));
                i = j;
            }

            var tmp = new OrchRow { curve = row.curve, trees = trees };
            COrchard.ProjectTreesOntoRow(tmp);      // sideways onto the line, position along kept
            return tmp.trees;
        }

        /// <summary>Distance of a point along the row, measured on the row curve.</summary>
        private static double AlongRow(OrchRow row, vec2 p)
        {
            double best = double.MaxValue, bestAlong = 0, along = 0;
            for (int i = 1; i < row.curve.Count; i++)
            {
                vec2 a = new vec2(row.curve[i - 1].easting, row.curve[i - 1].northing);
                vec2 b = new vec2(row.curve[i].easting, row.curve[i].northing);
                double dx = b.easting - a.easting, dy = b.northing - a.northing;
                double seg = Math.Sqrt(dx * dx + dy * dy);
                if (seg < 0.001) continue;

                double t = ((p.easting - a.easting) * dx + (p.northing - a.northing) * dy) / (seg * seg);
                if (t < 0) t = 0; else if (t > 1) t = 1;

                double qe = a.easting + t * dx, qn = a.northing + t * dy;
                double d = (p.easting - qe) * (p.easting - qe) + (p.northing - qn) * (p.northing - qn);
                if (d < best) { best = d; bestAlong = along + t * seg; }
                along += seg;
            }
            return bestAlong;
        }
    }
}
