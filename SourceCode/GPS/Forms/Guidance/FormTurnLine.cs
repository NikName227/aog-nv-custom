using AgOpenGPS.Controls;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Translations;
using AgOpenGPS.Forms;
using GMap.NET;
using GMap.NET.MapProviders;
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

        private readonly GMapOverlay backPicOverlay = new GMapOverlay("backpic");
        private readonly GMapOverlay boundaryOverlay = new GMapOverlay("bnd");
        private readonly GMapOverlay headlandOverlay = new GMapOverlay("headland");
        private readonly GMapOverlay parcelOverlay = new GMapOverlay("parcels");
        private readonly GMapOverlay planOverlay = new GMapOverlay("plan");
        private readonly GMapOverlay previewOverlay = new GMapOverlay("preview");
        private readonly GMapOverlay lineOverlay = new GMapOverlay("lines");
        private readonly GMapOverlay vehicleOverlay = new GMapOverlay("vehicle");
        private readonly GMapOverlay measureOverlay = new GMapOverlay("measure");

        // f05gui-09: draggable 2-handle ruler - the same measure tool as the Smart AB
        // window / geoportal (drag the two ends, live distance). Temporary, never saved.
        private bool measureOn;
        private PointLatLng mp0, mp1;
        private int measureDrag = -1;   // 0/1 while dragging an endpoint
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

        // field feedback 2026-07-27: drawn geometry that has not been written to disk yet
        private bool isDirty;

        // "Add line on border": first tap parks a point here, second tap builds the line.
        // Both taps near the boundary => the line follows the boundary between them,
        // otherwise it is a plain straight line. Either way it is editable afterwards.
        private bool hasBorderPending;
        private PointLatLng borderPending;
        private GMapMarker borderPendingMarker;
        private const double borderSnapMetres = 5.0;      // "on the boundary" tolerance
        private const double borderExtendMetres = 15.0;   // straight tail at each end
        private const double borderMaxTraceMetres = 400.0;// refuse absurd traces
        private const double borderSimplifyMetres = 0.25; // Douglas-Peucker tolerance

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
            btnMeasure.Text = gStr.gsMeasure;
            lblMeasure.Text = "";
            btnAddPoint.Text = gStr.gsAddPoint;
            btnDeletePoint.Text = gStr.gsDeletePoint;
            btnDeleteLine.Text = gStr.gsDeleteLine;
            chkParcels.Text = gStr.gsCadastralParcels;
            btnGoToField.Text = gStr.gsGoToPosition;
            //2026-08-12: this row shows what the TRACTOR can physically do, nothing else
            lblRadius.Text = gStr.gsMinTurnRadius + ": " + mf.yt.MinTurnRadius().ToString("N1") + " m";
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
            lblTolerance.Text = gStr.gsTriggerTolBefore;
            lblToleranceAfter.Text = gStr.gsTriggerTolAfter;
            lblBndOffset.Text = gStr.gsBndOffsetExtra;
            lblOvershootU.Text = gStr.gsOvershootDist;
        }

        private void FormTurnLine_Load(object sender, EventArgs e)
        {
            // No live tiles here at all (field report 2026-08-10). Drawing the captured field
            // background ON TOP of live tiles showed two different photographs of the same
            // ground at once, and the turn plan needs a boundary anyway - so this window shows
            // exactly what Smart AB shows: the captured picture around the field, nothing else.
            // No background captured yet = black.
            gMapControl.MapProvider = GMapProviders.EmptyProvider;
            gMapControl.EmptyMapBackground = Color.Black;
            gMapControl.EmptyTileColor = Color.Black;
            gMapControl.EmptyTileBorders = new Pen(Color.Black);
            gMapControl.ShowTileGridLines = false;

            gMapControl.Position = new PointLatLng(
                mf.AppModel.CurrentLatLon.Latitude,
                mf.AppModel.CurrentLatLon.Longitude);
            gMapControl.Zoom = 16;
            gMapControl.ShowCenter = false;
            gMapControl.DragButton = MouseButtons.Left;

            // The captured field background goes UNDER everything else: once it exists it is
            // sharper than the live tiles this window can pull (field report 2026-08-10 - the
            // map here showed none of the resolution that had just been downloaded). Outside
            // the captured box the live tiles still show, so the surroundings stay visible.
            gMapControl.Overlays.Add(backPicOverlay);
            RebuildBackPicOverlay();

            gMapControl.Overlays.Add(parcelOverlay);
            gMapControl.Overlays.Add(boundaryOverlay);
            gMapControl.Overlays.Add(headlandOverlay);
            gMapControl.Overlays.Add(planOverlay);
            gMapControl.Overlays.Add(previewOverlay);
            gMapControl.Overlays.Add(lineOverlay);
            gMapControl.Overlays.Add(vehicleOverlay);
            gMapControl.Overlays.Add(measureOverlay);

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

            // if the plan's reference AB line/curve was deleted, the plan is stale - drop
            // it so the wizard shows the prereqs and lets the user rebuild on another line
            if (mf.plan.HasPlan && !mf.plan.ReferenceTrackExists())
                mf.plan.Reset();

            // path planning: current per-field plan state
            FillTrackCombo();
            if (!mf.tool.isToolOrchard) BuildFieldPlanControls();
            syncingModeButtons = true;
            if (btnMode4 != null) btnMode4.Checked = mf.plan.mode == CTurnPlan.ModeMinUTurn;
            btnMode1.Checked = mf.plan.mode == CTurnPlan.ModeSequential;
            btnMode2.Checked = mf.plan.mode == CTurnPlan.ModeContour;
            btnMode3.Checked = mf.plan.mode == CTurnPlan.ModeZones
                || (mf.plan.mode != CTurnPlan.ModeSequential && mf.plan.mode != CTurnPlan.ModeContour
                    && !(btnMode4 != null && btnMode4.Checked));
            syncingModeButtons = false;
            if (mf.plan.zoneCount >= (int)nudZones.Minimum && mf.plan.zoneCount <= (int)nudZones.Maximum)
                nudZones.Value = mf.plan.zoneCount;
            //saved processing order, if the field has one; blank falls back to the default

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

            BuildStartFromControls();
            BuildOrchardStartControls();
            ResetStartFrom();
            BuildMinRadiusNote();
            if (!mf.tool.isToolOrchard) ApplyNarrowPathLayout();

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

        /// <summary>
        /// Put the field's captured background under the map, in its true place. Called on
        /// open; silently does nothing when the field has no background yet.
        /// </summary>
        private void RebuildBackPicOverlay()
        {
            backPicOverlay.Markers.Clear();
            if (mf == null || !mf.worldGrid.HasBingMap) return;

            var bm = mf.worldGrid.BingMap;
            if (bm?.Bitmap == null) return;

            //the stored box is in local plane metres - back to lat/lon for the map
            PointLatLng tl = LocalToLatLng(bm.GeoBoundingBox.MinEasting, bm.GeoBoundingBox.MaxNorthing);
            PointLatLng br = LocalToLatLng(bm.GeoBoundingBox.MaxEasting, bm.GeoBoundingBox.MinNorthing);

            backPicOverlay.Markers.Add(new BackPicMarker(bm.Bitmap, tl, br));
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
            //what was just loaded IS what is on disk
            isDirty = false;
            UpdateSaveState();
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
            MarkDirty();
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
            if (btnBorderLine != keep) btnBorderLine.Checked = false;
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

        private void btnBorderLine_CheckedChanged(object sender, EventArgs e)
        {
            if (btnBorderLine.Checked) ExclusiveTapMode(btnBorderLine);
            //leaving the mode drops a half-placed first point
            if (!btnBorderLine.Checked) ClearBorderPending();
            UpdateDragButton();
        }

        private void UpdateDragButton()
        {
            gMapControl.DragButton =
                (btnAddPoint.Checked || btnDeletePoint.Checked || btnDeleteLine.Checked
                 || btnBorderLine.Checked || ZoneCutModeOn)
                ? MouseButtons.Right : MouseButtons.Left;
        }

        // ── mouse editing ─────────────────────────────────────────────────
        private void gMap_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            if (measureOn)
            {
                int mh = HitTestMeasure(e.X, e.Y);
                if (mh >= 0) { measureDrag = mh; gMapControl.CanDragMap = false; return; }
            }
            if (btnAddPoint.Checked || btnDeletePoint.Checked || btnDeleteLine.Checked
                || btnBorderLine.Checked) return;

            if (HitTestMarker(e.X, e.Y, out EditLine el, out int idx))
            {
                dragLine = el;
                dragIdx = idx;
                gMapControl.CanDragMap = false;
            }
        }

        private void gMap_MouseMove(object sender, MouseEventArgs e)
        {
            if (measureDrag >= 0)
            {
                PointLatLng p = gMapControl.FromLocalToLatLng(e.X, e.Y);
                if (measureDrag == 0) mp0 = p; else mp1 = p;
                DrawMeasure();
                UpdateMeasureLabel();
                return;
            }
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
            if (measureDrag >= 0)
            {
                measureDrag = -1;
                gMapControl.CanDragMap = true;
                return;
            }
            if (dragLine != null)
            {
                dragLine = null;
                dragIdx = -1;
                gMapControl.CanDragMap = true;
                MarkDirty();
                UpdatePreview();
            }
        }

        private void gMap_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (measureOn) return;   // measure uses drag (MouseDown/Move), not taps

            //REQ 2026-10-11-2: zone step border tools
            if (activeTab == ViewTab.Path && ZoneCutModeOn)
            {
                ZoneCutTap(e.X, e.Y);
                return;
            }

            //PATH tab: the zone badges are the way the processing order is edited
            if (activeTab == ViewTab.Path && HitTestZoneBadge(e.X, e.Y, out int zoneIdx))
            {
                EditZoneOrder(zoneIdx);
                return;
            }

            if (btnBorderLine.Checked)
            {
                BorderModeTap(gMapControl.FromLocalToLatLng(e.X, e.Y));
                return;
            }

            if (btnAddPoint.Checked)
            {
                if (NearestSegment(e.X, e.Y, 40, out EditLine el, out int seg))
                {
                    PointLatLng pt = gMapControl.FromLocalToLatLng(e.X, e.Y);
                    el.pts.Insert(seg + 1, pt);
                    RebuildLineVisuals(el);
                    MarkDirty();
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
                    MarkDirty();
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
                    MarkDirty();
                    UpdatePreview();
                }
            }
        }

        // ── "Add line on border" (field feedback 2026-07-27) ──────────────
        // Two taps. Both within borderSnapMetres of the field boundary => the new line
        // follows the boundary between them; otherwise it is the plain straight line
        // between the two taps. Either way it gets a straight tail at each end and stays
        // fully editable. Snapping is judged in METRES, not pixels - pixels change with
        // zoom, so a pixel threshold would behave differently at every zoom level.
        private void BorderModeTap(PointLatLng tap)
        {
            if (!hasBorderPending)
            {
                hasBorderPending = true;
                borderPending = tap;
                borderPendingMarker = new CornerMarker(tap);
                lineOverlay.Markers.Add(borderPendingMarker);
                gMapControl.Invalidate();
                lblStatus.Text = gStr.gsBorderLineSecondPoint;
                return;
            }

            PointLatLng a = borderPending;
            PointLatLng b = tap;
            ClearBorderPending();

            List<PointLatLng> pts;
            if (!TryTraceBoundary(a, b, out pts))
                pts = new List<PointLatLng> { a, b };   //not on the boundary -> straight line

            ExtendEnds(pts, borderExtendMetres);

            var el = new EditLine();
            el.pts.AddRange(pts);
            editLines.Add(el);
            BuildLineVisuals(el);
            MarkDirty();
            UpdatePreview();
        }

        private void ClearBorderPending()
        {
            hasBorderPending = false;
            if (borderPendingMarker != null)
            {
                lineOverlay.Markers.Remove(borderPendingMarker);
                borderPendingMarker = null;
                gMapControl.Invalidate();
            }
        }

        /// <summary>
        /// Walk the fence ring between the points nearest a and b. Returns false when either
        /// tap is further than borderSnapMetres from the boundary, or when even the shorter
        /// way round is absurdly long (taps at opposite ends of the field).
        /// </summary>
        private bool TryTraceBoundary(PointLatLng a, PointLatLng b, out List<PointLatLng> pts)
        {
            pts = null;
            if (mf.bnd.bndList.Count == 0) return false;
            var fence = mf.bnd.bndList[0].fenceLine;
            int n = fence.Count;
            if (n < 3) return false;

            vec3 la = LatLngToLocal(a), lb = LatLngToLocal(b);
            int ia = NearestFenceIndex(fence, la, out double da);
            int ib = NearestFenceIndex(fence, lb, out double db);
            if (da > borderSnapMetres || db > borderSnapMetres) return false;
            if (ia == ib) return false;

            // the ring gives two ways round - measure both, keep the shorter
            var fwd = RingSlice(fence, ia, ib, true);
            var rev = RingSlice(fence, ia, ib, false);
            var pick = RingLength(fence, fwd) <= RingLength(fence, rev) ? fwd : rev;
            if (RingLength(fence, pick) > borderMaxTraceMetres) return false;

            // The fence is densified to 1.1-3.3 m by CFenceLine.FixFenceLine, so a straight
            // 200 m edge is ~180 points. Left as is that is one draggable handle per point,
            // and - worse - BuildEffectiveLines extrapolates 300 m along the LAST segment,
            // whose direction would be pure noise. Simplify back to real corners.
            var local = new List<vec3>(pick.Count);
            foreach (int i in pick) local.Add(fence[i]);
            var simplified = SimplifyLocal(local, borderSimplifyMetres);

            pts = new List<PointLatLng>(simplified.Count);
            foreach (var v in simplified) pts.Add(LocalToLatLng(v.easting, v.northing));
            return pts.Count >= 2;
        }

        private static int NearestFenceIndex(List<vec3> fence, vec3 p, out double dist)
        {
            int best = 0;
            double bestD = double.MaxValue;
            for (int i = 0; i < fence.Count; i++)
            {
                double de = fence[i].easting - p.easting;
                double dn = fence[i].northing - p.northing;
                double d = de * de + dn * dn;
                if (d < bestD) { bestD = d; best = i; }
            }
            dist = Math.Sqrt(bestD);
            return best;
        }

        /// <summary>Indices from ia to ib around the closed ring, forward or backward.</summary>
        private static List<int> RingSlice(List<vec3> fence, int ia, int ib, bool forward)
        {
            int n = fence.Count;
            var idx = new List<int>();
            int i = ia;
            idx.Add(i);
            while (i != ib)
            {
                i = forward ? (i + 1) % n : (i - 1 + n) % n;
                idx.Add(i);
            }
            return idx;
        }

        private static double RingLength(List<vec3> fence, List<int> idx)
        {
            double len = 0;
            for (int i = 0; i < idx.Count - 1; i++)
            {
                double de = fence[idx[i + 1]].easting - fence[idx[i]].easting;
                double dn = fence[idx[i + 1]].northing - fence[idx[i]].northing;
                len += Math.Sqrt(de * de + dn * dn);
            }
            return len;
        }

        /// <summary>Douglas-Peucker in local metres - keeps corners, drops densifier filler.</summary>
        private static List<vec3> SimplifyLocal(List<vec3> pts, double tol)
        {
            if (pts.Count < 3) return new List<vec3>(pts);

            var keep = new bool[pts.Count];
            keep[0] = keep[pts.Count - 1] = true;
            var stack = new Stack<int[]>();
            stack.Push(new[] { 0, pts.Count - 1 });

            while (stack.Count > 0)
            {
                var seg = stack.Pop();
                int first = seg[0], last = seg[1];
                double worst = 0;
                int worstAt = -1;
                for (int i = first + 1; i < last; i++)
                {
                    double d = DistPointToSeg(pts[i].easting, pts[i].northing,
                        pts[first].easting, pts[first].northing,
                        pts[last].easting, pts[last].northing);
                    if (d > worst) { worst = d; worstAt = i; }
                }
                if (worstAt > 0 && worst > tol)
                {
                    keep[worstAt] = true;
                    stack.Push(new[] { first, worstAt });
                    stack.Push(new[] { worstAt, last });
                }
            }

            var outp = new List<vec3>();
            for (int i = 0; i < pts.Count; i++) if (keep[i]) outp.Add(pts[i]);
            return outp;
        }

        /// <summary>Push the first and last point out by dist along their own end segment.</summary>
        private void ExtendEnds(List<PointLatLng> pts, double dist)
        {
            if (pts.Count < 2) return;

            vec3 p0 = LatLngToLocal(pts[0]), p1 = LatLngToLocal(pts[1]);
            vec3 pn = LatLngToLocal(pts[pts.Count - 1]), pm = LatLngToLocal(pts[pts.Count - 2]);

            if (TryStep(p1, p0, dist, out double e, out double n))
                pts[0] = LocalToLatLng(e, n);
            if (TryStep(pm, pn, dist, out e, out n))
                pts[pts.Count - 1] = LocalToLatLng(e, n);
        }

        /// <summary>One step of length dist past "to", along the from->to direction.</summary>
        private static bool TryStep(vec3 from, vec3 to, double dist, out double e, out double n)
        {
            double de = to.easting - from.easting, dn = to.northing - from.northing;
            double len = Math.Sqrt(de * de + dn * dn);
            e = n = 0;
            if (len < 0.001) return false;
            e = to.easting + de / len * dist;
            n = to.northing + dn / len * dist;
            return true;
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
        // Field feedback 2026-07-27: until now the only way to save was "OK", which also
        // closed the window - draw a line, leave any other way, and the work was gone with
        // no warning. The write is now its own method, "Save" keeps the window open, and
        // unsaved geometry is both visible (see UpdateSaveState) and caught on close.
        private void SaveAll()
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
            //IsFeatureOn just changed, and it decides the turn line inset (CTurn.cs:128)
            mf.bnd.BuildTurnLines();

            SaveTurnParams();
            paramsDirty = false;
            UpdateApplyButton();

            mf.plan.isFollowPlan = activateState == 2;   // 3rd state = follow plan
            UpdateZoneOrderLabel();
            mf.FileSaveTurnPlan();

            mf.FileSaveTurnLines();
            isDirty = false;
            UpdateSaveState();
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            SaveAll();
            mf.TimedMessageBox(1500, gStr.gsTurnLimitLines, gStr.gsTurnLinesSaved);
            Close();
        }

        private void btnSaveLines_Click(object sender, EventArgs e)
        {
            SaveAll();
            mf.TimedMessageBox(1500, gStr.gsTurnLimitLines, gStr.gsTurnLinesSaved);
        }

        /// <summary>Any change to the drawn geometry leaves the window out of sync with disk.</summary>
        private void MarkDirty()
        {
            isDirty = true;
            UpdateSaveState();
        }

        /// <summary>Save button carries the state so "is it saved?" is answered at a glance.</summary>
        private void UpdateSaveState()
        {
            if (btnSaveLines == null) return;
            btnSaveLines.Text = isDirty ? "Save *" : "Save";
            btnSaveLines.BackColor = isDirty ? Color.Gold : Color.Honeydew;
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

        // f05gui-09: the same measure tool as the Smart AB window / geoportal - a ruler
        // with two draggable ends and a live distance readout (lblMeasure), not a new
        // interaction. Toggle on -> two ends drop near the view centre, ready to drag.
        private void btnMeasure_Click(object sender, EventArgs e)
        {
            measureOn = !measureOn;
            if (measureOn)
            {
                var area = gMapControl.ViewArea;
                double cLat = (area.LocationTopLeft.Lat + area.LocationRightBottom.Lat) / 2.0;
                double cLon = (area.LocationTopLeft.Lng + area.LocationRightBottom.Lng) / 2.0;
                double hLon = (area.LocationRightBottom.Lng - area.LocationTopLeft.Lng) * 0.12;
                mp0 = new PointLatLng(cLat, cLon - hLon);
                mp1 = new PointLatLng(cLat, cLon + hLon);
                btnMeasure.BackColor = Color.IndianRed;
                btnMeasure.UseVisualStyleBackColor = false;
                UpdateMeasureLabel();
            }
            else
            {
                measureDrag = -1;
                btnMeasure.BackColor = SystemColors.Control;
                btnMeasure.UseVisualStyleBackColor = true;
                lblMeasure.Text = "";
            }
            DrawMeasure();
        }

        private void UpdateMeasureLabel()
        {
            if (!measureOn) { lblMeasure.Text = ""; return; }
            vec3 a = LatLngToLocal(mp0), b = LatLngToLocal(mp1);
            double dx = b.easting - a.easting, dy = b.northing - a.northing;
            double d = Math.Sqrt(dx * dx + dy * dy);
            lblMeasure.Text = d < 1.0 ? (d * 100.0).ToString("0") + " cm" : d.ToString("0.00") + " m";
        }

        private int HitTestMeasure(int px, int py)
        {
            if (!measureOn) return -1;
            GPoint s0 = gMapControl.FromLatLngToLocal(mp0);
            GPoint s1 = gMapControl.FromLatLngToLocal(mp1);
            if (Math.Abs(s0.X - px) <= 16 && Math.Abs(s0.Y - py) <= 16) return 0;
            if (Math.Abs(s1.X - px) <= 16 && Math.Abs(s1.Y - py) <= 16) return 1;
            return -1;
        }

        private void DrawMeasure()
        {
            measureOverlay.Routes.Clear();
            measureOverlay.Markers.Clear();
            if (measureOn)
            {
                measureOverlay.Routes.Add(new GMapRoute(new List<PointLatLng> { mp0, mp1 }, "measure")
                {
                    Stroke = new Pen(Color.Red, 2f)
                });
                measureOverlay.Markers.Add(new MeasureHandleMarker(mp0));
                measureOverlay.Markers.Add(new MeasureHandleMarker(mp1));
            }
            gMapControl.Invalidate();
        }

        // red square handle with a centre dot (same look as the other editors' ends)
        private class MeasureHandleMarker : GMapMarker
        {
            public MeasureHandleMarker(PointLatLng pos) : base(pos) { }
            public override void OnRender(Graphics g)
            {
                using (var pen = new Pen(Color.Red, 2f))
                    g.DrawRectangle(pen, LocalPosition.X - 8, LocalPosition.Y - 8, 16, 16);
                using (var b = new SolidBrush(Color.Red))
                    g.FillEllipse(b, LocalPosition.X - 3, LocalPosition.Y - 3, 6, 6);
            }
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
            //field feedback 2026-07-27: leaving with unsaved lines used to discard them
            //silently. SaveAll clears isDirty, so OK never reaches this prompt.
            if (isDirty)
            {
                var answer = MessageBox.Show(gStr.gsTurnLinesUnsaved, gStr.gsTurnLimitLines,
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) { e.Cancel = true; return; }
                if (answer == DialogResult.Yes) SaveAll();
            }
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

        // ── Min radius warning in OUR params tab (2026-08-12) ─────────────
        // The radius fields here can ask for a tighter turn than the tractor can drive; the
        // driving code silently lifts them to CYouTurn.MinTurnRadius(). Silently is the problem -
        // so the value that gets overridden goes red, with one small note under the block (the
        // number is the same for all three fields, no point repeating it).

        private Label lblMinRadiusNote;

        private void BuildMinRadiusNote()
        {
            lblMinRadiusNote = new Label
            {
                Text = "",
                Font = new Font("Tahoma", 8.5F),
                ForeColor = Color.Firebrick,
                Location = new Point(4, 210),
                Size = new Size(500, 20),
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };
            pnlK.Controls.Add(lblMinRadiusNote);
            lblMinRadiusNote.BringToFront();

            // the same note, shown under whichever style panel is up
            lblMinRadiusNoteU = new Label
            {
                Text = "",
                Font = new Font("Tahoma", 8.5F),
                ForeColor = Color.Firebrick,
                Location = new Point(4, 160),
                Size = new Size(500, 20),
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };
            pnlU.Controls.Add(lblMinRadiusNoteU);
            lblMinRadiusNoteU.BringToFront();
        }

        private Label lblMinRadiusNoteU;

        private void UpdateMinRadiusWarning()
        {
            if (lblMinRadiusNote == null) return;

            double min = mf.yt.MinTurnRadius();
            bool anyK = MarkIfBelow(nudExitK, min);
            bool anyU = MarkIfBelow(nudOutU, min) | MarkIfBelow(nudInU, min);

            string note = gStr.gsMinTurnRadiusIs + " " + min.ToString("N1") + " m";
            lblMinRadiusNote.Text = note;
            lblMinRadiusNoteU.Text = note;
            lblMinRadiusNote.Visible = anyK;
            lblMinRadiusNoteU.Visible = anyU;
        }

        /// <summary>Red when this field asks for less than the tractor can drive. 0 = "use the
        /// general radius", which is checked against the minimum on its own.</summary>
        private bool MarkIfBelow(NumericUpDown nud, double min)
        {
            if (nud == null) return false;

            double v = (double)nud.Value;
            bool below = min > 0 && v > 0.5 && v < min;
            nud.ForeColor = below ? Color.Firebrick : Color.Black;
            return below;
        }

        // ── "Kreni od": jump to a zone/pass of an existing plan (2026-08-12) ──
        // Built here rather than in the designer so the ▲▼ steppers stay together with the
        // logic. Deliberately NOT a NumericUpDown: those open the keypad on touch, and this is
        // meant to be nudged with two arrows while standing in the field.

        private Label lblStartFrom, lblSfZone, lblSfPass, lblSfZoneVal, lblSfPassVal;
        private Button btnSfZoneUp, btnSfZoneDown, btnSfPassUp, btnSfPassDown, btnSfApply;
        private int sfZone = 1, sfPass = 1;

        private void BuildStartFromControls()
        {
            var lblFont = new Font("Tahoma", 11.25F, FontStyle.Bold);
            var valFont = new Font("Tahoma", 12F);

            lblStartFrom = MkLabel(gStr.gsStartFrom, 10, 264, 200, 26, lblFont);
            lblSfZone = MkLabel(gStr.gsZone, 10, 296, 90, 30, valFont);
            lblSfZoneVal = MkLabel("1", 104, 296, 46, 30, valFont);
            lblSfZoneVal.TextAlign = ContentAlignment.MiddleCenter;
            lblSfZoneVal.BorderStyle = BorderStyle.FixedSingle;

            lblSfPass = MkLabel(gStr.gsPass, 10, 334, 90, 30, valFont);
            lblSfPassVal = MkLabel("1", 104, 334, 46, 30, valFont);
            lblSfPassVal.TextAlign = ContentAlignment.MiddleCenter;
            lblSfPassVal.BorderStyle = BorderStyle.FixedSingle;

            btnSfZoneUp = MkStep("▲", 158, 294, () => StepStartFrom(true, +1));
            btnSfZoneDown = MkStep("▼", 204, 294, () => StepStartFrom(true, -1));
            btnSfPassUp = MkStep("▲", 158, 332, () => StepStartFrom(false, +1));
            btnSfPassDown = MkStep("▼", 204, 332, () => StepStartFrom(false, -1));

            btnSfApply = new Button
            {
                Text = gStr.gsApply,
                Location = new Point(10, 372),
                Size = new Size(240, 38),
                Font = lblFont,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Gainsboro,
                UseVisualStyleBackColor = false
            };
            btnSfApply.Click += BtnSfApply_Click;

            // they belong to the wizard, and the wizard lives in pnlPath - added to the form they
            // ended up underneath that panel and were never seen (test 2026-08-12)
            pnlPath.Controls.AddRange(new Control[] { lblStartFrom, lblSfZone, lblSfZoneVal,
                lblSfPass, lblSfPassVal, btnSfZoneUp, btnSfZoneDown, btnSfPassUp, btnSfPassDown,
                btnSfApply });

            foreach (Control c in StartFromControls()) { c.Visible = false; c.BringToFront(); }
        }

        private Label MkLabel(string text, int x, int y, int w, int h, Font f)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                Font = f,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private Button MkStep(string text, int x, int y, Action onClick)
        {
            var b = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(42, 34),
                Font = new Font("Tahoma", 12F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Gainsboro,
                UseVisualStyleBackColor = false
            };
            b.Click += (s, e) => onClick();
            return b;
        }

        private Control[] StartFromControls()
        {
            return new Control[] { lblStartFrom, lblSfZone, lblSfZoneVal, lblSfPass, lblSfPassVal,
                btnSfZoneUp, btnSfZoneDown, btnSfPassUp, btnSfPassDown, btnSfApply };
        }

        /// <summary>Both fields go back to 1 / 1 every time the window opens, and they do
        /// nothing until Apply is pressed (decision 2026-08-12).</summary>
        private void ResetStartFrom()
        {
            sfZone = 1; sfPass = 1;
            if (lblSfZoneVal != null) lblSfZoneVal.Text = "1";
            if (lblSfPassVal != null) lblSfPassVal.Text = "1";
            if (btnSfApply != null) btnSfApply.BackColor = Color.Gainsboro;
        }

        private void StepStartFrom(bool isZone, int delta)
        {
            if (isZone)
            {
                int zones = mf.plan.TryZoneStatsFromPlan(out List<int> zp, out _) ? zp.Count : 1;
                sfZone = Clamp(sfZone + delta, 1, Math.Max(1, zones));
                sfPass = 1;                                  // a new zone starts at its first pass
            }
            else
            {
                int passes = mf.plan.ZonePasses(sfZone).Count;
                sfPass = Clamp(sfPass + delta, 1, Math.Max(1, passes));
            }

            lblSfZoneVal.Text = sfZone.ToString();
            lblSfPassVal.Text = sfPass.ToString();
            btnSfApply.BackColor = Color.Khaki;              // there is something to apply
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

        private void BtnSfApply_Click(object sender, EventArgs e)
        {
            if (!mf.plan.StartFrom(sfZone, sfPass, out string err))
            {
                lblPlanStatus.Text = err;
                return;
            }

            mf.plan.RebuildGeometry();
            DrawPlanOverlay();
            UpdatePlanInfo();
            lblPlanStatus.Text = gStr.gsStartFrom + ": " + gStr.gsZone + " " + sfZone
                + " / " + gStr.gsPass + " " + sfPass;
            btnSfApply.BackColor = Color.Gainsboro;
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
            if (mf.tool.isToolOrchard)
                return HasOrchardRows() && HasOrchardPasses() && HasTurnBoundaries();
            return HasFieldBoundary() && HasReferenceLine() && HasTurnBoundaries();
        }

        // ── orchard prerequisites (field 2026-08-16) ─────────────────────────
        // The orchard wizard used to answer "ready" to everything, so a field without turn
        // limit lines still reported a finished plan while the whole turn feature was in fact
        // asleep (tnl.IsFeatureOn needs drawn lines) - the driver had no way of telling.
        private bool HasOrchardRows() => mf.orchRows != null && mf.orchRows.Count >= 2;

        private bool HasOrchardPasses()
        {
            foreach (var t in mf.trk.gArr) if (t.isOrchardDerived) return true;
            return false;
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
            bool[] ok = mf.tool.isToolOrchard
                ? new[] { HasOrchardRows(), HasOrchardPasses(), HasTurnBoundaries() }
                : new[] { HasFieldBoundary(), HasReferenceLine(), HasTurnBoundaries() };
            string[] names = mf.tool.isToolOrchard
                ? new[] { gStr.gsOrchard + " - " + gStr.gsRows, gStr.gsOrchardPasses, gStr.gsTurnLimitLines }
                : new[] { gStr.gsBoundary, gStr.gsTrack + " / " + gStr.gsCurve, gStr.gsTurnLimitLines };
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
            SetZoneOrderVisible(false);
            lblPlanInfo.Visible = false;
            btnGeneratePlan.Visible = false;
            btnAddPath.Visible = btnDeletePath.Visible = false;
            btnWizBack.Visible = btnWizNext.Visible = btnWizCancel.Visible = false;
            btnWizNext.Text = gStr.gsNext;
            if (lblManualPass != null) lblManualPass.Visible = false;
            if (lblStartFrom != null) foreach (Control c in StartFromControls()) c.Visible = false;
            btnResetPath.Visible = false;      // only where it means something: step 0 with a plan
            if (btnMode4 != null) btnMode4.Visible = false;
            SetZoneCutControlsVisible(false);
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
                        foreach (Control c in StartFromControls()) c.Visible = true;
                        btnResetPath.Visible = true;
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
                    // REQ 2026-10-11-2: HOW first - the mode decides what the zone step holds.
                    // Four pictograms 2x2 + the headland clip (it shapes the passes, not zones).
                    lblWizTitle.Text = gStr.gsPlanMode;
                    btnMode1.Visible = btnMode2.Visible = btnMode3.Visible = true;
                    if (btnMode4 != null) btnMode4.Visible = true;
                    chkHeadlandClip.Visible = true;
                    btnWizBack.Visible = btnWizNext.Visible = btnWizCancel.Visible = true;
                    btnWizNext.Enabled = true;
                    break;

                case 3:
                    // zones as the mode wants them: none for row by row, the field shape only
                    // for min turn, field shape + count for contour / zones. Manual passes last.
                    lblWizTitle.Text = gStr.gsZones;
                    bool seq = mf.plan.mode == CTurnPlan.ModeSequential;
                    bool counted = mf.plan.mode == CTurnPlan.ModeZones || mf.plan.mode == CTurnPlan.ModeContour;
                    if (seq)
                    {
                        lblNoZones.Visible = true;
                    }
                    else
                    {
                        SetZoneCutControlsVisible(true);
                        lblZones.Visible = nudZones.Visible = counted;
                        SetZoneStepperVisible(counted);
                        SetZoneOrderVisible(true);
                    }
                    lblPlanInfo.Visible = true;
                    lblManualPass.Visible = true;                // "Ručno dodavanje prolaza"
                    lstLines.Visible = true;                     // manual pass target line
                    lstLines.Top = 322; lstLines.Height = 78;    // shrunk to fit the zone tools above
                    btnAddPath.Visible = btnDeletePath.Visible = true;
                    btnWizBack.Visible = btnWizCancel.Visible = true;
                    btnWizNext.Visible = true;                   // repurposed as "Završi" (same as OK)
                    btnWizNext.Text = gStr.gsFinish;
                    UpdatePlanInfo();
                    break;
            }

            if (wizStep != 3) { lstLines.Top = 64; lstLines.Height = 260; }
        }

        //orchard (F50 turn plan): a small wizard - two steps, no zones and no line picking.
        //Every row already has its own line, so there is nothing to choose there; what is left
        //is HOW the passes are ordered and WHERE to start.
        private void UpdateWizardOrchard()
        {
            lstLines.Visible = false;
            btnMode1.Visible = btnMode2.Visible = btnMode3.Visible = false;
            chkHeadlandClip.Visible = false;
            lblZones.Visible = nudZones.Visible = false;
            SetZoneStepperVisible(false);
            SetZoneOrderVisible(false);
            btnAddPath.Visible = btnDeletePath.Visible = false;
            btnWizBack.Visible = btnWizNext.Visible = btnWizCancel.Visible = false;
            btnWizNext.Text = gStr.gsNext;
            btnGeneratePlan.Visible = false;
            btnResetPath.Visible = false;
            lblPlanInfo.Visible = false;
            if (lblStartFrom != null) foreach (Control c in StartFromControls()) c.Visible = false;
            SetOrchardStartVisible(false);
            HidePrereqChecklist();

            //nothing to plan with? say what is missing instead of pretending a plan exists
            if (!PlanPrereqsOk())
            {
                lblWizTitle.Text = gStr.gsGeneratePlan;
                lblPlanStatus.Text = "";
                RenderPrereqChecklist();
                return;
            }
            HidePrereqChecklist();

            switch (wizStep)
            {
                case 1:                                    // how the passes are ordered
                    lblWizTitle.Text = gStr.gsRowByRow + " / " + gStr.gsMinUTurn;
                    btnMode1.Visible = btnMode2.Visible = true;
                    //showing the current choice must not read as making it
                    syncingModeButtons = true;
                    btnMode1.Checked = mf.plan.mode != CTurnPlan.ModeMinUTurn;
                    btnMode2.Checked = mf.plan.mode == CTurnPlan.ModeMinUTurn;
                    syncingModeButtons = false;
                    btnWizBack.Visible = btnWizCancel.Visible = true;
                    break;

                case 2:                                    // where it starts + the map preview
                    lblWizTitle.Text = gStr.gsStartInterRow;
                    SetOrchardStartVisible(true);
                    lblPlanInfo.Visible = true;
                    btnWizBack.Visible = btnWizCancel.Visible = btnWizNext.Visible = true;
                    btnWizNext.Text = gStr.gsFinish;
                    UpdatePlanInfo();
                    break;

                default:                                   // 0: what is there now
                    wizStep = 0;
                    lblPlanInfo.Visible = true;
                    if (mf.plan.HasPlan)
                    {
                        lblWizTitle.Text = gStr.gsPlanExists;
                        btnWizNext.Visible = true;
                        btnResetPath.Visible = true;
                        foreach (Control c in StartFromControls()) c.Visible = true;
                        UpdatePlanInfo();
                    }
                    else
                    {
                        lblWizTitle.Text = gStr.gsOrchard;
                        btnGeneratePlan.Visible = true;
                    }
                    break;
            }
        }

        // ── orchard: which inter-row the plan starts from (no keypad, arrows only) ──
        private bool syncingModeButtons;
        private Label lblOrchStart, lblOrchStartVal;
        private Button btnOrchStartUp, btnOrchStartDown;

        private void BuildOrchardStartControls()
        {
            var lblFont = new Font("Tahoma", 11.25F, FontStyle.Bold);
            var valFont = new Font("Tahoma", 12F);

            lblOrchStart = MkLabel(gStr.gsInterRow, 10, 70, 120, 30, lblFont);
            lblOrchStartVal = MkLabel("1", 134, 70, 56, 30, valFont);
            lblOrchStartVal.TextAlign = ContentAlignment.MiddleCenter;
            lblOrchStartVal.BorderStyle = BorderStyle.FixedSingle;

            btnOrchStartUp = MkStep("▲", 198, 68, () => StepOrchardStart(+1));
            btnOrchStartDown = MkStep("▼", 244, 68, () => StepOrchardStart(-1));

            pnlPath.Controls.AddRange(new Control[]
                { lblOrchStart, lblOrchStartVal, btnOrchStartUp, btnOrchStartDown });
            foreach (var c in OrchardStartControls()) { c.Visible = false; c.BringToFront(); }
        }

        private Control[] OrchardStartControls()
        {
            return new Control[] { lblOrchStart, lblOrchStartVal, btnOrchStartUp, btnOrchStartDown };
        }

        private void SetOrchardStartVisible(bool vis)
        {
            if (lblOrchStart == null) return;
            foreach (var c in OrchardStartControls()) c.Visible = vis;
            if (vis) lblOrchStartVal.Text = mf.plan.orchardStartZone.ToString();
        }

        /// <summary>Highest inter-row number among the derived passes (1 when there are none).</summary>
        private int OrchardMaxZone()
        {
            int max = 1;
            foreach (var t in mf.trk.gArr)
            {
                if (!t.isOrchardDerived) continue;
                int z = COrchard.ZoneFromDerivedName(t.name);
                if (z > max) max = z;
            }
            return max;
        }

        //every step regenerates: the driver sees the numbered plan on the map right away
        private void StepOrchardStart(int delta)
        {
            int next = Clamp(mf.plan.orchardStartZone + delta, 1, OrchardMaxZone());
            if (next == mf.plan.orchardStartZone) return;

            mf.plan.orchardStartZone = next;
            lblOrchStartVal.Text = next.ToString();
            GenerateOrchardPlan();
        }

        // ── zone processing order (field feedback 2026-07-27 / 28) ──────────
        // The order is edited by TAPPING the zone badge on the map (see ZoneNumMarker):
        // type a number and the zone that held it takes this zone's old one. A plain swap,
        // so every number the driver has already settled - anything below the one being
        // typed - is left alone. This label only reports the resulting layout, leftmost
        // zone first; which zone is left or right of the line comes from the geometry.
        private void SetZoneOrderVisible(bool vis)
        {
            lblZoneOrderRead.Visible = vis;
            if (vis) UpdateZoneOrderLabel();
        }

        /// <summary>
        /// Processing order per spatial zone, read back off the BUILT plan: a zone's number
        /// is the rank of its first pass. Going through the plan rather than through
        /// TryZoneLayout matters - the layout preview counts passes from the raw offset
        /// range, while the plan only holds the ones that survived the boundary clip, so
        /// the two can disagree on how many zones there are. Falls back to the preview only
        /// when no plan exists yet (label before the first Generate).
        /// </summary>
        private int[] CurrentZoneOrder(out int zoneCount)
        {
            var firstOrder = new Dictionary<int, int>();
            foreach (var p in mf.plan.entries)
            {
                if (p.IsManual || p.zone < 1) continue;
                if (!firstOrder.TryGetValue(p.zone, out int o) || p.order < o)
                    firstOrder[p.zone] = p.order;
            }

            if (firstOrder.Count > 0)
            {
                zoneCount = firstOrder.Count;
                var zones = new List<int>(firstOrder.Keys);
                zones.Sort((a, b) => firstOrder[a].CompareTo(firstOrder[b]));
                var built = new int[zoneCount];
                for (int rank = 0; rank < zones.Count; rank++)
                {
                    int idx = zones[rank] - 1;
                    if (idx < 0 || idx >= zoneCount) { zoneCount = 0; return null; }
                    built[idx] = rank + 1;
                }
                return built;
            }

            zoneCount = 0;
            if (!mf.plan.TryZoneLayout((int)nudZones.Value, out List<int> zonePasses, out _))
                return null;
            zoneCount = zonePasses.Count;
            return CTurnPlan.DefaultZoneOrder(zoneCount, 0);
        }

        private void UpdateZoneOrderLabel()
        {
            int[] order = CurrentZoneOrder(out int n);
            lblZoneOrderRead.Text = order == null
                ? ""
                : gStr.gsZoneOrderRead + " " + CTurnPlan.FormatZoneOrder(order);
        }

        /// <summary>
        /// Tap on a zone badge: ask for the new processing number and SWAP it with whichever
        /// zone currently holds it. Out-of-range input changes nothing. Swapping (rather than
        /// shifting the rest along) is what keeps the already-confirmed lower numbers put,
        /// which is how the driver works through the zones - 1, then 2, then 3.
        /// </summary>
        private void EditZoneOrder(int zoneIdx)
        {
            int[] order = CurrentZoneOrder(out int n);
            if (order == null || zoneIdx < 0 || zoneIdx >= n) return;

            int current = order[zoneIdx];
            using (var f = new FormNumeric(1, n, current))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;

                int wanted = (int)Math.Round(f.ReturnValue);
                if (wanted < 1 || wanted > n || wanted == current) return;

                int other = Array.IndexOf(order, wanted);
                if (other < 0) return;
                order[other] = current;
                order[zoneIdx] = wanted;
            }

            mf.plan.zoneOrder = CTurnPlan.FormatZoneOrder(order);
            UpdateZoneOrderLabel();
            //rebuild so the map follows the new order right away - the missing piece in the
            //first cut, where only the label moved (f05fb-14)
            GenerateFromWizard();
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
            //orchard: two steps only - order, then where to start (no line, no zones)
            if (mf.tool.isToolOrchard)
            {
                if (wizStep == 2) { btnSave_Click(sender, e); return; }
                wizStep = wizStep < 1 ? 1 : 2;
                UpdateWizard();
                return;
            }

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
                         : Mode4On ? CTurnPlan.ModeMinUTurn
                         : CTurnPlan.ModeZones;
            if (chkAutoZones != null) mf.plan.autoZones = chkAutoZones.Checked;
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
            if (mf.plan.zonesAddedByRule > 0)
                lblPlanStatus.Text += "\n+" + mf.plan.zonesAddedByRule + " " + gStr.gsZonesAddedByRule;
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
        private bool Mode4On { get { return btnMode4 != null && btnMode4.Checked; } }

        private void btnMode1_CheckedChanged(object sender, EventArgs e)
        {
            if (btnMode1.Checked) { btnMode2.Checked = false; btnMode3.Checked = false; if (btnMode4 != null) btnMode4.Checked = false; ModePicked(); }
            else if (!btnMode2.Checked && !btnMode3.Checked && !Mode4On) btnMode1.Checked = true;
        }

        private void btnMode4_CheckedChanged(object sender, EventArgs e)
        {
            if (btnMode4.Checked) { btnMode1.Checked = false; btnMode2.Checked = false; btnMode3.Checked = false; ModePicked(); }
            else if (!btnMode1.Checked && !btnMode2.Checked && !btnMode3.Checked) btnMode4.Checked = true;
        }

        private void ModePicked()
        {
            if (syncingModeButtons) return;      // the wizard is only showing the state

            //orchard step 1: the two pictograms ARE the choice - set the mode, rebuild the
            //plan with it and move straight on to the start inter-row
            if (mf.tool.isToolOrchard)
            {
                if (wizStep != 1) return;
                mf.plan.mode = btnMode2.Checked ? CTurnPlan.ModeMinUTurn : CTurnPlan.ModeSequential;
                GenerateOrchardPlan();
                wizStep = 2;
                UpdateWizard();
                return;
            }

            //REQ 2026-10-11-2: the mode is step 2 now (it decides what the zone step shows)
            if (wizStep == 2) GenerateFromWizard();
        }

        private void btnMode2_CheckedChanged(object sender, EventArgs e)
        {
            if (btnMode2.Checked) { btnMode1.Checked = false; btnMode3.Checked = false; if (btnMode4 != null) btnMode4.Checked = false; ModePicked(); }
            else if (!btnMode1.Checked && !btnMode3.Checked && !Mode4On) btnMode2.Checked = true;
        }

        private void btnMode3_CheckedChanged(object sender, EventArgs e)
        {
            if (btnMode3.Checked) { btnMode1.Checked = false; btnMode2.Checked = false; if (btnMode4 != null) btnMode4.Checked = false; ModePicked(); }
            else if (!btnMode1.Checked && !btnMode2.Checked && !Mode4On) btnMode3.Checked = true;
        }

        private void nudZones_ValueChanged(object sender, EventArgs e)
        {
            //a hand-picked order cannot survive a different zone count - back to default
            mf.plan.zoneOrder = "";
            //the zone step is interactive: the zone split redraws on every change
            if (wizStep == 3) GenerateFromWizard();
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
                //t10 G: the derived orchard passes are already the plan - listing all of them
                //here would drown the hand-made lines these pickers are for
                if (t.isOrchardDerived) continue;
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
                // which AB line/curve the plan is built on, two rows above the zone info
                string abName = mf.plan.trackName;
                if (string.IsNullOrEmpty(abName) && mf.trk.idx >= 0 && mf.trk.idx < mf.trk.gArr.Count)
                    abName = mf.trk.gArr[mf.trk.idx].name;
                lblPlanInfo.Text = gStr.gsABline + ": " + abName + "\n\n"
                    + ZoneWidthText(zoneWidth, passesPerZone);
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

        /// <summary>
        /// Zone widths for the PATH tab. With the plan split at the reference line the two
        /// sides can hold a different number of zones of a different width, so one number
        /// no longer describes the field - report per side then (field feedback 2026-07-27).
        /// </summary>
        private string ZoneWidthText(double zoneWidth, int passesPerZone)
        {
            double w = mf.plan.WorkWidth();

            // With a plan on the table the plan IS the truth: passes that never made it in
            // (too short, cut by the headland) must not widen a zone (field call 2026-08-12).
            // Without a plan this stays an estimate from the geometry.
            List<int> zonePasses;
            int leftZones;
            bool have = mf.plan.HasPlan
                ? mf.plan.TryZoneStatsFromPlan(out zonePasses, out leftZones)
                : mf.plan.TryZoneLayout((int)nudZones.Value, out zonePasses, out leftZones);

            if (w >= 0.1 && have && leftZones > 0 && leftZones < zonePasses.Count)
            {
                int leftPasses = 0, rightPasses = 0;
                for (int i = 0; i < zonePasses.Count; i++)
                {
                    if (i < leftZones) leftPasses += zonePasses[i];
                    else rightPasses += zonePasses[i];
                }
                int rightZones = zonePasses.Count - leftZones;
                return "L: " + leftZones + " x " + (leftPasses * w / leftZones).ToString("N1") + " m\n"
                     + "R: " + rightZones + " x " + (rightPasses * w / rightZones).ToString("N1") + " m";
            }

            if (w >= 0.1 && have && zonePasses.Count > 0)
            {
                int total = 0;
                foreach (int c in zonePasses) total += c;
                return gStr.gsZoneWidth + ": " + (total * w / zonePasses.Count).ToString("N1") + " m\n"
                     + (int)Math.Ceiling((double)total / zonePasses.Count) + " " + gStr.gsPassesPerZone;
            }

            return gStr.gsZoneWidth + ": " + zoneWidth.ToString("N1") + " m\n"
                 + passesPerZone + " " + gStr.gsPassesPerZone;
        }

        // ── field plan controls (REQ 2026-10-11-2) ─────────────────────────
        // A fourth order (min turn, the F50 walk), the zone tools of the zone step and the
        // narrower left panel. Built at runtime like the other wizard extras, field tools only:
        // the orchard wizard keeps its own two-button layout untouched.
        private CheckBox btnMode4, chkAutoZones, btnCutAdd, btnCutDel;
        private Button btnCutClear;
        private Label lblNoZones;
        private const int narrowPanelWidth = 244;     // was 348
        private const int contentWidth = 224;         // 10 .. 234

        private void BuildFieldPlanControls()
        {
            btnMode4 = new CheckBox
            {
                Appearance = Appearance.Button,
                BackColor = btnMode1.BackColor,
                FlatStyle = FlatStyle.Flat,
                Font = btnMode1.Font,
                Text = gStr.gsMinUTurn,
                TextAlign = ContentAlignment.BottomCenter,
                TextImageRelation = TextImageRelation.ImageAboveText,
                UseVisualStyleBackColor = false,
                Visible = false
            };
            btnMode4.FlatAppearance.CheckedBackColor = Color.Gold;
            btnMode4.CheckedChanged += btnMode4_CheckedChanged;
            pnlPath.Controls.Add(btnMode4);
            btnMode4.BringToFront();

            var bold = new Font("Tahoma", 10F, FontStyle.Bold);
            chkAutoZones = new CheckBox
            {
                Appearance = Appearance.Button,
                BackColor = Color.AliceBlue,
                FlatStyle = FlatStyle.Flat,
                Font = bold,
                Text = gStr.gsAutoZones,
                TextAlign = ContentAlignment.MiddleCenter,
                UseVisualStyleBackColor = false,
                Checked = mf.plan.autoZones,
                Visible = false
            };
            chkAutoZones.FlatAppearance.CheckedBackColor = Color.MediumAquamarine;
            chkAutoZones.CheckedChanged += (s, e) =>
            {
                mf.plan.autoZones = chkAutoZones.Checked;
                if (wizStep == 3) GenerateFromWizard();
            };

            btnCutAdd = MkCutToggle(gStr.gsAddPoint);
            btnCutDel = MkCutToggle(gStr.gsDeletePoint);
            btnCutAdd.CheckedChanged += (s, e) => { if (btnCutAdd.Checked) btnCutDel.Checked = false; UpdateDragButton(); };
            btnCutDel.CheckedChanged += (s, e) => { if (btnCutDel.Checked) btnCutAdd.Checked = false; UpdateDragButton(); };

            btnCutClear = new Button
            {
                Text = gStr.gsDeleteAllPoints,
                Font = new Font("Tahoma", 9F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Gainsboro,
                UseVisualStyleBackColor = false,
                Visible = false
            };
            btnCutClear.Click += (s, e) =>
            {
                if (mf.plan.zoneCuts.Count == 0) return;
                mf.plan.zoneCuts.Clear();
                GenerateFromWizard();
            };

            lblNoZones = new Label
            {
                Text = gStr.gsNoZonesRowByRow,
                Font = new Font("Tahoma", 11.25F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false
            };

            pnlPath.Controls.AddRange(new Control[] { chkAutoZones, btnCutAdd, btnCutDel, btnCutClear, lblNoZones });
            foreach (Control c in new Control[] { chkAutoZones, btnCutAdd, btnCutDel, btnCutClear, lblNoZones })
                c.BringToFront();
        }

        private CheckBox MkCutToggle(string text)
        {
            var b = new CheckBox
            {
                Appearance = Appearance.Button,
                BackColor = Color.AliceBlue,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Tahoma", 9F, FontStyle.Bold),
                Text = text,
                TextAlign = ContentAlignment.MiddleCenter,
                UseVisualStyleBackColor = false,
                Visible = false
            };
            b.FlatAppearance.CheckedBackColor = Color.Gold;
            return b;
        }

        private void SetZoneCutControlsVisible(bool vis)
        {
            if (chkAutoZones == null) return;
            chkAutoZones.Visible = btnCutAdd.Visible = btnCutDel.Visible = btnCutClear.Visible = vis;
            lblNoZones.Visible = false;
            if (vis) chkAutoZones.Checked = mf.plan.autoZones;
            else { btnCutAdd.Checked = false; btnCutDel.Checked = false; }
        }

        private bool ZoneCutModeOn
        {
            get { return btnCutAdd != null && btnCutAdd.Visible && (btnCutAdd.Checked || btnCutDel.Checked); }
        }

        /// <summary>
        /// The left panel narrowed for field tools (user 2026-10-11: "it does not need to be
        /// that wide"), and the 2x2 mode grid: row by row | min turn / contour | zones.
        /// </summary>
        private void ApplyNarrowPathLayout()
        {
            pnlPath.Width = narrowPanelWidth;
            const int x = 10, w = contentWidth;

            lblWizTitle.Width = w;
            lstLines.Width = w;
            btnGeneratePlan.Width = w;
            lblPlanInfo.Width = w;
            lblPlanStatus.Width = w;
            lblZoneOrderRead.Width = w;
            btnResetPath.Width = Math.Min(btnResetPath.Width, w);
            if (lblManualPass != null) lblManualPass.Width = w;
            if (prereqLabels != null) foreach (var l in prereqLabels) l.Width = w;

            // 2x2 mode grid, buttons keep their width (104), lower than before
            const int bw = 104, bh = 100, gap = 8;
            var icon = new Size(72, 64);
            btnMode1.SetBounds(x, 64, bw, bh);
            btnMode4.SetBounds(x + bw + gap + 8, 64, bw, bh);
            btnMode2.SetBounds(x, 64 + bh + gap, bw, bh);
            btnMode3.SetBounds(x + bw + gap + 8, 64 + bh + gap, bw, bh);
            btnMode1.Image = TurnModeIcons.Make(CTurnPlan.ModeSequential, icon.Width, icon.Height);
            btnMode4.Image = TurnModeIcons.Make(CTurnPlan.ModeMinUTurn, icon.Width, icon.Height);
            btnMode2.Image = TurnModeIcons.Make(CTurnPlan.ModeContour, icon.Width, icon.Height);
            btnMode3.Image = TurnModeIcons.Make(CTurnPlan.ModeZones, icon.Width, icon.Height);
            chkHeadlandClip.SetBounds(x, 64 + 2 * bh + 2 * gap + 6, w, 40);

            // zone step: auto split, zone count, the three zone border tools
            chkAutoZones.SetBounds(x, 64, w, 38);
            lblZones.SetBounds(x, 110, 70, 34);
            nudZones.Location = new Point(x + 72, 108);
            if (zoneSteppers.Count == 2)
            {
                zoneSteppers[0].SetBounds(nudZones.Right + 4, 105, 34, 40);
                zoneSteppers[1].SetBounds(nudZones.Right + 42, 105, 34, 40);
            }
            int cw = (w - 2 * 6) / 3;
            btnCutAdd.SetBounds(x, 152, cw, 40);
            btnCutDel.SetBounds(x + cw + 6, 152, cw, 40);
            btnCutClear.SetBounds(x + 2 * (cw + 6), 152, cw, 40);
            lblNoZones.SetBounds(x, 64, w, 60);
            lblPlanInfo.SetBounds(x, 198, w, 66);
            lblZoneOrderRead.SetBounds(x, 268, w, 24);
            lblManualPass.SetBounds(x, 294, w, 26);
            int hw = (w - 8) / 2;
            btnAddPath.SetBounds(x, 404, hw, 42);
            btnDeletePath.SetBounds(x + hw + 8, 404, hw, 42);

            // start-from (step 0) rows pulled in to the narrower width
            lblSfZone.Width = lblSfPass.Width = 70;
            lblSfZoneVal.Left = lblSfPassVal.Left = 84;
            btnSfZoneUp.Left = btnSfPassUp.Left = 136;
            btnSfZoneDown.Left = btnSfPassDown.Left = 182;
            btnSfApply.Width = w;
            lblStartFrom.Width = w;

            // Back / Next / Cancel
            int bb = (w - 2 * 7) / 3;
            btnWizBack.SetBounds(x, btnWizBack.Top, bb, btnWizBack.Height);
            btnWizNext.SetBounds(x + bb + 7, btnWizNext.Top, bb, btnWizNext.Height);
            btnWizCancel.SetBounds(x + 2 * (bb + 7), btnWizCancel.Top, bb, btnWizCancel.Height);
            btnWizCancel.Font = btnWizBack.Font;
        }

        // driver's zone borders on the map: a magenta ring at each tapped point
        private class ZoneCutMarker : GMapMarker
        {
            public ZoneCutMarker(PointLatLng pos) : base(pos) { }

            public override void OnRender(Graphics g)
            {
                const float r = 11f;
                using (var pen = new Pen(Color.Magenta, 4f))
                {
                    g.DrawEllipse(pen, LocalPosition.X - r, LocalPosition.Y - r, 2 * r, 2 * r);
                    g.DrawLine(pen, LocalPosition.X, LocalPosition.Y - r - 8, LocalPosition.X, LocalPosition.Y + r + 8);
                }
            }
        }

        private void DrawZoneCuts()
        {
            foreach (var c in mf.plan.zoneCuts)
                planOverlay.Markers.Add(new ZoneCutMarker(LocalToLatLng(c.easting, c.northing)));
        }

        /// <summary>Zone step tap: add a border at the point, or remove the nearest one.</summary>
        private void ZoneCutTap(int x, int y)
        {
            if (btnCutAdd.Checked)
            {
                vec3 p = LatLngToLocal(gMapControl.FromLocalToLatLng(x, y));
                mf.plan.zoneCuts.Add(new vec2(p.easting, p.northing));
            }
            else
            {
                int best = -1;
                double bestD = 24;   // px
                for (int i = 0; i < mf.plan.zoneCuts.Count; i++)
                {
                    var c = mf.plan.zoneCuts[i];
                    GPoint sp = gMapControl.FromLatLngToLocal(LocalToLatLng(c.easting, c.northing));
                    double d = Math.Sqrt((sp.X - x) * (sp.X - x) + (sp.Y - y) * (sp.Y - y));
                    if (d < bestD) { bestD = d; best = i; }
                }
                if (best < 0) return;
                mf.plan.zoneCuts.RemoveAt(best);
            }
            //a hand-picked order cannot survive a different zone layout
            mf.plan.zoneOrder = "";
            GenerateFromWizard();
        }

        // ── Orchard mode (F50 phase 3): the PATH tab plans from rows + tool ──
        // Field controls (mode/zones/line/headland/manual passes) make no sense in
        // an orchard - the plan is the ordered pass sequence from the implement
        // settings; Follow plan / HUD / skip stay the same single brain.
        private void SetupOrchardPathTab()
        {
            btnMode1.Visible = btnMode2.Visible = btnMode3.Visible = false;
            lblZones.Visible = nudZones.Visible = false;
            SetZoneOrderVisible(false);
            chkHeadlandClip.Visible = false;
            lblTrack.Visible = cboTrack.Visible = false;
            //t10 F: manual passes DO belong in an orchard - a skewed boundary, the strip along
            //it, the ground around the orchard. Those lines are drawn in Smart AB (they are not
            //derived, so a path rebuild leaves them alone) and added to the plan from here.
            btnAddPath.Visible = btnDeletePath.Visible = true;
            btnGeneratePlan.Text = gStr.gsOrchardPlan;
            lblPlanInfo.Text = gStr.gsOrchardModePlanInfo;

            //F50 turn plan: two orders to pick from - row by row, or skipping so every turn
            //has room for a U. Mode 3 (zones) has no meaning without zones.
            btnMode1.Text = gStr.gsRowByRow;
            btnMode2.Text = gStr.gsMinUTurn;
            btnMode2.Image = TurnModeIcons.Make(CTurnPlan.ModeMinUTurn, 88, 82);
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

            // SIM 0729 t5: the passes are derived centrally from the rows + the implement
            // (path mode included), so the plan uses that one derivation instead of building
            // its own set - no second copy of the path, no tracks piling up.
            if (mf.orchRows.Count == 0) mf.OrchardLoadRows(fieldDir);
            else mf.OrchardRebuildTracks("orchard plan");

            var passes = new List<CTrk>();
            foreach (var t in mf.trk.gArr) if (t.isOrchardDerived) passes.Add(t);
            if (passes.Count == 0)
            {
                lblPlanStatus.Text = "No passes - check the tool width and the path mode";
                return;
            }
            mf.trk.idx = mf.trk.gArr.IndexOf(passes[0]);   // first pass

            //t10 D: one place builds the plan from the passes (zones = inter-row numbers, local
            //pass order kept, manual passes carried over, saved per working width)
            mf.OrchardSyncPlan(true);

            DrawPlanOverlay();
            UpdatePlanInfo();
            lblPlanStatus.Text = passes.Count + " orchard passes"
                + (mf.plan.orchardSeqFallback ? "  ·  " + gStr.gsOrchardSeqFallback : "")
                + (mf.orchardWarn.Length > 0 ? "  ·  " + mf.orchardWarn : "");
        }

        //"Generate path" now opens the wizard (line -> zones -> mode); each step
        //generates and persists, so the plan is ready the moment the mode is picked
        private void btnGeneratePlan_Click(object sender, EventArgs e)
        {
            if (mf.tool.isToolOrchard) { wizStep = 1; UpdateWizard(); return; }

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

            // highlight which AB line the plan is built on (red dashed) - only once the user
            // explicitly picked it in the combo. Drawn after the passes, thick and on a white
            // halo: the first pass lies right on it and used to hide it
            if (cboTrack.SelectedIndex >= 0
                && mf.plan.TryGetActiveLineGeometry(out List<List<vec3>> lineSegs))
            {
                foreach (var seg in lineSegs)
                {
                    var route = new List<PointLatLng>(seg.Count);
                    for (int i = 0; i < seg.Count; i++)
                        route.Add(LocalToLatLng(seg[i].easting, seg[i].northing));
                    planOverlay.Routes.Add(new GMapRoute(route, "activeLineHalo")
                    {
                        Stroke = new Pen(Color.White, 9f)
                    });
                    planOverlay.Routes.Add(new GMapRoute(new List<PointLatLng>(route), "activeLine")
                    {
                        Stroke = new Pen(Color.Red, 6f) { DashStyle = DashStyle.Dash }
                    });
                }
            }

            DrawZoneBadges();
            DrawZoneCuts();
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

        /// <summary>
        /// Zone badge (field feedback 2026-07-28): a thick rounded box in the zone colour
        /// carrying the zone's PROCESSING number. Sits at a quarter of the pass instead of
        /// the middle, where the pass numbers already are, so the two never collide. Tap it
        /// to reorder - see EditZoneOrder.
        /// </summary>
        private class ZoneNumMarker : GMapMarker
        {
            public readonly int zoneIdx;          // 0-based spatial zone
            private readonly string text;
            private readonly Color color;

            public const int HalfW = 21, HalfH = 15;

            public ZoneNumMarker(PointLatLng pos, int idx, string t, Color c) : base(pos)
            {
                zoneIdx = idx;
                text = t;
                color = c;
            }

            public override void OnRender(Graphics g)
            {
                var box = new Rectangle(LocalPosition.X - HalfW, LocalPosition.Y - HalfH,
                    2 * HalfW, 2 * HalfH);
                using (var back = new SolidBrush(Color.FromArgb(225, Color.White)))
                    g.FillRectangle(back, box);
                using (var pen = new Pen(color, 5f))
                    g.DrawRectangle(pen, box);
                using (var f = new Font("Tahoma", 12f, FontStyle.Bold))
                {
                    SizeF sz = g.MeasureString(text, f);
                    g.DrawString(text, f, Brushes.Black,
                        LocalPosition.X - sz.Width / 2f, LocalPosition.Y - sz.Height / 2f);
                }
            }
        }

        /// <summary>
        /// One badge per zone, on the zone's middle pass at ~25 % of its length.
        /// </summary>
        private void DrawZoneBadges()
        {
            var byZone = new Dictionary<int, List<CPlanEntry>>();
            foreach (var p in mf.plan.entries)
            {
                if (p.IsManual || p.zone < 1 || p.segments.Count == 0) continue;
                if (!byZone.TryGetValue(p.zone, out List<CPlanEntry> lst))
                    byZone[p.zone] = lst = new List<CPlanEntry>();
                lst.Add(p);
            }

            foreach (var kv in byZone)
            {
                var lst = kv.Value;
                lst.Sort((a, b) => a.offsetIdx.CompareTo(b.offsetIdx));
                CPlanEntry mid = lst[lst.Count / 2];

                // longest segment of that pass, then a quarter of the way along it
                List<vec3> main = mid.segments[0];
                double bestLen = -1;
                foreach (var seg in mid.segments)
                {
                    double len = 0;
                    for (int i = 1; i < seg.Count; i++) len += glm.Distance(seg[i - 1], seg[i]);
                    if (len > bestLen) { bestLen = len; main = seg; }
                }
                if (main.Count < 2) continue;

                int at = Math.Max(0, Math.Min(main.Count - 1, main.Count / 4));
                planOverlay.Markers.Add(new ZoneNumMarker(
                    LocalToLatLng(main[at].easting, main[at].northing),
                    kv.Key - 1,
                    ZoneProcessingNumber(kv.Key - 1).ToString(),
                    zoneColors[(kv.Key - 1) % zoneColors.Length]));
            }
        }

        /// <summary>Processing number currently assigned to a spatial zone (1-based).</summary>
        private int ZoneProcessingNumber(int zoneIdx)
        {
            int[] order = CurrentZoneOrder(out int n);
            return order != null && zoneIdx >= 0 && zoneIdx < n ? order[zoneIdx] : zoneIdx + 1;
        }

        private bool HitTestZoneBadge(int x, int y, out int zoneIdx)
        {
            zoneIdx = -1;
            foreach (var m in planOverlay.Markers)
            {
                if (!(m is ZoneNumMarker z)) continue;
                GPoint sp = gMapControl.FromLatLngToLocal(z.Position);
                if (Math.Abs(sp.X - x) <= ZoneNumMarker.HalfW
                    && Math.Abs(sp.Y - y) <= ZoneNumMarker.HalfH)
                {
                    zoneIdx = z.zoneIdx;
                    return true;
                }
            }
            return false;
        }

        private void ShowStyle(System.Windows.Forms.Panel p)
        {
            pnlK.Visible = (p == pnlK);
            pnlU.Visible = (p == pnlU);
            pnlRS.Visible = (p == pnlRS);

            // Apply sits below the parameters, which is inside the style panels' band -
            // the panels are opaque, so it has to stay on top of whichever one is shown.
            btnApplyParams.BringToFront();
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
            isLoadingParams = true;
            //F51/2026-08-12: the whole turn set - shape, tolerances and the extra boundary
            //offset - follows the IMPLEMENT profile now; the tablet is not the machine, and the
            //implement is what changes the turn. The tractor still caps the radius from below.
            var v = Properties.ToolSettings.Default;
            var t = Properties.ToolSettings.Default;
            SetNud(nudExitK, v.setTurn_K_exitRadius);
            SetNud(nudDistK, v.setTurn_K_entryStartDist);
            SetNud(nudAngleK, v.setTurn_K_entryAngle);
            SetNud(nudTransK, v.setTurn_K_transitionRadius);
            SetNud(nudOutU, v.setTurn_U_exitRadius);
            SetNud(nudInU, v.setTurn_U_entryRadius);
            SetNud(nudOvershootU, v.setTurn_U_overshootDist);
            SetNud(nudTolerance, t.setTurn_triggerTolerance);
            SetNud(nudToleranceAfter, t.setTurn_triggerToleranceAfter);
            SetNud(nudBndOffset, t.setTurn_boundaryOffset);

            btnStyleK.Checked = true;
            ShowStyle(pnlK);

            isLoadingParams = false;
            paramsDirty = false;
            UpdateApplyButton();
            UpdateMinRadiusWarning();
        }

        // ── Apply (2026-08-10) ────────────────────────────────────────────
        // The parameters on this tab only reached the machine through OK - and OK closes the
        // window, so tuning one meant reopening it every time. Apply commits them and leaves
        // the window open. It stays greyed out until something actually changes, so it also
        // tells you at a glance whether there is anything pending.
        private bool paramsDirty;

        private void MarkParamsDirty()
        {
            if (isLoadingParams) return;
            paramsDirty = true;
            UpdateMinRadiusWarning();
            UpdateApplyButton();
        }

        private void UpdateApplyButton()
        {
            btnApplyParams.Enabled = paramsDirty;
            btnApplyParams.BackColor = paramsDirty
                ? System.Drawing.Color.Gold
                : System.Drawing.Color.Gainsboro;
        }

        private void btnApplyParams_Click(object sender, EventArgs e)
        {
            SaveTurnParams();
            mf.tnl.BuildEffectiveLines();   //the drawn wheel limit moves with the offset
            paramsDirty = false;
            UpdateApplyButton();
            gMapControl.Invalidate();
        }

        /// <summary>Hook every parameter field so editing one lights up Apply.</summary>
        private void WireParamChangeTracking()
        {
            var nuds = new System.Windows.Forms.NumericUpDown[]
            {
                nudExitK, nudDistK, nudAngleK, nudTransK, nudOutU, nudInU,
                nudOvershootU, nudTolerance, nudToleranceAfter, nudBndOffset
            };
            foreach (var n in nuds) n.ValueChanged += (s, e) => MarkParamsDirty();

            paramsDirty = false;
            UpdateApplyButton();
        }

        private bool isLoadingParams;

        private void SaveTurnParams()
        {
            var v = Properties.ToolSettings.Default;   // turn shape lives on the implement (2026-08-12)
            v.setTurn_K_exitRadius = (double)nudExitK.Value;
            v.setTurn_K_entryStartDist = (double)nudDistK.Value;
            v.setTurn_K_entryAngle = (double)nudAngleK.Value;
            v.setTurn_K_transitionRadius = (double)nudTransK.Value;
            v.setTurn_U_exitRadius = (double)nudOutU.Value;
            v.setTurn_U_entryRadius = (double)nudInU.Value;
            v.setTurn_U_overshootDist = (double)nudOvershootU.Value;
            v.Save();

            var t = Properties.ToolSettings.Default;
            double oldOffset = t.setTurn_boundaryOffset;
            t.setTurn_triggerTolerance = (double)nudTolerance.Value;
            t.setTurn_triggerToleranceAfter = (double)nudToleranceAfter.Value;
            t.setTurn_boundaryOffset = (double)nudBndOffset.Value;
            t.Save();

            //the dashed wheel limit is drawn with this offset baked in - redraw it at once
            if (Math.Abs(oldOffset - t.setTurn_boundaryOffset) > 0.001)
            {
                mf.tnl.BuildEffectiveLines();
                mf.yt.ReloadTurnSettings();
            }
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
            //field feedback 2026-07-27: metre values that need fine adjustment (the turn
            //radii and the trigger tolerances) step by 0.2; the rest keeps its old step
            MakeKeypadField(nudExitK, 0.2);
            MakeKeypadField(nudDistK, 0.5);
            MakeKeypadField(nudAngleK, 5);
            MakeKeypadField(nudOutU, 0.2);
            MakeKeypadField(nudInU, 0.2);
            MakeKeypadField(nudOvershootU, 0.5);
            MakeKeypadField(nudTolerance, 0.2);
            MakeKeypadField(nudToleranceAfter, 0.2);
            MakeKeypadField(nudBndOffset, 0.1);
            MakeKeypadField(nudZones, 1, zoneSteppers);   //wizard step 2 shows/hides these

            WireParamChangeTracking();
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
