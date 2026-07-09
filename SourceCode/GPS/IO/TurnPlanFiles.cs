// TurnPlanFiles.cs - per-field pass plan.
// One file per implement working width (TurnPlan_W<width>.txt) so switching the
// implement keeps the old plan on disk and it comes back with the same implement.
// Header flags + one line per pass entry (offsetIdx,order,zone,sameWay,worked[,track]).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace AgOpenGPS.IO
{
    public class TurnPlanData
    {
        public bool followPlan;
        public int mode = CTurnPlan.ModeZones;
        public int zoneCount = 2;
        public string trackName = "";
        public bool clipHeadland = true;
        public List<CPlanEntry> entries = new List<CPlanEntry>();
    }

    public static class TurnPlanFiles
    {
        // e.g. TurnPlan_W3.00.txt - the plan geometry only depends on the working width
        private static string FileName(double workWidth)
        {
            return "TurnPlan_W" + workWidth.ToString("0.00", CultureInfo.InvariantCulture) + ".txt";
        }

        // Returns null when the field has no plan for this working width.
        public static TurnPlanData Load(string fieldDirectory, double workWidth)
        {
            var path = Path.Combine(fieldDirectory, FileName(workWidth));

            // fall back to the legacy single-plan file
            if (!File.Exists(path)) path = Path.Combine(fieldDirectory, "TurnPlan.txt");
            if (!File.Exists(path)) return null;

            var data = new TurnPlanData();

            using (var reader = new StreamReader(path))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0 || line.StartsWith("$", StringComparison.Ordinal)) continue;

                    // track name may contain commas - take everything after the key
                    if (line.StartsWith("Track,", StringComparison.Ordinal))
                    {
                        data.trackName = line.Substring(6).Trim();
                        continue;
                    }

                    var kv = line.Split(',');
                    if (kv.Length == 2)
                    {
                        switch (kv[0].Trim())
                        {
                            case "FollowPlan":
                                if (bool.TryParse(kv[1].Trim(), out bool f)) data.followPlan = f;
                                continue;
                            case "ClipHeadland":
                                if (bool.TryParse(kv[1].Trim(), out bool c)) data.clipHeadland = c;
                                continue;
                            case "Mode":
                                if (int.TryParse(kv[1].Trim(), NumberStyles.Integer,
                                    CultureInfo.InvariantCulture, out int m)) data.mode = m;
                                continue;
                            case "ZoneCount":
                                if (int.TryParse(kv[1].Trim(), NumberStyles.Integer,
                                    CultureInfo.InvariantCulture, out int z)) data.zoneCount = z;
                                continue;
                        }
                    }

                    // pass entry: offsetIdx,order,zone,sameWay,worked[,trackName]
                    if (kv.Length >= 5 &&
                        int.TryParse(kv[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx) &&
                        int.TryParse(kv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int order) &&
                        int.TryParse(kv[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int zone) &&
                        bool.TryParse(kv[3], out bool sameWay) &&
                        bool.TryParse(kv[4], out bool worked))
                    {
                        // rejoin any remaining fields: the manual-pass track name
                        string track = kv.Length > 5
                            ? string.Join(",", kv, 5, kv.Length - 5).Trim()
                            : "";

                        data.entries.Add(new CPlanEntry
                        {
                            offsetIdx = idx,
                            order = order,
                            zone = zone,
                            isSameWay = sameWay,
                            isWorked = worked,
                            trackName = track
                        });
                    }
                }
            }

            return data;
        }

        public static void Save(string fieldDirectory, CTurnPlan plan, double workWidth)
        {
            var path = Path.Combine(fieldDirectory, FileName(workWidth));

            // no plan -> no file for this width (keeps the field folder clean)
            if (!plan.HasPlan)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }

            using (var writer = new StreamWriter(path, false))
            {
                writer.WriteLine("$TurnPlan");
                writer.WriteLine("FollowPlan," + plan.isFollowPlan);
                writer.WriteLine("ClipHeadland," + plan.clipToHeadland);
                writer.WriteLine("Mode," + plan.mode.ToString(CultureInfo.InvariantCulture));
                writer.WriteLine("ZoneCount," + plan.zoneCount.ToString(CultureInfo.InvariantCulture));
                if (!string.IsNullOrEmpty(plan.trackName))
                    writer.WriteLine("Track," + plan.trackName);

                foreach (var p in plan.entries)
                {
                    writer.WriteLine(
                        p.offsetIdx.ToString(CultureInfo.InvariantCulture) + "," +
                        p.order.ToString(CultureInfo.InvariantCulture) + "," +
                        p.zone.ToString(CultureInfo.InvariantCulture) + "," +
                        p.isSameWay + "," +
                        p.isWorked +
                        (p.IsManual ? "," + p.trackName : ""));
                }
            }
        }
    }
}
