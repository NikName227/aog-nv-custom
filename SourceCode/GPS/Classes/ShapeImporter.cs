using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using AgOpenGPS.Core.Models;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace AgOpenGPS
{
    /// <summary>One field read from a shapefile: name suggestion, attributes, outer boundary (WGS84).</summary>
    public class ShapeFeatureInfo
    {
        public string SuggestedName;
        public List<KeyValuePair<string, string>> Attributes = new List<KeyValuePair<string, string>>();
        public List<Wgs84> Boundary = new List<Wgs84>();
    }

    /// <summary>
    /// Reads polygons + attributes from a (zipped) shapefile and reprojects them to WGS84.
    /// Croatian cadastre shapefiles are EPSG:3765 (HTRS96 / Croatia TM); geographic
    /// coordinates are passed through unchanged.
    /// </summary>
    public static class ShapeImporter
    {
        private const string Htrs96Wkt =
            "PROJCS[\"HTRS96 / Croatia TM\",GEOGCS[\"HTRS96\",DATUM[\"Croatian_Terrestrial_Reference_System\"," +
            "SPHEROID[\"GRS 1980\",6378137,298.257222101]],PRIMEM[\"Greenwich\",0]," +
            "UNIT[\"degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"]," +
            "PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"central_meridian\",16.5]," +
            "PARAMETER[\"scale_factor\",0.9999],PARAMETER[\"false_easting\",500000]," +
            "PARAMETER[\"false_northing\",0],UNIT[\"metre\",1]]";

        /// <summary>Attribute (DBF) column names from the last read, in file order.</summary>
        public static List<string> AttributeNames { get; private set; } = new List<string>();

        public static List<ShapeFeatureInfo> ReadFeatures(string path, out string error)
        {
            error = null;
            var result = new List<ShapeFeatureInfo>();
            string shpPath = path;
            string tempDir = null;

            try
            {
                if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                    Directory.CreateDirectory(tempDir);
                    ZipFile.ExtractToDirectory(path, tempDir);
                    shpPath = Directory.GetFiles(tempDir, "*.shp", SearchOption.AllDirectories).FirstOrDefault();
                    if (shpPath == null) { error = "No .shp file found in the ZIP."; return result; }
                }

                var transform = CreateTransform();
                var factory = new GeometryFactory();
                var encoding = GetEncoding(shpPath);

                using (var reader = new ShapefileDataReader(shpPath, factory, encoding))
                {
                    var header = reader.DbaseHeader;

                    AttributeNames = new List<string>();
                    for (int i = 0; i < header.NumFields; i++)
                        AttributeNames.Add(header.Fields[i].Name);

                    while (reader.Read())
                    {
                        var coords = ExtractExteriorRing(reader.Geometry);
                        if (coords == null || coords.Length < 3) continue;

                        var feat = new ShapeFeatureInfo();
                        string geomName = "", farmId = "";

                        for (int i = 0; i < header.NumFields; i++)
                        {
                            string name = header.Fields[i].Name;
                            string val = reader.GetValue(i)?.ToString() ?? "";
                            feat.Attributes.Add(new KeyValuePair<string, string>(name, val));

                            string up = name.ToUpperInvariant();
                            if (up.Contains("GEOMETRY_A")) geomName = val;
                            if (up == "FARM_ID") farmId = val;
                        }

                        feat.Boundary = ConvertRing(coords, transform);
                        feat.SuggestedName = MakeSafeName(BuildName(geomName, farmId));
                        result.Add(feat);
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            finally
            {
                if (tempDir != null) { try { Directory.Delete(tempDir, true); } catch { } }
            }

            return result;
        }

        private static Coordinate[] ExtractExteriorRing(Geometry geom)
        {
            if (geom is Polygon poly)
                return poly.ExteriorRing.Coordinates;

            if (geom is MultiPolygon multi && multi.NumGeometries > 0)
            {
                // largest part
                Polygon largest = null;
                double maxArea = -1;
                for (int i = 0; i < multi.NumGeometries; i++)
                {
                    var p = (Polygon)multi.GetGeometryN(i);
                    if (p.Area > maxArea) { maxArea = p.Area; largest = p; }
                }
                return largest?.ExteriorRing.Coordinates;
            }
            return null;
        }

        private static List<Wgs84> ConvertRing(Coordinate[] coords, ICoordinateTransformation transform)
        {
            var list = new List<Wgs84>(coords.Length);
            foreach (var c in coords)
            {
                double lon, lat;
                // Heuristic: large values = projected (EPSG:3765); small = already lon/lat.
                if (Math.Abs(c.X) > 1000 || Math.Abs(c.Y) > 1000)
                {
                    var (x, y) = transform.MathTransform.Transform(c.X, c.Y);
                    lon = x; lat = y;
                }
                else { lon = c.X; lat = c.Y; }

                list.Add(new Wgs84(lat, lon));
            }
            return list;
        }

        private static ICoordinateTransformation CreateTransform()
        {
            var csFactory = new CoordinateSystemFactory();
            var ctFactory = new CoordinateTransformationFactory();
            var source = csFactory.CreateFromWkt(Htrs96Wkt);
            var target = GeographicCoordinateSystem.WGS84;
            return ctFactory.CreateFromCoordinateSystems(source, target);
        }

        private static string BuildName(string geomName, string farmId)
        {
            string a = (geomName ?? "").Trim();
            string b = (farmId ?? "").Trim();
            // FARM_ID may come as "1399605.0"
            if (double.TryParse(b, NumberStyles.Any, CultureInfo.InvariantCulture, out double d) && d == Math.Floor(d))
                b = ((long)d).ToString(CultureInfo.InvariantCulture);

            string name = string.Join("_", new[] { a, b }.Where(s => !string.IsNullOrEmpty(s)));
            return string.IsNullOrWhiteSpace(name) ? "Field" : name;
        }

        private static string MakeSafeName(string name)
        {
            string result = name
                .Replace("č", "c").Replace("ć", "c").Replace("ž", "z").Replace("š", "s").Replace("đ", "d")
                .Replace("Č", "C").Replace("Ć", "C").Replace("Ž", "Z").Replace("Š", "S").Replace("Đ", "D");

            var sb = new StringBuilder();
            foreach (char ch in result)
                sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');

            string clean = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "_+", "_").Trim('_');
            if (clean.Length > 50) clean = clean.Substring(0, 50);
            return string.IsNullOrEmpty(clean) ? "Field" : clean;
        }

        private static Encoding GetEncoding(string shpFile)
        {
            Encoding fallback = Encoding.GetEncoding(1250); // common for HR/EU
            string cpg = Path.ChangeExtension(shpFile, ".cpg");
            if (!File.Exists(cpg)) return fallback;

            try
            {
                string code = File.ReadAllText(cpg).Trim().ToUpperInvariant();
                if (code == "UTF-8" || code == "UTF8" || code == "65001") return Encoding.UTF8;
                if (code == "1250") return Encoding.GetEncoding(1250);
                if (code == "ISO-8859-2") return Encoding.GetEncoding("iso-8859-2");
                return Encoding.GetEncoding(code);
            }
            catch { return fallback; }
        }
    }
}
