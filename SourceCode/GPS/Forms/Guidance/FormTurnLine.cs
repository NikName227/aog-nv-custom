using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Translations;
using AgOpenGPS.Forms;
using GMap.NET;
using GMap.NET.WindowsForms;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AgOpenGPS
{
    /// <summary>
    /// Turn limit lines editor (geoportal-style map). A line is drawn at each end of the
    /// field marking how far the vehicle WHEELS may go while turning — usually outside
    /// the boundary, over the road. "Add line" drops a 2-point line in the view center;
    /// points are dragged, added and deleted; "Delete line" removes a whole line.
    /// Preview shows the wheel-safety line and whether a U-turn fits at each line.
    /// </summary>
    public partial class FormTurnLine : Form
    {
        private readonly FormGPS mf;

        private readonly GMapOverlay boundaryOverlay = new GMapOverlay("bnd");
        private readonly GMapOverlay headlandOverlay = new GMapOverlay("headland");
        private readonly GMapOverlay parcelOverlay = new GMapOverlay("parcels");
        private readonly GMapOverlay planOverlay = new GMapOverlay("plan");
        private readonly GMapOverlay previewOverlay = new GMapOverlay("preview");
        private readonly GMapOverlay lineOverlay = new GMapOverlay("lines");
        private readonly GMapOverlay vehicleOverlay = new GMapOverlay("vehicle");
        private VehicleMarker vehicleMarker;
        private readonly Timer vehicleTimer = new Timer();

        // one editable line: WGS84 points + its map visuals
        private class EditLine
        {
            public readonly List<PointLatLng> pts = new List<PointLatLng>();
            public GMapRoute route;
            public readonly List<CornerMarker> markers = new List<CornerMarker>();
        }

        private readonly List<EditLine> editLines = new List<EditLine>();
        private EditLine dragLine;
        private int dragIdx = -1;

        // round touch-friendly handle (same idea as the geoportal boundary editor)
        private class CornerMarker : GMapMarker
        {
            public CornerMarker(PointLatLng pos) : base(pos) { }

            public override void OnRender(Graphics g)
            {
                const float r = 9f;
                g.FillEllipse(Brushes.White, LocalPosition.X - r, LocalPosition.Y - r, 2 * r, 2 * r);
                using (var pen = new Pen(Color.OrangeRed, 2.5f))
                    g.DrawEllipse(pen, LocalPosition.X - r, LocalPosition.Y - r, 2 * r, 2 * r);
            }
        }

        // borderless-window drag (same as the main window)
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        private const int WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 0x2;

        public FormTurnLine(Form callingForm)
        {
            mf = callingForm as FormGPS;
            InitializeComponent();
            LocalizeText();

            // param-tab diagrams drawn live (GDI+) on the right of each style panel
            pnlK.Paint += (s, ev) => DrawTurnDiagram(ev.Graphics, DiagramArea(pnlK), 'K');
            pnlU.Paint += (s, ev) => DrawTurnDiagram(ev.Graphics, DiagramArea(pnlU), 'U');
            pnlRS.Paint += (s, ev) => DrawTurnDiagram(ev.Graphics, DiagramArea(pnlRS), 'R');

            vehicleTimer.Interval = 600;
            vehicleTimer.Tick += vehicleTimer_Tick;

            this.KeyPreview = true;
            this.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            this.MouseDown += FormDrag_MouseDown;
        }

        private void LocalizeText()
        {
            this.Text = gStr.gsTurnLimitLines;
            btnAddLine.Text = gStr.gsAddLine;
            btnAddPoint.Text = gStr.gsAddPoint;
            btnDeletePoint.Text = gStr.gsDeletePoint;
            btnDeleteLine.Text = gStr.gsDeleteLine;
            chkParcels.Text = gStr.gsCadastralParcels;
            btnGoToField.Text = gStr.gsGoToPosition;
            lblRadius.Text = gStr.gsTurnRadiusInfo + ": " + mf.yt.youTurnRadius.ToString("N1") + " m";
            lblStatus.Text = gStr.gsTurnLineHint;

            lblTrack.Text = gStr.gsTrack;
            btnMode1.Text = gStr.gsRowByRow;
            btnMode3.Text = gStr.gsZonesSkip;
            btnMode2.Text = gStr.gsContour;
            lblZones.Text = gStr.gsZones;
            chkHeadlandClip.Text = gStr.gsInsideHeadland;
            btnGeneratePlan.Text = gStr.gsGeneratePlan;
            btnAddPath.Text = gStr.gsAddPath;
            btnDeletePath.Text = gStr.gsDeletePath;
            btnResetPath.Text = gStr.gsResetPath;
            btnWizBack.Text = gStr.gsBack;
            btnWizNext.Text = gStr.gsNext;
            btnWizCancel.Text = gStr.gsCancel;

            btnStyleK.Text = gStr.gsTurnStyleK;
            btnStyleU.Text = gStr.gsTurnStyleU;
            btnStyleRS.Text = gStr.gsTurnStyleRS;
            lblExitK.Text = gStr.gsExitRadius;
            lblDistK.Text = gStr.gsEntryStartDist;
            lblAngleK.Text = gStr.gsEntryAngle;
            lblTransK.Text = gStr.gsTransitionRadius;
            //rigid K has no transition radius (covered by the hardcoded path smoothing)
            lblTransK.Visible = false;
            nudTransK.Visible = false;
            lblOutU.Text = gStr.gsExitRadius;
            lblInU.Text = gStr.gsEntryRadius;
            lblMinRS.Text = gStr.gsMinTurnRadius;
            lblTolerance.Text = gStr.gsTriggerTolerance;
            lblOvershootU.Text = gStr.gsOvershootDist;
        }

        private void FormTurnLine_Load(object sender, EventArgs e)
        {
            // base map = same WMS as the Map App window (shared config)
            string wmsUrl = Properties.Settings.Default.setMap_customWmsUrl;
            GeoportalWmsProvider.Instance.SetBaseUrl(
                string.IsNullOrWhiteSpace(wmsUrl) ? GeoportalWmsProvider.DefaultBaseUrl : wmsUrl);
            gMapControl.MapProvider = GeoportalWmsProvider.Instance;

            gMapControl.Position = new PointLatLng(
                mf.AppModel.CurrentLatLon.Latitude,
                mf.AppModel.CurrentLatLon.Longitude);
            gMapControl.Zoom = 16;
            gMapControl.ShowCenter = false;
            gMapControl.DragButton = MouseButtons.Left;

            gMapControl.Overlays.Add(parcelOverlay);
            gMapControl.Overlays.Add(boundaryOverlay);
            gMapControl.Overlays.Add(headlandOverlay);
            gMapControl.Overlays.Add(planOverlay);
            gMapControl.Overlays.Add(previewOverlay);
            gMapControl.Overlays.Add(lineOverlay);
            gMapControl.Overlays.Add(vehicleOverlay);

            vehicleMarker = new VehicleMarker(new PointLatLng(
                mf.AppModel.CurrentLatLon.Latitude,
                mf.AppModel.CurrentLatLon.Longitude));
            vehicleOverlay.Markers.Add(vehicleMarker);
            vehicleTimer.Start();

            gMapControl.MouseClick += gMap_MouseClick;
            gMapControl.MouseDown += gMap_MouseDown;
            gMapControl.MouseMove += gMap_MouseMove;
            gMapControl.MouseUp += gMap_MouseUp;

            // redesign 2026-07-16: 3-state activate (off / on / on+follow plan) and a
            // 2-state trigger toggle (immediate / after stop). Both buttons manage
            // their own state - AutoCheck off so a click never double-fires.
            chkActivate.AutoCheck = false;
            chkAutomatic.AutoCheck = false;
            activateState = !mf.tnl.isTurnActive ? 0
                : (mf.plan.isFollowPlan && mf.plan.HasPlan ? 2 : 1);
            afterStopMode = mf.tnl.isAfterStop;
            chkActivate.Click += (s, ev) => CycleActivate();
            chkAutomatic.Click += (s, ev) => { afterStopMode = !afterStopMode; ApplyTriggerVisual(); };
            ApplyActivateVisual();
            ApplyTriggerVisual();

            LoadTurnParams();
            SetupKeypadFields();

            LoadBoundary();
            LoadExistingLines();
            CenterOnField();

            // path planning: current per-field plan state
            FillTrackCombo();
            btnMode1.Checked = mf.plan.mode == CTurnPlan.ModeSequential;
            btnMode2.Checked = mf.plan.mode == CTurnPlan.ModeContour;
            btnMode3.Checked = mf.plan.mode == CTurnPlan.ModeZones;
            if (mf.plan.zoneCount >= (int)nudZones.Minimum && mf.plan.zoneCount <= (int)nudZones.Maximum)
                nudZones.Value = mf.plan.zoneCount;

            // mode buttons carry the confirmed pictograms (drawn at runtime)
            btnMode1.Image = TurnModeIcons.Make(1, 88, 82);
            btnMode2.Image = TurnModeIcons.Make(2, 88, 82);
            btnMode3.Image = TurnModeIcons.Make(3, 88, 82);
            btnMode1.TextImageRelation = TextImageRelation.ImageAboveText;
            btnMode2.TextImageRelation = TextImageRelation.ImageAboveText;
            btnMode3.TextImageRelation = TextImageRelation.ImageAboveText;

            // headland clip only makes sense with a headland defined
            bool hasHeadland = mf.bnd.bndList.Count > 0 && mf.bnd.bndList[0].hdLine.Count > 2;
            chkHeadlandClip.Enabled = hasHeadland;
            chkHeadlandClip.Checked = hasHeadland && mf.plan.clipToHeadland;
            chkHeadlandClip.CheckedChanged += (s, ev) => { DrawHeadland(); if (wizStep == 2) GenerateFromWizard(); };
            DrawHeadland();

            lstLines.SelectedIndexChanged += lstLines_SelectedIndexChanged;
            SetupPrereqChecklist();
            if (mf.tool.isToolOrchard) SetupOrchardPathTab();

            UpdatePlanInfo();
            if (mf.plan.HasPlan)
                mf.plan.RebuildGeometry();
            DrawPlanOverlay();   // also highlights the active AB line even without a plan

            // path planning is the DEFAULT view (redesign 2026-07-16)
            wizStep = 0;
            ShowTab(ViewTab.Path);
        }

        // ── row 1: 3-state activate + 2-state trigger toggle ──────────────
        // 0 = off (gray), 1 = turning on (light green), 2 = on + follow plan
        // (darker green, two-line caption). State 2 exists only with a plan.
        private int activateState;
        private bool afterStopMode;

        private void CycleActivate()
        {
            int states = mf.plan.HasPlan ? 3 : 2;
            activateState = (activateState + 1) % states;
            ApplyActivateVisual();
        }

        private void ApplyActivateVisual()
        {
            if (activateState == 0)
            {
                chkActivate.BackColor = Color.Gainsboro;
                chkActivate.ForeColor = Color.Black;
                chkActivate.Font = new Font("Tahoma", 12F, FontStyle.Bold);
                chkActivate.Text = gStr.gsActivateTurn;
            }
            else if (activateState == 1)
            {
                chkActivate.BackColor = Color.MediumSeaGreen;
                chkActivate.ForeColor = Color.Black;
                chkActivate.Font = new Font("Tahoma", 12F, FontStyle.Bold);
                chkActivate.Text = gStr.gsActivateTurn;
            }
            else
            {
                chkActivate.BackColor = Color.SeaGreen;
                chkActivate.ForeColor = Color.White;
                chkActivate.Font = new Font("Tahoma", 10F, FontStyle.Bold);
                chkActivate.Text = gStr.gsActivateTurn + "\n" + gStr.gsFollowPlan;
            }
        }

        private void ApplyTriggerVisual()
        {
            chkAutomatic.Text = afterStopMode ? gStr.gsTurnAfterStop : gStr.gsAutomaticTurn;
            chkAutomatic.BackColor = afterStopMode ? Color.Gold : Color.MediumAquamarine;
        }

        private void FormTurnLine_Shown(object sender, EventArgs e)
        {
            btnZoomIn.BringToFront();
            btnZoomOut.BringToFront();
            btnWinFull.BringToFront();
            btnWinClose.BringToFront();

            // WMS tiles often don't render until the view changes - nudge once
            int z = (int)gMapControl.Zoom;
            if (z > gMapControl.MinZoom) gMapControl.Zoom = z - 1;
            gMapControl.Zoom = z;
            gMapControl.ReloadMap();

            ZoomToField();   // redesign: the whole field neatly fills the view
            LoadParcelsFromCache();
            UpdatePreview();
            UpdateWizard();
        }

        // fit-to-field zoom: bbox of the fence + a margin, instead of a fixed deep zoom
        private void ZoomToField()
        {
            if (mf.bnd.bndList.Count == 0 || mf.bnd.bndList[0].fenceLine.Count < 3) return;

            double minLat = double.MaxValue, maxLat = double.MinValue;
            double minLng = double.MaxValue, maxLng = double.MinValue;
            foreach (var v in mf.bnd.bndList[0].fenceLine)
            {
                PointLatLng p = LocalToLatLng(v.easting, v.northing);
                if (p.Lat < minLat) minLat = p.Lat;
                if (p.Lat > maxLat) maxLat = p.Lat;
                if (p.Lng < minLng) minLng = p.Lng;
                if (p.Lng > maxLng) maxLng = p.Lng;
            }
            double mLat = (maxLat - minLat) * 0.15 + 1e-6;
            double mLng = (maxLng - minLng) * 0.15 + 1e-6;
            gMapControl.SetZoomToFitRect(RectLatLng.FromLTRB(
                minLng - mLng, maxLat + mLat, maxLng + mLng, minLat - mLat));
        }

        // ── conversions local plane <-> WGS84 ─────────────────────────────
        private PointLatLng LocalToLatLng(double easting, double northing)
        {
            Wgs84 w = mf.AppModel.LocalPlane.ConvertGeoCoordToWgs84(new GeoCoord(northing, easting));
            return new PointLatLng(w.Latitude, w.Longitude);
        }

        private vec3 LatLngToLocal(PointLatLng p)
        {
            GeoCoord gc = mf.AppModel.LocalPlane.ConvertWgs84ToGeoCoord(new Wgs84(p.Lat, p.Lng));
            return new vec3(gc);
        }

        // ── boundary + existing lines ─────────────────────────────────────
        private void LoadBoundary()
        {
            boundaryOverlay.Polygons.Clear();
            if (mf.bnd.bndList.Count == 0 || mf.bnd.bndList[0].fenceLine.Count < 3) return;

            var ring = new List<PointLatLng>();
            var fence = mf.bnd.bndList[0].fenceLine;
            for (int i = 0; i < fence.Count; i++)
                ring.Add(LocalToLatLng(fence[i].easting, fence[i].northing));

            boundaryOverlay.Polygons.Add(new GMapPolygon(ring, "fence")
            {
                Fill = new SolidBrush(Color.FromArgb(18, Color.LimeGreen)),
                Stroke = new Pen(Color.LimeGreen, 3f)
            });
        }

        // f05gui-08: draw the headland ring (bndList[0].hdLine) on the map ONLY while
        // the "Inside headland" (Unutar uvrtina) box is on - just the line, nothing else.
        private void DrawHeadland()
        {
            headlandOverlay.Routes.Clear();
            if (chkHeadlandClip.Enabled && chkHeadlandClip.Checked
                && mf.bnd.bndList.Count > 0 && mf.bnd.bndList[0].hdLine.Count > 2)
            {
                var hd = mf.bnd.bndList[0].hdLine;
                var ring = new List<PointLatLng>(hd.Count + 1);
                for (int i = 0; i < hd.Count; i++)
                    ring.Add(LocalToLatLng(hd[i].easting, hd[i].northing));
                ring.Add(ring[0]);   // close the ring
                headlandOverlay.Routes.Add(new GMapRoute(ring, "headland")
                {
                    Stroke = new Pen(Color.DarkOrange, 2.5f)
                });
            }
            gMapControl.Invalidate();
        }

        private void LoadExistingLines()
        {
            foreach (var ln in mf.tnl.lines)
            {
                if (ln.pts.Count < 2) continue;
                var el = new EditLine();
                for (int i = 0; i < ln.pts.Count; i++)
                    el.pts.Add(LocalToLatLng(ln.pts[i].easting, ln.pts[i].northing));
                editLines.Add(el);
                BuildLineVisuals(el);
            }
        }

        private void CenterOnField()
        {
            if (mf.bnd.bndList.Count > 0 && mf.bnd.bndList[0].fenceLine.Count > 2)
            {
                double e = 0, n = 0;
                var fence = mf.bnd.bndList[0].fenceLine;
                for (int i = 0; i < fence.Count; i++) { e += fence[i].easting; n += fence[i].northing; }
                gMapControl.Position = LocalToLatLng(e / fence.Count, n / fence.Count);
            }
        }

        // ── line visuals ──────────────────────────────────────────────────
        private void BuildLineVisuals(EditLine el)
        {
            el.route = new GMapRoute(new List<PointLatLng>(el.pts), "line")
            {
                Stroke = new Pen(Color.DarkOrange, 5f)
            };
            lineOverlay.Routes.Add(el.route);

            foreach (var p in el.pts)
            {
                var m = new CornerMarker(p);
                el.markers.Add(m);
                lineOverlay.Markers.Add(m);
            }
            gMapControl.Invalidate();
        }

        private void RemoveLineVisuals(EditLine el)
        {
            if (el.route != null) lineOverlay.Routes.Remove(el.route);
            foreach (var m in el.markers) lineOverlay.Markers.Remove(m);
            el.markers.Clear();
        }

        private void RebuildLineVisuals(EditLine el)
        {
            RemoveLineVisuals(el);
            BuildLineVisuals(el);
        }

        private void RedrawRoute(EditLine el)
        {
            el.route.Points.Clear();
            el.route.Points.AddRange(el.pts);
            gMapControl.UpdateRouteLocalPosition(el.route);
        }

        // ── add line / edit modes ─────────────────────────────────────────
        private void btnAddLine_Click(object sender, EventArgs e)
        {
            // Place the new line at the field end nearest the current view center: just
            // outside the boundary and spanning across it, so it already relates to the
            // real turn (the user then drags it out over the road). If there is no usable
            // boundary, fall back to a short line in the view center.
            var el = new EditLine();
            if (TryFieldEndLine(out PointLatLng a, out PointLatLng b))
            {
                el.pts.Add(a);
                el.pts.Add(b);
            }
            else
            {
                var area = gMapControl.ViewArea;
                double cLat = (area.LocationTopLeft.Lat + area.LocationRightBottom.Lat) / 2.0;
                double cLon = (area.LocationTopLeft.Lng + area.LocationRightBottom.Lng) / 2.0;
                double hLon = (area.LocationRightBottom.Lng - area.LocationTopLeft.Lng) * 0.18;
                el.pts.Add(new PointLatLng(cLat, cLon - hLon));
                el.pts.Add(new PointLatLng(cLat, cLon + hLon));
            }

            editLines.Add(el);
            BuildLineVisuals(el);
            UpdatePreview();
        }

        /// <summary>
        /// Suggest a starting turn line placed ACROSS the field end (perpendicular to the
        /// field's long/driving axis), pushed just past the boundary so there is turn room,
        /// and spanning the field width. It must be perpendicular to travel: a line drawn
        /// parallel to the pass would make the turn slide all the way down the AB line.
        /// The end is chosen by which side the current view is panned toward. Returns false
        /// without a usable boundary.
        /// </summary>
        private bool TryFieldEndLine(out PointLatLng a, out PointLatLng b)
        {
            a = b = new PointLatLng();
            if (mf.bnd.bndList.Count == 0) return false;
            var fence = mf.bnd.bndList[0].fenceLine;
            int n = fence.Count;
            if (n < 3) return false;

            // field centroid
            double ce = 0, cn = 0;
            for (int i = 0; i < n; i++) { ce += fence[i].easting; cn += fence[i].northing; }
            ce /= n; cn /= n;

            // principal (long) axis via the 2D covariance of the fence points
            double cxx = 0, cyy = 0, cxy = 0;
            for (int i = 0; i < n; i++)
            {
                double de = fence[i].easting - ce, dn = fence[i].northing - cn;
                cxx += de * de; cyy += dn * dn; cxy += de * dn;
            }
            double angle = 0.5 * Math.Atan2(2.0 * cxy, cxx - cyy);
            double ax = Math.Cos(angle), ay = Math.Sin(angle);   // long axis (driving dir)
            double px = -ay, py = ax;                            // perpendicular (across end)

            // extents along both axes (signed, relative to centroid)
            double maxA = double.MinValue, minA = double.MaxValue, maxP = 0;
            for (int i = 0; i < n; i++)
            {
                double de = fence[i].easting - ce, dn = fence[i].northing - cn;
                double pa = de * ax + dn * ay;
                double pp = Math.Abs(de * px + dn * py);
                if (pa > maxA) maxA = pa;
                if (pa < minA) minA = pa;
                if (pp > maxP) maxP = pp;
            }

            // which end is the view panned toward?
            var area = gMapControl.ViewArea;
            vec3 vc = LatLngToLocal(new PointLatLng(
                (area.LocationTopLeft.Lat + area.LocationRightBottom.Lat) / 2.0,
                (area.LocationTopLeft.Lng + area.LocationRightBottom.Lng) / 2.0));
            double pv = (vc.easting - ce) * ax + (vc.northing - cn) * ay;

            double s = pv >= 0 ? 1.0 : -1.0;
            double endA = pv >= 0 ? maxA : minA;               // signed axis coord of that end
            double offset = mf.yt.uturnDistanceFromBoundary + 2.0 * mf.yt.youTurnRadius;
            double half = maxP * 1.1;                           // span the field width + a bit

            double ee = ce + ax * (endA + s * offset);
            double en = cn + ay * (endA + s * offset);

            a = LocalToLatLng(ee - px * half, en - py * half);
            b = LocalToLatLng(ee + px * half, en + py * half);
            return true;
        }

        private void ExclusiveTapMode(CheckBox keep)
        {
            if (btnAddPoint != keep) btnAddPoint.Checked = false;
            if (btnDeletePoint != keep) btnDeletePoint.Checked = false;
            if (btnDeleteLine != keep) btnDeleteLine.Checked = false;
            UpdateDragButton();
        }

        private void btnAddPoint_CheckedChanged(object sender, EventArgs e)
        {
            if (btnAddPoint.Checked) ExclusiveTapMode(btnAddPoint);
            UpdateDragButton();
        }

        private void btnDeletePoint_CheckedChanged(object sender, EventArgs e)
        {
            if (btnDeletePoint.Checked) ExclusiveTapMode(btnDeletePoint);
            UpdateDragButton();
        }

        private void btnDeleteLine_CheckedChanged(object sender, EventArgs e)
        {
            if (btnDeleteLine.Checked) ExclusiveTapMode(btnDeleteLine);
            UpdateDragButton();
        }

        private void UpdateDragButton()
        {
            gMapControl.DragButton =
                (btnAddPoint.Checked || btnDeletePoint.Checked || btnDeleteLine.Checked)
                ? MouseButtons.Right : MouseButtons.Left;
        }

        // ── mouse editing ─────────────────────────────────────────────────
        private void gMap_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (btnAddPoint.Checked || btnDeletePoint.Checked || btnDeleteLine.Checked) return;

            if (HitTestMarker(e.X, e.Y, out EditLine el, out int idx))
            {
                dragLine = el;
                dragIdx = idx;
                gMapControl.CanDragMap = false;
            }
        }

        private void gMap_MouseMove(object sender, MouseEventArgs e)
        {
            if (dragLine == null) return;
            PointLatLng pt = gMapControl.FromLocalToLatLng(e.X, e.Y);
            dragLine.pts[dragIdx] = pt;
            dragLine.markers[dragIdx].Position = pt;
            gMapControl.UpdateMarkerLocalPosition(dragLine.markers[dragIdx]);
            RedrawRoute(dragLine);
            gMapControl.Invalidate();
        }

        private void gMap_MouseUp(object sender, MouseEventArgs e)
        {
            if (dragLine != null)
            {
                dragLine = null;
                dragIdx = -1;
                gMapControl.CanDragMap = true;
                UpdatePreview();
            }
        }

        private void gMap_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            if (btnAddPoint.Checked)
            {
                if (NearestSegment(e.X, e.Y, 40, out EditLine el, out int seg))
                {
                    PointLatLng pt = gMapControl.FromLocalToLatLng(e.X, e.Y);
                    el.pts.Insert(seg + 1, pt);
                    RebuildLineVisuals(el);
                    UpdatePreview();
                }
                return;
            }

            if (btnDeletePoint.Checked)
            {
                if (HitTestMarker(e.X, e.Y, out EditLine el, out int idx) && el.pts.Count > 2)
                {
                    el.pts.RemoveAt(idx);
                    RebuildLineVisuals(el);
                    UpdatePreview();
                }
                return;
            }

            if (btnDeleteLine.Checked)
            {
                if (NearestSegment(e.X, e.Y, 16, out EditLine el, out _))
                {
                    RemoveLineVisuals(el);
                    editLines.Remove(el);
                    gMapControl.Invalidate();
                    UpdatePreview();
                }
            }
        }

        private bool HitTestMarker(int x, int y, out EditLine line, out int idx)
        {
            foreach (var el in editLines)
            {
                for (int i = 0; i < el.markers.Count; i++)
                {
                    GPoint sp = gMapControl.FromLatLngToLocal(el.markers[i].Position);
                    if (Math.Abs(sp.X - x) <= 16 && Math.Abs(sp.Y - y) <= 16)
                    {
                        line = el; idx = i; return true;
                    }
                }
            }
            line = null; idx = -1; return false;
        }

        private bool NearestSegment(int x, int y, double maxPx, out EditLine line, out int seg)
        {
            line = null; seg = -1;
            double best = maxPx;
            foreach (var el in editLines)
            {
                for (int i = 0; i < el.pts.Count - 1; i++)
                {
                    GPoint a = gMapControl.FromLatLngToLocal(el.pts[i]);
                    GPoint b = gMapControl.FromLatLngToLocal(el.pts[i + 1]);
                    double d = DistPointToSeg(x, y, a.X, a.Y, b.X, b.Y);
                    if (d < best) { best = d; line = el; seg = i; }
                }
            }
            return line != null;
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

        // ── preview: wheel-safety line + does a U-turn fit ────────────────
        private void UpdatePreview()
        {
            previewOverlay.Routes.Clear();
            previewOverlay.Polygons.Clear();

            //orchard fields may have no boundary - no preview then
            if (mf.bnd.bndList.Count == 0 || mf.bnd.bndList[0].fenceLine.Count < 3) return;

            if (editLines.Count == 0)
            {
                lblStatus.Text = gStr.gsTurnLineHint;
                gMapControl.Invalidate();
                return;
            }

            // scratch copy so the live field state is untouched until Save
            var scratch = new CTurnLimitLines(mf);
            foreach (var el in editLines)
            {
                var tll = new CTurnLimitLine();
                foreach (var p in el.pts) tll.pts.Add(LatLngToLocal(p));
                scratch.lines.Add(tll);
            }
            scratch.BuildEffectiveLines();

            // field centroid for the "toward the field" direction
            double ce = 0, cn = 0;
            var fence = mf.bnd.bndList[0].fenceLine;
            for (int i = 0; i < fence.Count; i++) { ce += fence[i].easting; cn += fence[i].northing; }
            ce /= fence.Count; cn /= fence.Count;

            double uNeed = mf.yt.GetUTurnDepth();
            var status = new System.Text.StringBuilder();

            for (int L = 0; L < scratch.lines.Count; L++)
            {
                var ln = scratch.lines[L];
                if (ln.effPts.Count < 2) continue;

                // dashed wheel-safety line (where the pivot path may reach)
                var effRoute = new List<PointLatLng>();
                for (int i = 0; i < ln.effPts.Count; i++)
                    effRoute.Add(LocalToLatLng(ln.effPts[i].easting, ln.effPts[i].northing));
                previewOverlay.Routes.Add(new GMapRoute(effRoute, "eff")
                {
                    Stroke = new Pen(Color.OrangeRed, 2.5f) { DashStyle = DashStyle.Dash }
                });

                // measure available depth from the line midpoint toward the field
                int mid = ln.effPts.Count / 2;
                double me = (ln.effPts[mid - 1 >= 0 ? mid - 1 : 0].easting + ln.effPts[mid].easting) * 0.5;
                double mn = (ln.effPts[mid - 1 >= 0 ? mid - 1 : 0].northing + ln.effPts[mid].northing) * 0.5;

                double de = ce - me, dn = cn - mn;
                double heading = Math.Atan2(de, dn);
                if (heading < 0) heading += glm.twoPI;

                double available = -1;
                if (mf.bnd.bndList[0].turnLine.Count > 2)
                    available = CTurnLimitLines.RayDistanceToPolyline(
                        new vec2(me, mn), heading, mf.bnd.bndList[0].turnLine, true);

                bool fits = available > 0 && uNeed <= available;

                // U-turn space bubble: circle of the turn radius touching the line midpoint
                double r = mf.yt.youTurnRadius;
                double ne = Math.Sin(heading), nn = Math.Cos(heading);
                var circle = new List<PointLatLng>();
                for (int a = 0; a <= 24; a++)
                {
                    double ang = a / 24.0 * glm.twoPI;
                    circle.Add(LocalToLatLng(
                        me + ne * r + Math.Sin(ang) * r,
                        mn + nn * r + Math.Cos(ang) * r));
                }
                previewOverlay.Routes.Add(new GMapRoute(circle, "fit")
                {
                    Stroke = new Pen(fits ? Color.LimeGreen : Color.Red, 3f)
                });

                status.Append("L").Append(L + 1).Append(": ");
                status.Append(fits ? gStr.gsUTurnFits : gStr.gsKTurnOnly);
                if (available > 0)
                    status.Append(" (").Append(uNeed.ToString("N1")).Append(" / ")
                          .Append(available.ToString("N1")).Append(" m)");
                status.Append("   ");
            }

            lblStatus.Text = status.ToString();
            gMapControl.Invalidate();
        }

        // ── save ──────────────────────────────────────────────────────────
        private void btnSave_Click(object sender, EventArgs e)
        {
            mf.tnl.lines.Clear();
            foreach (var el in editLines)
            {
                if (el.pts.Count < 2) continue;
                var tll = new CTurnLimitLine();
                foreach (var p in el.pts) tll.pts.Add(LatLngToLocal(p));
                mf.tnl.lines.Add(tll);
            }

            mf.tnl.isTurnActive = activateState >= 1;
            mf.tnl.isAfterStop = afterStopMode;
            mf.tnl.isAutomatic = true;   // trigger is always automatic (2-state toggle picks HOW)
            // redesign 2026-07-16: the K-only / combined choice is gone from the UI -
            // the maneuver is always combined U/K (auto pick by available space)
            mf.tnl.isKPrimary = false;
            mf.tnl.isCombined = true;
            mf.tnl.BuildEffectiveLines();

            SaveTurnParams();

            mf.plan.isFollowPlan = activateState == 2;   // 3rd state = follow plan
            mf.FileSaveTurnPlan();

            mf.FileSaveTurnLines();
            mf.TimedMessageBox(1500, gStr.gsTurnLimitLines, gStr.gsTurnLinesSaved);
            Close();
        }

        // ── parcels from the shared tile cache (display only) ─────────────
        private void LoadParcelsFromCache()
        {
            parcelOverlay.Polygons.Clear();
            if (!chkParcels.Checked)
            {
                gMapControl.Invalidate();
                return;
            }

            var cached = CParcelCache.Snapshot();

            foreach (var p in cached)
            {
                if (p.Outer.Count < 3) continue;
                parcelOverlay.Polygons.Add(new GMapPolygon(p.Outer, "parcel")
                {
                    Fill = new SolidBrush(Color.FromArgb(16, Color.DeepSkyBlue)),
                    Stroke = new Pen(Color.DeepSkyBlue, 1.2f)
                });
            }
            gMapControl.Invalidate();
        }

        private void chkParcels_CheckedChanged(object sender, EventArgs e)
        {
            LoadParcelsFromCache();
        }

        // ── map navigation / window controls ──────────────────────────────
        private void btnGoToField_Click(object sender, EventArgs e)
        {
            CenterOnField();
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

        private void FormTurnLine_FormClosing(object sender, FormClosingEventArgs e)
        {
            vehicleTimer.Stop();
        }

        // ── PATH / PARAMS / MAP views (row 2 buttons) ─────────────────────
        private enum ViewTab { Map, Params, Path }
        private ViewTab activeTab = ViewTab.Path;

        private void ShowTab(ViewTab tab)
        {
            activeTab = tab;
            bool mapVisible = tab != ViewTab.Params;

            pnlParams.Visible = tab == ViewTab.Params;
            pnlPath.Visible = tab == ViewTab.Path;
            pnlMapTools.Visible = tab == ViewTab.Map;
            gMapControl.Visible = mapVisible;
            btnZoomIn.Visible = mapVisible;
            btnZoomOut.Visible = mapVisible;

            btnTabMap.BackColor = tab == ViewTab.Map ? Color.Gold : Color.Lavender;
            btnTabParams.BackColor = tab == ViewTab.Params ? Color.Gold : Color.Lavender;
            btnTabPath.BackColor = tab == ViewTab.Path ? Color.Gold : Color.Lavender;

            if (tab == ViewTab.Params)
            {
                if (!btnStyleK.Checked && !btnStyleU.Checked && !btnStyleRS.Checked)
                    btnStyleK.Checked = true;
                pnlParams.BringToFront();
            }
            else if (tab == ViewTab.Path)
            {
                pnlPath.BringToFront();
                UpdateWizard();
            }
            else
            {
                pnlMapTools.BringToFront();
            }
        }

        // ── Reset path: forget the worked/skipped flags, keep the geometry ──
        private void btnResetPath_Click(object sender, EventArgs e)
        {
            if (!mf.plan.HasPlan)
            {
                lblPlanStatus.Text = gStr.gsNoPlanHint;
                return;
            }
            if (FormDialog.ShowQuestion(gStr.gsResetPath, gStr.gsResetPathConfirm,
                    DialogSeverity.Warning) != DialogResult.OK) return;

            foreach (var p in mf.plan.entries)
            {
                p.isWorked = false;
                p.isSkipped = false;
            }
            mf.plan.isFlexPause = false;
            mf.plan.InvalidateRun();
            mf.FileSaveTurnPlan();
            DrawPlanOverlay();
            UpdatePlanInfo();
        }

        // ── path planning mini-wizard (default view) ──────────────────────
        // -1 = prerequisites missing, 0 = start/overview, 1 = pick the line,
        //  2 = headland + zones, 3 = mode (pictograms) + manual passes
        private int wizStep;

        // path-plan preconditions, split so the checklist can show each one
        private bool HasFieldBoundary() =>
            mf.bnd.bndList.Count > 0 && mf.bnd.bndList[0].fenceLine.Count > 2;

        private bool HasReferenceLine()               // at least one AB line or curve
        {
            for (int i = 0; i < mf.trk.gArr.Count; i++)
            {
                var t = mf.trk.gArr[i];
                if (t.mode == TrackMode.AB || t.curvePts.Count >= 2) return true;
            }
            return false;
        }

        private bool HasTurnBoundaries() => editLines.Count >= 2;   // both turn boundaries drawn

        private bool PlanPrereqsOk()
        {
            if (mf.tool.isToolOrchard) return true;   // orchard plans from rows
            return HasFieldBoundary() && HasReferenceLine() && HasTurnBoundaries();
        }

        // three checklist rows shown under "Generiraj plan" when a prereq is missing.
        // Built at runtime (like the zone steppers) so the designer stays untouched.
        private Label[] prereqLabels;
        private Label lblManualPass;
        private void SetupPrereqChecklist()
        {
            prereqLabels = new Label[3];
            for (int i = 0; i < 3; i++)
            {
                var lbl = new Label
                {
                    Location = new System.Drawing.Point(14, 70 + i * 42),
                    Size = new System.Drawing.Size(322, 38),
                    TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                    Visible = false
                };
                prereqLabels[i] = lbl;
                pnlPath.Controls.Add(lbl);
                lbl.BringToFront();
            }

            // step-3 caption above the manual-pass list ("Ručno dodavanje prolaza")
            lblManualPass = new Label
            {
                Text = gStr.gsManualPasses,
                Font = new System.Drawing.Font("Tahoma", 11.25F, System.Drawing.FontStyle.Bold),
                Location = new System.Drawing.Point(10, 210),
                Size = new System.Drawing.Size(326, 30),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Visible = false
            };
            pnlPath.Controls.Add(lblManualPass);
            lblManualPass.BringToFront();
        }

        private void RenderPrereqChecklist()
        {
            bool[] ok = { HasFieldBoundary(), HasReferenceLine(), HasTurnBoundaries() };
            string[] names = { gStr.gsBoundary, gStr.gsTrack + " / " + gStr.gsCurve, gStr.gsTurnLimitLines };
            for (int i = 0; i < 3; i++)
            {
                var lbl = prereqLabels[i];
                lbl.Text = (ok[i] ? "✓  " : "X  ") + names[i];
                lbl.ForeColor = ok[i] ? System.Drawing.Color.ForestGreen : System.Drawing.Color.Firebrick;
                lbl.Font = new System.Drawing.Font("Tahoma", 11.25F,
                    ok[i] ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular);
                lbl.Visible = true;
            }
        }

        private void HidePrereqChecklist()
        {
            if (prereqLabels == null) return;
            foreach (var l in prereqLabels) l.Visible = false;
        }

        private void UpdateWizard()
        {
            if (mf.tool.isToolOrchard) { UpdateWizardOrchard(); return; }

            //hide everything, then switch on what the step needs
            lstLines.Visible = false;
            btnMode1.Visible = btnMode2.Visible = btnMode3.Visible = false;
            chkHeadlandClip.Visible = false;
            lblZones.Visible = nudZones.Visible = false;
            SetZoneStepperVisible(false);
            lblPlanInfo.Visible = false;
            btnGeneratePlan.Visible = false;
            btnAddPath.Visible = btnDeletePath.Visible = false;
            btnWizBack.Visible = btnWizNext.Visible = btnWizCancel.Visible = false;
            btnWizNext.Text = gStr.gsNext;
            if (lblManualPass != null) lblManualPass.Visible = false;
            HidePrereqChecklist();

            if (!PlanPrereqsOk()) wizStep = -1;
            else if (wizStep == -1) wizStep = 0;

            switch (wizStep)
            {
                case -1:
                    // prereq missing: title "Generiraj plan" + a checklist below it
                    // (X / green ✓, ✓ rows bold). Next stays hidden until all three ✓.
                    lblWizTitle.Text = gStr.gsGeneratePlan;
                    lblPlanStatus.Text = "";
                    RenderPrereqChecklist();
                    break;

                case 0:
                    if (mf.plan.HasPlan)
                    {
                        //overview: the plan draws on the map, Next walks the steps
                        lblWizTitle.Text = gStr.gsPlanExists;
                        lblPlanInfo.Visible = true;
                        btnWizNext.Visible = true;
                        UpdatePlanInfo();
                    }
                    else
                    {
                        lblWizTitle.Text = "";
                        btnGeneratePlan.Visible = true;
                        lblPlanStatus.Text = "";
                    }
                    break;

                case 1:
                    lblWizTitle.Text = gStr.gsSelectLineFirst;
                    lstLines.Visible = true;
                    btnWizBack.Visible = btnWizNext.Visible = btnWizCancel.Visible = true;
                    btnWizNext.Enabled = lstLines.SelectedIndex >= 0;
                    break;

                case 2:
                    lblWizTitle.Text = gStr.gsZones;
                    chkHeadlandClip.Visible = true;
                    lblZones.Visible = nudZones.Visible = true;
                    SetZoneStepperVisible(true);
                    lblPlanInfo.Visible = true;
                    btnWizBack.Visible = btnWizNext.Visible = btnWizCancel.Visible = true;
                    btnWizNext.Enabled = true;
                    UpdatePlanInfo();
                    break;

                case 3:
                    lblWizTitle.Text = gStr.gsRowByRow + " / " + gStr.gsContour + " / " + gStr.gsZonesSkip;
                    btnMode1.Visible = btnMode2.Visible = btnMode3.Visible = true;
                    lblManualPass.Visible = true;                // "Ručno dodavanje prolaza"
                    lstLines.Visible = true;                     // manual pass target line
                    lstLines.Top = 244; lstLines.Height = 152;   // shrunk to fit the caption above
                    btnAddPath.Visible = btnDeletePath.Visible = true;
                    btnWizBack.Visible = btnWizCancel.Visible = true;
                    btnWizNext.Visible = true;                   // repurposed as "Završi" (same as OK)
                    btnWizNext.Text = gStr.gsFinish;
                    break;
            }

            if (wizStep != 3) { lstLines.Top = 64; lstLines.Height = 260; }
        }

        //orchard (F50): no wizard - the plan comes from rows + implement settings
        private void UpdateWizardOrchard()
        {
            lstLines.Visible = false;
            btnMode1.Visible = btnMode2.Visible = btnMode3.Visible = false;
            chkHeadlandClip.Visible = false;
            lblZones.Visible = nudZones.Visible = false;
            SetZoneStepperVisible(false);
            btnAddPath.Visible = btnDeletePath.Visible = false;
            btnWizBack.Visible = btnWizNext.Visible = btnWizCancel.Visible = false;
            lblWizTitle.Text = "Orchard";
            lblPlanInfo.Visible = true;
            btnGeneratePlan.Visible = true;
        }

        // the -/+ stepper buttons MakeKeypadField adds beside nudZones
        private readonly List<System.Windows.Forms.Button> zoneSteppers = new List<System.Windows.Forms.Button>();
        private void SetZoneStepperVisible(bool vis)
        {
            foreach (var b in zoneSteppers) b.Visible = vis;
        }

        private void lstLines_SelectedIndexChanged(object sender, EventArgs e)
        {
            //the hidden legacy combo stays the single source of truth for the track
            if (lstLines.SelectedIndex >= 0 && lstLines.SelectedIndex < cboTrack.Items.Count)
                cboTrack.SelectedIndex = lstLines.SelectedIndex;
            if (wizStep == 1) btnWizNext.Enabled = lstLines.SelectedIndex >= 0;
        }

        private void btnWizNext_Click(object sender, EventArgs e)
        {
            if (wizStep == 0) { wizStep = 1; UpdateWizard(); return; }
            if (wizStep == 1)
            {
                if (!RequireTrackChoice()) return;
                wizStep = 2;
                GenerateFromWizard();   //zones show interactively right away
                UpdateWizard();
                return;
            }
            if (wizStep == 2)
            {
                wizStep = 3;
                GenerateFromWizard();
                UpdateWizard();
                return;
            }
            if (wizStep == 3)
            {
                //step 3: the Next slot is relabeled "Završi" and finishes like the top OK
                btnSave_Click(sender, e);
            }
        }

        private void btnWizBack_Click(object sender, EventArgs e)
        {
            if (wizStep > 0) wizStep--;
            UpdateWizard();
        }

        private void btnWizCancel_Click(object sender, EventArgs e)
        {
            wizStep = 0;   //back to the wizard start ("Generate path")
            UpdateWizard();
        }

        //regenerate the plan from the current wizard choices (every step persists)
        private void GenerateFromWizard()
        {
            if (cboTrack.SelectedIndex < 0) return;

            mf.plan.mode = btnMode1.Checked ? CTurnPlan.ModeSequential
                         : btnMode2.Checked ? CTurnPlan.ModeContour
                         : CTurnPlan.ModeZones;
            mf.plan.zoneCount = (int)nudZones.Value;
            mf.plan.trackName = cboTrack.SelectedItem?.ToString() ?? "";
            mf.plan.clipToHeadland = chkHeadlandClip.Enabled && chkHeadlandClip.Checked;

            if (!mf.plan.Generate(out string error))
            {
                lblPlanStatus.Text = error;
                planOverlay.Routes.Clear();
                planOverlay.Markers.Clear();
                gMapControl.Invalidate();
                return;
            }
            mf.FileSaveTurnPlan();
            DrawPlanOverlay();
            UpdatePlanInfo();
        }

        private void btnTabMap_Click(object sender, EventArgs e)
        {
            ShowTab(ViewTab.Map);
        }

        private void btnTabParams_Click(object sender, EventArgs e)
        {
            ShowTab(ViewTab.Params);
        }

        private void btnTabPath_Click(object sender, EventArgs e)
        {
            ShowTab(ViewTab.Path);
        }

        // ── PATH tab: mode select, zone info, plan generation + display ────
        private static readonly Color[] zoneColors =
        {
            Color.DodgerBlue, Color.DarkOrange, Color.MediumSeaGreen, Color.MediumOrchid,
            Color.Goldenrod, Color.Tomato, Color.DarkTurquoise, Color.SlateBlue, Color.HotPink
        };

        // three-way exclusive mode toggle (Sequential / Contour / Zones); always one
        // on. In wizard step 3 a mode click regenerates the plan immediately.
        private void btnMode1_CheckedChanged(object sender, EventArgs e)
        {
            if (btnMode1.Checked) { btnMode2.Checked = false; btnMode3.Checked = false; ModePicked(); }
            else if (!btnMode2.Checked && !btnMode3.Checked) btnMode1.Checked = true;
        }

        private void ModePicked()
        {
            if (wizStep == 3) GenerateFromWizard();
        }

        private void btnMode2_CheckedChanged(object sender, EventArgs e)
        {
            if (btnMode2.Checked) { btnMode1.Checked = false; btnMode3.Checked = false; ModePicked(); }
            else if (!btnMode1.Checked && !btnMode3.Checked) btnMode2.Checked = true;
        }

        private void btnMode3_CheckedChanged(object sender, EventArgs e)
        {
            if (btnMode3.Checked) { btnMode1.Checked = false; btnMode2.Checked = false; ModePicked(); }
            else if (!btnMode1.Checked && !btnMode2.Checked) btnMode3.Checked = true;
        }

        private void nudZones_ValueChanged(object sender, EventArgs e)
        {
            //step 2 is interactive: the zone split redraws on every change
            if (wizStep == 2) GenerateFromWizard();
            UpdatePlanInfo();
        }

        // all usable guidance lines; nothing preselected on open - the red input
        // forces an explicit choice so a plan is never generated off whatever line
        // happened to be active (test f05-3.3r-02)
        private void FillTrackCombo()
        {
            cboTrack.Items.Clear();
            lstLines.Items.Clear();
            for (int i = 0; i < mf.trk.gArr.Count; i++)
            {
                var t = mf.trk.gArr[i];
                if (t.mode != TrackMode.AB && t.curvePts.Count < 2) continue;
                cboTrack.Items.Add(t.name);
                lstLines.Items.Add(t.name);
            }
            cboTrack.SelectedIndex = -1;

            //preselect the plan's own line so reopening shows the real state
            if (mf.plan.HasPlan && !string.IsNullOrEmpty(mf.plan.trackName))
            {
                int idx = cboTrack.Items.IndexOf(mf.plan.trackName);
                if (idx >= 0) { cboTrack.SelectedIndex = idx; lstLines.SelectedIndex = idx; }
            }

            cboTrack.SelectedIndexChanged += (s, e) =>
            {
                if (cboTrack.SelectedIndex < 0) return;
                mf.plan.trackName = cboTrack.SelectedItem?.ToString() ?? "";
                DrawPlanOverlay();
                UpdatePlanInfo();
            };
        }

        // Generate / Add path only run with an explicitly chosen line
        private bool RequireTrackChoice()
        {
            if (cboTrack.SelectedIndex >= 0) return true;
            lblPlanStatus.Text = gStr.gsSelectLineFirst;
            return false;
        }

        private void UpdatePlanInfo()
        {
            if (mf.plan.TryZoneInfo((int)nudZones.Value, out double zoneWidth, out int passesPerZone))
            {
                lblPlanInfo.Text = gStr.gsZoneWidth + ": " + zoneWidth.ToString("N1") + " m\n"
                    + passesPerZone + " " + gStr.gsPassesPerZone;
            }
            else
            {
                lblPlanInfo.Text = gStr.gsNoPlanHint;
            }

            lblPlanStatus.Text = mf.plan.HasPlan
                ? mf.plan.entries.Count + " " + gStr.gsPasses + " · "
                    + mf.plan.zoneCount + " " + gStr.gsZones
                : "";
        }

        // ── Orchard mode (F50 phase 3): the PATH tab plans from rows + tool ──
        // Field controls (mode/zones/line/headland/manual passes) make no sense in
        // an orchard - the plan is the ordered pass sequence from the implement
        // settings; Follow plan / HUD / skip stay the same single brain.
        private void SetupOrchardPathTab()
        {
            btnMode1.Visible = btnMode2.Visible = btnMode3.Visible = false;
            lblZones.Visible = nudZones.Visible = false;
            chkHeadlandClip.Visible = false;
            lblTrack.Visible = cboTrack.Visible = false;
            btnAddPath.Visible = btnDeletePath.Visible = false;
            btnGeneratePlan.Text = "Orchard plan";
            lblPlanInfo.Text = "Orchard mode: plan = rows + tool settings";
        }

        private void GenerateOrchardPlan()
        {
            string fieldDir = mf?.AppCore?.ActiveField?.FieldDirectory?.FullName;
            var rows = COrchard.LoadRows(fieldDir);
            if (rows.Count < 2)
            {
                lblPlanStatus.Text = "No orchard rows - draw them in the Orchard window";
                return;
            }

            double toolW = mf.tool.width > 0.1 ? mf.tool.width : 3.0;
            var ts = Properties.ToolSettings.Default;
            var passes = COrchard.BuildPasses(rows, toolW,
                ts.setTool_orchardWorkMode, ts.setTool_orchardSafeSpace,
                ts.setTool_orchardTreeSpace, ts.setTool_orchardRowOffset,
                ts.setTool_orchardOnePass, ts.setTool_orchardCenterFirst,
                out List<string> warns);
            if (passes.Count == 0)
            {
                lblPlanStatus.Text = warns.Count > 0 ? warns[0] : "No passes";
                return;
            }

            // regenerate = replace the previous orchard pass tracks ("Row ..." = fixed)
            mf.trk.gArr.RemoveAll(t => t.name.StartsWith("Row ", StringComparison.Ordinal));
            foreach (var t in passes) mf.trk.gArr.Add(t);
            mf.trk.idx = mf.trk.gArr.Count - passes.Count;   // first pass
            mf.FileSaveTracks();
            mf.curve.isCurveValid = false;
            mf.ABLine.isABValid = false;

            // zone (color) = inter-row number from the pass name "Row Mid N [pM]"
            var zones = new List<int>();
            foreach (var t in passes)
            {
                string[] parts = t.name.Split(' ');
                zones.Add(parts.Length > 2 && int.TryParse(parts[2], out int z) ? z : 1);
            }

            mf.plan.GenerateOrchard(passes, zones);
            mf.FileSaveTurnPlan();

            DrawPlanOverlay();
            UpdatePlanInfo();
            lblPlanStatus.Text = passes.Count + " orchard passes"
                + (warns.Count > 0 ? "  ·  " + warns.Count + " warning(s): " + warns[0] : "");
        }

        //"Generate path" now opens the wizard (line -> zones -> mode); each step
        //generates and persists, so the plan is ready the moment the mode is picked
        private void btnGeneratePlan_Click(object sender, EventArgs e)
        {
            if (mf.tool.isToolOrchard) { GenerateOrchardPlan(); return; }

            wizStep = 1;
            UpdateWizard();
        }

        // one manual closing pass beside the line selected in the combo; every
        // further click adds the next one, following the same offset rules
        private void btnAddPath_Click(object sender, EventArgs e)
        {
            if (!RequireTrackChoice()) return;

            string tName = cboTrack.SelectedItem?.ToString() ?? "";
            if (!mf.plan.AddManualPass(tName, out string error))
            {
                lblPlanStatus.Text = error;
                return;
            }

            DrawPlanOverlay();
            UpdatePlanInfo();
        }

        // undo the last manually added pass (for when the user tapped Add path too often)
        private void btnDeletePath_Click(object sender, EventArgs e)
        {
            if (!mf.plan.DeleteLastManualPass(out string error))
            {
                lblPlanStatus.Text = error;
                return;
            }

            DrawPlanOverlay();
            UpdatePlanInfo();
        }

        /// <summary>
        /// Draw the plan on the map: pass polylines colored per zone (worked = gray),
        /// an order-number bubble at the pass middle and a direction arrow beside it.
        /// </summary>
        private void DrawPlanOverlay()
        {
            planOverlay.Routes.Clear();
            planOverlay.Markers.Clear();

            // highlight which AB line the plan is built on (red dashed) - only
            // once the user explicitly picked it in the combo
            if (cboTrack.SelectedIndex >= 0
                && mf.plan.TryGetActiveLineGeometry(out List<List<vec3>> lineSegs))
            {
                foreach (var seg in lineSegs)
                {
                    var route = new List<PointLatLng>(seg.Count);
                    for (int i = 0; i < seg.Count; i++)
                        route.Add(LocalToLatLng(seg[i].easting, seg[i].northing));
                    planOverlay.Routes.Add(new GMapRoute(route, "activeLine")
                    {
                        Stroke = new Pen(Color.Red, 3f) { DashStyle = DashStyle.Dash }
                    });
                }
            }

            foreach (var p in mf.plan.entries)
            {
                if (p.segments.Count == 0) continue;
                //skipped = written off by the driver (HUD skip) - gray like worked,
                //but the number bubble gets a red X so it reads as "skipped", not "done"
                Color c = p.isWorked || p.isSkipped
                    ? Color.DarkGray
                    : zoneColors[(p.zone - 1) % zoneColors.Length];

                foreach (var seg in p.segments)
                {
                    var route = new List<PointLatLng>(seg.Count);
                    for (int i = 0; i < seg.Count; i++)
                        route.Add(LocalToLatLng(seg[i].easting, seg[i].northing));
                    planOverlay.Routes.Add(new GMapRoute(route, "pass")
                    {
                        Stroke = new Pen(Color.FromArgb(200, c), 4f)
                    });
                }

                // number bubble + arrow at the middle of the longest segment
                List<vec3> main = p.segments[0];
                double bestLen = -1;
                foreach (var seg in p.segments)
                {
                    double len = 0;
                    for (int i = 1; i < seg.Count; i++) len += glm.Distance(seg[i - 1], seg[i]);
                    if (len > bestLen) { bestLen = len; main = seg; }
                }

                int mid = main.Count / 2;
                int nxt = Math.Min(mid + 1, main.Count - 1);
                if (nxt == mid) continue;

                double dx = main[nxt].easting - main[mid].easting;
                double dy = main[nxt].northing - main[mid].northing;
                double dLen = Math.Sqrt(dx * dx + dy * dy);
                if (dLen < 0.01) continue;
                dx /= dLen; dy /= dLen;
                if (!p.isSameWay) { dx = -dx; dy = -dy; }

                planOverlay.Markers.Add(new PlanNumMarker(
                    LocalToLatLng(main[mid].easting, main[mid].northing),
                    p.order.ToString(), c, p.isSkipped));

                // arrow: shaft + head, a few meters past the number bubble
                double ax = main[mid].easting + dx * 7.0, ay = main[mid].northing + dy * 7.0;
                double tx = ax + dx * 8.0, ty = ay + dy * 8.0;
                double px = -dy, py = dx;
                planOverlay.Routes.Add(new GMapRoute(new List<PointLatLng>
                {
                    LocalToLatLng(ax, ay),
                    LocalToLatLng(tx, ty),
                    LocalToLatLng(tx - dx * 3.0 + px * 2.0, ty - dy * 3.0 + py * 2.0),
                    LocalToLatLng(tx, ty),
                    LocalToLatLng(tx - dx * 3.0 - px * 2.0, ty - dy * 3.0 - py * 2.0)
                }, "arrow")
                {
                    Stroke = new Pen(c, 3f)
                });
            }

            gMapControl.Invalidate();
        }

        // order-number bubble drawn over the pass (red X across it = skipped)
        private class PlanNumMarker : GMapMarker
        {
            private readonly string text;
            private readonly Color color;
            private readonly bool skipped;

            public PlanNumMarker(PointLatLng pos, string t, Color c, bool skip = false) : base(pos)
            {
                text = t;
                color = c;
                skipped = skip;
            }

            public override void OnRender(Graphics g)
            {
                using (var f = new Font("Tahoma", 9f, FontStyle.Bold))
                {
                    SizeF sz = g.MeasureString(text, f);
                    float r = Math.Max(sz.Width, sz.Height) * 0.55f + 3f;
                    using (var back = new SolidBrush(Color.FromArgb(235, Color.White)))
                        g.FillEllipse(back, LocalPosition.X - r, LocalPosition.Y - r, 2f * r, 2f * r);
                    using (var pen = new Pen(color, 2.5f))
                        g.DrawEllipse(pen, LocalPosition.X - r, LocalPosition.Y - r, 2f * r, 2f * r);
                    g.DrawString(text, f, Brushes.Black,
                        LocalPosition.X - sz.Width / 2f, LocalPosition.Y - sz.Height / 2f);

                    if (skipped)
                    {
                        using (var pen = new Pen(Color.Red, 2.5f))
                        {
                            g.DrawLine(pen, LocalPosition.X - r, LocalPosition.Y - r,
                                LocalPosition.X + r, LocalPosition.Y + r);
                            g.DrawLine(pen, LocalPosition.X - r, LocalPosition.Y + r,
                                LocalPosition.X + r, LocalPosition.Y - r);
                        }
                    }
                }
            }
        }

        private void ShowStyle(System.Windows.Forms.Panel p)
        {
            pnlK.Visible = (p == pnlK);
            pnlU.Visible = (p == pnlU);
            pnlRS.Visible = (p == pnlRS);
        }

        private void btnStyleK_CheckedChanged(object sender, EventArgs e)
        {
            if (btnStyleK.Checked) { btnStyleU.Checked = false; btnStyleRS.Checked = false; ShowStyle(pnlK); }
            else if (!btnStyleU.Checked && !btnStyleRS.Checked) btnStyleK.Checked = true;
        }

        private void btnStyleU_CheckedChanged(object sender, EventArgs e)
        {
            if (btnStyleU.Checked) { btnStyleK.Checked = false; btnStyleRS.Checked = false; ShowStyle(pnlU); }
            else if (!btnStyleK.Checked && !btnStyleRS.Checked) btnStyleU.Checked = true;
        }

        private void btnStyleRS_CheckedChanged(object sender, EventArgs e)
        {
            if (btnStyleRS.Checked) { btnStyleK.Checked = false; btnStyleU.Checked = false; ShowStyle(pnlRS); }
            else if (!btnStyleK.Checked && !btnStyleU.Checked) btnStyleRS.Checked = true;
        }

        private static void SetNud(System.Windows.Forms.NumericUpDown nud, double v)
        {
            decimal d = (decimal)v;
            if (d < nud.Minimum) d = nud.Minimum;
            if (d > nud.Maximum) d = nud.Maximum;
            nud.Value = d;
        }

        private void LoadTurnParams()
        {
            var s = Properties.Settings.Default;
            SetNud(nudExitK, s.set_turnK_exitRadius);
            SetNud(nudDistK, s.set_turnK_entryStartDist);
            SetNud(nudAngleK, s.set_turnK_entryAngle);
            SetNud(nudTransK, s.set_turnK_transitionRadius);
            SetNud(nudOutU, s.set_turnU_exitRadius);
            SetNud(nudInU, s.set_turnU_entryRadius);
            SetNud(nudTolerance, s.set_turnTriggerTolerance);
            SetNud(nudOvershootU, s.set_turnU_overshootDist);

            btnStyleK.Checked = true;
            ShowStyle(pnlK);
        }

        private void SaveTurnParams()
        {
            var s = Properties.Settings.Default;
            s.set_turnK_exitRadius = (double)nudExitK.Value;
            s.set_turnK_entryStartDist = (double)nudDistK.Value;
            s.set_turnK_entryAngle = (double)nudAngleK.Value;
            s.set_turnK_transitionRadius = (double)nudTransK.Value;
            s.set_turnU_exitRadius = (double)nudOutU.Value;
            s.set_turnU_entryRadius = (double)nudInU.Value;
            s.set_turnTriggerTolerance = (double)nudTolerance.Value;
            s.set_turnU_overshootDist = (double)nudOvershootU.Value;
            s.Save();
        }

        // right-hand diagram area inside a style panel (left side holds the number inputs + steppers)
        private static Rectangle DiagramArea(System.Windows.Forms.Panel p)
        {
            return new Rectangle(520, 6, p.Width - 528, p.Height - 12);
        }

        // Touch value fields: hide the tiny NumericUpDown spinner, shrink the box, add big
        // -/+ stepper buttons (SmartAB style) and open the AOG numeric keypad on tap.
        private void SetupKeypadFields()
        {
            MakeKeypadField(nudExitK, 0.5);
            MakeKeypadField(nudDistK, 0.5);
            MakeKeypadField(nudAngleK, 5);
            MakeKeypadField(nudOutU, 0.5);
            MakeKeypadField(nudInU, 0.5);
            MakeKeypadField(nudOvershootU, 0.5);
            MakeKeypadField(nudTolerance, 0.5);
            MakeKeypadField(nudZones, 1, zoneSteppers);   //wizard step 2 shows/hides these
        }

        private void MakeKeypadField(System.Windows.Forms.NumericUpDown nud, double step,
            List<System.Windows.Forms.Button> collect = null)
        {
            if (nud.Controls.Count > 0) nud.Controls[0].Visible = false;   // hide built-in spinner
            nud.ReadOnly = true;
            nud.Width = 84;
            nud.Click += (s, e) => OpenKeypad(nud);
            if (nud.Controls.Count > 1) nud.Controls[1].Click += (s, e) => OpenKeypad(nud);

            var p = nud.Parent;
            if (p == null) return;
            int by = nud.Top - 3;
            var minus = new System.Windows.Forms.Button
            {
                Text = "◀",
                Size = new Size(42, 40),
                Location = new Point(nud.Right + 6, by),
                Font = new Font("Tahoma", 12F, FontStyle.Bold),
                FlatStyle = System.Windows.Forms.FlatStyle.Flat,
                BackColor = Color.Gainsboro,
                UseVisualStyleBackColor = false
            };
            var plus = new System.Windows.Forms.Button
            {
                Text = "▶",
                Size = new Size(42, 40),
                Location = new Point(nud.Right + 52, by),
                Font = new Font("Tahoma", 12F, FontStyle.Bold),
                FlatStyle = System.Windows.Forms.FlatStyle.Flat,
                BackColor = Color.Gainsboro,
                UseVisualStyleBackColor = false
            };
            minus.Click += (s, e) => StepNud(nud, -step);
            plus.Click += (s, e) => StepNud(nud, step);
            p.Controls.Add(minus);
            p.Controls.Add(plus);
            minus.BringToFront();
            plus.BringToFront();
            if (collect != null) { collect.Add(minus); collect.Add(plus); }
        }

        private void StepNud(System.Windows.Forms.NumericUpDown nud, double d)
        {
            decimal v = nud.Value + (decimal)d;
            if (v < nud.Minimum) v = nud.Minimum;
            if (v > nud.Maximum) v = nud.Maximum;
            nud.Value = v;
        }

        private void OpenKeypad(System.Windows.Forms.NumericUpDown nud)
        {
            if (!nud.Enabled) return;
            using (var f = new FormNumeric((double)nud.Minimum, (double)nud.Maximum, (double)nud.Value))
            {
                if (f.ShowDialog(this) == System.Windows.Forms.DialogResult.OK)
                {
                    decimal v = (decimal)f.ReturnValue;
                    if (v < nud.Minimum) v = nud.Minimum;
                    if (v > nud.Maximum) v = nud.Maximum;
                    nud.Value = v;
                }
            }
        }

        // Live schematic of the turn geometry with its labelled parameters (same as the
        // design sketch): boundary + wheel-limit lines, the two rows, forward/reverse path.
        private void DrawTurnDiagram(Graphics g, Rectangle area, char style)
        {
            if (area.Width < 40 || area.Height < 40) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // scale the 620x372 sketch space uniformly into the area
            float sc = Math.Min(area.Width / 620f, area.Height / 372f);
            float ox = area.X + (area.Width - 620f * sc) / 2f;
            float oy = area.Y + (area.Height - 372f * sc) / 2f;

            PointF P(float x, float y) => new PointF(ox + x * sc, oy + y * sc);
            void Line(Pen pen, float x1, float y1, float x2, float y2) => g.DrawLine(pen, P(x1, y1), P(x2, y2));
            void Arc(Pen pen, float cx, float cy, float r, float start, float sweep)
                => g.DrawArc(pen, ox + (cx - r) * sc, oy + (cy - r) * sc, 2f * r * sc, 2f * r * sc, start, sweep);
            void Dot(float x, float y) { PointF q = P(x, y); g.FillEllipse(Brushes.DimGray, q.X - 3f, q.Y - 3f, 6f, 6f); }
            void Txt(string t, Brush b, Font f, float x, float y) => g.DrawString(t, f, b, P(x, y));

            using (var guide = new Pen(Color.FromArgb(56, 201, 214), 1.6f) { DashStyle = DashStyle.Dash })
            using (var limit = new Pen(Color.FromArgb(235, 150, 40), 2.4f))
            using (var limitDash = new Pen(Color.FromArgb(235, 150, 40), 1.3f) { DashStyle = DashStyle.Dash })
            using (var fwd = new Pen(Color.FromArgb(60, 175, 95), 2.6f))
            using (var rev = new Pen(Color.FromArgb(225, 70, 90), 2.6f))
            using (var dim = new Pen(Color.FromArgb(150, 120, 190), 1f) { DashStyle = DashStyle.Dash })
            using (var f = new Font("Tahoma", 8f, FontStyle.Bold))
            using (var fieldB = new SolidBrush(Color.FromArgb(14, 60, 175, 95)))
            using (var dimB = new SolidBrush(Color.FromArgb(120, 100, 160)))
            using (var fwdB = new SolidBrush(Color.FromArgb(45, 140, 80)))
            using (var revB = new SolidBrush(Color.FromArgb(200, 55, 75)))
            using (var limitB = new SolidBrush(Color.FromArgb(170, 110, 30)))
            using (var guideB = new SolidBrush(Color.FromArgb(35, 140, 155)))
            {
                float tgt = (style == 'R') ? 430f : 468f;

                // field tint below the boundary
                g.FillRectangle(fieldB, P(0, 160).X, P(0, 160).Y, 620f * sc, (372f - 160f) * sc);

                // boundary + wheel-limit (offset) lines
                Line(limit, 0, 148, 620, 148);
                Txt("boundary", limitB, f, 6, 130);
                Line(limitDash, 0, 160, 620, 160);
                Txt("- 1/2 track", limitB, f, 470, 164);

                // rows
                Line(guide, 150, 360, 150, 205);
                Txt("current row", guideB, f, 100, 344);
                Line(guide, tgt, 176, tgt, 360);
                Txt(style == 'R' ? "target pose" : "new line", guideB, f, tgt + 6, 344);

                if (style == 'K')
                {
                    Arc(fwd, 196, 206, 46, 180, 90);        // exit radius (tangent to boundary)
                    Line(fwd, 196, 160, 392, 160);          // parallel to boundary
                    Line(fwd, 392, 160, 471, 300);          // entry into field at angle
                    Line(rev, 471, 300, 464, 248);          // reverse -> align
                    Dot(471, 300);
                    Line(dim, 196, 206, 196, 160);
                    Txt("R_exit", dimB, f, 202, 186);
                    Txt("parallel", fwdB, f, 250, 150);
                    Txt("d", dimB, f, 430, 150);
                    Txt("a", limitB, f, 452, 272);
                }
                else if (style == 'U')
                {
                    // virtual line = new guidance line offset outward by "overshoot distance"
                    Line(dim, 486, 176, 486, 330);
                    Txt("virtual (AB + overshoot)", dimB, f, 452, 340);

                    Arc(fwd, 210, 220, 60, 180, 90);        // exit (outer) radius R_out
                    Line(fwd, 210, 160, 440, 160);          // bulb top
                    Arc(fwd, 440, 206, 46, 270, 90);        // entry (inner) radius R_in -> tangent to virtual line
                    Line(fwd, 486, 206, 486, 234);          // brief run to the virtual line
                    Line(rev, 486, 234, 468, 212);          // short reverse -> align onto the real line
                    Dot(486, 234);
                    Line(dim, 210, 220, 210, 160);
                    Txt("R_out", dimB, f, 214, 198);
                    Line(dim, 440, 206, 440, 160);
                    Txt("R_in", dimB, f, 404, 198);
                    Txt("keep real R_in, overshoot to virtual, reverse to line", revB, f, 150, 300);
                }
                else // RS
                {
                    Arc(fwd, 202, 212, 52, 180, 90);        // fwd exit
                    Line(fwd, 202, 160, 300, 160);
                    Dot(300, 160);
                    Line(rev, 300, 160, 356, 216);          // reverse
                    Line(rev, 356, 216, 390, 278);
                    Dot(390, 278);
                    Line(fwd, 390, 278, 430, 250);          // fwd onto pose
                    Line(dim, 202, 212, 202, 160);
                    Txt("R_min", dimB, f, 208, 192);
                    Txt("fwd", fwdB, f, 226, 150);
                    Txt("rev", revB, f, 318, 232);
                    Txt("solver picks fwd + reverse", dimB, f, 250, 300);
                }
            }
        }
    }
}
