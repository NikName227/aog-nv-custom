using System;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.Projections;

namespace AgOpenGPS
{
    /// <summary>
    /// Custom GMap.NET provider that fetches imagery from a WMS GetMap endpoint
    /// (default: Croatian DGU Geoportal orthophoto). Each map tile's geographic
    /// bounds are requested in CRS:84 (lon,lat) so no projection library is needed.
    /// The endpoint URL is configurable at runtime (App Map setting), because the
    /// Geoportal orthophoto URL changes every year.
    /// </summary>
    public class GeoportalWmsProvider : GMapProvider
    {
        public static readonly GeoportalWmsProvider Instance = new GeoportalWmsProvider();

        // WMS layer / version are INSPIRE-standard and stay constant; only the base URL changes.
        private const string WmsLayer = "OI.OrthoimageCoverage";

        // Default base endpoint (year is part of the path -> editable in settings).
        public const string DefaultBaseUrl =
            "https://geoportal.dgu.hr/services/inspire/orthophoto_2025_2026/wms";

        // Full GetMap template. {0}=minLon {1}=minLat {2}=maxLon {3}=maxLat
        private string urlFormat;

        // Cache identity (Id + DbId) is derived from the base URL in SetBaseUrl, so a different
        // endpoint/year uses a fresh tile-cache bucket instead of serving stale cached tiles.
        // Must be a valid unique Guid before the base ctor runs (it rejects duplicate/empty ids);
        // the field initializer provides that, then SetBaseUrl overrides it from the URL.
        private Guid id = new Guid("7B5E9C10-1D2F-4A6B-9C3E-0F1A2B3C4D5E");

        // GMapProvider.DbId is a readonly base field but is what GMap keys the tile cache by;
        // set it via reflection so the cache bucket follows the URL.
        private static readonly FieldInfo DbIdField =
            typeof(GMapProvider).GetField("DbId", BindingFlags.Public | BindingFlags.Instance);

        private GeoportalWmsProvider()
        {
            SetBaseUrl(DefaultBaseUrl);
            RefererUrl = "https://geoportal.dgu.hr/";
            Copyright = "© DGU Geoportal";
        }

        /// <summary>Set the WMS base endpoint (without query string).</summary>
        public void SetBaseUrl(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                baseUrl = DefaultBaseUrl;

            baseUrl = baseUrl.Trim().TrimEnd('?', '&');

            urlFormat = baseUrl +
                "?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap" +
                "&LAYERS=" + WmsLayer +
                "&STYLES=&CRS=CRS:84" +
                "&BBOX={0},{1},{2},{3}" +
                "&WIDTH=256&HEIGHT=256&FORMAT=image/jpeg";

            // Tie the tile-cache identity to the URL. GMap keys cached tiles (memory + disk)
            // by DbId, so without this a new URL (e.g. next year's orthophoto) would keep
            // serving the previous year's cached tiles.
            byte[] hash;
            using (var md5 = MD5.Create())
                hash = md5.ComputeHash(Encoding.UTF8.GetBytes(baseUrl));
            id = new Guid(hash);
            int h = BitConverter.ToInt32(hash, 0);
            DbIdField?.SetValue(this, h == int.MinValue ? 1 : Math.Abs(h));
        }

        #region GMapProvider members

        public override Guid Id => id;

        public override string Name => "Geoportal WMS";

        public override PureProjection Projection => MercatorProjection.Instance;

        private GMapProvider[] overlays;
        public override GMapProvider[] Overlays =>
            overlays ?? (overlays = new GMapProvider[] { this });

        public override PureImage GetTileImage(GPoint pos, int zoom)
        {
            string url = MakeTileImageUrl(pos, zoom);
            return GetTileImageUsingHttp(url);
        }

        #endregion

        private string MakeTileImageUrl(GPoint pos, int zoom)
        {
            // Pixel bounds of this tile, then convert the corners to lat/lon.
            long pxLeft = pos.X * Projection.TileSize.Width;
            long pxTop = pos.Y * Projection.TileSize.Height;
            long pxRight = pxLeft + Projection.TileSize.Width;
            long pxBottom = pxTop + Projection.TileSize.Height;

            PointLatLng topLeft = Projection.FromPixelToLatLng(pxLeft, pxTop, zoom);
            PointLatLng bottomRight = Projection.FromPixelToLatLng(pxRight, pxBottom, zoom);

            double minLon = topLeft.Lng;
            double maxLat = topLeft.Lat;
            double maxLon = bottomRight.Lng;
            double minLat = bottomRight.Lat;

            return string.Format(CultureInfo.InvariantCulture, urlFormat,
                minLon, minLat, maxLon, maxLat);
        }
    }
}
