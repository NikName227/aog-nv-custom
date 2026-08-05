using AgLibrary.Logging;
using AgOpenGPS.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

namespace AgOpenGPS
{
    // Feature 50 — Orchard.kml (SIM 0730). The orchard as a file anyone can open: the field
    // outline it lives in, the headland, the rows, and every tree. Written next to the field
    // when the field is closed (which is also what happens when the program is shut down), in
    // the same place the stock Field.kml is written.
    //
    // Layers are separate folders so Google Earth can switch them on and off one by one, and the
    // rows carry their CONTROL points rather than the dense curve - that way the file imports
    // back into the planner as the same rows instead of hundreds of points.
    public partial class FormGPS
    {
        public void ExportOrchardAs_KML()
        {
            if (orchRows.Count == 0) return;

            string dir = GetFieldDir(true);
            if (string.IsNullOrEmpty(dir)) return;

            using (var kml = new XmlTextWriter(Path.Combine(dir, "Orchard.kml"), Encoding.UTF8)
            {
                Formatting = Formatting.Indented,
                Indentation = 3
            })
            {
                kml.WriteStartDocument();
                kml.WriteStartElement("kml", "http://www.opengis.net/kml/2.2");
                kml.WriteStartElement("Document");
                kml.WriteElementString("name", currentFieldDirectory + " - orchard");

                WriteBoundaryFolder(kml);
                WriteHeadlandFolder(kml);
                WriteRowsFolder(kml);
                WriteTreesFolder(kml);

                kml.WriteEndElement(); // Document
                kml.WriteEndElement(); // kml
                kml.WriteEndDocument();
                kml.Flush();
            }
        }

        // ── layers ────────────────────────────────────────────────────────
        private void WriteBoundaryFolder(XmlTextWriter kml)
        {
            if (bnd?.bndList == null || bnd.bndList.Count == 0) return;

            kml.WriteStartElement("Folder");
            kml.WriteElementString("name", "Boundary");

            for (int i = 0; i < bnd.bndList.Count; i++)
            {
                var ring = bnd.bndList[i].fenceLine;
                if (ring == null || ring.Count < 3) continue;

                //the outer fence first, the rest are islands
                WriteLine(kml, i == 0 ? "Boundary" : "Island " + i, ring, true, "ff00ffff");
            }
            kml.WriteEndElement();
        }

        private void WriteHeadlandFolder(XmlTextWriter kml)
        {
            if (bnd?.bndList == null) return;

            bool any = false;
            for (int i = 0; i < bnd.bndList.Count; i++)
                if (bnd.bndList[i].hdLine != null && bnd.bndList[i].hdLine.Count > 2) { any = true; break; }
            if (!any) return;

            kml.WriteStartElement("Folder");
            kml.WriteElementString("name", "Headland");

            for (int i = 0; i < bnd.bndList.Count; i++)
            {
                var hd = bnd.bndList[i].hdLine;
                if (hd == null || hd.Count < 3) continue;
                WriteLine(kml, i == 0 ? "Headland" : "Headland " + i, hd, true, "ff00aaff");
            }
            kml.WriteEndElement();
        }

        private void WriteRowsFolder(XmlTextWriter kml)
        {
            kml.WriteStartElement("Folder");
            kml.WriteElementString("name", "Rows");

            for (int i = 0; i < orchRows.Count; i++)
            {
                var r = orchRows[i];
                if (r.pts.Count < 2) continue;

                var pts = new List<vec3>(r.pts.Count);
                foreach (var p in r.pts) pts.Add(new vec3(p.easting, p.northing, 0));

                WriteLine(kml, string.IsNullOrEmpty(r.name) ? "Row " + (i + 1) : r.name,
                    pts, false, "ff00ff00");
            }
            kml.WriteEndElement();
        }

        private void WriteTreesFolder(XmlTextWriter kml)
        {
            int total = 0;
            for (int i = 0; i < orchRows.Count; i++) total += orchRows[i].trees.Count;
            if (total == 0) return;

            kml.WriteStartElement("Folder");
            kml.WriteElementString("name", "Trees");

            for (int i = 0; i < orchRows.Count; i++)
            {
                var r = orchRows[i];
                if (r.trees.Count == 0) continue;

                //one sub-folder per row, so a row's trees can be looked at on their own
                kml.WriteStartElement("Folder");
                kml.WriteElementString("name", string.IsNullOrEmpty(r.name) ? "Row " + (i + 1) : r.name);

                for (int t = 0; t < r.trees.Count; t++)
                {
                    Wgs84 w = ToWgs(r.trees[t]);

                    kml.WriteStartElement("Placemark");
                    kml.WriteElementString("name", (t + 1).ToString(CultureInfo.InvariantCulture));
                    kml.WriteStartElement("Point");
                    kml.WriteElementString("coordinates", Coord(w));
                    kml.WriteEndElement();
                    kml.WriteEndElement();
                }
                kml.WriteEndElement();
            }
            kml.WriteEndElement();
        }

        // ── helpers ───────────────────────────────────────────────────────
        private void WriteLine(XmlTextWriter kml, string name, List<vec3> pts, bool close, string abgr)
        {
            var sb = new StringBuilder();
            foreach (var p in pts) sb.Append(Coord(ToWgs(new vec2(p.easting, p.northing)))).Append(' ');
            if (close && pts.Count > 2) sb.Append(Coord(ToWgs(new vec2(pts[0].easting, pts[0].northing))));

            kml.WriteStartElement("Placemark");
            kml.WriteElementString("name", name);

            kml.WriteStartElement("Style");
            kml.WriteStartElement("LineStyle");
            kml.WriteElementString("color", abgr);
            kml.WriteElementString("width", "2");
            kml.WriteEndElement();
            kml.WriteEndElement();

            kml.WriteStartElement("LineString");
            kml.WriteElementString("tessellate", "1");
            kml.WriteElementString("coordinates", sb.ToString().Trim());
            kml.WriteEndElement();

            kml.WriteEndElement(); // Placemark
        }

        private Wgs84 ToWgs(vec2 p)
            => AppModel.LocalPlane.ConvertGeoCoordToWgs84(new GeoCoord(p.northing, p.easting));

        private static string Coord(Wgs84 w)
            => w.Longitude.ToString("N7", CultureInfo.InvariantCulture) + ","
             + w.Latitude.ToString("N7", CultureInfo.InvariantCulture) + ",0";
    }
}
