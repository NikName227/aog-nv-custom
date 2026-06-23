using AgOpenGPS.Core.Models;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace AgOpenGPS
{
    /// <summary>
    /// Writes a boundary ring (WGS84) to a simple single-polygon KML file. Shared by the
    /// Shape File import and the Geoportal boundary tool so the proven AOG KML field
    /// pipeline (FormFieldKML) can build/replace a field boundary from one common artifact.
    /// </summary>
    public static class BoundaryKmlWriter
    {
        /// <summary>Write the ring to a temp .kml and return its path. Caller deletes it.</summary>
        public static string WriteTempKml(string name, IEnumerable<Wgs84> ring)
        {
            string path = Path.Combine(Path.GetTempPath(), System.Guid.NewGuid() + ".kml");

            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.Append("<kml xmlns=\"http://www.opengis.net/kml/2.2\">");
            sb.Append("<Placemark><name>" + name + "</name>");
            sb.Append("<Polygon><outerBoundaryIs><LinearRing><coordinates>");
            foreach (var p in ring)
            {
                sb.Append(p.Longitude.ToString("F8", CultureInfo.InvariantCulture));
                sb.Append(",");
                sb.Append(p.Latitude.ToString("F8", CultureInfo.InvariantCulture));
                sb.Append(",0 ");
            }
            sb.Append("</coordinates></LinearRing></outerBoundaryIs></Polygon></Placemark></kml>");

            File.WriteAllText(path, sb.ToString());
            return path;
        }
    }
}
