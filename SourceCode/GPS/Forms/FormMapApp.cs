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

            parcelTimer.Interval = 700; // debounce parcel coverage on pan/zoom
            parcelTimer.Tick += (s, e) => { parcelTimer.Stop(); KickCoverage(false); };

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
            lblQuality.Text = gStr.gsResolution;
            UpdateToggleText();
            UpdateParcelToggleText();
            UpdateQualityButton();
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
                btnQuality.Enabled = false;
                btnRecapture.Enabled = false;
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
            UpdateQualityButton();

            if (!HasBoundary)
                lblStatus.Text = gStr.gsResolutionNeedsBoundary;

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
        // Two paths, decided by whether the field already has a boundary:
        //
        //  boundary -> the capture is framed by the FIELD (bbox + BoundaryMargin) and fetched
        //              straight from the source at the chosen resolution. Independent of the
        //              window, of where the vehicle stands and of where the preview is panned.
        //  no boundary (new field) -> the old behaviour: screenshot the preview, centred on the
        //              vehicle. There is nothing to frame by yet, and this is the rough
        //              background the driver traces the boundary over in the first place.
        private bool capturing;

        private bool HasBoundary { get { return CMapCapture.HasBoundary(mf); } }

        private void BeginCapture(string status)
        {
            if (HasBoundary)
            {
                StartBoxCapture(status);
                return;
            }

            pendingCapture = true;
            lblStatus.Text = status;
            captureTimer.Stop();
            captureTimer.Start();
            gMapControl.ReloadMap();
        }

        /// <summary>
        /// Download the field's imagery on a worker thread. The old picture is only replaced
        /// once a new one has actually arrived - a failed download must never leave the driver
        /// without a background.
        /// </summary>
        private void StartBoxCapture(string status)
        {
            if (capturing) return;

            bool high = Properties.Settings.Default.setMap_captureHighQuality;

            capturing = true;
            SetBusy(true);
            lblStatus.Text = status;

            Task.Run(() =>
            {
                BingMap map = CMapCapture.CaptureField(mf,
                    (done, total) =>
                    {
                        if (!IsHandleCreated) return;
                        try
                        {
                            BeginInvoke((MethodInvoker)(() =>
                                lblStatus.Text = gStr.gsLoadingImagery + " " + done + "/" + total));
                        }
                        catch { }
                    },
                    out string error, out bool tooLarge);

                if (!IsHandleCreated) return;
                try
                {
                    BeginInvoke((MethodInvoker)(() => FinishBoxCapture(map, error, tooLarge, high)));
                }
                catch { }
            });
        }

        private void FinishBoxCapture(BingMap map, string error, bool tooLarge, bool high)
        {
            capturing = false;
            SetBusy(false);

            if (tooLarge)
            {
                lblStatus.Text = gStr.gsAreaTooLarge;
                Log.EventWriter("Map App: field box too large");
                return;
            }

            if (map == null)
            {
                //keep the existing background; drop back to Low so the next press is the
                //cheap one (a failed High is usually a slow or refusing server)
                if (high)
                {
                    Properties.Settings.Default.setMap_captureHighQuality = false;
                    UpdateQualityButton();
                }
                lblStatus.Text = gStr.gsMapCaptureFailed + (string.IsNullOrEmpty(error) ? "" : " - " + error);
                Log.EventWriter("Map App: capture failed - " + error);
                return;
            }

            SetAndSaveBingMap(map);
            ApplyParcelsToMainMap();

            lblStatus.Text = gStr.gsBackgroundSet
                + " (" + map.Bitmap.Width + "x" + map.Bitmap.Height + ")";
        }

        private void SetBusy(bool busy)
        {
            chkBackgroundOn.Enabled = !busy;
            btnQuality.Enabled = !busy && HasBoundary;
            btnRecapture.Enabled = !busy;
            grpSource.Enabled = !busy;
            btnClose.Enabled = !busy;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }

        // ── Resolution: Low (default) / High ──────────────────────────────
        private void btnQuality_Click(object sender, EventArgs e)
        {
            if (!HasBoundary) return;

            Properties.Settings.Default.setMap_captureHighQuality =
                !Properties.Settings.Default.setMap_captureHighQuality;
            UpdateQualityButton();

            //the press IS the request for a new picture at that resolution
            if (chkBackgroundOn.Checked) BeginCapture(gStr.gsLoadingImagery);
        }

        private void btnRecapture_Click(object sender, EventArgs e)
        {
            if (mf == null || !mf.isJobStarted) return;
            if (!chkBackgroundOn.Checked)
            {
                lblStatus.Text = gStr.gsBackgroundRemoved;
                return;
            }
            BeginCapture(gStr.gsUpdatingBackground);
        }

        private void UpdateQualityButton()
        {
            bool high = Properties.Settings.Default.setMap_captureHighQuality;
            btnQuality.Text = high ? gStr.gsHigh : gStr.gsLow;
            btnQuality.BackColor = !HasBoundary
                ? System.Drawing.Color.Gainsboro
                : (high ? System.Drawing.Color.MediumAquamarine : System.Drawing.Color.AliceBlue);
            btnQuality.Enabled = HasBoundary && !capturing;
            lblQuality.Text = gStr.gsResolution;
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

            var streamer = new BingMapStreamer();
            streamer.JpegQuality = CMapCapture.JpegQuality(
                Properties.Settings.Default.setMap_captureHighQuality);
            streamer.TryWrite(bingMap, mf.AppCore.ActiveField.FieldDirectory);
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

        // Shared-cache enable: show whatever tiles are already loaded (instant) and
        // let the background coverage pass fill the rest from disk/WFS.
        private void EnableParcels()
        {
            if (mf == null || !mf.isJobStarted) return;
            lblStatus.Text = gStr.gsLoadingParcels;
            DrawFromCache();
            KickCoverage(false);
        }

        // Ask the shared tile cache to cover ~1 km around the current MAP CENTER
        // (panning grows the cache). Parcels are drawn after EVERY landed tile with
        // a tile counter in the status - a WFS tile takes ~15 s, so the driver sees
        // it working instead of minutes of blank map (field test 2026-07-15).
        private void KickCoverage(bool force)
        {
            if (mf == null || !mf.isJobStarted || !chkParcels.Checked) return;

            var c = gMapControl.Position;
            CParcelCache.EnsureCoverage(c.Lat, c.Lng,
                Properties.Settings.Default.setMap_parcelWfsUrl, force, () =>
                {
                    if (IsHandleCreated) BeginInvoke((MethodInvoker)(() => DrawFromCache()));
                },
                (done, total) =>
                {
                    if (!IsHandleCreated) return;
                    BeginInvoke((MethodInvoker)(() =>
                    {
                        DrawFromCache();
                        if (done < total)
                            lblStatus.Text = gStr.gsLoadingParcels + " " + done + "/" + total;
                    }));
                });
        }

        private void btnParcelRefresh_Click(object sender, EventArgs e)
        {
            // Refresh re-downloads the tiles around the view and overwrites their
            // files (use when the cadastre changed).
            if (!chkParcels.Checked)
            {
                isLoading = true;
                chkParcels.Checked = true; // CheckedChanged updates text/visibility, skips auto-load
                isLoading = false;
                Properties.Settings.Default.setMap_showParcelsOnMain = true;
            }
            lblStatus.Text = gStr.gsLoadingParcels;
            KickCoverage(true);
        }

        private void tboxParcelUrl_Leave(object sender, EventArgs e)
        {
            if (isLoading) return;
            Properties.Settings.Default.setMap_parcelWfsUrl = tboxParcelUrl.Text.Trim();
            if (chkParcels.Checked) KickCoverage(true); // new URL -> re-fetch tiles
        }

        private void DebounceParcels()
        {
            if (isLoading || !chkParcels.Checked) return;
            parcelTimer.Stop();
            parcelTimer.Start();
        }

        // Redraw the overlay from the shared tile cache (memory only - instant).
        private void DrawFromCache()
        {
            if (!chkParcels.Checked) return;

            lastParcels = CParcelCache.Snapshot();
            parcelOverlay.Polygons.Clear();

            foreach (var p in lastParcels)
            {
                if (p.Outer.Count < 3) continue;
                var poly = new GMapPolygon(p.Outer, "parcel")
                {
                    Fill = new SolidBrush(Color.Transparent),
                    Stroke = new Pen(Color.DeepSkyBlue, 1.4f)
                };
                parcelOverlay.Polygons.Add(poly);
            }

            lblStatus.Text = lastParcels.Count + " " + gStr.gsParcels + ".";
            gMapControl.Invalidate();
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

            //segments come from the shared tile cache, built off the UI thread
            mf.RebuildParcelSegments();
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
            // Apply and close. It deliberately does NOT re-capture: a background that already
            // exists is kept as it is (2026-08-10). Re-capturing on every close was harmless
            // while a capture was a screenshot, but it now means a real download - so a new
            // picture is only ever fetched when something changed or the driver asked for it.
            if (capturing) return;

            if (chkBackgroundOn.Checked && mf != null && mf.isJobStarted
                && !mf.worldGrid.HasBingMap && !HasBoundary)
            {
                //no boundary yet and nothing captured: take the view as it stands, that is
                //what this window was opened for
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
