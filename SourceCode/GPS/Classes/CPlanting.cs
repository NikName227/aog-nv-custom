using AgLibrary.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace AgOpenGPS
{
    // Feature 50 — planting. The orchard planner lays out where every seedling should
    // go; this is the driving half: it tracks the PLANTER's drop point (not the pivot),
    // tells the driver how far the next seedling is, and remembers what is already in
    // the ground so the job can be stopped and picked up again another day.

    /// <summary>One planned seedling position and whether it is already planted.</summary>
    public class PlantSpot
    {
        public vec2 pos;
        public bool planted;

        public PlantSpot(vec2 p, bool isPlanted) { pos = p; planted = isPlanted; }
    }

    public class CPlanting
    {
        private readonly FormGPS mf;

        public readonly List<PlantSpot> spots = new List<PlantSpot>();

        /// <summary>
        /// Tree positions of the orchard rows (SIM 0730). The "how far to the next tree" readout
        /// is wanted in EVERY orchard mode, not only while planting - mulching or spraying wants
        /// to know it just as much. When a planting job exists its spots are the targets;
        /// otherwise these are, and nothing is ever marked on them.
        /// </summary>
        public readonly List<vec2> rowTrees = new List<vec2>();

        /// <summary>Distance from the planter drop point to the next seedling spot; -1 = none.</summary>
        public double DistToNext { get; private set; } = -1;

        public int PlantedCount { get; private set; }

        /// <summary>The planter drop point in field coordinates (valid while a job runs).</summary>
        public vec2 PlantPoint { get; private set; }

        public bool IsOn => spots.Count > 0;

        /// <summary>Is there anything to measure the distance to?</summary>
        public bool HasTrees => spots.Count > 0 || rowTrees.Count > 0;

        // the spot being driven at; -1 = look for a new one
        private int targetIdx = -1;

        // a spot is only looked for within this range, and only counts as planted once
        // the drop point has driven PAST it
        private const double SearchRange = 120.0;
        private const double MinSpeedKmh = 0.3;

        private bool dirty;

        public CPlanting(FormGPS _f) { mf = _f; }

        public void Clear()
        {
            spots.Clear();
            rowTrees.Clear();
            targetIdx = -1;
            PlantedCount = 0;
            DistToNext = -1;
            dirty = false;
        }

        /// <summary>
        /// Rebuild the spot list from the orchard rows, keeping what is already planted:
        /// re-running the planner must not wipe a half-finished planting job.
        /// </summary>
        public void BuildFromRows(List<OrchRow> rows)
        {
            var done = new List<vec2>();
            foreach (var s in spots) if (s.planted) done.Add(s.pos);

            spots.Clear();
            targetIdx = -1;
            if (rows != null)
            {
                foreach (var r in rows)
                    foreach (var t in r.trees)
                        spots.Add(new PlantSpot(t, WasPlanted(done, t)));
            }

            PlantedCount = 0;
            foreach (var s in spots) if (s.planted) PlantedCount++;
            dirty = true;
        }

        // same spot within 10 cm = the same tree (a hand edit may shift it slightly)
        private static bool WasPlanted(List<vec2> done, vec2 p)
        {
            foreach (var d in done)
            {
                double de = d.easting - p.easting, dn = d.northing - p.northing;
                if (de * de + dn * dn < 0.01) return true;
            }
            return false;
        }

        /// <summary>Per fix: follow the drop point, mark spots it drove past, aim at the next.</summary>
        public void Update()
        {
            if (!HasTrees) { DistToNext = -1; return; }

            //SIM 0730: the implement position AOG already computes is the measuring point -
            //toolPos comes from the hitch geometry (hitch length for a rigid tool, the trailing
            //lengths and the jackknife for a towed one), and the tool offset puts it sideways.
            //No F50 geometry of its own: a planter is set up like any other implement.
            vec3 tp = mf.toolPos;
            double h = tp.heading;
            double lat = mf.tool.offset;

            PlantPoint = new vec2(
                tp.easting + Math.Sin(h + glm.PIBy2) * lat,
                tp.northing + Math.Cos(h + glm.PIBy2) * lat);

            double fe = Math.Sin(h), fn = Math.Cos(h);   // forward unit vector
            bool driving = mf.avgSpeed > MinSpeedKmh && !mf.isReverse;

            // SIM 0730: driving past a spot no longer marks it planted. Geometry cannot know
            // whether a seedling actually went into the ground - a skipped spot, a hand refill,
            // a pass driven twice - and a wrong mark is worse than no mark. The call stays with
            // the driver (MarkTargetPlanted), the HUD keeps showing how far the next spot is.
            if (targetIdx >= 0)
            {
                PlantSpot t = spots[targetIdx];
                if (t.planted || (driving && Along(t.pos, fe, fn) < 0)) targetIdx = -1;
            }

            if (spots.Count == 0)
            {
                //no planting job: plain readout to the nearest tree of the rows ahead
                DistToNext = NearestAhead(rowTrees, fe, fn);
                return;
            }

            if (targetIdx < 0) targetIdx = FindNextAhead(fe, fn);
            DistToNext = targetIdx >= 0 ? Dist(spots[targetIdx].pos) : -1;
        }

        // distance to the nearest position ahead of the implement; -1 = none in range
        private double NearestAhead(List<vec2> pts, double fe, double fn)
        {
            double best = SearchRange;
            bool any = false;
            for (int i = 0; i < pts.Count; i++)
            {
                if (Along(pts[i], fe, fn) < 0) continue;
                double d = Dist(pts[i]);
                if (d < best) { best = d; any = true; }
            }
            return any ? best : -1;
        }

        // nearest not-yet-planted spot that is still in front of the drop point
        private int FindNextAhead(double fe, double fn)
        {
            int best = -1;
            double bestDist = SearchRange;
            for (int i = 0; i < spots.Count; i++)
            {
                if (spots[i].planted) continue;
                if (Along(spots[i].pos, fe, fn) < 0) continue;
                double d = Dist(spots[i].pos);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        private double Along(vec2 p, double fe, double fn)
            => (p.easting - PlantPoint.easting) * fe + (p.northing - PlantPoint.northing) * fn;

        private double Dist(vec2 p)
        {
            double de = p.easting - PlantPoint.easting, dn = p.northing - PlantPoint.northing;
            return Math.Sqrt(de * de + dn * dn);
        }

        /// <summary>Write off the target spot by hand (planted off the plan, or skipped).</summary>
        public void MarkTargetPlanted()
        {
            if (targetIdx < 0 || targetIdx >= spots.Count || spots[targetIdx].planted) return;
            spots[targetIdx].planted = true;
            PlantedCount++;
            dirty = true;
            targetIdx = -1;
        }

        // ── Persistence: Planting.txt next to the field ───────────────────
        // Positions are held here as well as in Orchard.txt on purpose - the planting
        // job survives edits to the orchard rows.
        public void Save(string fieldDir)
        {
            if (string.IsNullOrEmpty(fieldDir)) return;
            string path = Path.Combine(fieldDir, "Planting.txt");
            try
            {
                if (spots.Count == 0)
                {
                    if (File.Exists(path)) File.Delete(path);
                    dirty = false;
                    return;
                }

                using (var w = new StreamWriter(path, false))
                {
                    w.WriteLine("$Planting");
                    w.WriteLine(spots.Count.ToString(CultureInfo.InvariantCulture));
                    foreach (var s in spots)
                        w.WriteLine(s.pos.easting.ToString(CultureInfo.InvariantCulture) + ","
                                  + s.pos.northing.ToString(CultureInfo.InvariantCulture) + ","
                                  + (s.planted ? "1" : "0"));
                }
                dirty = false;
            }
            catch (Exception ex)
            {
                Log.EventWriter("Planting Save: " + ex);
            }
        }

        public void SaveIfDirty(string fieldDir)
        {
            if (dirty) Save(fieldDir);
        }

        public void Load(string fieldDir)
        {
            Clear();
            //F06: a locked install must not start guiding a planting job
            if (!CFeatureLock.IsOn(CFeatureLock.FeatureOrchard)) return;
            if (string.IsNullOrEmpty(fieldDir)) return;
            string path = Path.Combine(fieldDir, "Planting.txt");
            if (!File.Exists(path)) return;
            try
            {
                string[] L = File.ReadAllLines(path);
                int i = 0;
                if (i < L.Length && L[i].StartsWith("$Planting")) i++;
                if (i >= L.Length) return;
                int.TryParse(L[i++], out int count);

                for (int k = 0; k < count && i < L.Length; k++, i++)
                {
                    string[] c = L[i].Split(',');
                    if (c.Length < 2) continue;
                    if (!double.TryParse(c[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double e)) continue;
                    if (!double.TryParse(c[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double n)) continue;
                    bool done = c.Length >= 3 && c[2].Trim() == "1";
                    spots.Add(new PlantSpot(new vec2(e, n), done));
                    if (done) PlantedCount++;
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("Planting Load: " + ex);
            }
        }
    }
}
