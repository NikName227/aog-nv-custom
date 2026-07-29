using AgLibrary.Logging;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Translations;
using GMap.NET;
using GMap.NET.WindowsForms;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgOpenGPS
{
    /// <summary>
    /// Phase 1: show DGU ortophoto (WMS base) + cadastral parcels (WFS vector) over the map.
    /// Phase 2: click-select parcels (mark mode), highlight + selected count/area.
    /// Boundary creation from the selection comes in phase 3.
    /// GUI is in English; Croatian comes from the gStr translation map (gStr.hr.resx).
    /// </summary>
    public partial class FormGeoportalBoundary : Form
    {
        private readonly FormGPS mf;

        private readonly GMapOverlay parcelOverlay = new GMapOverlay("parcels");
        private readonly GMapOverlay vehicleOverlay = new GMapOverlay("vehicle");
        private VehicleMarker vehicleMarker;

        private readonly Timer refreshTimer = new Timer();   // debounce map moves
        private readonly Timer vehicleTimer = new Timer();   // vehicle marker
        private bool fetching;
        private bool isLoading;

        // Parcels currently displayed (for hit-test) + selected parcels kept by a stable
        // geometry key so the selection survives re-fetches on pan/zoom.
        private List<CadastralParcel> parcels = new List<CadastralParcel>();
        private readonly Dictionary<string, CadastralParcel> selected = new Dictionary<string, CadastralParcel>();

        // Boundary built from the union of selected parcels (WGS84 outer ring) + its preview.
        private readonly GMapOverlay madeOverlay = new GMapOverlay("made");
        private List<Wgs84> madeRing;

        // Phase 4: edit the made boundary's vertices (one draggable handle per vertex).
        private readonly GMapOverlay editOverlay = new GMapOverlay("bedit");
        private readonly List<CornerMarker> madeMarkers = new List<CornerMarker>();
        private int selVertex = -1;   // selected boundary vertex (index into madeRing), -1 = none
        private int dragVertex = -1;  // boundary vertex being dragged with the mouse
        private readonly List<List<Wgs84>> undoStack = new List<List<Wgs84>>(); // last 3 ring states

        // Bridges connect non-adjacent parcels into one polygon. Each bridge is a quad with
        // 4 draggable corner handles; "Add bridge" drops one in the view center to position.
        private readonly GMapOverlay bridgeOverlay = new GMapOverlay("bridges");
        private readonly List<BridgeRect> bridgeRects = new List<BridgeRect>();
        private BridgeRect dragRect;
        private int dragCorner = -1;

        // Point-edit (touch arrow-pad): select a bridge corner, then nudge it with the arrows.
        // OFF = arrows pan the map; Mark = waiting to pick a point; Move = a point is selected.
        private enum PointEdit { Off, Mark, Move }
        private PointEdit pointMode = PointEdit.Off;
        private BridgeRect selRect;
        private CornerMarker selMarker;
        private int selCorner = -1;
        private int stepPx = 20; // Coarse by default; Fine = 5

        // Existing field boundary shown for reference; "Edit boundary" loads the outer ring
        // into madeRing so the Phase-4 edit machinery works on it. Inner boundaries
        // (obstacles) are shown too and can be deleted in delete-obstacle mode.
        private readonly GMapOverlay existingOverlay = new GMapOverlay("existing");
        private List<Wgs84> existingOuterRing;
        private GMapPolygon existingOuterPoly;

        private class ExistingInner
        {
            public int BndIndex;                 // index into mf.bnd.bndList
            public List<PointLatLng> Pts;
            public GMapPolygon Poly;
        }
        private readonly List<ExistingInner> existingInners = new List<ExistingInner>();
        private readonly List<int> deletedInnerIdx = new List<int>(); // applied on Save

        // New obstacle circles (center handle to move, edge handle to resize). Saved as
        // inner boundaries (bndList[1..], non drive-thru) = avoidance areas.
        private readonly GMapOverlay obstacleOverlay = new GMapOverlay("obstacles");
        private class ObstacleCircle
        {
            public PointLatLng Center;
            public double RadiusM;
            public GMapPolygon Poly;
            public CornerMarker CenterMarker;
            public CornerMarker RadiusMarker;
        }
        private readonly List<ObstacleCircle> obstacles = new List<ObstacleCircle>();
        private ObstacleCircle dragObstacle;
        private bool dragObstacleRadius;
        private ObstacleCircle selObstacle;     // arrow-pad selection
        private bool selObstacleRadius;
        private const int ObstacleCirclePts = 24;

        private class BridgeRect
        {
            public readonly List<PointLatLng> Corners = new List<PointLatLng>(4);
            public GMapPolygon Poly;
            public readonly List<CornerMarker> Markers = new List<CornerMarker>(4);
        }

        // Round handle, big enough for touch. White/orange normally, green when selected.
        private class CornerMarker : GMapMarker
        {
            public bool Selected;
            public CornerMarker(PointLatLng pos) : base(pos) { }
            public override void OnRender(System.Drawing.Graphics g)
            {
                const float r = 9f;
                var fill = Selected ? System.Drawing.Brushes.LimeGreen : System.Drawing.Brushes.White;
                g.FillEllipse(fill, LocalPosition.X - r, LocalPosition.Y - r, 2 * r, 2 * r);
                var color = Selected ? System.Drawing.Color.Green : System.Drawing.Color.OrangeRed;
                using (var pen = new System.Drawing.Pen(color, 2.5f))
                    g.DrawEllipse(pen, LocalPosition.X - r, LocalPosition.Y - r, 2 * r, 2 * r);
            }
        }

        // Borderless-window drag (same idea as the main window).
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        private const int WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 0x2;

        public FormGeoportalBoundary(Form callingForm)
        {
            mf = callingForm as FormGPS;
            InitializeComponent();
            LocalizeText();

            refreshTimer.Interval = 700;
            refreshTimer.Tick += (s, e) => { refreshTimer.Stop(); RefreshParcels(); };

            vehicleTimer.Interval = 600;
            vehicleTimer.Tick += vehicleTimer_Tick;

            this.KeyPreview = true;
            this.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            this.MouseDown += FormDrag_MouseDown;
        }

        // English is the Designer default; pull localized text from gStr so HR (and other
        // languages) show translated strings when the app language changes.
        private void LocalizeText()
        {
            this.Text = gStr.gsBoundaryFromGeoportal;
            chkParcels.Text = gStr.gsCadastralParcels;
            btnRefresh.Text = gStr.gsRefreshParcels;
            btnGoToPos.Text = gStr.gsGoToPosition;
            chkMarkParcel.Text = gStr.gsMarkParcel;
            btnAddBridge.Text = gStr.gsAddBridge;
            btnClearSel.Text = gStr.gsClearSelection;
            btnFine.Text = gStr.gsFine;
            btnCoarse.Text = gStr.gsCoarse;

            // Grouped rows: a bold label carries the context, the buttons get short verbs.
            lblPointGroup.Text = gStr.gsPoint + ":";
            btnAddPoint.Text = gStr.gsAdd;
            btnDeletePoint.Text = gStr.gsDelete;
            btnUndo.Text = gStr.gsUndo;
            lblObstacleGroup.Text = gStr.gsObstacle + ":";
            btnAddObstacle.Text = gStr.gsAdd;
            btnDeleteObstacle.Text = gStr.gsDelete;
            lblBoundaryGroup.Text = gStr.gsBoundaryGroup + ":";
            btnMakeBoundary.Text = gStr.gsMake;
            btnSaveBoundary.Text = gStr.gsSave;
            btnEditBoundary.Text = gStr.gsEdit;
            lblPointMode.Text = "";
        }

        private void FormGeoportalBoundary_Load(object sender, EventArgs e)
        {
            isLoading = true;

            // Base map = same WMS as the App Map window (configurable there)
            string wmsUrl = Properties.Settings.Default.setMap_customWmsUrl;
            GeoportalWmsProvider.Instance.SetBaseUrl(
                string.IsNullOrWhiteSpace(wmsUrl) ? GeoportalWmsProvider.DefaultBaseUrl : wmsUrl);
            gMapControl.MapProvider = GeoportalWmsProvider.Instance;

            gMapControl.Position = new PointLatLng(
                mf.AppModel.CurrentLatLon.Latitude,
                mf.AppModel.CurrentLatLon.Longitude);

            int zoom = Properties.Settings.Default.setWindow_BingZoom;
            if (zoom < 15) zoom = 16;
            if (zoom > 16) zoom = 16;
            gMapControl.Zoom = zoom;
            gMapControl.ShowCenter = false;
            gMapControl.DragButton = MouseButtons.Left;

            gMapControl.Overlays.Add(parcelOverlay);
            gMapControl.Overlays.Add(existingOverlay);
            gMapControl.Overlays.Add(bridgeOverlay);
            gMapControl.Overlays.Add(madeOverlay);
            gMapControl.Overlays.Add(obstacleOverlay);
            gMapControl.Overlays.Add(editOverlay);
            gMapControl.Overlays.Add(vehicleOverlay);

            vehicleMarker = new VehicleMarker(new PointLatLng(
                mf.AppModel.CurrentLatLon.Latitude,
                mf.AppModel.CurrentLatLon.Longitude));
            vehicleOverlay.Markers.Add(vehicleMarker);
            vehicleTimer.Start();

            // Parcels come from the local cache (only Map App fetches from the URL); pan/zoom
            // does NOT auto-fetch. Use Refresh to pull a new area from the WFS when outside it.
            gMapControl.MouseClick += gMap_MouseClick;
            gMapControl.MouseDown += gMap_MouseDown;
            gMapControl.MouseMove += gMap_MouseMove;
            gMapControl.MouseUp += gMap_MouseUp;

            // Two-finger pan if this GMap build/device supports it (properties may be absent).
            TrySetBool(gMapControl, "MultiTouchEnabled", true);
            TrySetBool(gMapControl, "TouchEnabled", true);

            UpdateArrowColors();

            // Add point is always available: with no boundary it starts a manual polygon.
            btnAddPoint.Enabled = true;

            LoadExistingField();

            UpdateSelInfo();
            isLoading = false;
        }

        private void FormGeoportalBoundary_Shown(object sender, EventArgs e)
        {
            // keep the on-map buttons above the map
            btnZoomIn.BringToFront();
            btnZoomOut.BringToFront();
            btnWinFull.BringToFront();
            btnWinClose.BringToFront();

            // WMS tiles often don't render until the view changes - nudge once.
            ForceMapRefresh();
            LoadParcelsFromCache();
        }

        private void ForceMapRefresh()
        {
            int z = (int)gMapControl.Zoom;
            if (z > gMapControl.MinZoom) gMapControl.Zoom = z - 1;
            gMapControl.Zoom = z;
            gMapControl.ReloadMap();
        }

        private void DebounceRefresh()
        {
            if (isLoading) return;
            refreshTimer.Stop();
            refreshTimer.Start();
        }

        // ── Parcels: shared tile cache (default) ──────────────────────────
        // Parcels come from the shared ParcelCache tiles (memory/disk); if nothing
        // is loaded yet we show a message (manual polygon still works) and the
        // background coverage pass fills the view.
        private void LoadParcelsFromCache()
        {
            // this window shows whatever is already saved on disk regardless of the
            // main-map "show parcels" toggle - only missing coverage gets the
            // "load them in Map App" message, never a silent empty cache
            var c = gMapControl.Position;
            CParcelCache.LoadDiskCoverage(c.Lat, c.Lng);

            var cached = CParcelCache.Snapshot();
            if (cached.Count > 0)
            {
                parcels = cached;
                RenderParcels();
                lblStatus.Text = parcels.Count + " " + gStr.gsParcels + ".";
                return;
            }
            parcels = new List<CadastralParcel>();
            parcelOverlay.Polygons.Clear();
            gMapControl.Invalidate();
            lblStatus.Text = gStr.gsNoParcelCache;
        }

        // ── Parcels — Refresh: re-download the tiles around the view ──────
        private void RefreshParcels()
        {
            if (fetching || !chkParcels.Checked) return;

            fetching = true;
            lblStatus.Text = gStr.gsLoadingParcels;

            var c = gMapControl.Position;
            CParcelCache.EnsureCoverage(c.Lat, c.Lng,
                Properties.Settings.Default.setMap_parcelWfsUrl, true, () =>
                {
                    if (IsHandleCreated)
                        BeginInvoke((MethodInvoker)(() => OnParcelsFetched(CParcelCache.Snapshot(), null)));
                });
        }

        private void OnParcelsFetched(List<CadastralParcel> fetched, string err)
        {
            fetching = false;

            if (err != null)
            {
                lblStatus.Text = gStr.gsParcelsError + ": " + err;
                Log.EventWriter("Geoportal WFS error " + err);
                return;
            }

            parcels = fetched;
            RenderParcels();
            lblStatus.Text = parcels.Count + " " + gStr.gsParcels + ".";
        }

        // Stable key for a parcel (3765 centroid, 0.1 m) so a re-fetched parcel keeps its
        // selection state.
        private static string KeyOf(CadastralParcel p)
        {
            if (p?.Geom3765 == null) return null;
            var c = p.Geom3765.Centroid;
            return ((long)Math.Round(c.X * 10)) + "_" + ((long)Math.Round(c.Y * 10));
        }

        private void RenderParcels()
        {
            parcelOverlay.Polygons.Clear();

            foreach (var p in parcels)
            {
                if (p.Outer.Count < 3) continue;
                bool isSel = KeyOf(p) is string k && selected.ContainsKey(k);
                var poly = new GMapPolygon(p.Outer, "parcel")
                {
                    Fill = new SolidBrush(isSel
                        ? Color.FromArgb(70, Color.Gold)
                        : Color.FromArgb(20, Color.DeepSkyBlue)),
                    Stroke = isSel
                        ? new Pen(Color.Gold, 2.6f)
                        : new Pen(Color.DeepSkyBlue, 1.5f)
                };
                parcelOverlay.Polygons.Add(poly);
            }

            gMapControl.Invalidate();
        }

        private void gMap_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            // Add-point mode: a tap inserts a new vertex on the nearest boundary segment.
            if (btnAddPoint.Checked && madeRing != null)
            {
                InsertBoundaryPoint(e.X, e.Y);
                return;
            }

            // Delete-point mode: a tap on a vertex removes it.
            if (btnDeletePoint.Checked && madeRing != null)
            {
                if (HitTestVertex(e.X, e.Y, out int dv)) DeleteVertexAt(dv);
                return;
            }

            // Delete-obstacle mode: a tap on an obstacle (new circle or existing inner) removes it.
            if (btnDeleteObstacle.Checked)
            {
                DeleteObstacleAt(e.X, e.Y);
                return;
            }

            if (HitTestObstacleHandle(e.X, e.Y, out var hitOb, out bool hitObRadius))
            {
                if (pointMode != PointEdit.Off) SelectObstacleHandle(hitOb, hitObRadius);
                return;
            }
            if (HitTestCorner(e.X, e.Y, out var hitRect, out int hitCorner))
            {
                // In point-edit mode a click on a handle selects it for the arrow-pad.
                if (pointMode != PointEdit.Off) SelectCorner(hitRect, hitCorner);
                return;
            }
            if (HitTestVertex(e.X, e.Y, out int hitVertex))
            {
                if (pointMode != PointEdit.Off) SelectVertex(hitVertex);
                return;
            }

            if (!chkMarkParcel.Checked) return;

            PointLatLng pt = gMapControl.FromLocalToLatLng(e.X, e.Y);
            foreach (var p in parcels)
            {
                if (!CadastralWfsClient.Contains(p, pt.Lat, pt.Lng)) continue;

                string k = KeyOf(p);
                if (k == null) continue;
                if (selected.ContainsKey(k)) selected.Remove(k);
                else selected[k] = p;

                InvalidateMadeBoundary(); // selection changed -> the built boundary is stale
                RenderParcels();
                UpdateSelInfo();
                return; // toggle only the topmost hit
            }
        }

        private void UpdateSelInfo()
        {
            double areaM2 = 0;
            foreach (var p in selected.Values)
                if (p.Geom3765 != null) areaM2 += p.Geom3765.Area;

            lblSelInfo.Text = gStr.gsSelected + ": " + selected.Count +
                "  ·  " + (areaM2 / 10000.0).ToString("F2") + " ha";
        }

        private void chkMarkParcel_CheckedChanged(object sender, EventArgs e)
        {
            if (chkMarkParcel.Checked)
            {
                btnAddPoint.Checked = false; btnDeletePoint.Checked = false;
                btnDeleteObstacle.Checked = false;
            }
            // In mark mode left-click selects parcels, so pan with the right button.
            UpdateDragButton();
            lblStatus.Text = chkMarkParcel.Checked
                ? gStr.gsMarkParcel + " — " + gStr.gsCadastralParcels
                : "";
        }

        // ── Bridge: a draggable 4-corner quad to connect non-adjacent parcels ──
        private void btnAddBridge_Click(object sender, EventArgs e)
        {
            var area = gMapControl.ViewArea;
            double cLat = (area.LocationTopLeft.Lat + area.LocationRightBottom.Lat) / 2.0;
            double cLon = (area.LocationTopLeft.Lng + area.LocationRightBottom.Lng) / 2.0;
            double hLat = (area.LocationTopLeft.Lat - area.LocationRightBottom.Lat) * 0.15;
            double hLon = (area.LocationRightBottom.Lng - area.LocationTopLeft.Lng) * 0.15;

            var rect = new BridgeRect();
            rect.Corners.Add(new PointLatLng(cLat + hLat, cLon - hLon)); // top-left
            rect.Corners.Add(new PointLatLng(cLat + hLat, cLon + hLon)); // top-right
            rect.Corners.Add(new PointLatLng(cLat - hLat, cLon + hLon)); // bottom-right
            rect.Corners.Add(new PointLatLng(cLat - hLat, cLon - hLon)); // bottom-left

            BuildRectVisuals(rect);
            bridgeRects.Add(rect);
            InvalidateMadeBoundary();
            gMapControl.Invalidate();
            lblStatus.Text = gStr.gsBridgeAdded;
        }

        private void BuildRectVisuals(BridgeRect rect)
        {
            rect.Poly = new GMapPolygon(new List<PointLatLng>(rect.Corners), "bridge")
            {
                Fill = new SolidBrush(Color.FromArgb(60, Color.Orange)),
                Stroke = new Pen(Color.Orange, 2f)
            };
            bridgeOverlay.Polygons.Add(rect.Poly);

            foreach (var c in rect.Corners)
            {
                var m = new CornerMarker(c);
                rect.Markers.Add(m);
                bridgeOverlay.Markers.Add(m);
            }
        }

        private void RedrawRect(BridgeRect rect)
        {
            rect.Poly.Points.Clear();
            rect.Poly.Points.AddRange(rect.Corners);
            gMapControl.UpdatePolygonLocalPosition(rect.Poly);
        }

        // Find a bridge corner handle near a screen point (touch-friendly radius).
        private bool HitTestCorner(int x, int y, out BridgeRect rect, out int corner)
        {
            foreach (var r in bridgeRects)
            {
                for (int i = 0; i < r.Corners.Count; i++)
                {
                    GPoint sp = gMapControl.FromLatLngToLocal(r.Corners[i]);
                    if (Math.Abs(sp.X - x) <= 16 && Math.Abs(sp.Y - y) <= 16)
                    {
                        rect = r; corner = i; return true;
                    }
                }
            }
            rect = null; corner = -1; return false;
        }

        private void gMap_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            // tap modes; don't drag-grab
            if (btnAddPoint.Checked || btnDeletePoint.Checked || btnDeleteObstacle.Checked) return;
            if (HitTestObstacleHandle(e.X, e.Y, out var ob, out bool obRadius))
            {
                dragObstacle = ob; dragObstacleRadius = obRadius;
                gMapControl.CanDragMap = false;
                return;
            }
            if (HitTestCorner(e.X, e.Y, out var r, out int c))
            {
                dragRect = r; dragCorner = c;
                gMapControl.CanDragMap = false; // don't pan while dragging a corner
                return;
            }
            if (HitTestVertex(e.X, e.Y, out int vi))
            {
                dragVertex = vi;
                PushUndo();
                gMapControl.CanDragMap = false;
            }
        }

        private void gMap_MouseMove(object sender, MouseEventArgs e)
        {
            if (dragRect != null)
            {
                PointLatLng pt = gMapControl.FromLocalToLatLng(e.X, e.Y);
                dragRect.Corners[dragCorner] = pt;
                dragRect.Markers[dragCorner].Position = pt;
                gMapControl.UpdateMarkerLocalPosition(dragRect.Markers[dragCorner]);
                RedrawRect(dragRect);
                InvalidateMadeBoundary();
                gMapControl.Invalidate();
            }
            else if (dragVertex >= 0)
            {
                PointLatLng pt = gMapControl.FromLocalToLatLng(e.X, e.Y);
                madeMarkers[dragVertex].Position = pt;
                gMapControl.UpdateMarkerLocalPosition(madeMarkers[dragVertex]);
                madeRing[dragVertex] = new Wgs84(pt.Lat, pt.Lng);
                RedrawMadeBoundary();
                gMapControl.Invalidate();
            }
            else if (dragObstacle != null)
            {
                PointLatLng pt = gMapControl.FromLocalToLatLng(e.X, e.Y);
                if (dragObstacleRadius)
                    dragObstacle.RadiusM = Math.Max(1.0, MetersBetween(dragObstacle.Center, pt));
                else
                    dragObstacle.Center = pt;
                RedrawObstacle(dragObstacle);
                gMapControl.Invalidate();
            }
        }

        private void gMap_MouseUp(object sender, MouseEventArgs e)
        {
            if (dragRect != null || dragVertex >= 0 || dragObstacle != null)
            {
                dragRect = null; dragCorner = -1; dragVertex = -1; dragObstacle = null;
                gMapControl.CanDragMap = true;
            }
        }

        private void ClearBridges()
        {
            bridgeRects.Clear();
            dragRect = null; dragCorner = -1;
            bridgeOverlay.Polygons.Clear();
            bridgeOverlay.Markers.Clear();
            ResetPointEdit();
        }

        private void btnClearSel_Click(object sender, EventArgs e)
        {
            selected.Clear();
            ClearBridges();
            InvalidateMadeBoundary();
            RenderParcels();
            UpdateSelInfo();
        }

        // ── Point edit (touch arrow-pad) ──────────────────────────────────
        // POINT button cycles: OFF -> Mark (red) -> [tap a corner] Move (green) -> OFF.
        private void btnPoint_Click(object sender, EventArgs e)
        {
            if (pointMode == PointEdit.Off) SetPointMode(PointEdit.Mark);
            else ResetPointEdit(); // release / turn off
        }

        private void SetPointMode(PointEdit mode)
        {
            pointMode = mode;
            switch (mode)
            {
                case PointEdit.Off:
                    btnPoint.BackColor = Color.Gainsboro;
                    lblPointMode.Text = "";
                    break;
                case PointEdit.Mark:
                    btnPoint.BackColor = Color.IndianRed;
                    lblPointMode.Text = gStr.gsMarkPoint;
                    break;
                case PointEdit.Move:
                    btnPoint.BackColor = Color.LimeGreen;
                    lblPointMode.Text = gStr.gsMovePoint;
                    break;
            }
            UpdateArrowColors();
        }

        private void ResetPointEdit()
        {
            if (selMarker != null) { selMarker.Selected = false; selMarker = null; }
            selRect = null; selCorner = -1; selVertex = -1;
            selObstacle = null; selObstacleRadius = false;
            pointMode = PointEdit.Off;
            btnPoint.BackColor = Color.Gainsboro;
            lblPointMode.Text = "";
            UpdateArrowColors();
            gMapControl.Invalidate();
        }

        private void SelectCorner(BridgeRect rect, int corner)
        {
            if (selMarker != null) selMarker.Selected = false;
            selRect = rect; selCorner = corner; selVertex = -1; selObstacle = null;
            selMarker = rect.Markers[corner];
            selMarker.Selected = true;
            SetPointMode(PointEdit.Move);
            gMapControl.Invalidate();
        }

        // Select a boundary vertex for editing (move with arrows/drag). Snapshots for Undo.
        private void SelectVertex(int idx)
        {
            if (selMarker != null) selMarker.Selected = false;
            selRect = null; selCorner = -1; selVertex = idx; selObstacle = null;
            selMarker = madeMarkers[idx];
            selMarker.Selected = true;
            PushUndo();
            SetPointMode(PointEdit.Move);
            gMapControl.Invalidate();
        }

        // Arrows are blue while panning, green while a point is selected for moving.
        private void UpdateArrowColors()
        {
            Color c = pointMode == PointEdit.Move ? Color.Green : Color.RoyalBlue;
            btnNavUp.ForeColor = c;
            btnNavDown.ForeColor = c;
            btnNavLeft.ForeColor = c;
            btnNavRight.ForeColor = c;
        }

        private void btnNavUp_Click(object sender, EventArgs e) => Nav(0, -1);
        private void btnNavDown_Click(object sender, EventArgs e) => Nav(0, 1);
        private void btnNavLeft_Click(object sender, EventArgs e) => Nav(-1, 0);
        private void btnNavRight_Click(object sender, EventArgs e) => Nav(1, 0);

        // Move the selected point by the step (Fine/Coarse), else pan the map by 20%.
        private void Nav(int dx, int dy)
        {
            if (pointMode == PointEdit.Move && selMarker != null)
                MoveSelectedPoint(dx * stepPx, dy * stepPx);
            else
                PanByFraction(dx * 0.2, dy * 0.2);
        }

        private void MoveSelectedPoint(int dxPx, int dyPx)
        {
            GPoint sp = gMapControl.FromLatLngToLocal(selMarker.Position);
            PointLatLng pt = gMapControl.FromLocalToLatLng((int)sp.X + dxPx, (int)sp.Y + dyPx);
            ApplyPointMove(pt);
        }

        // Apply a new position to the currently selected handle (bridge corner OR boundary vertex).
        private void ApplyPointMove(PointLatLng pt)
        {
            selMarker.Position = pt;
            gMapControl.UpdateMarkerLocalPosition(selMarker);

            if (selVertex >= 0) // boundary vertex: update the ring + green preview, keep Save on
            {
                madeRing[selVertex] = new Wgs84(pt.Lat, pt.Lng);
                RedrawMadeBoundary();
            }
            else if (selRect != null) // bridge corner: the union must be re-made
            {
                selRect.Corners[selCorner] = pt;
                RedrawRect(selRect);
                InvalidateMadeBoundary();
            }
            else if (selObstacle != null) // obstacle: move the center or stretch the radius
            {
                if (selObstacleRadius)
                    selObstacle.RadiusM = Math.Max(1.0, MetersBetween(selObstacle.Center, pt));
                else
                    selObstacle.Center = pt;
                RedrawObstacle(selObstacle);
            }
            gMapControl.Invalidate();
        }

        private void PanByFraction(double fracLon, double fracLat)
        {
            var area = gMapControl.ViewArea;
            double spanLat = area.LocationTopLeft.Lat - area.LocationRightBottom.Lat;
            double spanLon = area.LocationRightBottom.Lng - area.LocationTopLeft.Lng;
            var p = gMapControl.Position;
            // dy: up arrow (fracLat<0) should move the view north (increase lat)
            gMapControl.Position = new PointLatLng(
                p.Lat - fracLat * spanLat,
                p.Lng + fracLon * spanLon);
        }

        private void btnFine_Click(object sender, EventArgs e)
        {
            stepPx = 5;
            btnFine.BackColor = Color.LightSteelBlue;
            btnCoarse.BackColor = SystemColors.Control;
            btnFine.UseVisualStyleBackColor = false;
            btnCoarse.UseVisualStyleBackColor = true;
        }

        private void btnCoarse_Click(object sender, EventArgs e)
        {
            stepPx = 20;
            btnCoarse.BackColor = Color.LightSteelBlue;
            btnFine.BackColor = SystemColors.Control;
            btnCoarse.UseVisualStyleBackColor = false;
            btnFine.UseVisualStyleBackColor = true;
        }

        // Set a bool property by name if it exists (for optional GMap touch flags).
        private static void TrySetBool(object obj, string prop, bool value)
        {
            try { obj.GetType().GetProperty(prop)?.SetValue(obj, value); } catch { }
        }

        // ── Build boundary from selected parcels (NTS union) ──────────────
        private void btnMakeBoundary_Click(object sender, EventArgs e)
        {
            if (selected.Count == 0)
            {
                lblStatus.Text = gStr.gsSelectParcelsFirst;
                return;
            }

            var bridgePolys = new List<Polygon>();
            foreach (var r in bridgeRects)
            {
                var quad = CadastralWfsClient.MakeQuad3765(r.Corners);
                if (quad != null) bridgePolys.Add(quad);
            }

            madeRing = CadastralWfsClient.UnionOuterRingWgs84(selected.Values, bridgePolys);
            if (madeRing == null || madeRing.Count < 3)
            {
                lblStatus.Text = gStr.gsSelectParcelsFirst;
                return;
            }

            DrawMadeBoundary();
            // Bridges are now baked into the boundary; remove their handles/quads so the map
            // isn't cluttered (if something's wrong, Clear and start over).
            ClearBridges();
            BuildMadeHandles();          // Phase 4: one handle per boundary vertex
            undoStack.Clear();
            EnableEditButtons(true);
            btnSaveBoundary.Enabled = true;
            lblStatus.Text = gStr.gsBoundaryReady;
        }

        private void DrawMadeBoundary()
        {
            madeOverlay.Polygons.Clear();
            var pts = new List<PointLatLng>(madeRing.Count);
            foreach (var w in madeRing) pts.Add(new PointLatLng(w.Latitude, w.Longitude));
            madeOverlay.Polygons.Add(new GMapPolygon(pts, "made")
            {
                Fill = new SolidBrush(Color.FromArgb(40, Color.LimeGreen)),
                Stroke = new Pen(Color.LimeGreen, 5f)
            });
            gMapControl.Invalidate();
        }

        // Update the green boundary polygon in place (after a vertex move), keep Save enabled.
        private void RedrawMadeBoundary()
        {
            if (madeOverlay.Polygons.Count == 0) { DrawMadeBoundary(); return; }
            var poly = madeOverlay.Polygons[0];
            poly.Points.Clear();
            foreach (var w in madeRing) poly.Points.Add(new PointLatLng(w.Latitude, w.Longitude));
            gMapControl.UpdatePolygonLocalPosition(poly);
        }

        // One draggable/selectable handle per boundary vertex (Phase 4 editing).
        private void BuildMadeHandles()
        {
            editOverlay.Markers.Clear();
            madeMarkers.Clear();
            if (madeRing == null) return;
            foreach (var w in madeRing)
            {
                var m = new CornerMarker(new PointLatLng(w.Latitude, w.Longitude));
                madeMarkers.Add(m);
                editOverlay.Markers.Add(m);
            }
            gMapControl.Invalidate();
        }

        private void EnableEditButtons(bool on)
        {
            btnAddPoint.Enabled = true; // always available (also starts a manual polygon)
            btnDeletePoint.Enabled = on;
            btnUndo.Enabled = on && undoStack.Count > 0;
        }

        private void ClearMadeEdit()
        {
            editOverlay.Markers.Clear();
            madeMarkers.Clear();
            undoStack.Clear();
            selVertex = -1; dragVertex = -1;
            btnAddPoint.Checked = false;
            btnDeletePoint.Checked = false;
            EnableEditButtons(false);
        }

        private void InvalidateMadeBoundary()
        {
            madeRing = null;
            madeOverlay.Polygons.Clear();
            ShowExistingOuter(true); // no preview ring anymore -> show the field's own boundary
            UpdateSaveEnabled();
            ClearMadeEdit();
        }

        // Save applies the made ring and/or the obstacle changes.
        private void UpdateSaveEnabled()
        {
            btnSaveBoundary.Enabled = madeRing != null
                || obstacles.Count > 0 || deletedInnerIdx.Count > 0;
        }

        // ── Phase 4: boundary vertex add / delete / undo ──────────────────
        private void PushUndo()
        {
            if (madeRing == null) return;
            undoStack.Add(new List<Wgs84>(madeRing)); // Wgs84 elements are replaced, not mutated
            if (undoStack.Count > 5) undoStack.RemoveAt(0);
            btnUndo.Enabled = true;
        }

        private void btnUndo_Click(object sender, EventArgs e)
        {
            if (undoStack.Count == 0) return;
            madeRing = undoStack[undoStack.Count - 1];
            undoStack.RemoveAt(undoStack.Count - 1);
            ResetPointEdit();
            DrawMadeBoundary();
            BuildMadeHandles();
            btnUndo.Enabled = undoStack.Count > 0;
            lblStatus.Text = "";
        }

        private void btnAddPoint_CheckedChanged(object sender, EventArgs e)
        {
            if (btnAddPoint.Checked)
            {
                chkMarkParcel.Checked = false; // exclusive tap mode
                btnDeletePoint.Checked = false;
                btnDeleteObstacle.Checked = false;
                ResetPointEdit();
                // With no boundary yet, start a manual polygon: spawn a triangle the user can
                // expand (drag corners) and add points to. (Otherwise taps insert points.)
                if (madeRing == null) CreateManualTriangle();
                lblStatus.Text = gStr.gsAddPoint;
            }
            UpdateDragButton();
        }

        // Spawn a default triangle in the centre of the view (~1/3 of it) as the start of a
        // manual boundary. Reuses the Phase-4 edit machinery (handles, move/add/delete/undo).
        private void CreateManualTriangle()
        {
            var area = gMapControl.ViewArea;
            double cLat = (area.LocationTopLeft.Lat + area.LocationRightBottom.Lat) / 2;
            double cLon = (area.LocationTopLeft.Lng + area.LocationRightBottom.Lng) / 2;
            double hLat = Math.Abs(area.LocationTopLeft.Lat - area.LocationRightBottom.Lat) / 6;
            double hLon = Math.Abs(area.LocationRightBottom.Lng - area.LocationTopLeft.Lng) / 6;

            madeRing = new List<Wgs84>
            {
                new Wgs84(cLat + hLat, cLon),        // top
                new Wgs84(cLat - hLat, cLon - hLon), // bottom-left
                new Wgs84(cLat - hLat, cLon + hLon), // bottom-right
            };

            DrawMadeBoundary();
            BuildMadeHandles();
            undoStack.Clear();
            EnableEditButtons(true);
            btnSaveBoundary.Enabled = true;
            lblStatus.Text = gStr.gsBoundaryReady;
        }

        // Delete point is a tap mode: while active, a tap on a vertex deletes it (keeps deleting).
        private void btnDeletePoint_CheckedChanged(object sender, EventArgs e)
        {
            if (btnDeletePoint.Checked)
            {
                btnAddPoint.Checked = false;
                chkMarkParcel.Checked = false;
                btnDeleteObstacle.Checked = false;
                ResetPointEdit();
                lblStatus.Text = gStr.gsDeletePoint;
            }
            UpdateDragButton();
        }

        private void btnDeleteObstacle_CheckedChanged(object sender, EventArgs e)
        {
            if (btnDeleteObstacle.Checked)
            {
                btnAddPoint.Checked = false;
                btnDeletePoint.Checked = false;
                chkMarkParcel.Checked = false;
                ResetPointEdit();
                lblStatus.Text = gStr.gsDeleteObstacle;
            }
            UpdateDragButton();
        }

        private void DeleteVertexAt(int idx)
        {
            if (madeRing == null || idx < 0 || idx >= madeRing.Count) return;
            if (madeRing.Count <= 3) { lblStatus.Text = gStr.gsDeletePoint; return; } // need >= 3
            PushUndo();
            madeRing.RemoveAt(idx);
            DrawMadeBoundary();
            BuildMadeHandles();
            lblStatus.Text = gStr.gsDeletePoint;
        }

        private void InsertBoundaryPoint(int x, int y)
        {
            PointLatLng pt = gMapControl.FromLocalToLatLng(x, y);
            int seg = NearestSegment(x, y);
            PushUndo();
            madeRing.Insert(seg + 1, new Wgs84(pt.Lat, pt.Lng));
            DrawMadeBoundary();
            BuildMadeHandles();
            lblStatus.Text = gStr.gsAddPoint;
        }

        // Index i of the ring segment (i, i+1) closest to a screen point.
        private int NearestSegment(int x, int y)
        {
            int n = madeRing.Count, best = 0;
            double bestD = double.MaxValue;
            for (int i = 0; i < n; i++)
            {
                GPoint a = gMapControl.FromLatLngToLocal(new PointLatLng(madeRing[i].Latitude, madeRing[i].Longitude));
                GPoint b = gMapControl.FromLatLngToLocal(new PointLatLng(madeRing[(i + 1) % n].Latitude, madeRing[(i + 1) % n].Longitude));
                double d = DistPointToSeg(x, y, a.X, a.Y, b.X, b.Y);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        private static double DistPointToSeg(double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax, dy = by - ay;
            double len2 = dx * dx + dy * dy;
            double t = len2 <= 0 ? 0 : ((px - ax) * dx + (py - ay) * dy) / len2;
            t = Math.Max(0, Math.Min(1, t));
            double cx = ax + t * dx, cy = ay + t * dy;
            return Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
        }

        private bool HitTestVertex(int x, int y, out int idx)
        {
            for (int i = 0; i < madeMarkers.Count; i++)
            {
                GPoint sp = gMapControl.FromLatLngToLocal(madeMarkers[i].Position);
                if (Math.Abs(sp.X - x) <= 16 && Math.Abs(sp.Y - y) <= 16) { idx = i; return true; }
            }
            idx = -1; return false;
        }

        private void UpdateDragButton()
        {
            gMapControl.DragButton = (chkMarkParcel.Checked || btnAddPoint.Checked
                || btnDeletePoint.Checked || btnDeleteObstacle.Checked)
                ? MouseButtons.Right : MouseButtons.Left;
        }

        // ── Existing field boundary + obstacles (inner boundaries) ────────
        // Show the field's current outer boundary and its inner boundaries (obstacles) so
        // both can be edited here: "Edit boundary" loads the outer ring into madeRing,
        // "Add/Delete obstacle" manages circles saved as inner boundaries.
        private void LoadExistingField()
        {
            if (mf.bnd.bndList.Count == 0 || mf.bnd.bndList[0].fenceLine.Count < 3)
            {
                btnEditBoundary.Enabled = false;
                return;
            }

            existingOuterRing = new List<Wgs84>(mf.bnd.bndList[0].fenceLine.Count);
            var pts = new List<PointLatLng>(mf.bnd.bndList[0].fenceLine.Count);
            foreach (var v in mf.bnd.bndList[0].fenceLine)
            {
                Wgs84 w = mf.AppModel.LocalPlane.ConvertGeoCoordToWgs84(v.ToGeoCoord());
                existingOuterRing.Add(w);
                pts.Add(new PointLatLng(w.Latitude, w.Longitude));
            }
            existingOuterPoly = new GMapPolygon(pts, "fieldOuter")
            {
                Fill = new SolidBrush(Color.FromArgb(15, Color.ForestGreen)),
                Stroke = new Pen(Color.ForestGreen, 3f)
            };
            existingOverlay.Polygons.Add(existingOuterPoly);
            btnEditBoundary.Enabled = true;

            for (int i = 1; i < mf.bnd.bndList.Count; i++)
            {
                if (mf.bnd.bndList[i].fenceLine.Count < 3) continue;
                var ipts = new List<PointLatLng>(mf.bnd.bndList[i].fenceLine.Count);
                foreach (var v in mf.bnd.bndList[i].fenceLine)
                {
                    Wgs84 w = mf.AppModel.LocalPlane.ConvertGeoCoordToWgs84(v.ToGeoCoord());
                    ipts.Add(new PointLatLng(w.Latitude, w.Longitude));
                }
                var poly = new GMapPolygon(ipts, "fieldInner")
                {
                    Fill = new SolidBrush(Color.FromArgb(70, Color.OrangeRed)),
                    Stroke = new Pen(Color.OrangeRed, 2.5f)
                };
                existingOverlay.Polygons.Add(poly);
                existingInners.Add(new ExistingInner { BndIndex = i, Pts = ipts, Poly = poly });
            }
        }

        private void ShowExistingOuter(bool show)
        {
            if (existingOuterPoly == null) return;
            bool shown = existingOverlay.Polygons.Contains(existingOuterPoly);
            if (show && !shown) existingOverlay.Polygons.Add(existingOuterPoly);
            else if (!show && shown) existingOverlay.Polygons.Remove(existingOuterPoly);
        }

        // Load the field's outer boundary into madeRing: all Phase-4 editing (handles,
        // ── Source ring (field 2026-07-16) ─────────────────────────────────
        // Boundary.txt is densified to 1-3 m spacing by FixFenceLine (the stock
        // coverage/fence/turn code needs dense points), so editing the saved
        // boundary showed 100+ handles instead of the 8-10 the user placed.
        // The ORIGINAL points are kept in BoundarySource.txt next to the field;
        // edit mode loads those, with a corner-recovery (RDP) fallback for
        // fields saved before this file existed.

        private string SourceRingPath()
        {
            if (string.IsNullOrEmpty(mf.currentFieldDirectory)) return null;
            return Path.Combine(RegistrySettings.fieldsDirectory,
                mf.currentFieldDirectory, "BoundarySource.txt");
        }

        private void SaveSourceRing(List<Wgs84> ring)
        {
            string path = SourceRingPath();
            if (path == null || ring == null || ring.Count < 3) return;
            try
            {
                using (var sw = new StreamWriter(path, false))
                {
                    sw.WriteLine("$BoundarySource");
                    sw.WriteLine(ring.Count.ToString(CultureInfo.InvariantCulture));
                    foreach (var w in ring)
                        sw.WriteLine(w.Latitude.ToString("F8", CultureInfo.InvariantCulture) + ","
                                   + w.Longitude.ToString("F8", CultureInfo.InvariantCulture));
                }
            }
            catch (Exception ex) { Log.EventWriter("Geoportal: save source ring failed " + ex.Message); }
        }

        private List<Wgs84> LoadSourceRing()
        {
            string path = SourceRingPath();
            try
            {
                if (path == null || !File.Exists(path)) return null;
                var ring = new List<Wgs84>();
                using (var sr = new StreamReader(path))
                {
                    sr.ReadLine(); // $BoundarySource
                    int n = int.Parse(sr.ReadLine(), CultureInfo.InvariantCulture);
                    for (int i = 0; i < n; i++)
                    {
                        var parts = sr.ReadLine().Split(',');
                        ring.Add(new Wgs84(
                            double.Parse(parts[0], CultureInfo.InvariantCulture),
                            double.Parse(parts[1], CultureInfo.InvariantCulture)));
                    }
                }
                return ring.Count >= 3 ? ring : null;
            }
            catch { return null; }
        }

        // The file can be stale (boundary replaced by KML import or driving after
        // the geoportal save) - accept it only when every source point lies within
        // 5 m of the current dense outline.
        private bool SourceMatchesExisting(List<Wgs84> src)
        {
            if (existingOuterRing == null || existingOuterRing.Count < 3) return false;

            var dense = new List<GeoCoord>(existingOuterRing.Count);
            foreach (var w in existingOuterRing)
                dense.Add(mf.AppModel.LocalPlane.ConvertWgs84ToGeoCoord(w));

            foreach (var s in src)
            {
                GeoCoord a = mf.AppModel.LocalPlane.ConvertWgs84ToGeoCoord(s);
                double best = double.MaxValue;
                foreach (var b in dense)
                {
                    double d = a.DistanceSquared(b);
                    if (d < best) best = d;
                }
                if (best > 25.0) return false;
            }
            return true;
        }

        // Corner recovery for old fields: Douglas-Peucker over the closed ring in
        // local meters - densified midpoints on straight runs collapse, corners stay.
        private List<Wgs84> SimplifyRing(List<Wgs84> ring, double tolMeters)
        {
            int n = ring.Count;
            if (n < 5) return new List<Wgs84>(ring);

            var p = new List<GeoCoord>(n);
            foreach (var w in ring) p.Add(mf.AppModel.LocalPlane.ConvertWgs84ToGeoCoord(w));

            //closed ring: anchor at point 0 and the point farthest from it
            int far = 1; double maxD = -1;
            for (int i = 1; i < n; i++)
            {
                double d = p[0].DistanceSquared(p[i]);
                if (d > maxD) { maxD = d; far = i; }
            }

            bool[] keep = new bool[n + 1];
            keep[0] = keep[far] = true;
            var pc = new List<GeoCoord>(p) { p[0] };   //wrap for the second half
            RdpKeep(pc, 0, far, tolMeters, keep);
            RdpKeep(pc, far, n, tolMeters, keep);

            var outRing = new List<Wgs84>();
            for (int i = 0; i < n; i++) if (keep[i]) outRing.Add(ring[i]);
            return outRing.Count >= 3 ? outRing : new List<Wgs84>(ring);
        }

        private static void RdpKeep(List<GeoCoord> p, int i0, int i1, double tol, bool[] keep)
        {
            if (i1 - i0 < 2) return;
            double ax = p[i0].Easting, ay = p[i0].Northing;
            double bx = p[i1].Easting, by = p[i1].Northing;
            double dx = bx - ax, dy = by - ay;
            double len2 = dx * dx + dy * dy;

            double maxD = -1; int idx = -1;
            for (int i = i0 + 1; i < i1; i++)
            {
                double t = len2 < 1e-9 ? 0
                    : Math.Max(0, Math.Min(1, ((p[i].Easting - ax) * dx + (p[i].Northing - ay) * dy) / len2));
                double ex = ax + t * dx - p[i].Easting, ey = ay + t * dy - p[i].Northing;
                double d = ex * ex + ey * ey;
                if (d > maxD) { maxD = d; idx = i; }
            }
            if (idx > 0 && maxD > tol * tol)
            {
                keep[idx] = true;
                RdpKeep(p, i0, idx, tol, keep);
                RdpKeep(p, idx, i1, tol, keep);
            }
        }

        // move/add/delete/undo) now works on it. Clicking again reverts to the original.
        private void btnEditBoundary_Click(object sender, EventArgs e)
        {
            if (existingOuterRing == null) return;

            //field 2026-07-16: edit the points the user actually placed, not the
            //densified fence - source file first, corner recovery as the fallback
            List<Wgs84> src = LoadSourceRing();
            madeRing = (src != null && SourceMatchesExisting(src))
                ? src
                : SimplifyRing(existingOuterRing, 0.25);
            ShowExistingOuter(false);
            DrawMadeBoundary();
            BuildMadeHandles();
            undoStack.Clear();
            EnableEditButtons(true);
            UpdateSaveEnabled();
            lblStatus.Text = gStr.gsBoundaryReady;
        }

        // ── Obstacles: circle = center handle (move) + edge handle (radius) ──
        private void btnAddObstacle_Click(object sender, EventArgs e)
        {
            if (existingOuterRing == null && madeRing == null)
            {
                lblStatus.Text = gStr.gsObstacleNeedsBoundary;
                return;
            }

            var area = gMapControl.ViewArea;
            var ob = new ObstacleCircle
            {
                Center = new PointLatLng(
                    (area.LocationTopLeft.Lat + area.LocationRightBottom.Lat) / 2.0,
                    (area.LocationTopLeft.Lng + area.LocationRightBottom.Lng) / 2.0),
                RadiusM = 10.0
            };
            BuildObstacleVisuals(ob);
            obstacles.Add(ob);
            UpdateSaveEnabled();
            gMapControl.Invalidate();
            lblStatus.Text = gStr.gsRadius + ": " + ob.RadiusM.ToString("F1") + " m";
        }

        private void BuildObstacleVisuals(ObstacleCircle ob)
        {
            ob.Poly = new GMapPolygon(CirclePoints(ob), "obstacle")
            {
                Fill = new SolidBrush(Color.FromArgb(70, Color.OrangeRed)),
                Stroke = new Pen(Color.OrangeRed, 2.5f)
            };
            obstacleOverlay.Polygons.Add(ob.Poly);

            ob.CenterMarker = new CornerMarker(ob.Center);
            ob.RadiusMarker = new CornerMarker(RadiusHandlePos(ob));
            obstacleOverlay.Markers.Add(ob.CenterMarker);
            obstacleOverlay.Markers.Add(ob.RadiusMarker);
        }

        private void RedrawObstacle(ObstacleCircle ob)
        {
            ob.Poly.Points.Clear();
            ob.Poly.Points.AddRange(CirclePoints(ob));
            gMapControl.UpdatePolygonLocalPosition(ob.Poly);

            ob.CenterMarker.Position = ob.Center;
            ob.RadiusMarker.Position = RadiusHandlePos(ob);
            gMapControl.UpdateMarkerLocalPosition(ob.CenterMarker);
            gMapControl.UpdateMarkerLocalPosition(ob.RadiusMarker);
            lblStatus.Text = gStr.gsRadius + ": " + ob.RadiusM.ToString("F1") + " m";
        }

        private static List<PointLatLng> CirclePoints(ObstacleCircle ob)
        {
            double mLat = ob.RadiusM / 111320.0;
            double mLon = ob.RadiusM / (111320.0 * Math.Cos(ob.Center.Lat * Math.PI / 180.0));
            var pts = new List<PointLatLng>(ObstacleCirclePts);
            for (int k = 0; k < ObstacleCirclePts; k++)
            {
                double a = 2.0 * Math.PI * k / ObstacleCirclePts;
                pts.Add(new PointLatLng(ob.Center.Lat + mLat * Math.Cos(a),
                                        ob.Center.Lng + mLon * Math.Sin(a)));
            }
            return pts;
        }

        // The resize handle sits on the east edge of the circle.
        private static PointLatLng RadiusHandlePos(ObstacleCircle ob)
        {
            double mLon = ob.RadiusM / (111320.0 * Math.Cos(ob.Center.Lat * Math.PI / 180.0));
            return new PointLatLng(ob.Center.Lat, ob.Center.Lng + mLon);
        }

        private bool HitTestObstacleHandle(int x, int y, out ObstacleCircle ob, out bool isRadius)
        {
            foreach (var o in obstacles)
            {
                GPoint rp = gMapControl.FromLatLngToLocal(o.RadiusMarker.Position);
                if (Math.Abs(rp.X - x) <= 16 && Math.Abs(rp.Y - y) <= 16)
                {
                    ob = o; isRadius = true; return true;
                }
                GPoint cp = gMapControl.FromLatLngToLocal(o.CenterMarker.Position);
                if (Math.Abs(cp.X - x) <= 16 && Math.Abs(cp.Y - y) <= 16)
                {
                    ob = o; isRadius = false; return true;
                }
            }
            ob = null; isRadius = false; return false;
        }

        private void SelectObstacleHandle(ObstacleCircle ob, bool isRadius)
        {
            if (selMarker != null) selMarker.Selected = false;
            selRect = null; selCorner = -1; selVertex = -1;
            selObstacle = ob; selObstacleRadius = isRadius;
            selMarker = isRadius ? ob.RadiusMarker : ob.CenterMarker;
            selMarker.Selected = true;
            SetPointMode(PointEdit.Move);
            gMapControl.Invalidate();
        }

        // Delete-obstacle tap: a new circle (tap inside it) or an existing inner boundary.
        private void DeleteObstacleAt(int x, int y)
        {
            PointLatLng pt = gMapControl.FromLocalToLatLng(x, y);

            for (int i = 0; i < obstacles.Count; i++)
            {
                if (MetersBetween(obstacles[i].Center, pt) > obstacles[i].RadiusM) continue;
                var ob = obstacles[i];
                obstacleOverlay.Polygons.Remove(ob.Poly);
                obstacleOverlay.Markers.Remove(ob.CenterMarker);
                obstacleOverlay.Markers.Remove(ob.RadiusMarker);
                if (selObstacle == ob) ResetPointEdit();
                obstacles.RemoveAt(i);
                UpdateSaveEnabled();
                gMapControl.Invalidate();
                lblStatus.Text = gStr.gsDeleteObstacle;
                return;
            }

            foreach (var inner in existingInners)
            {
                if (!PointInLatLngRing(inner.Pts, pt)) continue;
                deletedInnerIdx.Add(inner.BndIndex);
                existingOverlay.Polygons.Remove(inner.Poly);
                existingInners.Remove(inner);
                UpdateSaveEnabled();
                gMapControl.Invalidate();
                lblStatus.Text = gStr.gsDeleteObstacle;
                return;
            }
        }

        // Equirectangular approximation - plenty for field-size distances.
        private static double MetersBetween(PointLatLng a, PointLatLng b)
        {
            double dLat = (b.Lat - a.Lat) * 111320.0;
            double dLon = (b.Lng - a.Lng) * 111320.0
                * Math.Cos((a.Lat + b.Lat) * 0.5 * Math.PI / 180.0);
            return Math.Sqrt(dLat * dLat + dLon * dLon);
        }

        private static bool PointInLatLngRing(List<PointLatLng> ring, PointLatLng pt)
        {
            bool inside = false;
            int j = ring.Count - 1;
            for (int i = 0; i < ring.Count; j = i++)
            {
                if ((ring[i].Lat > pt.Lat) != (ring[j].Lat > pt.Lat) &&
                    pt.Lng < (ring[j].Lng - ring[i].Lng) * (pt.Lat - ring[i].Lat)
                        / (ring[j].Lat - ring[i].Lat) + ring[i].Lng)
                    inside = !inside;
            }
            return inside;
        }

        private static bool PointInWgsRing(List<Wgs84> ring, PointLatLng pt)
        {
            bool inside = false;
            int j = ring.Count - 1;
            for (int i = 0; i < ring.Count; j = i++)
            {
                if ((ring[i].Latitude > pt.Lat) != (ring[j].Latitude > pt.Lat) &&
                    pt.Lng < (ring[j].Longitude - ring[i].Longitude) * (pt.Lat - ring[i].Latitude)
                        / (ring[j].Latitude - ring[i].Latitude) + ring[i].Longitude)
                    inside = !inside;
            }
            return inside;
        }

        // Every circle must lie fully inside the outer ring and must not overlap another
        // obstacle (new circle or remaining existing inner boundary).
        private bool ValidateObstacles(List<Wgs84> outer)
        {
            for (int i = 0; i < obstacles.Count; i++)
            {
                var ob = obstacles[i];
                foreach (var p in CirclePoints(ob))
                {
                    if (!PointInWgsRing(outer, p))
                    {
                        lblStatus.Text = gStr.gsObstacleOutside;
                        return false;
                    }
                }

                for (int k = i + 1; k < obstacles.Count; k++)
                {
                    if (MetersBetween(ob.Center, obstacles[k].Center)
                        < ob.RadiusM + obstacles[k].RadiusM)
                    {
                        lblStatus.Text = gStr.gsObstacleOutside;
                        return false;
                    }
                }

                foreach (var inner in existingInners)
                {
                    bool overlap = PointInLatLngRing(inner.Pts, ob.Center);
                    if (!overlap)
                        foreach (var p in inner.Pts)
                            if (MetersBetween(ob.Center, p) < ob.RadiusM) { overlap = true; break; }
                    if (overlap)
                    {
                        lblStatus.Text = gStr.gsObstacleOutside;
                        return false;
                    }
                }
            }
            return true;
        }

        // Apply obstacle edits to the field: remove inner boundaries marked for deletion,
        // append each new circle as an inner boundary (non drive-thru), then rebuild.
        private bool ApplyObstacleChanges()
        {
            if (obstacles.Count == 0 && deletedInnerIdx.Count == 0) return false;

            deletedInnerIdx.Sort();
            for (int i = deletedInnerIdx.Count - 1; i >= 0; i--)
            {
                if (deletedInnerIdx[i] > 0 && deletedInnerIdx[i] < mf.bnd.bndList.Count)
                    mf.bnd.bndList.RemoveAt(deletedInnerIdx[i]);
            }
            deletedInnerIdx.Clear();

            foreach (var ob in obstacles)
            {
                var b = new CBoundaryList();
                foreach (var p in CirclePoints(ob))
                {
                    GeoCoord gc = mf.AppModel.LocalPlane.ConvertWgs84ToGeoCoord(new Wgs84(p.Lat, p.Lng));
                    b.fenceLine.Add(new vec3(gc));
                }
                int idx = mf.bnd.bndList.Count; // idx > 0 -> CalculateFenceArea fixes winding
                b.CalculateFenceArea(idx);
                b.FixFenceLine(idx);
                mf.bnd.bndList.Add(b);
            }
            obstacles.Clear();

            mf.FileSaveBoundary();
            mf.bnd.BuildTurnLines();
            mf.fd.UpdateFieldBoundaryGUIAreas();
            mf.CalculateMinMax();
            return true;
        }

        // ── Save boundary: always export a temp KML, then apply (new field / replace) ──
        private async void btnSaveBoundary_Click(object sender, EventArgs e)
        {
            bool obstacleChanges = obstacles.Count > 0 || deletedInnerIdx.Count > 0;

            // Obstacles must end up inside whichever outer ring will be in effect.
            if (obstacles.Count > 0)
            {
                List<Wgs84> effOuter = madeRing ?? existingOuterRing;
                if (effOuter == null)
                {
                    lblStatus.Text = gStr.gsObstacleNeedsBoundary;
                    return;
                }
                if (!ValidateObstacles(effOuter)) return;
            }

            // Obstacle-only save: the outer boundary is untouched, apply directly.
            if (madeRing == null)
            {
                if (!obstacleChanges)
                {
                    lblStatus.Text = gStr.gsSelectParcelsFirst;
                    return;
                }
                ApplyObstacleChanges();
                lblStatus.Text = gStr.gsBoundarySaved;
                Close();
                return;
            }

            if (madeRing.Count < 3)
            {
                lblStatus.Text = gStr.gsSelectParcelsFirst;
                return;
            }

            string name = mf.currentFieldDirectory;
            string tempKml = BoundaryKmlWriter.WriteTempKml(name, madeRing);

            // Empty field (no boundary / AB lines / recorded paths / flags) -> rebuild the
            // field from the KML so StartFix lands at the boundary bbox center. Otherwise ask.
            if (IsFieldEmpty())
            {
                await RebuildFieldFromKml(name, tempKml);
                return;
            }

            bool outside = StartFixOutsideBbox(madeRing);
            string msg = gStr.gsApplyBoundaryQ + "\n\n" + gStr.gsReplaceOuterOpt + "\n" + gStr.gsNewFieldOpt;
            if (outside) msg = gStr.gsStartFixOutsideWarning + "\n\n" + msg;

            DialogResult r = MessageBox.Show(msg, gStr.gsBoundaryFromGeoportal,
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            if (r == DialogResult.Cancel)
            {
                TryDelete(tempKml);
                return;
            }

            if (r == DialogResult.Yes) // replace outer boundary on current field, keep inner holes
            {
                ReplaceOuterBoundary(madeRing);
                ApplyObstacleChanges();
                SaveSourceRing(madeRing);   //original points for the next edit
                TryDelete(tempKml);
                lblStatus.Text = gStr.gsBoundarySaved;
                Close();
            }
            else // No = create a new field from the boundary
            {
                await RebuildFieldFromKml(name, tempKml);
            }
        }

        // Close + delete the (empty) current field, then recreate it from the KML via the
        // proven AOG pipeline (StartFix = boundary bbox center). Same path as Shape import.
        private async Task RebuildFieldFromKml(string name, string tempKml)
        {
            try
            {
                if (mf.isJobStarted)
                    await mf.FileSaveEverythingBeforeClosingField();

                string dir = Path.Combine(RegistrySettings.fieldsDirectory, name);
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
                catch (Exception ex) { Log.EventWriter("Geoportal: delete field dir failed " + ex.Message); }

                using (var kmlForm = new FormFieldKML(mf))
                    kmlForm.CreateFieldFromKml(tempKml, name);

                // The rebuilt field starts with just the outer boundary; the old inner
                // boundaries are gone, so only the new obstacle circles get applied.
                deletedInnerIdx.Clear();
                ApplyObstacleChanges();
                SaveSourceRing(madeRing);   //original points for the next edit
            }
            finally
            {
                TryDelete(tempKml);
            }
            Close();
        }

        // Replace the outer boundary (bndList[0]) on the current field, keeping inner holes.
        private void ReplaceOuterBoundary(List<Wgs84> ring)
        {
            var newB = new CBoundaryList();
            foreach (var w in ring)
            {
                GeoCoord gc = mf.AppModel.LocalPlane.ConvertWgs84ToGeoCoord(new Wgs84(w.Latitude, w.Longitude));
                newB.fenceLine.Add(new vec3(gc));
            }
            newB.CalculateFenceArea(0);
            newB.FixFenceLine(0);

            if (mf.bnd.bndList.Count > 0) mf.bnd.bndList[0] = newB;
            else mf.bnd.bndList.Add(newB);

            mf.FileSaveBoundary();
            mf.bnd.BuildTurnLines();
            mf.fd.UpdateFieldBoundaryGUIAreas();
            mf.CalculateMinMax();
        }

        private bool IsFieldEmpty()
        {
            return mf.bnd.bndList.Count == 0
                && mf.trk.gArr.Count == 0
                && mf.recPath.recList.Count == 0
                && mf.flagPts.Count == 0;
        }

        // True if StartFix (local-plane origin) is more than ~1 m outside the ring's bbox.
        private bool StartFixOutsideBbox(List<Wgs84> ring)
        {
            double minLat = double.MaxValue, maxLat = double.MinValue;
            double minLon = double.MaxValue, maxLon = double.MinValue;
            foreach (var w in ring)
            {
                if (w.Latitude < minLat) minLat = w.Latitude;
                if (w.Latitude > maxLat) maxLat = w.Latitude;
                if (w.Longitude < minLon) minLon = w.Longitude;
                if (w.Longitude > maxLon) maxLon = w.Longitude;
            }

            Wgs84 o = mf.AppModel.LocalPlane.Origin;
            double mLat = 1.0 / 111320.0;
            double mLon = 1.0 / (111320.0 * Math.Cos(o.Latitude * Math.PI / 180.0));
            return o.Latitude < minLat - mLat || o.Latitude > maxLat + mLat
                || o.Longitude < minLon - mLon || o.Longitude > maxLon + mLon;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private void chkParcels_CheckedChanged(object sender, EventArgs e)
        {
            if (chkParcels.Checked)
            {
                LoadParcelsFromCache();
            }
            else
            {
                parcelOverlay.Polygons.Clear();
                gMapControl.Invalidate();
                lblStatus.Text = gStr.gsParcelsHidden;
            }
        }

        // Refresh = the one place this window goes to the internet: pull parcels for the
        // current view from the WFS (e.g. when panned outside the cached area). Cache is not
        // written here (only Map App writes it).
        private void btnRefresh_Click(object sender, EventArgs e)
        {
            RefreshParcels();
        }

        private void btnGoToPos_Click(object sender, EventArgs e)
        {
            gMapControl.Position = new PointLatLng(
                mf.AppModel.CurrentLatLon.Latitude,
                mf.AppModel.CurrentLatLon.Longitude);
        }

        private void btnZoomIn_Click(object sender, EventArgs e)
        {
            if (gMapControl.Zoom < gMapControl.MaxZoom)
                gMapControl.Zoom = (int)gMapControl.Zoom + 1;
        }

        private void btnZoomOut_Click(object sender, EventArgs e)
        {
            if (gMapControl.Zoom > gMapControl.MinZoom)
                gMapControl.Zoom = (int)gMapControl.Zoom - 1;
        }

        // ── Window controls (touch, like the main window) ─────────────────
        private void btnWinFull_Click(object sender, EventArgs e)
        {
            // Toggle full screen (maximize) / normal. No dedicated "restore" icon in
            // Resources, so the maximize icon stays for both states.
            this.WindowState = this.WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal
                : FormWindowState.Maximized;
        }

        private void btnWinClose_Click(object sender, EventArgs e)
        {
            Close();
        }

        // Drag the borderless window by its background (top strip gaps / empty areas).
        private void FormDrag_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(this.Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
        }

        private void vehicleTimer_Tick(object sender, EventArgs e)
        {
            if (vehicleMarker == null || mf == null) return;
            vehicleMarker.Position = new PointLatLng(
                mf.AppModel.CurrentLatLon.Latitude,
                mf.AppModel.CurrentLatLon.Longitude);
            gMapControl.UpdateMarkerLocalPosition(vehicleMarker);
            gMapControl.Invalidate();
        }

        private void FormGeoportalBoundary_FormClosing(object sender, FormClosingEventArgs e)
        {
            refreshTimer.Stop();
            vehicleTimer.Stop();
        }
    }
}
