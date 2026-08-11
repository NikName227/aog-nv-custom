using AgLibrary.Logging;
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsForms;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Net;

namespace AgOpenGPS
{
    /// <summary>
    /// Captures the field background straight from the imagery source for a GIVEN geographic
    /// box, instead of screenshotting the preview control (2026-08-10).
    ///
    /// The old way (gMapControl.DrawToBitmap) tied the saved background to the window: 692x388
    /// pixels of whatever happened to be on screen, centred on the vehicle. Two things were
    /// wrong with that - standing at the edge of a long field cut the far end off the image,
    /// and the imagery was thrown away at ~0.27 MP no matter how sharp the source was.
    ///
    /// Here the caller says WHICH box (the field boundary plus a margin) and HOW MANY pixels,
    /// and this class fetches exactly that:
    ///   - Custom/DGU: one WMS GetMap per chunk, at any size we ask for (no tile grid at all)
    ///   - Google/Bing: the provider's tiles for the finest zoom that fits, stitched and cropped
    /// </summary>
    public static class CMapCapture
    {
        // Long side of the captured image. Low is the everyday setting; High is for when the
        // field is mapped once and wants to stay sharp (see GL_MAX_TEXTURE_SIZE guard in
        // Texture2D - a GPU that cannot take 4096 gets a scaled-down texture, not a black one).
        public const int LowPx = 1536;
        public const int HighPx = 4096;
        public const long LowJpegQuality = 75L;
        public const long HighJpegQuality = 88L;

        // A single WMS request stays modest; servers commonly cap width/height around 4096.
        private const int WmsChunkPx = 2048;
        private const int HttpTimeoutMs = 25000;

        // Ceiling on one capture's tile downloads. A High capture that has to go one zoom
        // deeper than the budget can ask for a four-figure tile count; beyond this we settle
        // for the coarser zoom rather than sit on the network for minutes.
        private const int MaxTiles = 1200;

        // Imagery kept around the field on every side. Fixed metres, not a percentage: on a
        // 500 m field 15% would have spent a quarter of the pixels on the neighbour's land.
        public const double BoundaryMargin = 50.0;

        // Hard limit on the captured area - measured on the BOX, not on its distance from the
        // field origin (the old check used absolute coordinates and could refuse a small field
        // that simply sat far from the origin).
        public const double MaxBoxMeters = 4000.0;

        public static int TargetPx(bool high) { return high ? HighPx : LowPx; }
        public static long JpegQuality(bool high) { return high ? HighJpegQuality : LowJpegQuality; }

        public static bool HasBoundary(FormGPS mf)
        {
            return mf != null && mf.bnd != null && mf.bnd.bndList.Count > 0
                && mf.bnd.bndList[0].fenceLine.Count > 2;
        }

        /// <summary>Field bbox + BoundaryMargin as a lat/lon box. False when there is no
        /// boundary to frame by, or the field is bigger than MaxBoxMeters.</summary>
        public static bool FieldLatLonBox(FormGPS mf, out double minLat, out double minLon,
            out double maxLat, out double maxLon, out bool tooLarge)
        {
            minLat = minLon = maxLat = maxLon = 0;
            tooLarge = false;
            if (!HasBoundary(mf)) return false;

            double minE = double.MaxValue, maxE = double.MinValue;
            double minN = double.MaxValue, maxN = double.MinValue;

            foreach (var bnd in mf.bnd.bndList)
            {
                if (bnd.fenceLine.Count < 3) continue;
                foreach (vec3 p in bnd.fenceLine)
                {
                    if (p.easting < minE) minE = p.easting;
                    if (p.easting > maxE) maxE = p.easting;
                    if (p.northing < minN) minN = p.northing;
                    if (p.northing > maxN) maxN = p.northing;
                }
            }
            if (minE > maxE) return false;

            minE -= BoundaryMargin; maxE += BoundaryMargin;
            minN -= BoundaryMargin; maxN += BoundaryMargin;

            if (maxE - minE > MaxBoxMeters || maxN - minN > MaxBoxMeters)
            {
                tooLarge = true;
                return false;
            }

            //all four corners: the local plane and lat/lon are not exactly parallel
            minLat = minLon = double.MaxValue;
            maxLat = maxLon = double.MinValue;
            for (int i = 0; i < 4; i++)
            {
                double e = (i == 0 || i == 3) ? minE : maxE;
                double n = (i < 2) ? minN : maxN;
                var w = mf.AppModel.LocalPlane.ConvertGeoCoordToWgs84(
                    new AgOpenGPS.Core.Models.GeoCoord(n, e));
                if (w.Latitude < minLat) minLat = w.Latitude;
                if (w.Latitude > maxLat) maxLat = w.Latitude;
                if (w.Longitude < minLon) minLon = w.Longitude;
                if (w.Longitude > maxLon) maxLon = w.Longitude;
            }
            return maxLat > minLat && maxLon > minLon;
        }

        /// <summary>
        /// The field's background at the current quality setting, ready to store. Null when
        /// there is no boundary to frame by or the download failed - the caller keeps whatever
        /// background the field already has.
        /// </summary>
        public static AgOpenGPS.Core.Models.BingMap CaptureField(FormGPS mf,
            Action<int, int> progress, out string error, out bool tooLarge)
        {
            error = null;
            if (!FieldLatLonBox(mf, out double minLat, out double minLon,
                                out double maxLat, out double maxLon, out tooLarge))
            {
                if (tooLarge) error = "Area too large";
                return null;
            }

            bool high = Properties.Settings.Default.setMap_captureHighQuality;
            Bitmap bmp = Capture(minLat, minLon, maxLat, maxLon, TargetPx(high), progress, out error);
            if (bmp == null) return null;

            var box = AgOpenGPS.Core.Models.GeoBoundingBox.CreateEmpty();
            box.Include(mf.AppModel.LocalPlane.ConvertWgs84ToGeoCoord(
                new AgOpenGPS.Core.Models.Wgs84(maxLat, minLon)));
            box.Include(mf.AppModel.LocalPlane.ConvertWgs84ToGeoCoord(
                new AgOpenGPS.Core.Models.Wgs84(minLat, maxLon)));

            return new AgOpenGPS.Core.Models.BingMap(box, bmp);
        }

        /// <summary>
        /// Fetch the imagery covering the given lat/lon box. Runs on a background thread -
        /// never call it on the UI thread. Returns null and sets error on failure; the caller
        /// then keeps whatever background the field already has.
        /// </summary>
        public static Bitmap Capture(double minLat, double minLon, double maxLat, double maxLon,
            int maxPx, Action<int, int> progress, out string error)
        {
            error = null;
            if (maxLat <= minLat || maxLon <= minLon) { error = "Bad area"; return null; }

            try
            {
                if (Properties.Settings.Default.setMap_appMapMode == AppMapHelper.ModeCustom)
                    return CaptureWms(minLat, minLon, maxLat, maxLon, maxPx, progress, out error);

                return CaptureTiles(minLat, minLon, maxLat, maxLon, maxPx, progress, out error);
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }
        }

        /// <summary>Metres per degree at this latitude - good enough to keep the image's
        /// aspect ratio true over a field-sized box.</summary>
        private static void BoxMeters(double minLat, double minLon, double maxLat, double maxLon,
            out double wMeters, out double hMeters)
        {
            double midLat = (minLat + maxLat) * 0.5;
            wMeters = (maxLon - minLon) * 111320.0 * Math.Cos(midLat * Math.PI / 180.0);
            hMeters = (maxLat - minLat) * 110540.0;
            if (wMeters < 1) wMeters = 1;
            if (hMeters < 1) hMeters = 1;
        }

        // ── WMS: ask for the picture we want, in one or a few requests ────
        private static Bitmap CaptureWms(double minLat, double minLon, double maxLat, double maxLon,
            int maxPx, Action<int, int> progress, out string error)
        {
            error = null;
            BoxMeters(minLat, minLon, maxLat, maxLon, out double wM, out double hM);

            int wPx, hPx;
            if (wM >= hM) { wPx = maxPx; hPx = Math.Max(1, (int)Math.Round(maxPx * hM / wM)); }
            else { hPx = maxPx; wPx = Math.Max(1, (int)Math.Round(maxPx * wM / hM)); }

            int cols = (int)Math.Ceiling(wPx / (double)WmsChunkPx);
            int rows = (int)Math.Ceiling(hPx / (double)WmsChunkPx);
            int total = cols * rows, done = 0, failed = 0;

            Log.EventWriter("Map App capture (WMS): " + wPx + "x" + hPx + " px over "
                + wM.ToString("N0") + "x" + hM.ToString("N0") + " m = "
                + (wM / wPx).ToString("N2") + " m/px, " + total + " request(s)");

            string baseUrl = Properties.Settings.Default.setMap_customWmsUrl;
            Bitmap canvas = new Bitmap(wPx, hPx, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.DimGray);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                for (int cy = 0; cy < rows; cy++)
                {
                    for (int cx = 0; cx < cols; cx++)
                    {
                        int x0 = cx * WmsChunkPx;
                        int y0 = cy * WmsChunkPx;
                        int cw = Math.Min(WmsChunkPx, wPx - x0);
                        int ch = Math.Min(WmsChunkPx, hPx - y0);

                        // pixel rect -> its share of the box (top of the image = maxLat)
                        double loLon = minLon + (maxLon - minLon) * (x0 / (double)wPx);
                        double hiLon = minLon + (maxLon - minLon) * ((x0 + cw) / (double)wPx);
                        double hiLat = maxLat - (maxLat - minLat) * (y0 / (double)hPx);
                        double loLat = maxLat - (maxLat - minLat) * ((y0 + ch) / (double)hPx);

                        string url = GeoportalWmsProvider.MakeGetMapUrl(
                            baseUrl, loLon, loLat, hiLon, hiLat, cw, ch);

                        Image img = Download(url, GeoportalWmsProvider.RefererFor(baseUrl));
                        if (img == null) failed++;
                        else
                        {
                            using (img) g.DrawImage(img, new Rectangle(x0, y0, cw, ch));
                        }

                        done++;
                        progress?.Invoke(done, total);
                    }
                }
            }

            if (failed == total)
            {
                canvas.Dispose();
                error = "WMS: no imagery";
                return null;
            }
            return canvas;
        }

        // ── Tiles: stitch the provider's own tiles, then crop to the box ──
        private static Bitmap CaptureTiles(double minLat, double minLon, double maxLat, double maxLon,
            int maxPx, Action<int, int> progress, out string error)
        {
            error = null;
            GMapProvider provider = AppMapHelper.GetProvider();
            PureProjection proj = provider.Projection;

            // Which zoom to fetch. Tile zooms go in factor-of-two steps, so the finest one
            // that FITS the budget can land at little over half of it - measured 2026-08-10:
            // a High capture came out 2651 px instead of 4096, i.e. 65% of the budget, and a
            // whole zoom level of real detail was left on the server. So take the first zoom
            // that REACHES the budget (it may overshoot up to 2x) and scale down to the exact
            // target: full detail, exact size. The tile count is capped so a deep zoom on a
            // large field cannot turn into a download of thousands of tiles.
            int zoom = -1;
            long wPx = 0, hPx = 0;
            GPoint topLeft = GPoint.Empty, bottomRight = GPoint.Empty;

            int zMax = Math.Min(provider.MaxZoom ?? 20, 20);
            int tileW = (int)proj.TileSize.Width, tileH = (int)proj.TileSize.Height;

            // Walk zooms from coarse to fine: keep the last affordable one, and stop as soon
            // as it covers the pixel budget (deeper would only cost more tiles for detail we
            // would scale away) or as soon as it stops being affordable.
            for (int z = provider.MinZoom; z <= zMax; z++)
            {
                GPoint tl = proj.FromLatLngToPixel(maxLat, minLon, z);
                GPoint br = proj.FromLatLngToPixel(minLat, maxLon, z);
                long w = br.X - tl.X, h = br.Y - tl.Y;
                if (w < 2 || h < 2) continue;

                long tiles = ((br.X - 1) / tileW - tl.X / tileW + 1) *
                             ((br.Y - 1) / tileH - tl.Y / tileH + 1);
                if (tiles > MaxTiles && zoom >= 0) break;   //deeper is only more expensive

                zoom = z; wPx = w; hPx = h; topLeft = tl; bottomRight = br;
                if (Math.Max(w, h) >= maxPx) break;         //budget covered
            }
            if (zoom < 0) { error = "No usable zoom"; return null; }

            //final size: the budget, or the source size when the source cannot reach it
            double scale = 1.0;
            long longSide = Math.Max(wPx, hPx);
            if (longSide > maxPx) scale = maxPx / (double)longSide;

            int outW = Math.Max(1, (int)Math.Round(wPx * scale));
            int outH = Math.Max(1, (int)Math.Round(hPx * scale));

            long tx0 = topLeft.X / tileW, ty0 = topLeft.Y / tileH;
            long tx1 = (bottomRight.X - 1) / tileW, ty1 = (bottomRight.Y - 1) / tileH;

            int total = (int)((tx1 - tx0 + 1) * (ty1 - ty0 + 1));
            int done = 0, failed = 0;

            Log.EventWriter("Map App capture: zoom " + zoom + ", source " + wPx + "x" + hPx
                + " px, output " + outW + "x" + outH + " px, " + total + " tiles");

            //tiles are drawn straight into the final-size canvas - no huge intermediate bitmap
            Bitmap canvas = new Bitmap(outW, outH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.DimGray);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                for (long ty = ty0; ty <= ty1; ty++)
                {
                    for (long tx = tx0; tx <= tx1; tx++)
                    {
                        PureImage pure = null;
                        try
                        {
                            pure = GMaps.Instance.GetImageFrom(
                                provider, new GPoint(tx, ty), zoom, out Exception _);
                            Image img = (pure as GMapImage)?.Img;

                            if (img == null) failed++;
                            else
                            {
                                //destination in FINAL pixels: float rect so the scaled tiles
                                //butt up against each other instead of leaving seams
                                g.DrawImage(img, new RectangleF(
                                    (float)((tx * tileW - topLeft.X) * scale),
                                    (float)((ty * tileH - topLeft.Y) * scale),
                                    (float)(tileW * scale) + 0.5f,
                                    (float)(tileH * scale) + 0.5f));
                            }
                        }
                        catch { failed++; }
                        finally { pure?.Dispose(); }

                        done++;
                        progress?.Invoke(done, total);
                    }
                }
            }

            if (failed == total)
            {
                canvas.Dispose();
                error = "No tiles";
                return null;
            }
            return canvas;
        }

        private static Image Download(string url, string referer)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = HttpTimeoutMs;
                req.ReadWriteTimeout = HttpTimeoutMs;
                req.UserAgent = "AgOpenGPS";
                if (!string.IsNullOrEmpty(referer)) req.Referer = referer;

                using (WebResponse resp = req.GetResponse())
                using (Stream s = resp.GetResponseStream())
                {
                    if (s == null) return null;
                    //copy first: Image.FromStream needs a seekable stream to stay valid
                    MemoryStream ms = new MemoryStream();
                    s.CopyTo(ms);
                    ms.Position = 0;
                    if (ms.Length < 128) { ms.Dispose(); return null; }   // an error page, not imagery
                    return Image.FromStream(ms);
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Invariant-culture number for URLs.</summary>
        internal static string Num(double v)
        {
            return v.ToString("0.#########", CultureInfo.InvariantCulture);
        }
    }
}
