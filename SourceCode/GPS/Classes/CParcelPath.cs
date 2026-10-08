using System;
using System.Collections.Generic;

namespace AgOpenGPS
{
    /// <summary>
    /// Cadastral parcel lines as a network (SmartAB): snap a point onto the nearest parcel
    /// line, and find the shortest way ALONG the lines between two points - so a guidance
    /// line can follow a field edge made of several neighbouring parcels.
    ///
    /// Input is the flat segment list the main map draws (x0, y0, x1, y1 per segment, local
    /// easting/northing). Segment ends closer than MergeTol are one node, which joins the
    /// rings of neighbouring parcels into one network. No AOG types, so it can be tested alone.
    /// </summary>
    public class CParcelPath
    {
        public const double MergeTol = 0.05;   // metres

        private readonly double[] seg;         // 4 doubles per segment
        private readonly int segCount;

        public CParcelPath(double[] segments)
        {
            seg = segments ?? new double[0];
            segCount = seg.Length / 4;
        }

        public int SegmentCount => segCount;

        /// <summary>Nearest point on any parcel line within maxDist. False when none is that close.</summary>
        public bool Snap(double px, double py, double maxDist, out double cx, out double cy, out int segIdx)
        {
            cx = px; cy = py; segIdx = -1;
            double best = maxDist;
            for (int s = 0; s < segCount; s++)
            {
                if (Dist(seg[4 * s], seg[4 * s + 1], seg[4 * s + 2], seg[4 * s + 3]) < 1e-6) continue;
                double d = ClosestOnSeg(px, py, seg[4 * s], seg[4 * s + 1], seg[4 * s + 2], seg[4 * s + 3],
                                        out double qx, out double qy);
                if (d <= best) { best = d; cx = qx; cy = qy; segIdx = s; }
            }
            return segIdx >= 0;
        }

        /// <summary>
        /// Shortest way along the parcel lines from a to b, each first projected onto its nearest
        /// line (within maxDist). Points are spaced at most maxStep apart. Null when either point
        /// is not near a line or the two are not connected.
        /// </summary>
        public List<double[]> Path(double ax, double ay, double bx, double by, double maxDist, double maxStep)
        {
            if (!Snap(ax, ay, maxDist, out double pax, out double pay, out int sa)) return null;
            if (!Snap(bx, by, maxDist, out double pbx, out double pby, out int sb)) return null;

            // only the parcels around A..B: a field edge does not wander far from its two ends
            double margin = 100.0 + 0.5 * Math.Sqrt((pbx - pax) * (pbx - pax) + (pby - pay) * (pby - pay));
            double minX = Math.Min(pax, pbx) - margin, maxX = Math.Max(pax, pbx) + margin;
            double minY = Math.Min(pay, pby) - margin, maxY = Math.Max(pay, pby) + margin;

            var g = new Graph();
            int nodeA = g.AddFree(pax, pay), nodeB = g.AddFree(pbx, pby);

            for (int s = 0; s < segCount; s++)
            {
                double x0 = seg[4 * s], y0 = seg[4 * s + 1], x1 = seg[4 * s + 2], y1 = seg[4 * s + 3];
                if (Math.Max(x0, x1) < minX || Math.Min(x0, x1) > maxX
                    || Math.Max(y0, y1) < minY || Math.Min(y0, y1) > maxY) continue;
                if (Dist(x0, y0, x1, y1) < 1e-6) continue;

                int n0 = g.Node(x0, y0), n1 = g.Node(x1, y1);
                if (n0 == n1) continue;

                // A or B lies on this segment: split it there, so the path can start mid-segment
                var cuts = new List<int>();
                if (s == sa) cuts.Add(nodeA);
                if (s == sb) cuts.Add(nodeB);
                if (cuts.Count == 2
                    && Dist(x0, y0, g.X[nodeB], g.Y[nodeB]) < Dist(x0, y0, g.X[nodeA], g.Y[nodeA]))
                    cuts.Reverse();

                int prev = n0;
                foreach (int c in cuts) { g.Edge(prev, c); prev = c; }
                g.Edge(prev, n1);
            }

            List<int> nodes = g.Shortest(nodeA, nodeB);
            if (nodes == null) return null;

            var path = new List<double[]>();
            for (int i = 0; i < nodes.Count; i++)
            {
                double x = g.X[nodes[i]], y = g.Y[nodes[i]];
                if (path.Count > 0)
                {
                    double[] last = path[path.Count - 1];
                    double d = Dist(last[0], last[1], x, y);
                    if (d < 1e-3) continue;
                    int steps = (int)Math.Ceiling(d / maxStep);
                    for (int k = 1; k < steps; k++)
                    {
                        double f = (double)k / steps;
                        path.Add(new[] { last[0] + (x - last[0]) * f, last[1] + (y - last[1]) * f });
                    }
                }
                path.Add(new[] { x, y });
            }
            return path;
        }

        /// <summary>Distance from p to a polyline (list of {x, y}).</summary>
        public static double DistToPolyline(double px, double py, List<double[]> line)
        {
            double best = double.MaxValue;
            for (int i = 0; i + 1 < line.Count; i++)
            {
                double d = ClosestOnSeg(px, py, line[i][0], line[i][1], line[i + 1][0], line[i + 1][1], out _, out _);
                if (d < best) best = d;
            }
            return best;
        }

        public static double ClosestOnSeg(double px, double py, double ax, double ay, double bx, double by,
            out double cx, out double cy)
        {
            double dx = bx - ax, dy = by - ay;
            double len2 = dx * dx + dy * dy;
            double t = len2 <= 0 ? 0 : ((px - ax) * dx + (py - ay) * dy) / len2;
            t = Math.Max(0, Math.Min(1, t));
            cx = ax + t * dx; cy = ay + t * dy;
            return Dist(px, py, cx, cy);
        }

        private static double Dist(double ax, double ay, double bx, double by)
        {
            return Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
        }

        // Nodes merged on a MergeTol grid (3x3 cell lookup, so two ends just across a cell
        // border still become one node); edges weighted by length; Dijkstra with a binary heap.
        private class Graph
        {
            public readonly List<double> X = new List<double>(), Y = new List<double>();
            private readonly List<List<int>> adj = new List<List<int>>();
            private readonly List<List<double>> w = new List<List<double>>();
            private readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();

            private static long Key(long cx, long cy) { return (cx << 32) ^ (cy & 0xffffffffL); }

            public int AddFree(double x, double y)
            {
                X.Add(x); Y.Add(y); adj.Add(new List<int>()); w.Add(new List<double>());
                return X.Count - 1;
            }

            public int Node(double x, double y)
            {
                long cx = (long)Math.Floor(x / MergeTol), cy = (long)Math.Floor(y / MergeTol);
                for (long i = cx - 1; i <= cx + 1; i++)
                    for (long j = cy - 1; j <= cy + 1; j++)
                        if (cells.TryGetValue(Key(i, j), out var list))
                            foreach (int n in list)
                                if (Dist(X[n], Y[n], x, y) <= MergeTol) return n;

                int id = AddFree(x, y);
                long k = Key(cx, cy);
                if (!cells.TryGetValue(k, out var l)) { l = new List<int>(); cells[k] = l; }
                l.Add(id);
                return id;
            }

            public void Edge(int a, int b)
            {
                if (a == b) return;
                double d = Dist(X[a], Y[a], X[b], Y[b]);
                adj[a].Add(b); w[a].Add(d);
                adj[b].Add(a); w[b].Add(d);
            }

            public List<int> Shortest(int from, int to)
            {
                int n = X.Count;
                var dist = new double[n];
                var prev = new int[n];
                for (int i = 0; i < n; i++) { dist[i] = double.MaxValue; prev[i] = -1; }
                dist[from] = 0;

                var heap = new List<KeyValuePair<double, int>>();
                Push(heap, 0, from);
                while (heap.Count > 0)
                {
                    var top = Pop(heap);
                    int u = top.Value;
                    if (top.Key > dist[u]) continue;
                    if (u == to) break;
                    for (int k = 0; k < adj[u].Count; k++)
                    {
                        int v = adj[u][k];
                        double nd = dist[u] + w[u][k];
                        if (nd < dist[v]) { dist[v] = nd; prev[v] = u; Push(heap, nd, v); }
                    }
                }
                if (from != to && prev[to] < 0) return null;

                var path = new List<int>();
                for (int v = to; v != -1; v = prev[v]) path.Add(v);
                path.Reverse();
                return path;
            }

            private static void Push(List<KeyValuePair<double, int>> h, double key, int val)
            {
                h.Add(new KeyValuePair<double, int>(key, val));
                int i = h.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (h[p].Key <= h[i].Key) break;
                    var t = h[p]; h[p] = h[i]; h[i] = t; i = p;
                }
            }

            private static KeyValuePair<double, int> Pop(List<KeyValuePair<double, int>> h)
            {
                var top = h[0];
                h[0] = h[h.Count - 1];
                h.RemoveAt(h.Count - 1);
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, m = i;
                    if (l < h.Count && h[l].Key < h[m].Key) m = l;
                    if (r < h.Count && h[r].Key < h[m].Key) m = r;
                    if (m == i) break;
                    var t = h[m]; h[m] = h[i]; h[i] = t; i = m;
                }
                return top;
            }
        }
    }
}
