using AgLibrary.Logging;
using AgOpenGPS.Core;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Streamers;
using GMap.NET;
using GMap.NET.WindowsForms;
using System;
using System.Collections.Generic;
using System.Drawing;
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

        public FormMapApp(Form callingForm)
        {
            mf = callingForm as FormGPS;
            InitializeComponent();

            captureTimer.Interval = 2500; // fallback if tile-load event never fires
            captureTimer.Tick += captureTimer_Tick;

            vehicleTimer.Interval = 600; // update vehicle marker position
            vehicleTimer.Tick += vehicleTimer_Tick;

            this.Shown += FormMapApp_Shown;
        }

        // Custom WMS tiles often don't render on first show until the view changes
        // (Bing/Google usually show from cache). Nudge the view once shown so the
        // imagery appears immediately instead of after a manual pan/zoom.
        private void FormMapApp_Shown(object sender, EventArgs e)
        {
            if (mf == null || !mf.isJobStarted) return;
            ForceMapRefresh();
        }

        private void ForceMapRefresh()
        {
            int z = (int)gMapControl.Zoom;
            if (z > gMapControl.MinZoom) gMapControl.Zoom = z - 1;
            gMapControl.Zoom = z;
            gMapControl.ReloadMap();
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
                gMapControl.Enabled = false;
                lblStatus.Text = "Open a field first to use a background map.";
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
            tboxWmsUrl.Enabled = rbtnCustom.Checked;

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

            tboxWmsUrl.Enabled = rbtnCustom.Checked;
            SaveSourceSettings();

            gMapControl.MapProvider = AppMapHelper.GetProvider();
            ForceMapRefresh();

            if (chkBackgroundOn.Checked)
                BeginCapture("Updating background...");
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
                    BeginCapture("Updating background...");
            }
        }

        // ── ON / OFF ──────────────────────────────────────────────────────
        private void chkBackgroundOn_CheckedChanged(object sender, EventArgs e)
        {
            UpdateToggleText();
            if (isLoading) return;

            if (chkBackgroundOn.Checked)
            {
                BeginCapture("Loading imagery...");
            }
            else
            {
                pendingCapture = false;
                captureTimer.Stop();
                SetAndSaveBingMap(null);
                lblStatus.Text = "Background removed.";
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
                lblStatus.Text = "Area too large - zoom in on the map, then toggle again.";
                Log.EventWriter("Map App: BingMap area too large");
                return;
            }

            SetAndSaveBingMap(bingMap);
            lblStatus.Text = "Background set.";
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

            // Hide boundary/vehicle overlay so they are not baked into the saved background
            overlay.IsVisibile = false;
            Bitmap bitmap = new Bitmap(gMapControl.Width, gMapControl.Height);
            gMapControl.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            overlay.IsVisibile = true;
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
            chkBackgroundOn.Text = chkBackgroundOn.Checked
                ? "Background Map: ON"
                : "Background Map: OFF";
        }

        private void FormMapApp_FormClosing(object sender, FormClosingEventArgs e)
        {
            gMapControl.OnTileLoadComplete -= gMap_OnTileLoadComplete;
            captureTimer.Stop();
            vehicleTimer.Stop();
            Properties.Settings.Default.Save();
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            // Apply: if the background is ON, capture the current (panned/zoomed)
            // view so pan/zoom changes take effect without toggling OFF/ON.
            if (chkBackgroundOn.Checked && mf != null && mf.isJobStarted)
            {
                pendingCapture = false;
                captureTimer.Stop();

                BingMap bingMap = CreateBingMap();
                if (bingMap != null)
                    SetAndSaveBingMap(bingMap);
            }
            Close();
        }
    }
}
