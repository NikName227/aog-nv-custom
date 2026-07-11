using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using AgOpenGPS.Core.Models;
using GMap.NET;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace AgOpenGPS
{
    /// <summary>One cadastral parcel: outer ring as WGS84 points (for GMap display)
    /// plus the original EPSG:3765 polygon geometry (for hit-testing and union).</summary>
    public class CadastralParcel
    {
        public List<PointLatLng> Outer = new List<PointLatLng>();

        /// <summary>Original parcel polygon in EPSG:3765 (metric) — used for point-in-polygon
        /// selection and NetTopologySuite union when building the boundary.</summary>
        public Polygon Geom3765;

        /// <summary>Selection state (toggled by clicking on the map).</summary>
        public bool Selected;

        /// <summary>Cadastral feature id (from the WFS) — dedups parcels that appear
        /// in more than one cache tile. "" when the WFS gave none.</summary>
        public string Id = "";

        /// <summary>Id, falling back to the first outline point for old data without one.</summary>
        public string DedupKey()
        {
            if (Id.Length > 0) return Id;
            if (Outer.Count == 0) return "";
            return Outer[0].Lat.ToString("F6", CultureInfo.InvariantCulture) + ","
                 + Outer[0].Lng.ToString("F6", CultureInfo.InvariantCulture) + "," + Outer.Count;
        }
    }

    /// <summary>
    /// Fetches HR cadastral parcels (CP:CadastralParcel) from the Uredjena zemlja WFS
    /// as GeoJSON in EPSG:3765 and reprojects them to WGS84 for display on a GMap.
    /// </summary>
    public static class CadastralWfsClient
    {
        public const string WfsUrl = "https://api.uredjenazemlja.hr/services/inspire/cp/wfs";

        private const string Htrs96Wkt =
            "PROJCS[\"HTRS96 / Croatia TM\",GEOGCS[\"HTRS96\",DATUM[\"Croatian_Terrestrial_Reference_System\"," +
            "SPHEROID[\"GRS 1980\",6378137,298.257222101]],PRIMEM[\"Greenwich\",0]," +
            "UNIT[\"degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"]," +
            "PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"central_meridian\",16.5]," +
            "PARAMETER[\"scale_factor\",0.9999],PARAMETER[\"false_easting\",500000]," +
            "PARAMETER[\"false_northing\",0],UNIT[\"metre\",1]]";

        private static readonly ICoordinateTransformation ToWgs84; // 3765 -> WGS84
        private static readonly ICoordinateTransformation To3765;  // WGS84 -> 3765

        static CadastralWfsClient()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }

            var csf = new CoordinateSystemFactory();
            var ctf = new CoordinateTransformationFactory();
            var htrs = csf.CreateFromWkt(Htrs96Wkt);
            var wgs = GeographicCoordinateSystem.WGS84;
            ToWgs84 = ctf.CreateFromCoordinateSystems(htrs, wgs);
            To3765 = ctf.CreateFromCoordinateSystems(wgs, htrs);
        }

        /// <summary>Fetch parcels intersecting a WGS84 bbox. Blocking — call off the UI thread.</summary>
        public static List<CadastralParcel> GetParcels(
            double minLat, double minLon, double maxLat, double maxLon, out string error)
        {
            return GetParcels(WfsUrl, minLat, minLon, maxLat, maxLon, out error);
        }

        public static List<CadastralParcel> GetParcels(
            string wfsUrl, double minLat, double minLon, double maxLat, double maxLon, out string error)
        {
            var (e1, n1) = To3765.MathTransform.Transform(minLon, minLat);
            var (e2, n2) = To3765.MathTransform.Transform(maxLon, maxLat);
            return GetParcelsBbox3765(wfsUrl,
                Math.Min(e1, e2), Math.Min(n1, n2), Math.Max(e1, e2), Math.Max(n1, n2), out error);
        }

        /// <summary>Fetch parcels intersecting an EPSG:3765 bbox (the parcel-cache tiles
        /// live in this grid). Blocking — call off the UI thread.</summary>
        public static List<CadastralParcel> GetParcelsBbox3765(
            string wfsUrl, double minE, double minN, double maxE, double maxN, out string error)
        {
            error = null;
            var parcels = new List<CadastralParcel>();
            if (string.IsNullOrWhiteSpace(wfsUrl)) wfsUrl = WfsUrl;
            wfsUrl = wfsUrl.Trim().TrimEnd('?', '&');
            try
            {
                string url = wfsUrl +
                    "?SERVICE=WFS&VERSION=2.0.0&REQUEST=GetFeature&TYPENAMES=CP:CadastralParcel" +
                    "&SRSNAME=EPSG:3765&OUTPUTFORMAT=application/json&COUNT=800&BBOX=" +
                    F(minE) + "," + F(minN) + "," + F(maxE) + "," + F(maxN) + ",EPSG:3765";

                string json;
                using (var wc = new WebClient())
                {
                    wc.Encoding = System.Text.Encoding.UTF8;
                    json = wc.DownloadString(url);
                }

                var fc = new GeoJsonReader().Read<FeatureCollection>(json);
                foreach (var feat in fc)
                    AddPolygons(feat.Geometry, parcels, FeatureId(feat));
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            return parcels;
        }

        // cadastral id of a WFS feature (for tile dedup); "" when absent
        private static string FeatureId(IFeature feat)
        {
            var at = feat?.Attributes;
            if (at == null) return "";
            foreach (string key in new[] { "inspireId", "INSPIREID", "id", "ID", "label", "LABEL", "gml_id" })
            {
                if (at.Exists(key))
                {
                    object v = at[key];
                    if (v != null) return v.ToString();
                }
            }
            return "";
        }

        private static void AddPolygons(Geometry geom, List<CadastralParcel> parcels, string id)
        {
            if (geom is Polygon poly)
            {
                parcels.Add(ToParcel(poly, id));
            }
            else if (geom is MultiPolygon mp)
            {
                for (int i = 0; i < mp.NumGeometries; i++)
                    parcels.Add(ToParcel((Polygon)mp.GetGeometryN(i),
                        id.Length > 0 ? id + "#" + i : ""));
            }
        }

        private static CadastralParcel ToParcel(Polygon poly, string id)
        {
            // poly is in EPSG:3765 (GeoJSON SRSNAME=EPSG:3765) — keep it for hit-test/union.
            var p = new CadastralParcel { Geom3765 = poly, Id = id };
            foreach (var c in poly.ExteriorRing.Coordinates)
            {
                var (lon, lat) = ToWgs84.MathTransform.Transform(c.X, c.Y);
                p.Outer.Add(new PointLatLng(lat, lon));
            }
            return p;
        }

        /// <summary>WGS84 → EPSG:3765 (the parcel-cache tile grid lives in 3765 meters).</summary>
        public static (double easting, double northing) ToEN3765(double lat, double lon)
        {
            return To3765.MathTransform.Transform(lon, lat);
        }

        /// <summary>True if a WGS84 point falls inside the parcel (tested in EPSG:3765).</summary>
        public static bool Contains(CadastralParcel parcel, double lat, double lon)
        {
            if (parcel?.Geom3765 == null) return false;
            var (e, n) = To3765.MathTransform.Transform(lon, lat);
            return parcel.Geom3765.Contains(GeomFactory.CreatePoint(new Coordinate(e, n)));
        }

        /// <summary>Reproject an EPSG:3765 coordinate to WGS84 (lat/lon). For union output rings.</summary>
        public static PointLatLng To84(double easting, double northing)
        {
            var (lon, lat) = ToWgs84.MathTransform.Transform(easting, northing);
            return new PointLatLng(lat, lon);
        }

        internal static readonly GeometryFactory GeomFactory = new GeometryFactory();

        /// <summary>
        /// Union the selected parcels (EPSG:3765) and return the outer ring of the largest
        /// resulting polygon as WGS84 points (for the boundary). Returns null if nothing usable.
        /// </summary>
        public static List<Wgs84> UnionOuterRingWgs84(IEnumerable<CadastralParcel> parcels)
        {
            return UnionOuterRingWgs84(parcels, null);
        }

        /// <summary>
        /// Union the selected parcels plus optional bridge polygons (all EPSG:3765) and return
        /// the outer ring of the largest resulting polygon as WGS84. Bridges connect parcels
        /// that aren't adjacent (a path on the map but not in the field) into one polygon.
        /// </summary>
        public static List<Wgs84> UnionOuterRingWgs84(
            IEnumerable<CadastralParcel> parcels, IEnumerable<Polygon> bridges3765)
        {
            Geometry acc = null;
            foreach (var p in parcels)
            {
                if (p?.Geom3765 == null) continue;
                acc = acc == null ? (Geometry)p.Geom3765 : acc.Union(p.Geom3765);
            }
            if (bridges3765 != null)
            {
                foreach (var b in bridges3765)
                {
                    if (b == null) continue;
                    acc = acc == null ? (Geometry)b : acc.Union(b);
                }
            }
            if (acc == null) return null;

            Polygon largest = null;
            if (acc is Polygon pg)
            {
                largest = pg;
            }
            else if (acc is MultiPolygon mp)
            {
                for (int i = 0; i < mp.NumGeometries; i++)
                {
                    var g = (Polygon)mp.GetGeometryN(i);
                    if (largest == null || g.Area > largest.Area) largest = g;
                }
            }
            if (largest == null) return null;

            var ring = new List<Wgs84>();
            foreach (var c in largest.ExteriorRing.Coordinates)
            {
                var (lon, lat) = ToWgs84.MathTransform.Transform(c.X, c.Y);
                ring.Add(new Wgs84(lat, lon));
            }
            return ring;
        }

        /// <summary>
        /// Build an EPSG:3765 polygon from a quad of WGS84 corners (a bridge rectangle the user
        /// dragged into place). Used to connect non-adjacent parcels into one polygon. If the
        /// quad is self-intersecting it is repaired (Buffer(0) / convex hull). Null if invalid.
        /// </summary>
        public static Polygon MakeQuad3765(IList<PointLatLng> cornersWgs)
        {
            if (cornersWgs == null || cornersWgs.Count < 3) return null;

            var cs = new List<Coordinate>(cornersWgs.Count + 1);
            foreach (var p in cornersWgs)
            {
                var (e, n) = To3765.MathTransform.Transform(p.Lng, p.Lat);
                cs.Add(new Coordinate(e, n));
            }
            cs.Add(new Coordinate(cs[0].X, cs[0].Y)); // close the ring

            Polygon poly;
            try { poly = GeomFactory.CreatePolygon(cs.ToArray()); }
            catch { return null; }

            if (!poly.IsValid)
            {
                Geometry g = poly.Buffer(0);
                if (g is Polygon pg && !pg.IsEmpty) poly = pg;
                else poly = poly.ConvexHull() as Polygon;
            }
            return poly;
        }

        /// <summary>Exterior ring of an EPSG:3765 polygon as WGS84 points (for map drawing).</summary>
        public static List<PointLatLng> Ring84(Polygon poly3765)
        {
            var pts = new List<PointLatLng>();
            foreach (var c in poly3765.ExteriorRing.Coordinates)
            {
                var (lon, lat) = ToWgs84.MathTransform.Transform(c.X, c.Y);
                pts.Add(new PointLatLng(lat, lon));
            }
            return pts;
        }

        private static string F(double v) => v.ToString("F2", CultureInfo.InvariantCulture);

        // ── Shared parcel-cache tile files (field test 2026-07-10) ──────────
        // One file per 1x1 km EPSG:3765 tile in the shared ParcelCache folder;
        // same line format as Parcels.txt plus a parcel-id line for dedup at
        // tile borders. See CParcelCache for the tiling/threading logic.

        /// <summary>Write one cache tile (parcel id + WGS84 outer ring each).</summary>
        public static bool SaveTile(string filePath, IEnumerable<CadastralParcel> parcels, out string error)
        {
            error = null;
            try
            {
                var list = new List<CadastralParcel>();
                foreach (var p in parcels)
                    if (p != null && p.Outer.Count >= 3) list.Add(p);

                using (var sw = new System.IO.StreamWriter(filePath, false))
                {
                    sw.WriteLine("$ParcelTile");
                    sw.WriteLine(list.Count.ToString(CultureInfo.InvariantCulture));
                    foreach (var p in list)
                    {
                        sw.WriteLine(p.Id);
                        sw.WriteLine(p.Outer.Count.ToString(CultureInfo.InvariantCulture));
                        foreach (var pt in p.Outer)
                            sw.WriteLine(pt.Lat.ToString("F8", CultureInfo.InvariantCulture) + "," +
                                         pt.Lng.ToString("F8", CultureInfo.InvariantCulture));
                    }
                }
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        /// <summary>Read one cache tile; rebuilds outlines + EPSG:3765 geometry. Null on error.</summary>
        public static List<CadastralParcel> LoadTile(string filePath, out string error)
        {
            error = null;
            var parcels = new List<CadastralParcel>();
            try
            {
                using (var sr = new System.IO.StreamReader(filePath))
                {
                    sr.ReadLine(); // header $ParcelTile
                    int count = int.Parse(sr.ReadLine(), CultureInfo.InvariantCulture);
                    for (int k = 0; k < count; k++)
                    {
                        var p = new CadastralParcel { Id = sr.ReadLine() ?? "" };
                        int n = int.Parse(sr.ReadLine(), CultureInfo.InvariantCulture);
                        var cs = new List<Coordinate>(n + 1);
                        for (int i = 0; i < n; i++)
                        {
                            var parts = sr.ReadLine().Split(',');
                            double lat = double.Parse(parts[0], CultureInfo.InvariantCulture);
                            double lon = double.Parse(parts[1], CultureInfo.InvariantCulture);
                            p.Outer.Add(new PointLatLng(lat, lon));
                            var (e, nn) = To3765.MathTransform.Transform(lon, lat);
                            cs.Add(new Coordinate(e, nn));
                        }
                        RebuildGeom(p, cs);
                        parcels.Add(p);
                    }
                }
                return parcels;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        // rebuild the EPSG:3765 polygon of a parcel read back from disk
        private static void RebuildGeom(CadastralParcel p, List<Coordinate> cs)
        {
            if (cs.Count < 3) return;
            cs.Add(new Coordinate(cs[0].X, cs[0].Y)); // close ring
            try
            {
                var poly = GeomFactory.CreatePolygon(cs.ToArray());
                if (!poly.IsValid)
                {
                    Geometry g = poly.Buffer(0);
                    if (g is Polygon pg && !pg.IsEmpty) poly = pg;
                }
                p.Geom3765 = poly;
            }
            catch { }
        }

        // ── Per-field parcel cache (WGS84 outlines) ─────────────────────────
        // Parcels are saved next to the field so turning them ON later loads from
        // disk (instant) instead of re-fetching from the WFS over the internet.

        /// <summary>Write parcels (WGS84 outer rings) to a cache file in the field folder.</summary>
        public static bool SaveParcels(string filePath, IEnumerable<CadastralParcel> parcels, out string error)
        {
            error = null;
            try
            {
                var list = new List<CadastralParcel>();
                foreach (var p in parcels)
                    if (p != null && p.Outer.Count >= 3) list.Add(p);

                using (var sw = new System.IO.StreamWriter(filePath, false))
                {
                    sw.WriteLine("$Parcels");
                    sw.WriteLine(list.Count.ToString(CultureInfo.InvariantCulture));
                    foreach (var p in list)
                    {
                        sw.WriteLine(p.Outer.Count.ToString(CultureInfo.InvariantCulture));
                        foreach (var pt in p.Outer)
                            sw.WriteLine(pt.Lat.ToString("F8", CultureInfo.InvariantCulture) + "," +
                                         pt.Lng.ToString("F8", CultureInfo.InvariantCulture));
                    }
                }
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        /// <summary>Read parcels from the cache file. Rebuilds the WGS84 outline and the
        /// EPSG:3765 geometry (so hit-test/union still work). Returns null on error.</summary>
        public static List<CadastralParcel> LoadParcels(string filePath, out string error)
        {
            error = null;
            var parcels = new List<CadastralParcel>();
            try
            {
                using (var sr = new System.IO.StreamReader(filePath))
                {
                    sr.ReadLine(); // header $Parcels
                    int count = int.Parse(sr.ReadLine(), CultureInfo.InvariantCulture);
                    for (int k = 0; k < count; k++)
                    {
                        int n = int.Parse(sr.ReadLine(), CultureInfo.InvariantCulture);
                        var p = new CadastralParcel();
                        var cs = new List<Coordinate>(n + 1);
                        for (int i = 0; i < n; i++)
                        {
                            var parts = sr.ReadLine().Split(',');
                            double lat = double.Parse(parts[0], CultureInfo.InvariantCulture);
                            double lon = double.Parse(parts[1], CultureInfo.InvariantCulture);
                            p.Outer.Add(new PointLatLng(lat, lon));
                            var (e, nn) = To3765.MathTransform.Transform(lon, lat);
                            cs.Add(new Coordinate(e, nn));
                        }
                        if (cs.Count >= 3)
                        {
                            cs.Add(new Coordinate(cs[0].X, cs[0].Y)); // close ring
                            try
                            {
                                var poly = GeomFactory.CreatePolygon(cs.ToArray());
                                if (!poly.IsValid)
                                {
                                    Geometry g = poly.Buffer(0);
                                    if (g is Polygon pg && !pg.IsEmpty) poly = pg;
                                }
                                p.Geom3765 = poly;
                            }
                            catch { }
                        }
                        parcels.Add(p);
                    }
                }
                return parcels;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }
    }
}
