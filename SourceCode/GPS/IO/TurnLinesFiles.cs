// TurnLinesFiles.cs - per-field turn limit lines (TurnLines.txt).
// A list of polylines (local plane easting,northing points) + per-implement offsets.
// REQ 2026-10-11-1: the old Active/Automatic/AfterStop flags moved to the app settings;
// they are still in older files and simply skipped on read.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace AgOpenGPS.IO
{
    public class TurnLinesData
    {
        public List<List<vec3>> lines = new List<List<vec3>>();
        // per line (same index as lines): implement profile -> pivot offset (m)
        public List<Dictionary<string, double>> offsets = new List<Dictionary<string, double>>();
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
                            if (current.Count > 1)
                            {
                                data.lines.Add(current);
                                data.offsets.Add(new Dictionary<string, double>());
                            }
                            current = null;
                        }
                        continue;
                    }

                    // Offset,<line index>,<metres>,<implement profile> - the profile name goes
                    // last so a comma inside it cannot shift the numbers
                    if (line.StartsWith("Offset,", StringComparison.Ordinal))
                    {
                        var op = line.Split(new[] { ',' }, 4);
                        if (op.Length == 4
                            && int.TryParse(op[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int li)
                            && double.TryParse(op[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double off)
                            && li >= 0 && li < data.offsets.Count)
                        {
                            data.offsets[li][op[3]] = off;
                        }
                        continue;
                    }

                    //old header flags (Active,True ...) - now app settings, skipped
                    var kv = line.Split(',');
                    if (kv.Length == 2 && bool.TryParse(kv[1].Trim(), out _)) continue;

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

                //offsets are written after ALL the lines: on read a line has to exist before
                //its offset can attach to it
                var written = new List<CTurnLimitLine>();
                foreach (var ln in tnl.lines)
                {
                    if (ln.pts.Count < 2) continue;
                    written.Add(ln);
                    writer.WriteLine(ln.pts.Count.ToString(CultureInfo.InvariantCulture));
                    for (int i = 0; i < ln.pts.Count; i++)
                    {
                        writer.WriteLine(
                            FileIoUtils.FormatDouble(ln.pts[i].easting, 3) + "," +
                            FileIoUtils.FormatDouble(ln.pts[i].northing, 3));
                    }
                }

                for (int li = 0; li < written.Count; li++)
                {
                    foreach (var kv in written[li].offsets)
                    {
                        if (kv.Value == 0) continue;
                        writer.WriteLine("Offset," + li.ToString(CultureInfo.InvariantCulture) + ","
                            + FileIoUtils.FormatDouble(kv.Value, 3) + "," + kv.Key);
                    }
                }
            }
        }
    }
}
