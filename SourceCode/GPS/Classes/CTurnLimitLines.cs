using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;

namespace AgOpenGPS
{
    /// <summary>
    /// One turn limit line: a polyline drawn at an end of the field marking how far the
    /// vehicle WHEELS may go while turning (usually outside the boundary, over the road).
    /// </summary>
    public class CTurnLimitLine
    {
        // drawn points (local plane easting/northing, heading unused)
        public List<vec3> pts = new List<vec3>();

        // wheel-safety line: drawn line offset toward the field so that the pivot path
        // keeps the outer front wheel inside the drawn line
        public List<vec2> effPts = new List<vec2>();

        // which cross-product sign means "field side" for each effective segment
        public List<double> effSegSign = new List<double>();

        // effective line EXTRAPOLATED far (+300 m) along the end-segment directions, so a
        // line that was not drawn all the way across the field still works as the limit.
        // Used for the corridor checks and the rigid turn builders; display shows only a
        // short stub of the extension.
        public List<vec2> effExtPts = new List<vec2>();
        public List<double> effExtSegSign = new List<double>();
    }

    /// <summary>
    /// Turn limit lines (per field). The corridor between the turn line (headland) and
    /// these lines is additionally allowed for youturn placement, so the maneuver can use
    /// the road outside the boundary. Where no line is drawn the boundary stays the limit.
    /// Maneuver selection: K-turn primary (always K) or Combined (U-turn if it fits between
    /// the turn line and the limit line, otherwise K). Both off = stock AOG behavior.
    /// </summary>
    public class CTurnLimitLines
    {
        private readonly FormGPS mf;

        public List<CTurnLimitLine> lines = new List<CTurnLimitLine>();

        // master switch + options, saved per field in TurnLines.txt
        // isAutomatic = auto-engage the youturn button; isAfterStop = trigger only after
        // the vehicle has stopped and moves forward again (else = automatic/immediate turn)
        public bool isTurnActive, isAutomatic, isKPrimary, isCombined, isAfterStop;

        // wheel margin the effective lines were built with (-1 = not built)
        private double builtMargin = -1;

        public CTurnLimitLines(FormGPS _f)
        {
            mf = _f;
        }

        // the whole feature only acts when activated, lines exist and a maneuver is chosen
        public bool IsFeatureOn
        {
            get { return isTurnActive && lines.Count > 0 && (isKPrimary || isCombined); }
        }

        public void Reset()
        {
            lines.Clear();
            isTurnActive = isAutomatic = isKPrimary = isCombined = isAfterStop = false;
            builtMargin = -1;
        }

        public void ApplyLoaded(AgOpenGPS.IO.TurnLinesData data)
        {
            Reset();
            if (data == null) return;
            //F05/F06: activation follows the field file; the F06 turnapp/orchard
            //gate in SaveOpen decides whether this is ever called with real data
            //(locked installs get ApplyLoaded(null) and never reach here).
            isTurnActive = data.active;
            isAutomatic = data.automatic;
            isKPrimary = data.kPrimary;
            isCombined = data.combined;
            isAfterStop = data.afterStop;
            foreach (var l in data.lines)
            {
                if (l.Count > 1) lines.Add(new CTurnLimitLine { pts = l });
            }
            if (lines.Count > 0) BuildEffectiveLines();
        }

        /// <summary>
        /// How far inside the drawn line the pivot path has to stay: HALF THE TRACTOR
        /// WIDTH plus whatever extra room the driver asked for.
        ///
        /// Field 2026-08-15: this used to be the sweep of the outer FRONT wheel around a
        /// turn, sqrt((r + track/2)^2 + wheelbase^2) - r. That models one moment of a
        /// maneuver but was applied to the whole line, including the straight run where the
        /// tractor only takes up half a track, so with the extra offset at 0 the dashed line
        /// still sat 1.5-2 m in - and it moved whenever the turn radius changed. The lateral
        /// gabarit of the tractor is what belongs here; the offset is the knob for machines
        /// that need more room, and it is now predictable: what you type is what you get.
        /// </summary>
        public double WheelSafetyMargin()
        {
            double halfTrack = Math.Abs(mf.vehicle.VehicleConfig.TrackWidth) * 0.5;

            // F51: plus whatever room THIS implement needs. A wide machine may not let the
            // tractor drive right up to the drawn line, and how much is a property of the
            // implement, so it travels with the implement profile. Applies to the wheel limit
            // only - the passes still stop at the boundary/headland, where the implement
            // reference already is.
            return halfTrack + Math.Max(0, Properties.ToolSettings.Default.setTurn_boundaryOffset);
        }

        private void EnsureBuilt()
        {
            if (lines.Count == 0) return;
            if (builtMargin < 0 || Math.Abs(builtMargin - WheelSafetyMargin()) > 0.01)
                BuildEffectiveLines();
        }

        // centroid of the outer fence = which side of a line is "the field"
        private bool TryGetFieldCentroid(out double ce, out double cn)
        {
            ce = cn = 0;
            if (mf.bnd.bndList.Count == 0 || mf.bnd.bndList[0].fenceLine.Count < 3) return false;
            var fence = mf.bnd.bndList[0].fenceLine;
            for (int i = 0; i < fence.Count; i++)
            {
                ce += fence[i].easting;
                cn += fence[i].northing;
            }
            ce /= fence.Count;
            cn /= fence.Count;
            return true;
        }

        public void BuildEffectiveLines()
        {
            double margin = WheelSafetyMargin();
            builtMargin = margin;

            if (!TryGetFieldCentroid(out double ce, out double cn)) return;

            foreach (var ln in lines)
            {
                ln.effPts.Clear();
                ln.effSegSign.Clear();
                int n = ln.pts.Count;
                if (n < 2) continue;

                // offset every point toward the field by the wheel margin, using the
                // averaged direction of the adjacent segments
                for (int i = 0; i < n; i++)
                {
                    int iPrev = i > 0 ? i - 1 : 0;
                    int iNext = i < n - 1 ? i + 1 : n - 1;
                    double dx = ln.pts[iNext].easting - ln.pts[iPrev].easting;
                    double dy = ln.pts[iNext].northing - ln.pts[iPrev].northing;
                    double len = Math.Sqrt(dx * dx + dy * dy);
                    if (len < 0.001) { dx = 1; dy = 0; len = 1; }
                    dx /= len; dy /= len;

                    // perpendicular, flipped so it points toward the field centroid
                    double nx = dy, ny = -dx;
                    if (nx * (ce - ln.pts[i].easting) + ny * (cn - ln.pts[i].northing) < 0)
                    {
                        nx = -nx; ny = -ny;
                    }
                    ln.effPts.Add(new vec2(ln.pts[i].easting + nx * margin,
                                           ln.pts[i].northing + ny * margin));
                }

                // per-segment "field side" cross-product sign
                for (int i = 0; i < ln.effPts.Count - 1; i++)
                {
                    double sx = ln.effPts[i + 1].easting - ln.effPts[i].easting;
                    double sy = ln.effPts[i + 1].northing - ln.effPts[i].northing;
                    double cross = sx * (cn - ln.effPts[i].northing) - sy * (ce - ln.effPts[i].easting);
                    ln.effSegSign.Add(cross >= 0 ? 1.0 : -1.0);
                }

                // extrapolated effective line (math + corridor)
                ln.effExtPts = ExtendPolyline(ln.effPts, effExtension);
                ln.effExtSegSign.Clear();
                for (int i = 0; i < ln.effExtPts.Count - 1; i++)
                {
                    double sx = ln.effExtPts[i + 1].easting - ln.effExtPts[i].easting;
                    double sy = ln.effExtPts[i + 1].northing - ln.effExtPts[i].northing;
                    double cross = sx * (cn - ln.effExtPts[i].northing) - sy * (ce - ln.effExtPts[i].easting);
                    ln.effExtSegSign.Add(cross >= 0 ? 1.0 : -1.0);
                }
            }
        }

        // how far (m) past its drawn ends an effective line keeps acting - for the
        // ray pick, the turn-corridor checks and the rigid builder geometry. Field
        // 2026-07-28: was 300 m, which let a line reach clear across the field so
        // the forward ray could pick a line at the WRONG end; 50 m still covers
        // passes that overhang a line's end (45 m in the test field) but no more.
        public const double effExtension = 50.0;

        /// <summary>
        /// Extend a polyline by dist along its first/last segment directions.
        /// </summary>
        public static List<vec2> ExtendPolyline(List<vec2> pts, double dist)
        {
            var outp = new List<vec2>((pts?.Count ?? 0) + 2);
            if (pts == null || pts.Count < 2) { if (pts != null) outp.AddRange(pts); return outp; }
            int n = pts.Count;
            double d0x = pts[0].easting - pts[1].easting, d0y = pts[0].northing - pts[1].northing;
            double l0 = Math.Sqrt(d0x * d0x + d0y * d0y); if (l0 < 1e-6) l0 = 1;
            outp.Add(new vec2(pts[0].easting + d0x / l0 * dist, pts[0].northing + d0y / l0 * dist));
            outp.AddRange(pts);
            double d1x = pts[n - 1].easting - pts[n - 2].easting, d1y = pts[n - 1].northing - pts[n - 2].northing;
            double l1 = Math.Sqrt(d1x * d1x + d1y * d1y); if (l1 < 1e-6) l1 = 1;
            outp.Add(new vec2(pts[n - 1].easting + d1x / l1 * dist, pts[n - 1].northing + d1y / l1 * dist));
            return outp;
        }

        /// <summary>
        /// Extended effective polyline of the line first hit by the ray (origin, heading);
        /// null when no line is hit. Input geometry for the rigid turn builders.
        /// </summary>
        public List<vec2> GetEffPolylineByRay(vec2 origin, double heading)
        {
            return GetEffPolylineByRay(origin, heading, out _, out _);
        }

        /// <summary>
        /// Same pick, plus WHICH line was hit and how far along the ray (diagnostics
        /// for the wrong-end turn reports). lineIndex -1 / distance -1 = no hit.
        /// </summary>
        public List<vec2> GetEffPolylineByRay(vec2 origin, double heading,
            out int lineIndex, out double distance)
        {
            lineIndex = -1; distance = -1;
            if (lines.Count == 0) return null;
            EnsureBuilt();
            double dirX = Math.Sin(heading), dirY = Math.Cos(heading);
            int bestLine = -1; double best = -1;
            for (int li = 0; li < lines.Count; li++)
            {
                var ln = lines[li];
                for (int i = 0; i < ln.effExtPts.Count - 1; i++)
                {
                    if (RaySegment(origin.easting, origin.northing, dirX, dirY,
                        ln.effExtPts[i].easting, ln.effExtPts[i].northing,
                        ln.effExtPts[i + 1].easting, ln.effExtPts[i + 1].northing, out double t))
                    {
                        if (best < 0 || t < best) { best = t; bestLine = li; }
                    }
                }
            }
            if (bestLine < 0) return null;
            lineIndex = bestLine;
            distance = best;
            return lines[bestLine].effExtPts;
        }

        /// <summary>
        /// True when the point lies on the field side of an effective limit line, within
        /// that line's lateral extent — i.e. inside the extra corridor allowed for turning.
        /// </summary>
        public bool IsPointInsideCorridor(double easting, double northing)
        {
            if (lines.Count == 0) return false;
            EnsureBuilt();

            foreach (var ln in lines)
            {
                for (int i = 0; i < ln.effExtPts.Count - 1; i++)
                {
                    double ax = ln.effExtPts[i].easting, ay = ln.effExtPts[i].northing;
                    double dx = ln.effExtPts[i + 1].easting - ax;
                    double dy = ln.effExtPts[i + 1].northing - ay;
                    double len2 = dx * dx + dy * dy;
                    if (len2 < 0.0001) continue;

                    double t = ((easting - ax) * dx + (northing - ay) * dy) / len2;
                    if (t < 0 || t > 1) continue;

                    double cross = dx * (northing - ay) - dy * (easting - ax);
                    if (cross == 0 || (cross > 0 ? 1.0 : -1.0) == ln.effExtSegSign[i]) return true;
                }
            }
            return false;
        }

        public bool IsPointInsideCorridor(vec3 pt)
        {
            return IsPointInsideCorridor(pt.easting, pt.northing);
        }

        /// <summary>
        /// How far (m) beyond the field boundary the turn is ever allowed to reach. Caps the
        /// maneuver so it can only use a strip of road up to the drawn line — never run far
        /// down the pass (e.g. onto a stale/too-far line left from an earlier edit).
        /// </summary>
        public double MaxReachBeyondFence()
        {
            return Math.Max(20.0, mf.yt.uturnDistanceFromBoundary + 4.0 * mf.yt.youTurnRadius);
        }

        /// <summary>
        /// Distance (m) a point lies OUTSIDE the outer fence (0 when inside the field).
        /// </summary>
        public double DistanceOutsideFence(double easting, double northing)
        {
            if (mf.bnd.bndList.Count == 0) return 0;
            var fence = mf.bnd.bndList[0].fenceLine;
            if (fence.Count < 3) return 0;
            if (fence.IsPointInPolygon(new vec3(easting, northing, 0))) return 0;

            double best = double.MaxValue;
            for (int i = 0; i < fence.Count; i++)
            {
                int j = (i + 1) % fence.Count;
                double dx = fence[j].easting - fence[i].easting;
                double dy = fence[j].northing - fence[i].northing;
                double len2 = dx * dx + dy * dy;
                double t = len2 <= 0 ? 0 : ((easting - fence[i].easting) * dx + (northing - fence[i].northing) * dy) / len2;
                if (t < 0) t = 0; else if (t > 1) t = 1;
                double cx = fence[i].easting + t * dx, cy = fence[i].northing + t * dy;
                double d = Math.Sqrt((easting - cx) * (easting - cx) + (northing - cy) * (northing - cy));
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>
        /// True when the point is in the extra turn strip allowed by the limit lines: on the
        /// field side of a drawn line AND not more than MaxReachBeyondFence past the boundary.
        /// This is the bounded band the maneuver may use (headland out to the drawn line).
        /// </summary>
        public bool IsInAllowedExtension(vec3 pt)
        {
            if (!IsFeatureOn) return false;
            EnsureBuilt();

            double e = pt.easting, n = pt.northing;
            double maxReach = MaxReachBeyondFence();
            if (DistanceOutsideFence(e, n) > maxReach) return false;

            // The extra strip only exists NEAR a drawn line (the band between the boundary
            // and that line), not across the whole field - otherwise a line at one end would
            // let the turn poke out at the far end too. Require the point to be on the field
            // side AND within maxReach of the line.
            foreach (var ln in lines)
            {
                for (int i = 0; i < ln.effExtPts.Count - 1; i++)
                {
                    double ax = ln.effExtPts[i].easting, ay = ln.effExtPts[i].northing;
                    double dx = ln.effExtPts[i + 1].easting - ax;
                    double dy = ln.effExtPts[i + 1].northing - ay;
                    double len2 = dx * dx + dy * dy;
                    if (len2 < 0.0001) continue;

                    double t = ((e - ax) * dx + (n - ay) * dy) / len2;
                    if (t < 0 || t > 1) continue;

                    double cross = dx * (n - ay) - dy * (e - ax);
                    bool fieldSide = cross == 0 || (cross > 0 ? 1.0 : -1.0) == ln.effExtSegSign[i];
                    if (!fieldSide) continue;

                    double perp = Math.Abs(cross) / Math.Sqrt(len2);
                    if (perp <= maxReach) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Shortest perpendicular distance from a point to any effective limit line, -1 when
        /// there is none. Side-agnostic on purpose (REQ 2026-08-15-9): what matters is the
        /// BAND around the line, not which side of it the vehicle is on - fields differ in
        /// where the boundary runs, and the driver should not have to think about it. Only the
        /// drawn extent counts, not the extrapolation, so the band cannot appear off the ends.
        /// </summary>
        // how far past its drawn ends the line still counts for the bands below. Field
        // 2026-08-16: the driver refills the seeder OFF the field, past the end of the drawn
        // line, and that spot must still count as "at the headland" (REQ 2026-08-15-9).
        public const double bandEndExtension = 30.0;

        public double DistanceToEffectiveLines(double easting, double northing)
        {
            if (lines.Count == 0) return -1;
            EnsureBuilt();

            double best = -1;
            foreach (var ln in lines)
            {
                int last = ln.effPts.Count - 2;
                for (int i = 0; i <= last; i++)
                {
                    double ax = ln.effPts[i].easting, ay = ln.effPts[i].northing;
                    double dx = ln.effPts[i + 1].easting - ax;
                    double dy = ln.effPts[i + 1].northing - ay;
                    double len2 = dx * dx + dy * dy;
                    if (len2 < 0.0001) continue;

                    //the first and last segment reach bandEndExtension past the drawn end
                    double reach = bandEndExtension / Math.Sqrt(len2);
                    double tMin = i == 0 ? -reach : 0;
                    double tMax = i == last ? 1.0 + reach : 1.0;

                    double t = ((easting - ax) * dx + (northing - ay) * dy) / len2;
                    if (t < tMin) t = tMin; else if (t > tMax) t = tMax;

                    double ex = ax + t * dx - easting, ey = ay + t * dy - northing;
                    double d = Math.Sqrt(ex * ex + ey * ey);
                    if (best < 0 || d < best) best = d;
                }
            }
            return best;
        }

        /// <summary>
        /// Distance along the ray (origin, heading) to the nearest effective limit line,
        /// -1 when no line is hit. Used to measure the space available for a U-turn.
        /// </summary>
        public double RayDistanceToEffectiveLines(vec2 origin, double heading)
        {
            if (lines.Count == 0) return -1;
            EnsureBuilt();

            double dirX = Math.Sin(heading), dirY = Math.Cos(heading);
            double best = -1;
            foreach (var ln in lines)
            {
                for (int i = 0; i < ln.effPts.Count - 1; i++)
                {
                    if (RaySegment(origin.easting, origin.northing, dirX, dirY,
                        ln.effPts[i].easting, ln.effPts[i].northing,
                        ln.effPts[i + 1].easting, ln.effPts[i + 1].northing, out double t))
                    {
                        if (best < 0 || t < best) best = t;
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// Distance along the ray to the nearest crossing of a polyline (closed = ring),
        /// -1 when nothing is hit. Used for the turn line and the fence.
        /// </summary>
        public static double RayDistanceToPolyline(vec2 origin, double heading, List<vec3> poly, bool closed)
        {
            if (poly == null || poly.Count < 2) return -1;
            double dirX = Math.Sin(heading), dirY = Math.Cos(heading);
            double best = -1;
            int cnt = poly.Count;
            int segs = closed ? cnt : cnt - 1;
            for (int i = 0; i < segs; i++)
            {
                int j = (i + 1) % cnt;
                if (RaySegment(origin.easting, origin.northing, dirX, dirY,
                    poly[i].easting, poly[i].northing, poly[j].easting, poly[j].northing, out double t))
                {
                    if (best < 0 || t < best) best = t;
                }
            }
            return best;
        }

        // ray (o, d) vs segment (a, b); t = distance along the ray at the crossing
        private static bool RaySegment(double ox, double oy, double dx, double dy,
            double ax, double ay, double bx, double by, out double t)
        {
            t = -1;
            double rx = bx - ax, ry = by - ay;
            double det = rx * dy - dx * ry;
            if (Math.Abs(det) < 1e-12) return false;
            t = (-(ax - ox) * ry + rx * (ay - oy)) / det;
            double u = (dx * (ay - oy) - dy * (ax - ox)) / det;
            return t >= 0 && u >= 0 && u <= 1;
        }

        /// <summary>
        /// Draw the limit lines on the main map. Bright orange when the feature is active
        /// (+ dashed wheel-safety line), dimmed gray dashes when not activated.
        /// </summary>
        public void DrawTurnLines()
        {
            if (lines.Count == 0) return;
            EnsureBuilt();

            foreach (var ln in lines)
            {
                if (ln.pts.Count < 2) continue;

                if (isTurnActive)
                {
                    GL.LineWidth(5);
                    GL.Color4(1.0f, 0.55f, 0.0f, 0.95f);
                }
                else
                {
                    GL.LineWidth(3);
                    GL.Color4(0.62f, 0.62f, 0.62f, 0.9f);
                    GL.Enable(EnableCap.LineStipple);
                    GL.LineStipple(1, 0x0F0F);
                }

                GL.Begin(PrimitiveType.LineStrip);
                for (int i = 0; i < ln.pts.Count; i++)
                    GL.Vertex3(ln.pts[i].easting, ln.pts[i].northing, 0);
                GL.End();

                if (!isTurnActive) GL.Disable(EnableCap.LineStipple);

                // short extrapolation stubs (math uses a far extension; show only ~3 m)
                if (ln.pts.Count > 1)
                {
                    int n = ln.pts.Count;
                    double s0x = ln.pts[0].easting - ln.pts[1].easting, s0y = ln.pts[0].northing - ln.pts[1].northing;
                    double sl0 = Math.Sqrt(s0x * s0x + s0y * s0y); if (sl0 < 1e-6) sl0 = 1;
                    double s1x = ln.pts[n - 1].easting - ln.pts[n - 2].easting, s1y = ln.pts[n - 1].northing - ln.pts[n - 2].northing;
                    double sl1 = Math.Sqrt(s1x * s1x + s1y * s1y); if (sl1 < 1e-6) sl1 = 1;

                    GL.LineWidth(2);
                    GL.Color4(1.0f, 0.75f, 0.25f, 0.85f);
                    GL.Enable(EnableCap.LineStipple);
                    GL.LineStipple(1, 0x0F0F);
                    GL.Begin(PrimitiveType.Lines);
                    GL.Vertex3(ln.pts[0].easting, ln.pts[0].northing, 0);
                    GL.Vertex3(ln.pts[0].easting + s0x / sl0 * 3.0, ln.pts[0].northing + s0y / sl0 * 3.0, 0);
                    GL.Vertex3(ln.pts[n - 1].easting, ln.pts[n - 1].northing, 0);
                    GL.Vertex3(ln.pts[n - 1].easting + s1x / sl1 * 3.0, ln.pts[n - 1].northing + s1y / sl1 * 3.0, 0);
                    GL.End();
                    GL.Disable(EnableCap.LineStipple);
                }

                // end/vertex points so the line is easy to spot
                GL.PointSize(8.0f);
                GL.Color4(1.0f, 0.85f, 0.2f, 0.95f);
                GL.Begin(PrimitiveType.Points);
                for (int i = 0; i < ln.pts.Count; i++)
                    GL.Vertex3(ln.pts[i].easting, ln.pts[i].northing, 0);
                GL.End();

                // wheel-safety (pivot path) limit, dashed thin — only when active
                if (isTurnActive && ln.effPts.Count > 1)
                {
                    GL.LineWidth(2);
                    GL.Color4(1.0f, 0.35f, 0.0f, 0.85f);
                    GL.Enable(EnableCap.LineStipple);
                    GL.LineStipple(1, 0x0700);
                    GL.Begin(PrimitiveType.LineStrip);
                    for (int i = 0; i < ln.effPts.Count; i++)
                        GL.Vertex3(ln.effPts[i].easting, ln.effPts[i].northing, 0);
                    GL.End();
                    GL.Disable(EnableCap.LineStipple);
                }
            }
            GL.LineWidth(1);
        }
    }
}
