using AgLibrary.Logging;
using AgOpenGPS.Core;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Streamers;
using AgOpenGPS.Core.Translations;
using GMap.NET;
using GMap.NET.WindowsForms;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgOpenGPS
{
    /// <summary>
    /// "Map App" window: single, discoverable place to turn the field background
    /// map ON/OFF and pick its source (Default Bing / Google / Custom WMS).
    /// When turned ON the imagery is captured automatically, centered on the
    /// current GPS position, and saved as the field background.
    /// </summary>
    public partial class FormMapApp : Form
    {
        private readonly FormGPS mf;

        private bool isLoading;       // suppress events while loading initial state
        private bool pendingCapture;  // a capture is queued, waiting for tiles
        private readonly Timer captureTimer = new Timer();

        // Display-only overlay: field boundary + vehicle position marker
        private readonly GMapOverlay overlay = new GMapOverlay("appmap");
        private VehicleMarker vehicleMarker;
        private readonly Timer vehicleTimer = new Timer();

        // Parcel overlay: thin vector lines (WFS) drawn on the preview and baked into
        // the captured background on Apply (so they appear on the main map).
        private readonly GMapOverlay parcelOverlay = new GMapOverlay("parcels");
        private readonly Timer parcelTimer = new Timer();
        private bool fetchingParcels;
        private List<CadastralParcel> lastParcels = new List<CadastralParcel>();

        // Borderless-window drag (same idea as the main window).
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        private const int WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 0x2;

        public FormMapApp(Form callingForm)
        {
            mf = callingForm as FormGPS;
            InitializeComponent();
            LocalizeText();

            captureTimer.Interval = 2500; // fallback if tile-load event never fires
            captureTimer.Tick += captureTimer_Tick;

            vehicleTimer.Interval = 600; // update vehicle marker position
            vehicleTimer.Tick += vehicleTimer_Tick;

            parcelTimer.Interval = 700; // debounce parcel re-fetch on pan/zoom
            parcelTimer.Tick += (s, e) => { parcelTimer.Stop(); RefreshParcels(false); };

            this.KeyPreview = true;
            this.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            this.MouseDown += FormDrag_MouseDown;
            this.Shown += FormMapApp_Shown;
        }

        // English is the Designer default; pull localized text from gStr so HR (and other
        // languages) show translated strings when the app language changes.
        private void LocalizeText()
        {
            grpSource.Text = gStr.gsMapSource;
            rbtnDefault.Text = gStr.gsMapSourceDefault;
            rbtnCustom.Text = gStr.gsCustomWmsDgu;
            lblUrl.Text = gStr.gsWmsUrl;
            lblParcelUrl.Text = gStr.gsParcelWfsUrl;
            btnClose.Text = gStr.gsApplyAndClose;
            UpdateToggleText();
            UpdateParcelToggleText();
        }

        // ── Window controls (touch, like the main window) ─────────────────
        private void btnWinFull_Click(object sender, EventArgs e)
        {
            this.WindowState = this.WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal
                : FormWindowState.Maximized;
        }

        private void btnWinClose_Click(object sender, EventArgs e)
        {
            Close();
        }

        // Drag the borderless window by its background (top strip / empty areas).
        private void FormDrag_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(this.Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
        }

        // Custom WMS tiles often don't render on first show until the view changes
        // (Bing/Google usually show from cache). Nudge the view once shown so the
        // imagery appears immediately instead of after a manual pan/zoom.
        private void FormMapApp_Shown(object sender, EventArgs e)
        {
            // keep the on-map buttons above the map
            btnZoomIn.BringToFront();
            btnZoomOut.BringToFront();
            btnWinFull.BringToFront();
            btnWinClose.BringToFront();

            if (mf == null || !mf.isJobStarted) return;
            ForceMapRefresh();
            if (chkParcels.Checked) EnableParcels();
        }

        private void ForceMapRefresh()
        {
            int z = (int)gMapControl.Zoom;
            if (z > gMapControl.MinZoom) gMapControl.Zoom = z - 1;
            gMapControl.Zoom = z;
            gMapControl.ReloadMap();
        }

        // On-screen zoom buttons (touchscreen-friendly). Zoom in/out by one integer
        // step; OnMapZoomChanged already re-fetches parcels for the new view.
        private void btnZoomIn_Click(object sender, EventArgs e)
        {
            int z = (int)gMapControl.Zoom + 1;
            if (z > gMapControl.MaxZoom) z = (int)gMapControl.MaxZoom;
            gMapControl.Zoom = z;
        }

        private void btnZoomOut_Click(object sender, EventArgs e)
        {
            int z = (int)gMapControl.Zoom - 1;
            if (z < gMapControl.MinZoom) z = (int)gMapControl.MinZoom;
            gMapControl.Zoom = z;
        }

        private void FormMapApp_Load(object sender, EventArgs e)
        {
            isLoading = true;

            // The background map is stored per field, so a field must be open.
            if (mf == null || !mf.isJobStarted)
            {
                chkBackgroundOn.Enabled = false;
                grpSource.Enabled = false;
                tboxWmsUrl.Enabled = false;
                chkParcels.Enabled = false;
                tboxParcelUrl.Enabled = false;
                gMapControl.Enabled = false;
                lblStatus.Text = gStr.gsOpenFieldFirst;
                isLoading = false;
                return;
            }

            // Source selection from settings
            switch (Properties.Settings.Default.setMap_appMapMode)
            {
                case AppMapHelper.ModeGoogle: rbtnGoogle.Checked = true; break;
                case AppMapHelper.ModeCustom: rbtnCustom.Checked = true; break;
                default: rbtnDefault.Checked = true; break;
            }

            string url = Properties.Settings.Default.setMap_customWmsUrl;
            tboxWmsUrl.Text = string.IsNullOrWhiteSpace(url)
                ? GeoportalWmsProvider.DefaultBaseUrl
                : url;
            // Always editable: this DGU WMS endpoint is shared config (used by Custom mode
            // here AND by the Geoportal Boundary window), so it must not be locked to Custom.
            tboxWmsUrl.Enabled = true;

            // Parcel overlay (vector lines, WFS)
            string parcelUrl = Properties.Settings.Default.setMap_parcelWfsUrl;
            tboxParcelUrl.Text = string.IsNullOrWhiteSpace(parcelUrl)
                ? "https://api.uredjenazemlja.hr/services/inspire/cp/wfs"
                : parcelUrl;
            chkParcels.Checked = Properties.Settings.Default.setMap_showParcelsOnMain;
            UpdateParcelToggleText();

            // Configure the preview map, centered on current position
            gMapControl.MapProvider = AppMapHelper.GetProvider();
            gMapControl.Position = new PointLatLng(
                mf.AppModel.CurrentLatLon.Latitude,
                mf.AppModel.CurrentLatLon.Longitude);

            // Cap zoom: very deep zoom = many WMS tiles per view, some fail on the
            // first load and only an unzoom recovers them. A moderate zoom loads
            // reliably and is plenty detailed for a field background.
            int zoom = Properties.Settings.Default.setWindow_BingZoom;
            if (zoom < 14) zoom = 14;
            if (zoom > 16) zoom = 16;
            gMapControl.MaxZoom = 18;
            gMapControl.Zoom = zoom;
            gMapControl.ShowCenter = false;
            gMapControl.DragButton = MouseButtons.Left;
            gMapControl.OnTileLoadComplete += gMap_OnTileLoadComplete;

            // Parcel overlay (vector, baked into capture) + boundary/vehicle overlay (not baked)
            gMapControl.Overlays.Add(parcelOverlay);
            parcelOverlay.IsVisibile = chkParcels.Checked;
            gMapControl.OnPositionChanged += pos => DebounceParcels();
            gMapControl.OnMapZoomChanged += () => DebounceParcels();

            // Display overlay: boundary + vehicle position
            gMapControl.Overlays.Add(overlay);
            BuildBoundaryOverlay();

            vehicleMarker = new VehicleMarker(new PointLatLng(
                mf.AppModel.CurrentLatLon.Latitude,
                mf.AppModel.CurrentLatLon.Longitude));
            overlay.Markers.Add(vehicleMarker);
            vehicleTimer.Start();

            chkBackgroundOn.Checked = mf.worldGrid.HasBingMap;
            UpdateToggleText();

            isLoading = false;
        }

        // ── Source selection ──────────────────────────────────────────────
        private void source_CheckedChanged(object sender, EventArgs e)
        {
            if (isLoading) return;
            RadioButton rb = sender as RadioButton;
            if (rb != null && !rb.Checked) return; // only react to the newly checked one

            SaveSourceSettings();

            gMapControl.MapProvider = AppMapHelper.GetProvider();
            ForceMapRefresh();

            if (chkBackgroundOn.Checked)
                BeginCapture(gStr.gsUpdatingBackground);
        }

        private void tboxWmsUrl_Leave(object sender, EventArgs e)
        {
            if (isLoading) return;
            SaveSourceSettings();

            if (rbtnCustom.Checked)
            {
                gMapControl.MapProvider = AppMapHelper.GetProvider();
                ForceMapRefresh();
                if (chkBackgroundOn.Checked)
                    BeginCapture(gStr.gsUpdatingBackground);
            }
        }

        // ── ON / OFF ──────────────────────────────────────────────────────
        private void chkBackgroundOn_CheckedChanged(object sender, EventArgs e)
        {
            UpdateToggleText();
            if (isLoading) return;

            if (chkBackgroundOn.Checked)
            {
                BeginCapture(gStr.gsLoadingImagery);
            }
            else
            {
                pendingCapture = false;
                captureTimer.Stop();
                SetAndSaveBingMap(null);
                mf.parcels.Clear();   // map off -> parcels off too
                lblStatus.Text = gStr.gsBackgroundRemoved;
            }
        }

        // ── Capture flow ──────────────────────────────────────────────────
        private void BeginCapture(string status)
        {
            pendingCapture = true;
            lblStatus.Text = status;
            captureTimer.Stop();
            captureTimer.Start();
            gMapControl.ReloadMap();
        }

        private void gMap_OnTileLoadComplete(long elapsedMilliseconds)
        {
            if (!pendingCapture) return;
            if (InvokeRequired) { BeginInvoke((MethodInvoker)DoCapture); return; }
            DoCapture();
        }

        private void captureTimer_Tick(object sender, EventArgs e)
        {
            if (pendingCapture) DoCapture();
        }

        private void DoCapture()
        {
            if (!pendingCapture) return;
            pendingCapture = false;
            captureTimer.Stop();

            BingMap bingMap = CreateBingMap();
            if (bingMap == null)
            {
                lblStatus.Text = gStr.gsAreaTooLarge;
                Log.EventWriter("Map App: BingMap area too large");
                return;
            }

            SetAndSaveBingMap(bingMap);
            ApplyParcelsToMainMap();
            lblStatus.Text = gStr.gsBackgroundSet;
        }

        // Capture the current map view into a BingMap (same logic as FormMap).
        private BingMap CreateBingMap()
        {
            PointLatLng topLeft = gMapControl.ViewArea.LocationTopLeft;
            PointLatLng bottomRight = gMapControl.ViewArea.LocationRightBottom;

            GeoBoundingBox box = GeoBoundingBox.CreateEmpty();
            box.Include(ToGeoCoord(topLeft));
            box.Include(ToGeoCoord(bottomRight));

            bool tooBig =
                4000 < Math.Abs(box.MaxNorthing) || 4000 < Math.Abs(box.MinEasting) ||
                4000 < Math.Abs(box.MinNorthing) || 4000 < Math.Abs(box.MaxEasting);
            if (tooBig) return null;

            // Hide overlays so they are not baked into the saved background image.
            // Parcels are drawn as crisp vectors on the main map, not baked in.
            overlay.IsVisibile = false;
            bool parcelsVisible = parcelOverlay.IsVisibile;
            parcelOverlay.IsVisibile = false;
            Bitmap bitmap = new Bitmap(gMapControl.Width, gMapControl.Height);
            gMapControl.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            overlay.IsVisibile = true;
            parcelOverlay.IsVisibile = parcelsVisible;
            gMapControl.Invalidate();

            return new BingMap(box, bitmap);
        }

        private GeoCoord ToGeoCoord(PointLatLng p)
        {
            return mf.AppModel.LocalPlane.ConvertWgs84ToGeoCoord(new Wgs84(p.Lat, p.Lng));
        }

        private void SetAndSaveBingMap(BingMap bingMap)
        {
            mf.worldGrid.BingMap = bingMap;
            new BingMapStreamer().TryWrite(bingMap, mf.AppCore.ActiveField.FieldDirectory);
        }

        // ── Overlay: boundary + vehicle (display only) ────────────────────
        private void BuildBoundaryOverlay()
        {
            if (mf.bnd == null || mf.bnd.bndList.Count == 0) return;

            foreach (var bnd in mf.bnd.bndList)
            {
                if (bnd.fenceLine.Count < 3) continue;

                var pts = new List<PointLatLng>(bnd.fenceLine.Count);
                foreach (vec3 p in bnd.fenceLine)
                {
                    Wgs84 w = mf.AppModel.LocalPlane.ConvertGeoCoordToWgs84(
                        new GeoCoord(p.northing, p.easting));
                    pts.Add(new PointLatLng(w.Latitude, w.Longitude));
                }

                GMapPolygon poly = new GMapPolygon(pts, "boundary")
                {
                    Fill = new SolidBrush(Color.FromArgb(40, Color.Yellow)),
                    Stroke = new Pen(Color.Yellow, 2f)
                };
                overlay.Polygons.Add(poly);
            }
        }

        private void vehicleTimer_Tick(object sender, EventArgs e)
        {
            if (vehicleMarker == null || mf == null || !mf.isJobStarted) return;

            vehicleMarker.Position = new PointLatLng(
                mf.AppModel.CurrentLatLon.Latitude,
                mf.AppModel.CurrentLatLon.Longitude);
            gMapControl.UpdateMarkerLocalPosition(vehicleMarker);
            gMapControl.Invalidate();
        }

        // ── Helpers ───────────────────────────────────────────────────────
        private void SaveSourceSettings()
        {
            int mode = AppMapHelper.ModeDefault;
            if (rbtnGoogle.Checked) mode = AppMapHelper.ModeGoogle;
            else if (rbtnCustom.Checked) mode = AppMapHelper.ModeCustom;
            Properties.Settings.Default.setMap_appMapMode = mode;

            string url = tboxWmsUrl.Text.Trim();
            if (string.IsNullOrWhiteSpace(url)) url = GeoportalWmsProvider.DefaultBaseUrl;
            Properties.Settings.Default.setMap_customWmsUrl = url;
        }

        private void UpdateToggleText()
        {
            chkBackgroundOn.Text = gStr.gsBackgroundMap + ": " +
                (chkBackgroundOn.Checked ? gStr.gsOn : gStr.gsOff);
        }

        private void UpdateParcelToggleText()
        {
            chkParcels.Text = gStr.gsParcel + ": " +
                (chkParcels.Checked ? gStr.gsOn : gStr.gsOff);
        }

        // ── Parcel overlay: thin vector lines (WFS), baked into capture ──
        private void chkParcels_CheckedChanged(object sender, EventArgs e)
        {
            UpdateParcelToggleText();
            parcelOverlay.IsVisibile = chkParcels.Checked;
            if (isLoading) return;

            Properties.Settings.Default.setMap_showParcelsOnMain = chkParcels.Checked;

            if (chkParcels.Checked)
            {
                EnableParcels();
            }
            else
            {
                parcelOverlay.Polygons.Clear();
                gMapControl.Invalidate();
                lblStatus.Text = gStr.gsParcelsHidden;
            }
        }

        // Cache-first enable: load parcels from the field's cache file (instant, no
        // internet); if there is none, fetch from the WFS and save the cache.
        private void EnableParcels()
        {
            if (fetchingParcels || mf == null || !mf.isJobStarted) return;

            string path = ParcelCachePath();
            if (path != null && System.IO.File.Exists(path))
            {
                var cached = CadastralWfsClient.LoadParcels(path, out string err);
                if (err == null && cached != null && cached.Count > 0)
                {
                    DrawParcels(cached, null, false);
                    return;
                }
            }
            RefreshParcels(true); // no cache -> internet, then save
        }

        // Full path of the per-field parcel cache file (or null if no field).
        private string ParcelCachePath()
        {
            var dir = mf?.AppCore?.ActiveField?.FieldDirectory;
            if (dir == null) return null;
            return System.IO.Path.Combine(dir.FullName, "Parcels.txt");
        }

        private void btnParcelRefresh_Click(object sender, EventArgs e)
        {
            // Refresh always goes to the internet and overwrites the cache (use when the
            // cadastre changed). Turn parcels on without triggering the cache-first load.
            if (!chkParcels.Checked)
            {
                isLoading = true;
                chkParcels.Checked = true; // CheckedChanged updates text/visibility, skips auto-load
                isLoading = false;
                Properties.Settings.Default.setMap_showParcelsOnMain = true;
            }
            RefreshParcels(true);
        }

        private void tboxParcelUrl_Leave(object sender, EventArgs e)
        {
            if (isLoading) return;
            Properties.Settings.Default.setMap_parcelWfsUrl = tboxParcelUrl.Text.Trim();
            if (chkParcels.Checked) RefreshParcels(true); // new URL -> fetch and refresh cache
        }

        private void DebounceParcels()
        {
            if (isLoading || !chkParcels.Checked) return;
            parcelTimer.Stop();
            parcelTimer.Start();
        }

        // saveAfter: write the fetched parcels to the field cache (on explicit enable/refresh,
        // not on pan/zoom re-fetch so the cache keeps the field area).
        private void RefreshParcels(bool saveAfter)
        {
            if (fetchingParcels || !chkParcels.Checked || mf == null || !mf.isJobStarted) return;

            var area = gMapControl.ViewArea;
            double minLat = area.LocationRightBottom.Lat;
            double maxLat = area.LocationTopLeft.Lat;
            double minLon = area.LocationTopLeft.Lng;
            double maxLon = area.LocationRightBottom.Lng;
            string wfsUrl = Properties.Settings.Default.setMap_parcelWfsUrl;

            fetchingParcels = true;
            lblStatus.Text = gStr.gsLoadingParcels;

            Task.Run(() =>
            {
                var parcels = CadastralWfsClient.GetParcels(wfsUrl, minLat, minLon, maxLat, maxLon, out string err);
                if (!IsHandleCreated) return;
                BeginInvoke((MethodInvoker)(() => DrawParcels(parcels, err, saveAfter)));
            });
        }

        private void DrawParcels(List<CadastralParcel> parcels, string err, bool saveAfter)
        {
            fetchingParcels = false;
            parcelOverlay.Polygons.Clear();

            if (err != null)
            {
                lblStatus.Text = gStr.gsParcelsError + ": " + err;
                Log.EventWriter("Parcel WFS error " + err);
                return;
            }

            lastParcels = parcels;

            foreach (var p in parcels)
            {
                if (p.Outer.Count < 3) continue;
                var poly = new GMapPolygon(p.Outer, "parcel")
                {
                    Fill = new SolidBrush(Color.Transparent),
                    Stroke = new Pen(Color.DeepSkyBlue, 1.4f)
                };
                parcelOverlay.Polygons.Add(poly);
            }

            lblStatus.Text = parcels.Count + " " + gStr.gsParcels + ".";
            gMapControl.Invalidate();

            // Cache the fetched parcels next to the field for instant load next time.
            if (saveAfter && parcels.Count > 0)
            {
                string path = ParcelCachePath();
                if (path != null) CadastralWfsClient.SaveParcels(path, parcels, out _);
            }
        }

        // Convert the captured parcels to local line segments for the crisp main-map vector draw.
        private void ApplyParcelsToMainMap()
        {
            if (mf == null) return;

            if (!chkParcels.Checked)
            {
                mf.parcels.Clear();
                return;
            }

            var segs = new List<GeoCoord>();
            foreach (var p in lastParcels)
            {
                int n = p.Outer.Count;
                if (n < 3) continue;

                var ring = new GeoCoord[n];
                for (int i = 0; i < n; i++)
                    ring[i] = mf.AppModel.LocalPlane.ConvertWgs84ToGeoCoord(
                        new Wgs84(p.Outer[i].Lat, p.Outer[i].Lng));

                for (int i = 0; i < n; i++)
                {
                    segs.Add(ring[i]);
                    segs.Add(ring[(i + 1) % n]);
                }
            }
            mf.parcels.Segments = segs.ToArray();
        }

        private void FormMapApp_FormClosing(object sender, FormClosingEventArgs e)
        {
            gMapControl.OnTileLoadComplete -= gMap_OnTileLoadComplete;
            captureTimer.Stop();
            vehicleTimer.Stop();
            parcelTimer.Stop();
            Properties.Settings.Default.Save();
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            // Apply: if the background is ON, capture the current (panned/zoomed)
            // view. The parcel overlay (if visible) is baked into the captured image.
            if (chkBackgroundOn.Checked && mf != null && mf.isJobStarted)
            {
                pendingCapture = false;
                captureTimer.Stop();

                BingMap bingMap = CreateBingMap();
                if (bingMap != null)
                {
                    SetAndSaveBingMap(bingMap);
                    ApplyParcelsToMainMap();
                }
            }
            Close();
        }
    }
}
