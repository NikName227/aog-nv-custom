// TurnLinesFiles.cs - per-field turn limit lines (TurnLines.txt).
// Header flags + a list of polylines (local plane easting,northing points).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace AgOpenGPS.IO
{
    public class TurnLinesData
    {
        public bool active, automatic, kPrimary, combined, afterStop;
        public List<List<vec3>> lines = new List<List<vec3>>();
    }

    public static class TurnLinesFiles
    {
        // Returns null when the field has no TurnLines.txt.
        public static TurnLinesData Load(string fieldDirectory)
        {
            var path = Path.Combine(fieldDirectory, "TurnLines.txt");
            if (!File.Exists(path)) return null;

            var data = new TurnLinesData();

            using (var reader = new StreamReader(path))
            {
                string line;
                int pendingPoints = -1;
                List<vec3> current = null;

                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0 || line.StartsWith("$", StringComparison.Ordinal)) continue;

                    if (pendingPoints > 0 && current != null)
                    {
                        var parts = line.Split(',');
                        if (parts.Length >= 2 &&
                            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double e) &&
                            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
                        {
                            current.Add(new vec3(e, n, 0));
                        }
                        if (--pendingPoints == 0)
                        {
                            if (current.Count > 1) data.lines.Add(current);
                            current = null;
                        }
                        continue;
                    }

                    var kv = line.Split(',');
                    if (kv.Length == 2 && bool.TryParse(kv[1].Trim(), out bool flag))
                    {
                        switch (kv[0].Trim())
                        {
                            case "Active": data.active = flag; break;
                            case "Automatic": data.automatic = flag; break;
                            case "KPrimary": data.kPrimary = flag; break;
                            case "Combined": data.combined = flag; break;
                            case "AfterStop": data.afterStop = flag; break;
                        }
                        continue;
                    }

                    // a bare integer starts a new polyline with that many points
                    if (int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) && count > 0)
                    {
                        pendingPoints = count;
                        current = new List<vec3>(count);
                    }
                }
            }

            return data;
        }

        public static void Save(string fieldDirectory, CTurnLimitLines tnl)
        {
            var path = Path.Combine(fieldDirectory, "TurnLines.txt");

            using (var writer = new StreamWriter(path, false))
            {
                writer.WriteLine("$TurnLines");
                writer.WriteLine("Active," + tnl.isTurnActive);
                writer.WriteLine("Automatic," + tnl.isAutomatic);
                writer.WriteLine("KPrimary," + tnl.isKPrimary);
                writer.WriteLine("Combined," + tnl.isCombined);
                writer.WriteLine("AfterStop," + tnl.isAfterStop);

                foreach (var ln in tnl.lines)
                {
                    if (ln.pts.Count < 2) continue;
                    writer.WriteLine(ln.pts.Count.ToString(CultureInfo.InvariantCulture));
                    for (int i = 0; i < ln.pts.Count; i++)
                    {
                        writer.WriteLine(
                            FileIoUtils.FormatDouble(ln.pts[i].easting, 3) + "," +
                            FileIoUtils.FormatDouble(ln.pts[i].northing, 3));
                    }
                }
            }
        }
    }
}
