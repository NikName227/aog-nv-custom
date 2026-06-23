using AgLibrary.Logging;
using AgOpenGPS.Core.DrawLib;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Translations;
using AgOpenGPS.Core.Visuals;
using AgOpenGPS.Helpers;
using AgOpenGPS.Visuals;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AgOpenGPS
{
    /// <summary>
    /// "Smart AB" — enhanced AB/curve builder (NEW window; original FormABDraw is untouched).
    /// Phase 1: free points placed/moved anywhere (not snapped to the boundary), arrow-pad point
    /// editing (Fine 1px / Coarse 10px or drag), Add/Delete point, +/- zoom and pan, "active
    /// operation" text. Line generation / satellite / check / smooth come in later phases.
    /// GUI is English; Croatian comes from the gStr translation map.
    /// </summary>
    public partial class FormSmartAB : Form
    {
        private readonly FormGPS mf;

        // GL view (persistent): camera distance factor + normalized pan offsets.
        private double zoom = 2.0, sX = 0, sY = 0;

        // Free points (field easting/northing). A and B = the two FARTHEST-APART points
        // (recomputed on any change), so adding a point can move A/B.
        private readonly List<vec2> pts = new List<vec2>();
        private int aIndex = 0, bIndex = -1;
        private string lastMake; // last line type built ("AB"/"Curve"/"Bnd"), for auto-regenerate

        // Tracks (starts as a copy of the field's tracks). Committed to mf.trk.gArr on Accept.
        private readonly List<CTrk> gTemp = new List<CTrk>();
        // Control points per track (Phase 2b, option A): remembered for lines created/edited this
        // session so Edit restores them 1:1; null for lines loaded from the field (derived on Edit).
        private readonly List<List<vec2>> ctrlPts = new List<List<vec2>>();

        // Phase 2b modes: Browse = show only the active saved line (◄◄/►►/Del); Build = place/edit
        // points for a New or Edited line.
        private enum LineMode { Browse, Build }
        private LineMode mode = LineMode.Build;
        private int activeIdx = -1; // Browse selection
        private int workIdx = -1;   // line being built/edited (in gTemp)

        // Point edit (arrow-pad), same idea as the geoportal boundary tool.
        private enum PointEdit { Off, Mark, Move }
        private PointEdit pointMode = PointEdit.Off;
        private int selIndex = -1;
        private int stepPx = 10; // Coarse by default; Fine = 1

        // Mouse drag state.
        private int dragIndex = -1;
        private bool panning;
        private Point downPt;
        private double panStartSX, panStartSY;
        private bool moved;

        private readonly Timer redraw = new Timer();
        private bool glReady; // GL context exists (set in oglSelf_Load); Resize can fire before it

        // Phase 3: App-Map satellite background (reuse worldGrid.BingMap) + cadastral parcels.
        private BingMapVisual satVisual;

        // Phase 4: boxes where the line deviates from the boundary by > threshold (and < 2 m).
        private readonly List<vec2> checkBoxes = new List<vec2>();

        // Phase 5: smoothing. smoothVal -1 = off ("--"); smoothLine = preview overlay.
        private int smoothVal = -1;
        private List<vec3> smoothLine;

        // Measure tool: a temporary ruler — two free RED points joined by a thin red line.
        // Always draggable while on; shows the distance between them; never saved anywhere.
        private bool measureOn;
        private vec2 mp0, mp1;
        private int measureDrag = -1; // 0 or 1 while dragging a measure endpoint

        // Borderless-window drag + layout-on-resize (fullscreen).
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        private const int WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 0x2;
        private Dictionary<Control, Point> origLoc;

        public FormSmartAB(Form callingForm)
        {
            mf = callingForm as FormGPS;
            InitializeComponent();
            LocalizeText();

            mf.CalculateMinMax();

            // start from the field's existing tracks (so Accept keeps them)
            foreach (var t in mf.trk.gArr) { gTemp.Add(new CTrk(t)); ctrlPts.Add(null); }

            redraw.Interval = 250;
            redraw.Tick += (s, e) => oglSelf.Invalidate();

            this.KeyPreview = true;
            this.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            this.MouseDown += FormDrag_MouseDown;
        }

        // ── Window controls (touch) + layout ──────────────────────────────
        private void FormDrag_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(this.Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
        }

        private void btnWinFull_Click(object sender, EventArgs e)
        {
            this.WindowState = this.WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal : FormWindowState.Maximized;
        }

        private void btnWinClose_Click(object sender, EventArgs e) => Close();

        private void FormSmartAB_Resize(object sender, EventArgs e) => LayoutControls();

        // Grow the (square) GL map to fit the height; keep the tool panel hugging the right.
        private void LayoutControls()
        {
            if (origLoc == null)
            {
                origLoc = new Dictionary<Control, Point>();
                foreach (Control c in Controls) if (c != oglSelf) origLoc[c] = c.Location;
            }

            // GL fills the area left of the tool panel (rectangle, aspect-correct projection).
            int glW = Math.Max(300, ClientSize.Width - 332);
            int glH = Math.Max(300, ClientSize.Height - 36);
            oglSelf.SetBounds(8, 8, glW, glH);

            int dx = ClientSize.Width - 1000; // design width
            foreach (var kv in origLoc)
                if (kv.Value.X >= 676) kv.Key.Location = new Point(kv.Value.X + dx, kv.Value.Y);

            btnSat.Location = new Point(oglSelf.Right - 64, oglSelf.Top + 6);
            btnZoomIn.Location = new Point(oglSelf.Right - 64, oglSelf.Bottom - 124);
            btnZoomOut.Location = new Point(oglSelf.Right - 64, oglSelf.Bottom - 62);
            lblActiveOp.Location = new Point(8, oglSelf.Bottom + 6);
            btnWinClose.Location = new Point(ClientSize.Width - 60, 6);
            btnWinFull.Location = new Point(ClientSize.Width - 118, 6);

            SetupProjection();
            oglSelf.Invalidate();
        }

        private void LocalizeText()
        {
            // Fall back to the English default if a translation key is missing/empty,
            // so a button never ends up blank.
            string L(string s, string def) => string.IsNullOrEmpty(s) ? def : s;

            this.Text = L(gStr.gsSmartAB, "Smart AB");
            btnFine.Text = L(gStr.gsFine, "Fine");
            btnCoarse.Text = L(gStr.gsCoarse, "Coarse");
            btnMeasure.Text = L(gStr.gsMeasure, "Measure");
            btnAddPoint.Text = L(gStr.gsAddPoint, "Add point");
            btnDeletePoint.Text = L(gStr.gsDeletePoint, "Delete point");
            btnNew.Text = L(gStr.gsNew, "New");
            btnEdit.Text = L(gStr.gsEdit, "Edit");
            btnDeleteLine.Text = L(gStr.gsDelete, "Del");
            lblName.Text = L(gStr.gsName, "Name");
            lblSmoot.Text = L(gStr.gsSmooth, "Smooth");
            lblCheck.Text = L(gStr.gsCheckOutline, "Check boundary outline");
            btnMakeAB.Text = L(gStr.gsABline, "AB line");
            btnMakeBndCurve.Text = L(gStr.gsCurveByBoundary, "Curve by Boundary");
            btnMakeCurve.Text = L(gStr.gsCurve, "Curve");
            lblPointMode.Text = "";
            lblActiveOp.Text = "";
        }

        private void FormSmartAB_Load(object sender, EventArgs e)
        {
            // Start in Browse if the field already has lines, else in Build (ready for a new line).
            if (gTemp.Count > 0) { activeIdx = 0; SetMode(LineMode.Browse); RefreshActiveName(); }
            else SetMode(LineMode.Build);

            UpdateArrowColors();
            UpdateLineButtons();
            LayoutControls();
            redraw.Start();

            // keep the on-map buttons above the GL control
            btnZoomIn.BringToFront();
            btnZoomOut.BringToFront();
            btnSat.BringToFront();
            btnWinFull.BringToFront();
            btnWinClose.BringToFront();
        }

        private void FormSmartAB_FormClosing(object sender, FormClosingEventArgs e)
        {
            redraw.Stop();
        }

        // ── GL setup / render ─────────────────────────────────────────────
        private void oglSelf_Load(object sender, EventArgs e)
        {
            oglSelf.MakeCurrent();
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Back);
            GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
            glReady = true;
            SetupProjection();
        }

        private void oglSelf_Resize(object sender, EventArgs e)
        {
            SetupProjection();
        }

        private void SetupProjection()
        {
            if (!glReady || oglSelf.Width <= 0 || oglSelf.Height <= 0) return;
            oglSelf.MakeCurrent();
            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadIdentity();
            GL.Viewport(0, 0, oglSelf.Width, oglSelf.Height);
            float aspect = (float)oglSelf.Width / oglSelf.Height;
            Matrix4 mat = Matrix4.CreatePerspectiveFieldOfView(1.01f, aspect, 1.0f, 20000);
            GL.LoadMatrix(ref mat);
            GL.MatrixMode(MatrixMode.Modelview);
        }

        private void oglSelf_Paint(object sender, PaintEventArgs e)
        {
            if (!glReady) return;
            oglSelf.MakeCurrent();
            GL.Clear(ClearBufferMask.DepthBufferBit | ClearBufferMask.ColorBufferBit);
            GL.LoadIdentity();

            GL.Translate(0, 0, -mf.maxFieldDistance * zoom);
            GL.Translate(-mf.fieldCenterX + sX * mf.maxFieldDistance,
                         -mf.fieldCenterY + sY * mf.maxFieldDistance, 0);

            // satellite background (App-Map) + cadastral parcels (Phase 3)
            if (satVisual != null)
            {
                GL.Disable(EnableCap.CullFace); // the textured quad is back-facing here -> would be culled
                satVisual.Draw();
                GL.Enable(EnableCap.CullFace);
                if (Properties.Settings.Default.setMap_showParcelsOnMain && mf.parcels.Segments.Length >= 2)
                {
                    GL.LineWidth(1);
                    GL.Color4(0f, 0.75f, 1f, 0.9f);
                    GLW.DrawLinesPrimitive(mf.parcels.Segments);
                }
            }

            // boundary
            for (int j = 0; j < mf.bnd.bndList.Count; j++)
            {
                GeoCoord[] fenceLineEar = GeoRefactorHelper.ToGeoCoordArray(mf.bnd.bndList[j].fenceLineEar);
                FenceLineVisual.DrawFenceLine(fenceLineEar, false);
            }

            VehicleDotVisual.DrawVehicleDot(mf.pivotAxlePos.ToGeoCoord());

            DrawBuiltLines();
            if (smoothLine != null && smoothLine.Count > 1)
            {
                GL.LineWidth(3);
                GL.Color3(1.0f, 0.2f, 1.0f); // smoothed overlay (magenta)
                GL.Begin(PrimitiveType.LineStrip);
                foreach (vec3 p in smoothLine) GL.Vertex3(p.easting, p.northing, 0);
                GL.End();
            }
            if (mode == LineMode.Build) DrawPoints();
            DrawCheckBoxes();
            DrawMeasure();

            GL.Flush();
            oglSelf.SwapBuffers();
        }

        // Markers for free points. A = cyan, B = orange, others white; selected = green.
        private void DrawPoints()
        {
            double h = 8.0 * MetersPerPixel(); // ~16px square, constant on screen
            GL.LineWidth(2);
            for (int i = 0; i < pts.Count; i++)
            {
                if (i == selIndex) GL.Color3(0.2f, 1.0f, 0.2f);
                else if (i == aIndex) GL.Color3(0.2f, 0.9f, 1.0f); // A
                else if (i == bIndex) GL.Color3(1.0f, 0.6f, 0.1f); // B
                else GL.Color3(1.0f, 1.0f, 1.0f);

                double x = pts[i].easting, y = pts[i].northing;
                GL.Begin(PrimitiveType.LineLoop);
                GL.Vertex3(x - h, y - h, 0);
                GL.Vertex3(x + h, y - h, 0);
                GL.Vertex3(x + h, y + h, 0);
                GL.Vertex3(x - h, y + h, 0);
                GL.End();

                // centre dot to emphasise the exact point
                GL.PointSize(4f);
                GL.Begin(PrimitiveType.Points);
                GL.Vertex3(x, y, 0);
                GL.End();
            }
        }

        // ── Coordinate transforms (field <-> screen) ──────────────────────
        // Meters per pixel is set by the VERTICAL FOV (height); the projection aspect makes
        // the horizontal scale match, so the GL view can be any rectangle (fills fullscreen).
        private double MetersPerPixel()
        {
            double scale = oglSelf.Height * 0.903;
            return mf.maxFieldDistance / scale * zoom;
        }

        private vec2 ScreenToWorld(int px, int py)
        {
            double W = oglSelf.Width, H = oglSelf.Height, k = MetersPerPixel();
            double e = (px - W / 2.0) * k + mf.fieldCenterX - sX * mf.maxFieldDistance;
            double n = (H / 2.0 - py) * k + mf.fieldCenterY - sY * mf.maxFieldDistance;
            return new vec2(e, n);
        }

        private Point WorldToScreen(vec2 w)
        {
            double W = oglSelf.Width, H = oglSelf.Height, k = MetersPerPixel();
            double px = (w.easting - mf.fieldCenterX + sX * mf.maxFieldDistance) / k + W / 2.0;
            double py = H / 2.0 - (w.northing - mf.fieldCenterY + sY * mf.maxFieldDistance) / k;
            return new Point((int)Math.Round(px), (int)Math.Round(py));
        }

        private int HitTestPoint(int px, int py)
        {
            for (int i = 0; i < pts.Count; i++)
            {
                Point sp = WorldToScreen(pts[i]);
                if (Math.Abs(sp.X - px) <= 16 && Math.Abs(sp.Y - py) <= 16) return i;
            }
            return -1;
        }

        // ── Mouse ─────────────────────────────────────────────────────────
        private void oglSelf_MouseDown(object sender, MouseEventArgs e)
        {
            downPt = e.Location;
            moved = false;
            dragIndex = -1;
            panning = false;

            // Right button always pans — also while in point-edit/add/delete modes.
            if (e.Button == MouseButtons.Right)
            {
                panning = true; panStartSX = sX; panStartSY = sY;
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            // Measure endpoints are always grabbable while the ruler is on (any mode).
            int mh = HitTestMeasure(e.X, e.Y);
            if (mh >= 0) { measureDrag = mh; return; }

            int hit = HitTestPoint(e.X, e.Y);
            if (hit >= 0 && !btnAddPoint.Checked && !btnDeletePoint.Checked)
            {
                dragIndex = hit; // grab a point to drag
            }
            else if (!btnAddPoint.Checked && !btnDeletePoint.Checked)
            {
                panning = true; panStartSX = sX; panStartSY = sY; // left-drag empty space = pan
            }
        }

        private void oglSelf_MouseMove(object sender, MouseEventArgs e)
        {
            if (measureDrag >= 0)
            {
                moved = true;
                if (measureDrag == 0) mp0 = ScreenToWorld(e.X, e.Y);
                else mp1 = ScreenToWorld(e.X, e.Y);
                UpdateMeasureLabel();
                oglSelf.Invalidate();
                return;
            }
            if (dragIndex >= 0)
            {
                moved = true;
                pts[dragIndex] = ScreenToWorld(e.X, e.Y);
                RecomputeAB();
                oglSelf.Invalidate();
            }
            else if (panning)
            {
                moved = true;
                double k = MetersPerPixel();
                sX = panStartSX + (e.X - downPt.X) * k / mf.maxFieldDistance;
                sY = panStartSY - (e.Y - downPt.Y) * k / mf.maxFieldDistance;
                oglSelf.Invalidate();
            }
        }

        private void oglSelf_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { panning = false; return; }
            if (e.Button != MouseButtons.Left) return;

            if (measureDrag >= 0) { measureDrag = -1; moved = false; oglSelf.Invalidate(); return; }

            bool wasPointDrag = dragIndex >= 0 && moved;

            if (!moved) // a tap
            {
                if (btnAddPoint.Checked)
                {
                    pts.Add(ScreenToWorld(e.X, e.Y));
                    RecomputeAB();
                    UpdateLineButtons();
                    Regenerate();           // new point -> update the line
                    lblActiveOp.Text = gStr.gsAddPoint;
                }
                else if (btnDeletePoint.Checked)
                {
                    int hit = HitTestPoint(e.X, e.Y);
                    if (hit >= 0) DeletePoint(hit);
                }
                else if (pointMode != PointEdit.Off)
                {
                    int hit = HitTestPoint(e.X, e.Y);
                    if (hit >= 0) SelectPoint(hit);
                }
            }

            dragIndex = -1; panning = false; moved = false;
            if (wasPointDrag) Regenerate();  // dragged a point -> update the line
            oglSelf.Invalidate();
        }

        // ── Free-point model ──────────────────────────────────────────────
        // A,B = the pair of points with the maximum distance between them (the "diameter").
        private void RecomputeAB()
        {
            aIndex = 0; bIndex = -1;
            if (pts.Count < 2) return;
            double best = -1;
            for (int i = 0; i < pts.Count; i++)
                for (int j = i + 1; j < pts.Count; j++)
                {
                    double dx = pts[i].easting - pts[j].easting;
                    double dy = pts[i].northing - pts[j].northing;
                    double d = dx * dx + dy * dy;
                    if (d > best) { best = d; aIndex = i; bIndex = j; }
                }
        }

        // Re-run the last line build after the points were edited (live update).
        private void Regenerate()
        {
            if (lastMake == "AB" && pts.Count == 2) btnMakeAB_Click(null, null);
            else if (lastMake == "Curve" && pts.Count > 2) btnMakeCurve_Click(null, null);
            else if (lastMake == "Bnd" && pts.Count == 2) btnMakeBndCurve_Click(null, null);
        }

        private void DeletePoint(int idx)
        {
            if (idx < 0 || idx >= pts.Count) return;
            pts.RemoveAt(idx);
            if (selIndex == idx) { selIndex = -1; SetPointMode(PointEdit.Mark); }
            else if (selIndex > idx) selIndex--;
            RecomputeAB();
            UpdateLineButtons();
            Regenerate();
            lblActiveOp.Text = gStr.gsDeletePoint;
            oglSelf.Invalidate();
        }

        // ── Point edit (POINT button + arrows) ────────────────────────────
        private void btnPoint_Click(object sender, EventArgs e)
        {
            if (pointMode == PointEdit.Off)
            {
                btnAddPoint.Checked = false;
                btnDeletePoint.Checked = false;
                SetPointMode(PointEdit.Mark);
            }
            else
            {
                selIndex = -1;
                SetPointMode(PointEdit.Off);
            }
            oglSelf.Invalidate();
        }

        private void SelectPoint(int idx)
        {
            selIndex = idx;
            SetPointMode(PointEdit.Move);
            oglSelf.Invalidate();
        }

        private void SetPointMode(PointEdit mode)
        {
            pointMode = mode;
            switch (mode)
            {
                case PointEdit.Off: btnPoint.BackColor = Color.Gainsboro; lblPointMode.Text = ""; lblActiveOp.Text = ""; break;
                case PointEdit.Mark: btnPoint.BackColor = Color.IndianRed; lblPointMode.Text = gStr.gsMarkPoint; lblActiveOp.Text = gStr.gsMarkPoint; break;
                case PointEdit.Move: btnPoint.BackColor = Color.LimeGreen; lblPointMode.Text = gStr.gsMovePoint; lblActiveOp.Text = gStr.gsMovePoint; break;
            }
            UpdateArrowColors();
        }

        private void UpdateArrowColors()
        {
            Color c = pointMode == PointEdit.Move ? Color.Green : Color.RoyalBlue;
            btnNavUp.ForeColor = c; btnNavDown.ForeColor = c; btnNavLeft.ForeColor = c; btnNavRight.ForeColor = c;
        }

        private void btnNavUp_Click(object sender, EventArgs e) => Nav(0, -1);
        private void btnNavDown_Click(object sender, EventArgs e) => Nav(0, 1);
        private void btnNavLeft_Click(object sender, EventArgs e) => Nav(-1, 0);
        private void btnNavRight_Click(object sender, EventArgs e) => Nav(1, 0);

        // Move the selected point by step px (Fine/Coarse), else pan the view by 20%.
        private void Nav(int dx, int dy)
        {
            if (pointMode == PointEdit.Move && selIndex >= 0)
            {
                double k = MetersPerPixel();
                var p = pts[selIndex];
                pts[selIndex] = new vec2(p.easting + dx * stepPx * k, p.northing - dy * stepPx * k);
                RecomputeAB();
                Regenerate();
            }
            else
            {
                double dPan = 0.2 * zoom / 0.903;
                sX += -dx * dPan;
                sY += dy * dPan;
            }
            oglSelf.Invalidate();
        }

        private void btnFine_Click(object sender, EventArgs e)
        {
            stepPx = 1;
            btnFine.BackColor = Color.LightSteelBlue; btnCoarse.BackColor = SystemColors.Control;
            btnFine.UseVisualStyleBackColor = false; btnCoarse.UseVisualStyleBackColor = true;
        }

        private void btnCoarse_Click(object sender, EventArgs e)
        {
            stepPx = 10;
            btnCoarse.BackColor = Color.LightSteelBlue; btnFine.BackColor = SystemColors.Control;
            btnCoarse.UseVisualStyleBackColor = false; btnFine.UseVisualStyleBackColor = true;
        }

        // ── Add / Delete toggles ──────────────────────────────────────────
        private void btnAddPoint_CheckedChanged(object sender, EventArgs e)
        {
            if (btnAddPoint.Checked)
            {
                btnDeletePoint.Checked = false;
                selIndex = -1; SetPointMode(PointEdit.Off);
                lblActiveOp.Text = gStr.gsAddPoint;
            }
            else if (!btnDeletePoint.Checked) lblActiveOp.Text = "";
        }

        private void btnDeletePoint_CheckedChanged(object sender, EventArgs e)
        {
            if (btnDeletePoint.Checked)
            {
                btnAddPoint.Checked = false;
                selIndex = -1; SetPointMode(PointEdit.Off);
                lblActiveOp.Text = gStr.gsDeletePoint;
            }
            else if (!btnAddPoint.Checked) lblActiveOp.Text = "";
        }

        // ── Satellite background (App-Map) ────────────────────────────────
        private void btnSat_CheckedChanged(object sender, EventArgs e)
        {
            if (btnSat.Checked)
            {
                if (!glReady || !mf.worldGrid.HasBingMap)
                {
                    btnSat.Checked = false;
                    lblActiveOp.Text = "No background — capture it in App Map first";
                    return;
                }
                // texture must be created in THIS GL context
                oglSelf.MakeCurrent();
                satVisual = new BingMapVisual(mf.worldGrid.BingMap);
                lblActiveOp.Text = "Satellite ON";
            }
            else
            {
                satVisual = null;
            }
            oglSelf.Invalidate();
        }

        // ── Measure tool (temporary ruler; never saved) ───────────────────
        private void btnMeasure_Click(object sender, EventArgs e)
        {
            measureOn = !measureOn;
            if (measureOn)
            {
                // place the two endpoints near the view centre, a bit apart, ready to drag
                double k = MetersPerPixel();
                vec2 c = ScreenToWorld(oglSelf.Width / 2, oglSelf.Height / 2);
                mp0 = new vec2(c.easting - 40 * k, c.northing);
                mp1 = new vec2(c.easting + 40 * k, c.northing);
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
            oglSelf.Invalidate();
        }

        private void UpdateMeasureLabel()
        {
            double d = Math.Sqrt((mp1.easting - mp0.easting) * (mp1.easting - mp0.easting)
                               + (mp1.northing - mp0.northing) * (mp1.northing - mp0.northing));
            lblMeasure.Text = d < 1.0 ? (d * 100.0).ToString("0") + " cm"
                                      : d.ToString("0.00") + " m";
        }

        private int HitTestMeasure(int px, int py)
        {
            if (!measureOn) return -1;
            Point s0 = WorldToScreen(mp0), s1 = WorldToScreen(mp1);
            if (Math.Abs(s0.X - px) <= 16 && Math.Abs(s0.Y - py) <= 16) return 0;
            if (Math.Abs(s1.X - px) <= 16 && Math.Abs(s1.Y - py) <= 16) return 1;
            return -1;
        }

        private void DrawMeasure()
        {
            if (!measureOn) return;
            double h = 8.0 * MetersPerPixel();
            // thin red connecting line
            GL.LineWidth(1);
            GL.Color3(1.0f, 0.1f, 0.1f);
            GL.Begin(PrimitiveType.Lines);
            GL.Vertex3(mp0.easting, mp0.northing, 0);
            GL.Vertex3(mp1.easting, mp1.northing, 0);
            GL.End();
            // the two red endpoints (same square + centre-dot design as the other markers)
            GL.LineWidth(2);
            foreach (var p in new[] { mp0, mp1 })
            {
                GL.Begin(PrimitiveType.LineLoop);
                GL.Vertex3(p.easting - h, p.northing - h, 0);
                GL.Vertex3(p.easting + h, p.northing - h, 0);
                GL.Vertex3(p.easting + h, p.northing + h, 0);
                GL.Vertex3(p.easting - h, p.northing + h, 0);
                GL.End();
                GL.PointSize(4f);
                GL.Begin(PrimitiveType.Points);
                GL.Vertex3(p.easting, p.northing, 0);
                GL.End();
            }
        }

        // ── Phase 4: Check boundary outline ───────────────────────────────
        // Mark (with small boxes) where the shown line strays from the boundary by more than the
        // threshold (cm) but less than 2 m (so the far side of the field is ignored).
        private void btnCheck_Click(object sender, EventArgs e)
        {
            checkBoxes.Clear();
            int show = (mode == LineMode.Browse) ? activeIdx : workIdx;
            if (show < 0 || show >= gTemp.Count) { lblActiveOp.Text = "No line"; oglSelf.Invalidate(); return; }
            if (mf.bnd.bndList.Count == 0) { lblActiveOp.Text = "No boundary"; oglSelf.Invalidate(); return; }

            double cm;
            if (!double.TryParse(tboxCheckCm.Text.Trim(), out cm) || cm <= 0) cm = 10;
            double thr = cm / 100.0;

            // If a smoothed preview is showing, check that instead of the raw line.
            List<vec2> chk;
            if (smoothLine != null)
            {
                chk = new List<vec2>();
                foreach (vec3 p in smoothLine) chk.Add(new vec2(p.easting, p.northing));
            }
            else chk = LinePoints(gTemp[show]);

            // Only the core A->B matters: the 150 m straight tails leave the boundary tangentially
            // and would falsely flag points that are still < 2 m away near the ends.
            chk = TrimToCore(chk, gTemp[show].ptA, gTemp[show].ptB);

            foreach (var p in chk)
            {
                double d = DistToBoundary(p);
                if (d > thr && d < 2.0) checkBoxes.Add(p);
            }
            lblActiveOp.Text = checkBoxes.Count + " spots > " + cm + " cm off boundary";
            oglSelf.Invalidate();
        }

        private List<vec2> LinePoints(CTrk t)
        {
            var list = new List<vec2>();
            if (t.mode == TrackMode.AB)
            {
                double dx = t.ptB.easting - t.ptA.easting, dy = t.ptB.northing - t.ptA.northing;
                double len = Math.Sqrt(dx * dx + dy * dy);
                int steps = Math.Max(1, (int)len);
                for (int s = 0; s <= steps; s++)
                {
                    double f = (double)s / steps;
                    list.Add(new vec2(t.ptA.easting + dx * f, t.ptA.northing + dy * f));
                }
            }
            else foreach (vec3 p in t.curvePts) list.Add(new vec2(p.easting, p.northing));
            return list;
        }

        // The curve "core" = the part between A and B (excludes the 150 m straight tails).
        private List<vec2> CurveCore(CTrk t)
        {
            var all = new List<vec2>();
            foreach (vec3 p in t.curvePts) all.Add(new vec2(p.easting, p.northing));
            return TrimToCore(all, t.ptA, t.ptB);
        }

        // Keep only the sub-range between the points nearest to a and b (inclusive).
        private static List<vec2> TrimToCore(List<vec2> line, vec2 a, vec2 b)
        {
            if (line.Count < 3) return line;
            int sI = 0, eI = line.Count - 1; double bs = double.MaxValue, be = double.MaxValue;
            for (int i = 0; i < line.Count; i++)
            {
                double da = (line[i].easting - a.easting) * (line[i].easting - a.easting)
                          + (line[i].northing - a.northing) * (line[i].northing - a.northing);
                if (da < bs) { bs = da; sI = i; }
                double db = (line[i].easting - b.easting) * (line[i].easting - b.easting)
                          + (line[i].northing - b.northing) * (line[i].northing - b.northing);
                if (db < be) { be = db; eI = i; }
            }
            if (sI > eI) { int tmp = sI; sI = eI; eI = tmp; }
            return line.GetRange(sI, eI - sI + 1);
        }

        private double DistToBoundary(vec2 p)
        {
            double best = double.MaxValue;
            for (int j = 0; j < mf.bnd.bndList.Count; j++)
            {
                var f = mf.bnd.bndList[j].fenceLine;
                for (int i = 0; i < f.Count - 1; i++)
                {
                    double d = DistSeg(p.easting, p.northing, f[i].easting, f[i].northing, f[i + 1].easting, f[i + 1].northing);
                    if (d < best) best = d;
                }
            }
            return best;
        }

        private static double DistSeg(double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax, dy = by - ay;
            double len2 = dx * dx + dy * dy;
            double t = len2 <= 0 ? 0 : ((px - ax) * dx + (py - ay) * dy) / len2;
            t = Math.Max(0, Math.Min(1, t));
            double cx = ax + t * dx, cy = ay + t * dy;
            return Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
        }

        private void DrawCheckBoxes()
        {
            if (checkBoxes.Count == 0) return;
            double h = 7.0 * MetersPerPixel();
            GL.LineWidth(2);
            GL.Color3(1.0f, 0.1f, 0.1f);
            foreach (var c in checkBoxes)
            {
                GL.Begin(PrimitiveType.LineLoop);
                GL.Vertex3(c.easting - h, c.northing - h, 0);
                GL.Vertex3(c.easting + h, c.northing - h, 0);
                GL.Vertex3(c.easting + h, c.northing + h, 0);
                GL.Vertex3(c.easting - h, c.northing + h, 0);
                GL.End();
            }
        }

        // ── Phase 5: Smooth ───────────────────────────────────────────────
        private void btnSmoothUp_Click(object sender, EventArgs e)
        {
            if (smoothVal < 0) smoothVal = 20; else smoothVal++;
            ApplySmooth();
        }

        private void btnSmoothDown_Click(object sender, EventArgs e)
        {
            if (smoothVal < 0) smoothVal = 20;
            else { smoothVal--; if (smoothVal < 2) smoothVal = -1; } // below 2 -> off
            ApplySmooth();
        }

        private void btnSmoothApply_Click(object sender, EventArgs e)
        {
            if (smoothLine == null) return;
            int show = (mode == LineMode.Browse) ? activeIdx : workIdx;
            if (show < 0 || show >= gTemp.Count) return;

            gTemp[show].curvePts.Clear();
            foreach (vec3 p in smoothLine) gTemp[show].curvePts.Add(p);
            var cp = gTemp[show].curvePts; CABCurve.CalculateHeadings(ref cp);
            ctrlPts[show] = null; // control points no longer match the smoothed curve

            smoothLine = null; smoothVal = -1;
            checkBoxes.Clear();
            UpdateSmoothUI();
            lblActiveOp.Text = "Smoothed";
            oglSelf.Invalidate();
        }

        private void ApplySmooth()
        {
            smoothLine = null;
            if (smoothVal >= 0)
            {
                int show = (mode == LineMode.Browse) ? activeIdx : workIdx;
                if (show >= 0 && show < gTemp.Count && gTemp[show].curvePts.Count > 4)
                    smoothLine = SmoothPoints(gTemp[show].curvePts, smoothVal * 2);
            }
            UpdateSmoothUI();
            oglSelf.Invalidate();
        }

        // Smooth arrows are usable only when the shown line is a curve.
        private void UpdateSmoothUI()
        {
            int show = (mode == LineMode.Browse) ? activeIdx : workIdx;
            bool isCurve = show >= 0 && show < gTemp.Count && gTemp[show].curvePts.Count > 1;
            btnSmoothUp.Enabled = isCurve;
            btnSmoothDown.Enabled = isCurve;
            btnSmoothApply.Enabled = isCurve && smoothLine != null;
            if (!isCurve) { smoothVal = -1; smoothLine = null; }
            lblSmoothVal.Text = smoothVal < 0 ? "--" : smoothVal.ToString();
        }

        // Center-weighted moving average (same idea as CABCurve.SmoothAB), on a copy.
        private static List<vec3> SmoothPoints(List<vec3> src, int smPts)
        {
            int cnt = src.Count;
            var arr = new List<vec3>(cnt);
            for (int i = 0; i < cnt; i++) arr.Add(new vec3(src[i]));
            if (cnt < smPts || smPts < 2) return arr;
            for (int i = smPts / 2; i < cnt - smPts / 2; i++)
            {
                double e = 0, n = 0;
                for (int j = -smPts / 2; j < smPts / 2; j++) { e += src[j + i].easting; n += src[j + i].northing; }
                arr[i] = new vec3(e / smPts, n / smPts, src[i].heading);
            }
            return arr;
        }

        // ── Zoom ──────────────────────────────────────────────────────────
        private void btnZoomIn_Click(object sender, EventArgs e)
        {
            zoom *= 0.8; if (zoom < 0.08) zoom = 0.08; // deeper zoom (was 0.2 → 0.1 → 0.08)
            oglSelf.Invalidate();
        }

        private void btnZoomOut_Click(object sender, EventArgs e)
        {
            zoom /= 0.8; if (zoom > 6.0) zoom = 6.0;
            oglSelf.Invalidate();
        }

        // Line buttons enable rules (Phase 2 will wire the actual generation):
        // 0 pts = all off; 2 pts = AB + CurveByBoundary; >2 = Curve only.
        private void UpdateLineButtons()
        {
            bool build = mode == LineMode.Build;
            int n = pts.Count;
            btnMakeAB.Enabled = build && (n == 2);
            btnMakeBndCurve.Enabled = build && (n == 2);
            btnMakeCurve.Enabled = build && (n > 2);
        }

        // ── Phase 2b: New / Browse / Edit line management ─────────────────
        private void SetMode(LineMode m)
        {
            mode = m;
            bool build = m == LineMode.Build;
            btnAddPoint.Enabled = build;
            btnDeletePoint.Enabled = build;
            btnPoint.Enabled = build;
            tboxName.Enabled = true; // name editable in Browse too (rename active line) and in Build
            if (!build)
            {
                btnAddPoint.Checked = false; btnDeletePoint.Checked = false;
                selIndex = -1; pointMode = PointEdit.Off;
                btnPoint.BackColor = Color.Gainsboro; lblPointMode.Text = "";
            }
            UpdateLineButtons();
            UpdateNavButtons();
            UpdateSmoothUI();
            UpdateArrowColors();
            oglSelf.Invalidate();
        }

        private void UpdateNavButtons()
        {
            bool has = gTemp.Count > 0;
            btnPrevLine.Enabled = has;
            btnNextLine.Enabled = has;
            btnDeleteLine.Enabled = has;
            btnEdit.Enabled = has && mode == LineMode.Browse;
        }

        private void btnNew_Click(object sender, EventArgs e)
        {
            pts.Clear(); workIdx = -1; lastMake = null; selIndex = -1;
            tboxName.Text = "";
            checkBoxes.Clear();                               // clear stale check marks too
            smoothVal = -1; smoothLine = null; UpdateSmoothUI();
            RecomputeAB();
            SetMode(LineMode.Build);
            lblActiveOp.Text = "New line";
        }

        private void btnEdit_Click(object sender, EventArgs e)
        {
            if (activeIdx < 0 || activeIdx >= gTemp.Count) return;
            workIdx = activeIdx;
            LoadControlPoints(activeIdx);
            tboxName.Text = gTemp[activeIdx].name ?? "";
            SetMode(LineMode.Build);
            lblActiveOp.Text = "Edit line";
        }

        private void btnPrevLine_Click(object sender, EventArgs e) => Navigate(-1);
        private void btnNextLine_Click(object sender, EventArgs e) => Navigate(1);

        private void Navigate(int dir)
        {
            if (gTemp.Count == 0) return;
            if (mode != LineMode.Browse) { SetMode(LineMode.Browse); if (activeIdx < 0) activeIdx = 0; }
            else
            {
                activeIdx += dir;
                if (activeIdx < 0) activeIdx = gTemp.Count - 1;
                if (activeIdx >= gTemp.Count) activeIdx = 0;
            }
            UpdateNavButtons();
            checkBoxes.Clear();
            smoothVal = -1; smoothLine = null; UpdateSmoothUI();
            RefreshActiveName();
            lblActiveOp.Text = (gTemp[activeIdx].name ?? "line") + "  (" + (activeIdx + 1) + "/" + gTemp.Count + ")";
            oglSelf.Invalidate();
        }

        // Show the active (Browse) line's name in the Name box so it can be renamed.
        private void RefreshActiveName()
        {
            if (activeIdx >= 0 && activeIdx < gTemp.Count)
                tboxName.Text = gTemp[activeIdx].name ?? "";
        }

        private void btnDeleteLine_Click(object sender, EventArgs e)
        {
            if (activeIdx < 0 || activeIdx >= gTemp.Count) return;
            gTemp.RemoveAt(activeIdx); ctrlPts.RemoveAt(activeIdx);
            workIdx = -1;
            if (activeIdx >= gTemp.Count) activeIdx = gTemp.Count - 1;
            if (gTemp.Count == 0) btnNew_Click(null, null);
            else { UpdateNavButtons(); lblActiveOp.Text = "deleted"; }
            oglSelf.Invalidate();
        }

        private void LoadControlPoints(int idx)
        {
            pts.Clear();
            var t = gTemp[idx];
            if (ctrlPts[idx] != null && ctrlPts[idx].Count >= 2)
            {
                pts.AddRange(ctrlPts[idx]);
            }
            else if (t.mode == TrackMode.AB)
            {
                pts.Add(new vec2(t.ptA.easting, t.ptA.northing));
                pts.Add(new vec2(t.ptB.easting, t.ptB.northing));
            }
            else // sample a few control points from the dense curve CORE (skip the 150 m tails)
            {
                var core = CurveCore(t);
                int nC = core.Count;
                int samples = Math.Min(6, Math.Max(3, nC / 30));
                for (int s = 0; s < samples; s++)
                {
                    int k = (samples == 1) ? 0 : (int)((double)s / (samples - 1) * (nC - 1));
                    pts.Add(core[k]);
                }
            }
            lastMake = (t.mode == TrackMode.AB) ? "AB" : "Curve";
            RecomputeAB();
            UpdateLineButtons();
        }

        // ── Phase 2: build lines from the free points (reuse AOG generation) ──
        // The line built from the current points is kept at workIdx (replaced on re-make);
        // its control points are remembered for later Edit (option A).
        private void StoreGenerated(CTrk t)
        {
            if (workIdx >= 0 && workIdx < gTemp.Count) { gTemp[workIdx] = t; ctrlPts[workIdx] = new List<vec2>(pts); }
            else { gTemp.Add(t); ctrlPts.Add(new List<vec2>(pts)); workIdx = gTemp.Count - 1; }
            checkBoxes.Clear(); // line changed -> old check marks are stale
            smoothVal = -1; smoothLine = null; UpdateSmoothUI();
            oglSelf.Invalidate();
        }

        private string LineName(string prefix, double headingRad)
        {
            string n = tboxName.Text.Trim();
            if (n.Length > 0) return n;
            return prefix + " " + Math.Round(glm.toDegrees(headingRad), 1).ToString(CultureInfo.InvariantCulture) + "°";
        }

        private void btnMakeAB_Click(object sender, EventArgs e)
        {
            if (pts.Count != 2 || bIndex < 0) return;
            lastMake = "AB";
            vec2 a = pts[aIndex], b = pts[bIndex];
            double abHead = Math.Atan2(b.easting - a.easting, b.northing - a.northing);
            if (abHead < 0) abHead += glm.twoPI;

            var t = new CTrk { mode = TrackMode.AB, heading = abHead, isVisible = true };
            t.ptA = new vec2(a.easting, a.northing);
            t.ptB = new vec2(b.easting, b.northing);
            t.name = LineName("AB", abHead);
            StoreGenerated(t);
            lblActiveOp.Text = "AB line";
        }

        private void btnMakeCurve_Click(object sender, EventArgs e)
        {
            if (pts.Count < 3) return;
            lastMake = "Curve";
            var ordered = OrderPointsAtoB();        // A → nearest-neighbour … → B
            var des = Densify(ordered, 1.6);
            if (des.Count < 4) { lblActiveOp.Text = "Curve error"; return; }

            CABCurve.CalculateHeadings(ref des);

            double x = 0, y = 0;
            foreach (vec3 p in des) { x += Math.Cos(p.heading); y += Math.Sin(p.heading); }
            double head = Math.Atan2(y / des.Count, x / des.Count);
            if (head < 0) head += glm.twoPI;

            ExtendEnds(des, ExtendMeters);          // 150 m tails BOTH directions
            CABCurve.CalculateHeadings(ref des);

            var t = new CTrk { mode = TrackMode.Curve, heading = head, isVisible = true };
            t.ptA = new vec2(pts[aIndex].easting, pts[aIndex].northing);
            t.ptB = new vec2(pts[bIndex].easting, pts[bIndex].northing);
            t.name = LineName("Cu", head);
            foreach (vec3 p in des) t.curvePts.Add(p);
            StoreGenerated(t);
            lblActiveOp.Text = "Curve";
        }

        private void btnMakeBndCurve_Click(object sender, EventArgs e)
        {
            if (pts.Count != 2) return;
            lastMake = "Bnd";
            try
            {
                if (mf.bnd.bndList.Count == 0) { lblActiveOp.Text = "No boundary"; return; }

                // snap A and B to the nearest boundary vertex
                int bSel = NearestBoundaryVertex(pts[aIndex], out int start);
                NearestBoundaryVertexOn(bSel, pts[bIndex], out int end);

                var fence = mf.bnd.bndList[bSel].fenceLine;
                bool isLoop = Math.Abs(start - end) > fence.Count * 0.5;
                int limit = end;
                if (isLoop) { if (start < end) (end, start) = (start, end); limit = end; end = fence.Count; }
                else { if (start > end) (end, start) = (start, end); }

                var des = new List<vec3>();
                for (int i = start; i < end; i++)
                {
                    var p = new vec3(fence[i]);
                    // skip consecutive coincident points (avoids zero-length segments / folds)
                    if (des.Count == 0 || Math.Abs(p.easting - des[des.Count - 1].easting) > 0.02
                                       || Math.Abs(p.northing - des[des.Count - 1].northing) > 0.02)
                        des.Add(p);
                    if (isLoop && i == fence.Count - 1) { i = -1; isLoop = false; end = limit; }
                }
                if (des.Count < 4) { lblActiveOp.Text = "Curve by boundary error"; return; }

                CABCurve.CalculateHeadings(ref des);
                double head = des.Count > 1 ? des[des.Count / 2].heading : 0;
                vec3 a0 = des[0], bN = des[des.Count - 1];
                ExtendEnds(des, ExtendMeters);   // 150 m tails BOTH directions, past the boundary
                CABCurve.CalculateHeadings(ref des);

                // Use a normal Curve (an OPEN slice), not bndCurve — bndCurve is a closed boundary
                // loop and AOG would draw/guide a second wrap-around line ("double line").
                var t = new CTrk { mode = TrackMode.Curve, heading = head, isVisible = true };
                t.ptA = new vec2(a0.easting, a0.northing);
                t.ptB = new vec2(bN.easting, bN.northing);
                t.name = LineName("CuBnd", head);
                foreach (vec3 p in des) t.curvePts.Add(p);
                StoreGenerated(t);
                lblActiveOp.Text = "Curve by Boundary";
            }
            catch (Exception ex)
            {
                lblActiveOp.Text = "Curve by boundary error";
                Log.EventWriter("SmartAB CurveByBoundary " + ex.Message);
            }
        }

        private const double ExtendMeters = 150.0; // guidance tail length each end

        // Order the points into a path: A (pts[0]) → nearest-neighbour through the middle → B (farthest).
        private List<vec2> OrderPointsAtoB()
        {
            var remaining = new List<int>();
            for (int i = 0; i < pts.Count; i++) if (i != aIndex && i != bIndex) remaining.Add(i);

            var ordered = new List<vec2> { pts[aIndex] };
            int cur = aIndex;
            while (remaining.Count > 0)
            {
                int best = 0; double bestD = double.MaxValue;
                for (int k = 0; k < remaining.Count; k++)
                {
                    double dx = pts[remaining[k]].easting - pts[cur].easting;
                    double dy = pts[remaining[k]].northing - pts[cur].northing;
                    double d = dx * dx + dy * dy;
                    if (d < bestD) { bestD = d; best = k; }
                }
                cur = remaining[best];
                ordered.Add(pts[cur]);
                remaining.RemoveAt(best);
            }
            if (bIndex >= 0) ordered.Add(pts[bIndex]); // B is the far endpoint
            return ordered;
        }

        // Extend the polyline tangentially by 'meters' at BOTH ends (for autoguidance).
        private static void ExtendEnds(List<vec3> des, double meters)
        {
            if (des.Count < 2) return;
            const double step = 1.6;
            int n = Math.Max(1, (int)(meters / step));

            double h0 = des[0].heading;
            var front = new List<vec3>();
            for (int i = n; i >= 1; i--)
            {
                double d = i * step;
                front.Add(new vec3(des[0].easting - Math.Sin(h0) * d, des[0].northing - Math.Cos(h0) * d, h0));
            }

            int last = des.Count - 1;
            double hl = des[last].heading;
            var back = new List<vec3>();
            for (int i = 1; i <= n; i++)
            {
                double d = i * step;
                back.Add(new vec3(des[last].easting + Math.Sin(hl) * d, des[last].northing + Math.Cos(hl) * d, hl));
            }

            des.InsertRange(0, front);
            des.AddRange(back);
        }

        // Linear densification of the point list to ~spacing metres (for curve points/headings).
        private static List<vec3> Densify(List<vec2> p, double spacing)
        {
            var list = new List<vec3>();
            for (int i = 0; i < p.Count - 1; i++)
            {
                double dx = p[i + 1].easting - p[i].easting, dy = p[i + 1].northing - p[i].northing;
                double len = Math.Sqrt(dx * dx + dy * dy);
                int steps = Math.Max(1, (int)(len / spacing));
                for (int s = 0; s < steps; s++)
                {
                    double f = (double)s / steps;
                    list.Add(new vec3(p[i].easting + dx * f, p[i].northing + dy * f, 0));
                }
            }
            list.Add(new vec3(p[p.Count - 1].easting, p[p.Count - 1].northing, 0));
            return list;
        }

        private int NearestBoundaryVertex(vec2 w, out int idx)
        {
            double best = double.MaxValue; int bSel = 0; idx = 0;
            for (int j = 0; j < mf.bnd.bndList.Count; j++)
            {
                var f = mf.bnd.bndList[j].fenceLine;
                for (int i = 0; i < f.Count; i++)
                {
                    double dx = w.easting - f[i].easting, dy = w.northing - f[i].northing;
                    double d = dx * dx + dy * dy;
                    if (d < best) { best = d; bSel = j; idx = i; }
                }
            }
            return bSel;
        }

        private void NearestBoundaryVertexOn(int bSel, vec2 w, out int idx)
        {
            double best = double.MaxValue; idx = 0;
            var f = mf.bnd.bndList[bSel].fenceLine;
            for (int i = 0; i < f.Count; i++)
            {
                double dx = w.easting - f[i].easting, dy = w.northing - f[i].northing;
                double d = dx * dx + dy * dy;
                if (d < best) { best = d; idx = i; }
            }
        }

        // Draw only the ACTIVE line (Browse) or the line being built/edited (Build).
        private void DrawBuiltLines()
        {
            int show = (mode == LineMode.Browse) ? activeIdx : workIdx;
            if (show < 0 || show >= gTemp.Count) return;
            var t = gTemp[show];

            if (t.mode == TrackMode.AB)
            {
                GL.LineStipple(1, 0x0707);
                GL.Enable(EnableCap.LineStipple);
                GL.LineWidth(6);
                GL.Color3(1.0f, 0.20f, 0.20f);
                GL.Begin(PrimitiveType.Lines);
                GL.Vertex3(t.ptA.easting - Math.Sin(t.heading) * mf.ABLine.abLength, t.ptA.northing - Math.Cos(t.heading) * mf.ABLine.abLength, 0);
                GL.Vertex3(t.ptB.easting + Math.Sin(t.heading) * mf.ABLine.abLength, t.ptB.northing + Math.Cos(t.heading) * mf.ABLine.abLength, 0);
                GL.End();
                GL.Disable(EnableCap.LineStipple);
            }
            else if (t.curvePts.Count > 1)
            {
                GL.LineWidth(4);
                if (t.mode == TrackMode.bndCurve) GL.Color3(0.70f, 0.5f, 0.2f);
                else GL.Color3(0.30f, 0.97f, 0.30f);
                GL.Begin(PrimitiveType.LineStrip);
                foreach (vec3 p in t.curvePts) GL.Vertex3(p.easting, p.northing, 0);
                GL.End();
            }
        }

        // ── Bottom: cancel / accept (Phase 1: just close) ─────────────────
        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void btnAccept_Click(object sender, EventArgs e)
        {
            // Apply the typed name to the relevant line (active one in Browse, the built one in Build).
            int target = (mode == LineMode.Browse) ? activeIdx : workIdx;
            string nm = tboxName.Text.Trim();
            if (nm.Length > 0 && target >= 0 && target < gTemp.Count) gTemp[target].name = nm;

            // Commit the tracks (existing + new/edited) to the field and save — but keep the window
            // OPEN so the user can keep managing lines (close with the X top-right, or Cancel).
            mf.trk.gArr.Clear();
            foreach (var t in gTemp) mf.trk.gArr.Add(new CTrk(t));
            mf.trk.idx = (target >= 0 && target < gTemp.Count) ? target : (gTemp.Count > 0 ? 0 : -1);
            mf.FileSaveTracks();
            mf.curve.isCurveValid = false;
            mf.ABLine.isABValid = false;

            UpdateNavButtons();
            lblActiveOp.Text = gStr.gsSaved ?? "Saved";
            oglSelf.Invalidate();
        }
    }
}
