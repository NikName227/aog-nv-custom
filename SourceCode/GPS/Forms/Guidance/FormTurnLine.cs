using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Translations;
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
            chkActivate.Text = gStr.gsActivateTurn;
            chkAutomatic.Text = gStr.gsAutomaticTurn;
            chkAfterStop.Text = gStr.gsTurnAfterStop;
            btnAddLine.Text = gStr.gsAddLine;
            btnAddPoint.Text = gStr.gsAddPoint;
            btnDeletePoint.Text = gStr.gsDeletePoint;
            btnDeleteLine.Text = gStr.gsDeleteLine;
            chkParcels.Text = gStr.gsCadastralParcels;
            btnGoToField.Text = gStr.gsGoToPosition;
            chkKPrimary.Text = gStr.gsKTurnPrimary;
            chkCombined.Text = gStr.gsCombinedUK;
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
            chkFollowPlan.Text = gStr.gsFollowPlan;

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

            // current per-field state. Trigger mode = Automatic turn XOR Turn after stop;
            // both use auto-engage (isAutomatic), after-stop only changes the trigger gate.
            chkActivate.Checked = mf.tnl.isTurnActive;
            chkAfterStop.Checked = mf.tnl.isAfterStop;
            chkAutomatic.Checked = mf.tnl.isAutomatic && !mf.tnl.isAfterStop;
            chkKPrimary.Checked = mf.tnl.isKPrimary;
            chkCombined.Checked = mf.tnl.isCombined;

            LoadTurnParams();
            SetupKeypadFields();

            LoadBoundary();
            LoadExistingLines();
            CenterOnField();

            // PATH tab: current per-field plan state
            FillTrackCombo();
            btnMode1.Checked = mf.plan.mode == CTurnPlan.ModeSequential;
            btnMode2.Checked = mf.plan.mode == CTurnPlan.ModeContour;
            btnMode3.Checked = mf.plan.mode == CTurnPlan.ModeZones;
            if (mf.plan.zoneCount >= (int)nudZones.Minimum && mf.plan.zoneCount <= (int)nudZones.Maximum)
                nudZones.Value = mf.plan.zoneCount;
            chkFollowPlan.Checked = mf.plan.isFollowPlan;

            // headland clip only makes sense with a headland defined
            bool hasHeadland = mf.bnd.bndList.Count > 0 && mf.bnd.bndList[0].hdLine.Count > 2;
            chkHeadlandClip.Enabled = hasHeadland;
            chkHeadlandClip.Checked = hasHeadland && mf.plan.clipToHeadland;
            UpdatePlanInfo();
            if (mf.plan.HasPlan)
                mf.plan.RebuildGeometry();
            DrawPlanOverlay();   // also highlights the active AB line even without a plan
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

            LoadParcelsFromCache();
            UpdatePreview();
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

        // ── trigger mode checkboxes (mutually exclusive) ──────────────────
        private void chkAutomatic_CheckedChanged(object sender, EventArgs e)
        {
            if (chkAutomatic.Checked) chkAfterStop.Checked = false;
        }

        private void chkAfterStop_CheckedChanged(object sender, EventArgs e)
        {
            if (chkAfterStop.Checked) chkAutomatic.Checked = false;
        }

        // ── maneuver checkboxes (mutually exclusive) ──────────────────────
        private void chkKPrimary_CheckedChanged(object sender, EventArgs e)
        {
            if (chkKPrimary.Checked) chkCombined.Checked = false;
        }

        private void chkCombined_CheckedChanged(object sender, EventArgs e)
        {
            if (chkCombined.Checked) chkKPrimary.Checked = false;
        }

        // ── preview: wheel-safety line + does a U-turn fit ────────────────
        private void UpdatePreview()
        {
            previewOverlay.Routes.Clear();
            previewOverlay.Polygons.Clear();

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

            mf.tnl.isTurnActive = chkActivate.Checked;
            mf.tnl.isAfterStop = chkAfterStop.Checked;
            // auto-engage the youturn button in either trigger mode (Automatic or After stop)
            mf.tnl.isAutomatic = chkAutomatic.Checked || chkAfterStop.Checked;
            mf.tnl.isKPrimary = chkKPrimary.Checked;
            mf.tnl.isCombined = chkCombined.Checked;
            mf.tnl.BuildEffectiveLines();

            SaveTurnParams();

            mf.plan.isFollowPlan = chkFollowPlan.Checked;
            mf.FileSaveTurnPlan();

            mf.FileSaveTurnLines();
            mf.TimedMessageBox(1500, gStr.gsTurnLimitLines, gStr.gsTurnLinesSaved);
            Close();
        }

        // ── parcels from the per-field cache (display only) ───────────────
        private void LoadParcelsFromCache()
        {
            parcelOverlay.Polygons.Clear();
            if (!chkParcels.Checked)
            {
                gMapControl.Invalidate();
                return;
            }

            var dir = mf?.AppCore?.ActiveField?.FieldDirectory;
            if (dir == null) return;
            string path = Path.Combine(dir.FullName, "Parcels.txt");
            if (!File.Exists(path)) return;

            var cached = CadastralWfsClient.LoadParcels(path, out string err);
            if (err != null || cached == null) return;

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

        // ── MAP / PARAMS / PATH icon tabs ─────────────────────────────────
        private enum ViewTab { Map, Params, Path }
        private ViewTab activeTab = ViewTab.Map;

        private void ShowTab(ViewTab tab)
        {
            activeTab = tab;
            bool mapVisible = tab != ViewTab.Params;

            pnlParams.Visible = tab == ViewTab.Params;
            pnlPath.Visible = tab == ViewTab.Path;
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
                UpdatePlanInfo();
            }
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

        // three-way exclusive mode toggle (Sequential / Contour / Zones); always one on
        private void btnMode1_CheckedChanged(object sender, EventArgs e)
        {
            if (btnMode1.Checked) { btnMode2.Checked = false; btnMode3.Checked = false; }
            else if (!btnMode2.Checked && !btnMode3.Checked) btnMode1.Checked = true;
        }

        private void btnMode2_CheckedChanged(object sender, EventArgs e)
        {
            if (btnMode2.Checked) { btnMode1.Checked = false; btnMode3.Checked = false; }
            else if (!btnMode1.Checked && !btnMode3.Checked) btnMode2.Checked = true;
        }

        private void btnMode3_CheckedChanged(object sender, EventArgs e)
        {
            if (btnMode3.Checked) { btnMode1.Checked = false; btnMode2.Checked = false; }
            else if (!btnMode1.Checked && !btnMode2.Checked) btnMode3.Checked = true;
        }

        private void nudZones_ValueChanged(object sender, EventArgs e)
        {
            UpdatePlanInfo();
        }

        // all usable guidance lines; select the plan's line, else the active one
        private void FillTrackCombo()
        {
            cboTrack.Items.Clear();
            int select = -1;
            for (int i = 0; i < mf.trk.gArr.Count; i++)
            {
                var t = mf.trk.gArr[i];
                if (t.mode != TrackMode.AB && t.curvePts.Count < 2) continue;
                int at = cboTrack.Items.Add(t.name);
                if (t.name == mf.plan.trackName) select = at;
                if (select < 0 && i == mf.trk.idx) select = at;
            }
            if (select < 0 && cboTrack.Items.Count > 0) select = 0;
            if (select >= 0) cboTrack.SelectedIndex = select;

            cboTrack.SelectedIndexChanged += (s, e) =>
            {
                mf.plan.trackName = cboTrack.SelectedItem?.ToString() ?? "";
                DrawPlanOverlay();
                UpdatePlanInfo();
            };
            mf.plan.trackName = cboTrack.SelectedItem?.ToString() ?? "";
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

        private void btnGeneratePlan_Click(object sender, EventArgs e)
        {
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

            DrawPlanOverlay();
            UpdatePlanInfo();
        }

        // one manual closing pass beside the line selected in the combo; every
        // further click adds the next one, following the same offset rules
        private void btnAddPath_Click(object sender, EventArgs e)
        {
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

            // highlight which AB line the plan is built on (red dashed)
            if (mf.plan.TryGetActiveLineGeometry(out List<List<vec3>> lineSegs))
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
                Color c = p.isWorked
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
                    p.order.ToString(), c));

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

        // order-number bubble drawn over the pass
        private class PlanNumMarker : GMapMarker
        {
            private readonly string text;
            private readonly Color color;

            public PlanNumMarker(PointLatLng pos, string t, Color c) : base(pos)
            {
                text = t;
                color = c;
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
            MakeKeypadField(nudZones, 1);
        }

        private void MakeKeypadField(System.Windows.Forms.NumericUpDown nud, double step)
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
