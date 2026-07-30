using AgLibrary.Logging;
using AgOpenGPS.Controls;
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
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AgOpenGPS
{
    /// <summary>
    /// Feature 50 — Orchard Navigation, Phase 1 (MVP "sredina"). A new window (à la Smart AB;
    /// FormSmartAB is untouched) to draw/import FIXED tree ROWS, then generate one mid-inter-row
    /// guidance line per gap ("Make path"). The result is ordinary AOG Curve tracks, driven by the
    /// existing guidance + K-turn. Rows are saved under the field (Orchard.txt). Multi-pass /
    /// safe-space / turns / implement shift are later phases.
    /// </summary>
    public partial class FormOrchardPlanner : Form
    {
        private readonly FormGPS mf;

        // GL view: camera distance factor + normalized pan offsets (same scheme as Smart AB).
        private const double InitZoom = 2.0;
        private double zoom = InitZoom, sX = 0, sY = 0;

        // All committed rows. Editing works on one row's control points (pts) at a time.
        private readonly List<OrchRow> rows = new List<OrchRow>();
        private readonly List<vec2> pts = new List<vec2>(); // control points of the row being built/edited
        private readonly List<vec2> trees = new List<vec2>(); // loose imported tree positions (display aid)

        // Generated mid-inter-row path (yellow). Rebuilt by "Make path"; committed on Accept.
        private readonly List<CTrk> midlines = new List<CTrk>();

        // Planting: while the new-orchard panel is open, "pts" holds the REFERENCE line
        // (row direction) instead of a row - the layout is generated from it.
        private bool genMode;

        // set once the seedling layout is actually used in this session, so Accept knows
        // this is a planting orchard and not a survey of trees that already stand
        private bool plantingTouched;

        private enum ViewMode { Browse, Build }
        private ViewMode mode = ViewMode.Build;
        private int activeIdx = -1; // Browse selection
        private int workIdx = -1;   // row being built/edited (index into rows)

        // Point edit (arrow-pad), same as Smart AB.
        private enum PointEdit { Off, Mark, Move }
        private PointEdit pointMode = PointEdit.Off;
        private int selIndex = -1;
        private int stepPx = 10;

        private int addStage = 0;
        private bool AddActive => addStage != 0;

        // Mouse drag state.
        private int dragIndex = -1;
        private bool panning;
        private Point downPt;
        private double panStartSX, panStartSY;
        private bool moved;

        private readonly Timer redraw = new Timer();
        private bool glReady;

        private BingMapVisual satVisual;

        // Row filter (t9): thin + smooth, previewed in magenta and only written on Apply.
        // filterOrig holds the row's UNTOUCHED points while the values are being tried, so
        // turning a knob re-solves from the original instead of stacking filter on filter.
        private int filterRow = -1;
        private List<vec2> filterOrig;
        private List<vec2> filterPreview;
        private double thinVal = -1;      // metres, -1 = off
        private int smoothVal = -1;       // window/2, -1 = off

        // Measure ruler (temporary; never saved).
        private bool measureOn;
        private vec2 mp0, mp1;
        private int measureDrag = -1;

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        private const int WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 0x2;
        private Dictionary<Control, Point> origLoc;

        public FormOrchardPlanner(Form callingForm)
        {
            mf = callingForm as FormGPS;
            InitializeComponent();

            this.Text = gStr.gsOrchardPlanner;
            btnNewOrchard.Text = gStr.gsNewOrchardPlanting;
            lblGenCap.Text = gStr.gsNewOrchardPlantingLayoutCaps;

            //SIM 0730: the whole window follows the app language, not just its title
            lblName.Text = gStr.gsOrchName;
            lblLegCap.Text = gStr.gsOrchLegend;
            lblLegRow.Text = gStr.gsOrchLegRow;
            lblLegMid.Text = gStr.gsOrchLegMid;
            lblLegTree.Text = gStr.gsOrchLegTree;
            btnFine.Text = gStr.gsOrchFine;
            btnCoarse.Text = gStr.gsOrchCoarse;
            btnMeasure.Text = gStr.gsOrchMeasure;
            btnAddPoint.Text = gStr.gsOrchAddPoint;
            btnDeletePoint.Text = gStr.gsOrchDeletePoint;
            btnImportKml.Text = gStr.gsOrchImportKml;
            btnRecordRow.Text = gStr.gsOrchRecordRow;
            btnNewRow.Text = gStr.gsOrchNewRow;
            btnEdit.Text = gStr.gsOrchEditRow;
            btnDeleteRow.Text = gStr.gsOrchDelRow;
            lblGenRowSpace.Text = gStr.gsOrchGenRowSpace;
            lblGenTreeSpace.Text = gStr.gsOrchGenTreeSpace;
            lblGenHeadland.Text = gStr.gsOrchGenHeadland;
            lblGenSetback.Text = gStr.gsOrchGenSetback;
            chkGenStagger.Text = gStr.gsOrchGenStagger;
            btnGenLine.Text = gStr.gsOrchGenLine;
            btnGenerate.Text = gStr.gsOrchGenerate;
            btnGenClose.Text = gStr.gsOrchClose;
            lblThin.Text = gStr.gsOrchThin;
            lblSmooth.Text = gStr.gsOrchSmoothLbl;
            btnFilterApply.Text = gStr.gsOrchFilterApply;

            mf.CalculateMinMax();

            // load rows + loose trees saved under this field
            var loaded = COrchard.LoadRows(FieldDir(), trees);
            foreach (var r in loaded) rows.Add(r);

            redraw.Interval = 250;
            redraw.Tick += (s, e) => { RecordSample(); if (recOn) CenterOnVehicle(); oglSelf.Invalidate(); };

            this.KeyPreview = true;
            this.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            this.MouseDown += FormDrag_MouseDown;
            oglSelf.MouseWheel += oglSelf_MouseWheel;
            oglSelf.MouseEnter += (s, e) => { if (oglSelf.CanFocus) oglSelf.Focus(); };
            tboxName.Click += (s, e) => tboxName.ShowKeyboard(this);
        }

        //F50 (SIM 0730): every number field in this window opens the on-screen keypad
        private void nudOrchard_Click(object sender, EventArgs e)
        {
            ((NudlessNumericUpDown)sender).ShowKeypad(this);
        }

        private string FieldDir() => mf?.AppCore?.ActiveField?.FieldDirectory?.FullName;

        // ── Window controls + layout ──────────────────────────────────────
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

        private void FormOrchardPlanner_Resize(object sender, EventArgs e) => LayoutControls();

        private void LayoutControls()
        {
            if (origLoc == null)
            {
                origLoc = new Dictionary<Control, Point>();
                foreach (Control c in Controls) if (c != oglSelf) origLoc[c] = c.Location;
            }

            int glW = Math.Max(300, ClientSize.Width - 332);
            int glH = Math.Max(300, ClientSize.Height - 36);
            oglSelf.SetBounds(8, 8, glW, glH);

            int dx = ClientSize.Width - 1000;
            foreach (var kv in origLoc)
                if (kv.Value.X >= 676) kv.Key.Location = new Point(kv.Value.X + dx, kv.Value.Y);

            btnSat.Location = new Point(oglSelf.Right - 64, oglSelf.Top + 6);
            btnZoomIn.Location = new Point(oglSelf.Right - 64, oglSelf.Bottom - 186);
            btnZoomRestore.Location = new Point(oglSelf.Right - 64, oglSelf.Bottom - 124);
            btnZoomOut.Location = new Point(oglSelf.Right - 64, oglSelf.Bottom - 62);
            lblActiveOp.Location = new Point(8, oglSelf.Bottom + 6);
            btnWinClose.Location = new Point(ClientSize.Width - 60, 6);
            btnWinFull.Location = new Point(ClientSize.Width - 118, 6);

            SetupProjection();
            oglSelf.Invalidate();
        }

        private void FormOrchardPlanner_Load(object sender, EventArgs e)
        {
            if (rows.Count > 0) { activeIdx = 0; SetMode(ViewMode.Browse); RefreshActiveName(); }
            else SetMode(ViewMode.Build);

            UpdateArrowColors();
            UpdateNavButtons();
            LayoutControls();
            redraw.Start();

            btnZoomIn.BringToFront();
            btnZoomRestore.BringToFront();
            btnZoomOut.BringToFront();
            btnSat.BringToFront();
            btnWinFull.BringToFront();
            btnWinClose.BringToFront();
        }

        private void FormOrchardPlanner_FormClosing(object sender, FormClosingEventArgs e) => redraw.Stop();

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

        private void oglSelf_Resize(object sender, EventArgs e) => SetupProjection();

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

            if (satVisual != null)
            {
                GL.Disable(EnableCap.CullFace);
                satVisual.Draw();
                GL.Enable(EnableCap.CullFace);
                if (mf.parcels.Segments.Length >= 2)
                {
                    GL.LineWidth(1);
                    GL.Color4(0f, 0.75f, 1f, 0.9f);
                    GLW.DrawLinesPrimitive(mf.parcels.Segments);
                }
            }

            for (int j = 0; j < mf.bnd.bndList.Count; j++)
            {
                GeoCoord[] fenceLineEar = GeoRefactorHelper.ToGeoCoordArray(mf.bnd.bndList[j].fenceLineEar);
                FenceLineVisual.DrawFenceLine(fenceLineEar, false);
            }

            VehicleDotVisual.DrawVehicleDot(mf.pivotAxlePos.ToGeoCoord());

            DrawTrees();
            DrawRows();
            DrawMidlines();
            DrawRefLine();
            DrawFilterPreview();
            if (mode == ViewMode.Build) DrawPoints();
            DrawMeasure();

            GL.Flush();
            oglSelf.SwapBuffers();
        }

        // Rows: the row being edited (workIdx in Build) is bright green; others are dim green.
        // In Browse only the active row is shown bright, others dim.
        private void DrawRows()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].curve.Count < 2) continue;
                bool hot = (mode == ViewMode.Build) ? (i == workIdx) : (i == activeIdx);
                GL.LineWidth(hot ? 4 : 2);
                if (hot) GL.Color3(0.30f, 0.97f, 0.30f);
                else GL.Color3(0.15f, 0.45f, 0.15f);
                GL.Begin(PrimitiveType.LineStrip);
                foreach (vec3 p in rows[i].curve) GL.Vertex3(p.easting, p.northing, 0);
                GL.End();
            }
        }

        private void DrawMidlines()
        {
            if (midlines.Count == 0) return;
            GL.LineWidth(3);
            GL.Color3(1.0f, 0.85f, 0.1f); // yellow guidance
            foreach (var t in midlines)
            {
                if (t.curvePts.Count < 2) continue;
                GL.Begin(PrimitiveType.LineStrip);
                foreach (vec3 p in t.curvePts) GL.Vertex3(p.easting, p.northing, 0);
                GL.End();
            }
        }

        // Planting reference line (cyan): the direction the generated rows follow.
        private void DrawRefLine()
        {
            if (!genMode || pts.Count < 2) return;
            GL.LineWidth(2);
            GL.Color3(0.2f, 0.9f, 1.0f);
            GL.Begin(PrimitiveType.LineStrip);
            foreach (var p in pts) GL.Vertex3(p.easting, p.northing, 0);
            GL.End();
        }

        // Trees / planting spots as brown dots; the target row's own trees are drawn
        // brighter and bigger so it is obvious which row an edit will act on.
        private void DrawTrees()
        {
            int hotRow = genMode ? -1 : TargetRow();

            var all = new List<vec2>(trees);
            for (int i = 0; i < rows.Count; i++)
                if (i != hotRow) all.AddRange(rows[i].trees);

            if (all.Count > 0)
            {
                GL.Color3(0.55f, 0.35f, 0.15f);
                GL.PointSize(5f);
                GL.Begin(PrimitiveType.Points);
                foreach (var p in all) GL.Vertex3(p.easting, p.northing, 0);
                GL.End();
            }

            if (hotRow >= 0 && rows[hotRow].trees.Count > 0)
            {
                GL.Color3(1.0f, 0.65f, 0.25f);
                GL.PointSize(8f);
                GL.Begin(PrimitiveType.Points);
                foreach (var p in rows[hotRow].trees) GL.Vertex3(p.easting, p.northing, 0);
                GL.End();
            }
        }

        // Filter preview (t9): what the row would look like after thin + smooth. Magenta, like
        // the Smart AB smooth preview, so it reads as "not committed yet".
        private void DrawFilterPreview()
        {
            if (filterPreview == null || filterPreview.Count < 2) return;

            GL.LineWidth(3);
            GL.Color3(1.0f, 0.2f, 1.0f);
            GL.Begin(PrimitiveType.LineStrip);
            foreach (var p in filterPreview) GL.Vertex3(p.easting, p.northing, 0);
            GL.End();

            GL.PointSize(5f);
            GL.Begin(PrimitiveType.Points);
            foreach (var p in filterPreview) GL.Vertex3(p.easting, p.northing, 0);
            GL.End();
        }

        // ── Row filter: thin + smooth (t9) ────────────────────────────────
        // Both work on the row's CONTROL points: Orchard.txt keeps only those and rebuilds the
        // curve on load, so a filter applied to the curve would be gone the next time the field
        // is opened. Thin first (that is what removes the coarse steps of a 1 m recording), then
        // the moving average.
        private void btnThinDown_Click(object sender, EventArgs e)
        {
            if (thinVal < 0) thinVal = 3;
            else { thinVal -= 1; if (thinVal < 1) thinVal = -1; }
            ApplyFilterPreview();
        }

        private void btnThinUp_Click(object sender, EventArgs e)
        {
            if (thinVal < 0) thinVal = 3; else thinVal += 1;
            if (thinVal > 20) thinVal = 20;
            ApplyFilterPreview();
        }

        private void btnSmoothDown_Click(object sender, EventArgs e)
        {
            if (smoothVal < 0) smoothVal = 5;
            else { smoothVal -= 1; if (smoothVal < 2) smoothVal = -1; }
            ApplyFilterPreview();
        }

        private void btnSmoothUp_Click(object sender, EventArgs e)
        {
            if (smoothVal < 0) smoothVal = 5; else smoothVal++;
            if (smoothVal > 30) smoothVal = 30;
            ApplyFilterPreview();
        }

        /// <summary>Re-solve the preview from the row's original points (never cumulative).</summary>
        private void ApplyFilterPreview()
        {
            int t = TargetRow();
            if (t < 0 || genMode) { ResetFilter(); return; }

            if (filterRow != t || filterOrig == null)
            {
                filterRow = t;
                filterOrig = new List<vec2>(rows[t].pts);
            }

            if (thinVal < 0 && smoothVal < 0) filterPreview = null;
            else filterPreview = COrchard.FilterRowPoints(filterOrig,
                    thinVal < 0 ? 0 : thinVal, smoothVal < 0 ? 0 : smoothVal * 2);

            UpdateFilterUI();
            oglSelf.Invalidate();
        }

        private void ResetFilter()
        {
            filterRow = -1; filterOrig = null; filterPreview = null;
            thinVal = -1; smoothVal = -1;
            UpdateFilterUI();
        }

        private void UpdateFilterUI()
        {
            bool canFilter = !genMode && TargetRow() >= 0;
            btnThinUp.Enabled = btnThinDown.Enabled = canFilter;
            btnSmoothUp.Enabled = btnSmoothDown.Enabled = canFilter;
            btnFilterApply.Enabled = canFilter && filterPreview != null;

            lblThinVal.Text = thinVal < 0 ? "--" : thinVal.ToString("0");
            lblSmoothVal.Text = smoothVal < 0 ? "--" : smoothVal.ToString();

            if (filterPreview != null && filterOrig != null)
                lblActiveOp.Text = "Filter: " + filterOrig.Count + " -> " + filterPreview.Count + " points";
        }

        private void btnFilterApply_Click(object sender, EventArgs e)
        {
            int t = TargetRow();
            if (t < 0 || filterPreview == null || filterPreview.Count < 2) return;

            //keep the measurement before the row changes - once per row, and only for a row that
            //was actually recorded (t9). Orchard.txt stays the working copy.
            COrchard.ArchiveRecordedRow(FieldDir(), rows[t]);

            int had = filterOrig != null ? filterOrig.Count : rows[t].pts.Count;

            rows[t].pts = new List<vec2>(filterPreview);
            rows[t].BuildCurve();

            int now = rows[t].pts.Count;

            //seedlings stay where they are - same rule as a hand edit
            lblWarn.Text = rows[t].trees.Count > 0
                ? "Row filtered - seedlings still on the old shape (Generate again to re-lay them)"
                : "";

            ResetFilter();
            if (mode == ViewMode.Build && workIdx == t) { pts.Clear(); pts.AddRange(rows[t].pts); }

            lblActiveOp.Text = "Filtered: " + had + " -> " + now + " points";
            oglSelf.Invalidate();
        }

        // Control-point markers. Selected = green, first = cyan, last = orange, others white.
        private void DrawPoints()
        {
            double h = 8.0 * MetersPerPixel();
            GL.LineWidth(2);
            for (int i = 0; i < pts.Count; i++)
            {
                if (i == selIndex) GL.Color3(0.2f, 1.0f, 0.2f);
                else if (i == 0) GL.Color3(0.2f, 0.9f, 1.0f);
                else if (i == pts.Count - 1) GL.Color3(1.0f, 0.6f, 0.1f);
                else GL.Color3(1.0f, 1.0f, 1.0f);

                double x = pts[i].easting, y = pts[i].northing;
                GL.Begin(PrimitiveType.LineLoop);
                GL.Vertex3(x - h, y - h, 0);
                GL.Vertex3(x + h, y - h, 0);
                GL.Vertex3(x + h, y + h, 0);
                GL.Vertex3(x - h, y + h, 0);
                GL.End();
                GL.PointSize(4f);
                GL.Begin(PrimitiveType.Points);
                GL.Vertex3(x, y, 0);
                GL.End();
            }
        }

        // ── Coordinate transforms ─────────────────────────────────────────
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

            if (e.Button == MouseButtons.Right)
            {
                panning = true; panStartSX = sX; panStartSY = sY;
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            int mh = HitTestMeasure(e.X, e.Y);
            if (mh >= 0) { measureDrag = mh; return; }

            int hit = HitTestPoint(e.X, e.Y);
            if (hit >= 0 && !AddActive && !btnDeletePoint.Checked)
                dragIndex = hit;
            else if (!AddActive && !btnDeletePoint.Checked)
            {
                panning = true; panStartSX = sX; panStartSY = sY;
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

            if (!moved)
            {
                if (AddActive)
                {
                    if (!genMode) EnsureWorkRow();   // in genMode the points are the reference line
                    pts.Add(ScreenToWorld(e.X, e.Y));
                    BuildActiveRow();
                    lblActiveOp.Text = genMode ? "Reference line: " + pts.Count + " points" : "Add point";
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
            if (wasPointDrag) BuildActiveRow();
            oglSelf.Invalidate();
        }

        // ── Record row by driving (phase 2) ───────────────────────────────
        // Boundary-recorder style: every >=1 m of travel drops a point laterally
        // OFFSET from the pivot (nudRecOffset m, btnRecSide side), so the tractor
        // drives a safe line while the recorded row lands on the tree line.
        private bool recOn;
        private bool recSideRight = true;
        private double recLastE = double.MaxValue, recLastN;

        private void btnRecSide_Click(object sender, EventArgs e)
        {
            recSideRight = !recSideRight;
            btnRecSide.Text = recSideRight ? "R →" : "← L";
        }

        private void btnRecordRow_Click(object sender, EventArgs e)
        {
            if (!recOn)
            {
                if (mf == null || !mf.isJobStarted) { lblActiveOp.Text = "Open a field first"; return; }
                // fresh row buffer, then sampling fills it live
                pts.Clear(); workIdx = -1; selIndex = -1;
                tboxName.Text = "";
                SetMode(ViewMode.Build);
                recLastE = double.MaxValue;
                recOn = true;
                btnRecordRow.Text = gStr.gsOrchRecordStop;
                btnRecordRow.BackColor = System.Drawing.Color.LightGreen;
                lblActiveOp.Text = "Recording row - drive along it";
            }
            else
            {
                recOn = false;
                btnRecordRow.Text = gStr.gsOrchRecordRow;
                btnRecordRow.BackColor = System.Drawing.Color.MistyRose;
                if (pts.Count >= 2)
                {
                    EnsureWorkRow();
                    rows[workIdx].source = RowSource.Recorded;
                    BuildActiveRow();
                    activeIdx = workIdx;
                    SetMode(ViewMode.Browse);
                    RefreshActiveName();
                    lblActiveOp.Text = "Row recorded (" + pts.Count + " pts)";
                }
                else lblActiveOp.Text = "Recording too short - discarded";
            }
        }

        // SIM 0729 t1: while a row is being recorded the driver is steering, not panning -
        // keep the vehicle in the middle of the view. Only while recording: in browse/edit
        // the pan/zoom the user set stays exactly where it was (zoom is never touched).
        private void CenterOnVehicle()
        {
            if (mf == null || mf.maxFieldDistance < 1.0) return;
            vec3 piv = mf.pivotAxlePos;
            sX = (mf.fieldCenterX - piv.easting) / mf.maxFieldDistance;
            sY = (mf.fieldCenterY - piv.northing) / mf.maxFieldDistance;
        }

        private void RecordSample()
        {
            if (!recOn || mf == null || !mf.isJobStarted) return;

            // point = pivot shifted laterally by the offset (R = right of heading)
            double off = (double)nudRecOffset.Value * (recSideRight ? 1.0 : -1.0);
            vec3 piv = mf.pivotAxlePos;
            double e = piv.easting + Math.Sin(piv.heading + glm.PIBy2) * off;
            double n = piv.northing + Math.Cos(piv.heading + glm.PIBy2) * off;

            if (recLastE != double.MaxValue)
            {
                double de = e - recLastE, dn = n - recLastN;
                if (de * de + dn * dn < 1.0) return;   // need >= 1 m of travel
            }
            recLastE = e; recLastN = n;

            pts.Add(new vec2(e, n));
            if (pts.Count >= 2) { EnsureWorkRow(); BuildActiveRow(); }
        }

        // ── Row build ─────────────────────────────────────────────────────
        // Ensure there is a work row (create one on first point of a new row).
        private void EnsureWorkRow()
        {
            if (workIdx >= 0 && workIdx < rows.Count) return;
            var r = new OrchRow { source = RowSource.Manual, name = "Row " + (rows.Count + 1) };
            rows.Add(r);
            workIdx = rows.Count - 1;
            UpdateNavButtons();
        }

        // Sync the edited control points into the work row and rebuild its curve.
        private void BuildActiveRow()
        {
            //planting: the edited points are the reference line, not a row
            if (genMode || workIdx < 0 || workIdx >= rows.Count) { oglSelf.Invalidate(); return; }
            rows[workIdx].pts = new List<vec2>(pts);
            rows[workIdx].BuildCurve();
            midlines.Clear();            // rows changed -> old path is stale
            //the seedlings still sit on the OLD shape - say so instead of moving them
            //behind the driver's back (a small nudge may well be intentional).
            //SIM 0729 t2: 'Trees on row' is gone, so the way out is a re-generate.
            lblWarn.Text = rows[workIdx].trees.Count > 0
                ? "Row edited - seedlings still on the old shape (Generate again to re-lay them)"
                : "";
            oglSelf.Invalidate();
        }

        private void DeletePoint(int idx)
        {
            if (idx < 0 || idx >= pts.Count) return;
            pts.RemoveAt(idx);
            if (selIndex == idx) { selIndex = -1; SetPointMode(PointEdit.Mark); }
            else if (selIndex > idx) selIndex--;
            BuildActiveRow();
            lblActiveOp.Text = "Delete point";
            oglSelf.Invalidate();
        }

        // ── Point edit (arrow-pad) ────────────────────────────────────────
        private void btnPoint_Click(object sender, EventArgs e)
        {
            if (pointMode == PointEdit.Off)
            {
                ResetAddStage();
                btnDeletePoint.Checked = false;
                SetPointMode(PointEdit.Mark);
            }
            else { selIndex = -1; SetPointMode(PointEdit.Off); }
            oglSelf.Invalidate();
        }

        private void SelectPoint(int idx) { selIndex = idx; SetPointMode(PointEdit.Move); oglSelf.Invalidate(); }

        private void SetPointMode(PointEdit m)
        {
            pointMode = m;
            switch (m)
            {
                case PointEdit.Off: btnPoint.BackColor = Color.Gainsboro; lblPointMode.Text = ""; lblActiveOp.Text = ""; break;
                case PointEdit.Mark: btnPoint.BackColor = Color.IndianRed; lblPointMode.Text = "Mark point"; lblActiveOp.Text = "Mark point"; break;
                case PointEdit.Move: btnPoint.BackColor = Color.LimeGreen; lblPointMode.Text = "Move point"; lblActiveOp.Text = "Move point"; break;
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

        private void Nav(int dx, int dy)
        {
            if (pointMode == PointEdit.Move && selIndex >= 0)
            {
                double k = MetersPerPixel();
                var p = pts[selIndex];
                pts[selIndex] = new vec2(p.easting + dx * stepPx * k, p.northing - dy * stepPx * k);
                BuildActiveRow();
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

        // ── Add / Delete point toggles ────────────────────────────────────
        private void btnAddPoint_Click(object sender, EventArgs e)
        {
            if (!btnAddPoint.Enabled) return;
            addStage = addStage == 0 ? 1 : 0;
            if (addStage != 0)
            {
                btnDeletePoint.Checked = false;
                selIndex = -1; SetPointMode(PointEdit.Off);
            }
            ApplyAddStageVisual();
            oglSelf.Invalidate();
        }

        private void ApplyAddStageVisual()
        {
            if (addStage != 0) { btnAddPoint.BackColor = Color.Orange; lblActiveOp.Text = "Add point"; }
            else { btnAddPoint.BackColor = Color.AliceBlue; if (!btnDeletePoint.Checked) lblActiveOp.Text = ""; }
        }

        private void ResetAddStage() { addStage = 0; btnAddPoint.BackColor = Color.AliceBlue; }

        private void btnDeletePoint_CheckedChanged(object sender, EventArgs e)
        {
            if (btnDeletePoint.Checked)
            {
                ResetAddStage();
                selIndex = -1; SetPointMode(PointEdit.Off);
                lblActiveOp.Text = "Delete point";
            }
            else if (!AddActive) lblActiveOp.Text = "";
        }

        // ── Satellite background + parcels (from local cache, no internet) ─
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
                oglSelf.MakeCurrent();
                satVisual = new BingMapVisual(mf.worldGrid.BingMap);
                lblActiveOp.Text = "Satellite ON";
                EnsureParcelsLoaded();
            }
            else satVisual = null;
            oglSelf.Invalidate();
        }

        private void EnsureParcelsLoaded()
        {
            if (mf.parcels.Segments != null && mf.parcels.Segments.Length >= 2) return;

            //shared tile cache (memory/disk; background pass fills from the WFS)
            var cached = CParcelCache.Snapshot();
            if (cached.Count > 0) { ApplyParcels(cached); return; }

            lblActiveOp.Text = gStr.gsNoParcelCache;
            CParcelCache.EnsureCoverage(
                mf.AppModel.CurrentLatLon.Latitude, mf.AppModel.CurrentLatLon.Longitude,
                Properties.Settings.Default.setMap_parcelWfsUrl, false, () =>
                {
                    if (!IsHandleCreated) return;
                    BeginInvoke((System.Windows.Forms.MethodInvoker)(() =>
                    {
                        var now = CParcelCache.Snapshot();
                        if (now.Count > 0) ApplyParcels(now);
                    }));
                });
        }

        private void ApplyParcels(List<CadastralParcel> parcels)
        {
            var segs = new List<GeoCoord>();
            foreach (var p in parcels)
            {
                int n = p.Outer.Count;
                if (n < 3) continue;
                var ring = new GeoCoord[n];
                for (int i = 0; i < n; i++)
                    ring[i] = mf.AppModel.LocalPlane.ConvertWgs84ToGeoCoord(new Wgs84(p.Outer[i].Lat, p.Outer[i].Lng));
                for (int i = 0; i < n; i++) { segs.Add(ring[i]); segs.Add(ring[(i + 1) % n]); }
            }
            mf.parcels.Segments = segs.ToArray();
            lblActiveOp.Text = parcels.Count + " parcels";
            oglSelf.Invalidate();
        }

        // ── Measure ───────────────────────────────────────────────────────
        private void btnMeasure_Click(object sender, EventArgs e)
        {
            measureOn = !measureOn;
            if (measureOn)
            {
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
            lblMeasure.Text = d < 1.0 ? (d * 100.0).ToString("0") + " cm" : d.ToString("0.00") + " m";
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
            GL.LineWidth(1);
            GL.Color3(1.0f, 0.1f, 0.1f);
            GL.Begin(PrimitiveType.Lines);
            GL.Vertex3(mp0.easting, mp0.northing, 0);
            GL.Vertex3(mp1.easting, mp1.northing, 0);
            GL.End();
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

        // ── Zoom ──────────────────────────────────────────────────────────
        private void btnZoomIn_Click(object sender, EventArgs e)
        { zoom *= 0.7; if (zoom < 0.02) zoom = 0.02; oglSelf.Invalidate(); }

        private void btnZoomOut_Click(object sender, EventArgs e)
        { zoom /= 0.7; if (zoom > 6.0) zoom = 6.0; oglSelf.Invalidate(); }

        private void oglSelf_MouseWheel(object sender, MouseEventArgs e)
        {
            if (e.Delta > 0) zoom *= 0.7; else zoom /= 0.7;
            if (zoom < 0.02) zoom = 0.02;
            if (zoom > 6.0) zoom = 6.0;
            oglSelf.Invalidate();
        }

        private void btnZoomRestore_Click(object sender, EventArgs e)
        { zoom = InitZoom; sX = 0; sY = 0; oglSelf.Invalidate(); }

        // ── New / Browse / Edit / Delete rows ─────────────────────────────
        private void SetMode(ViewMode m)
        {
            mode = m;
            bool build = m == ViewMode.Build;
            btnAddPoint.Enabled = build;
            btnDeletePoint.Enabled = build;
            btnPoint.Enabled = build;
            tboxName.Enabled = true;
            if (!build)
            {
                ResetAddStage(); btnDeletePoint.Checked = false;
                selIndex = -1; pointMode = PointEdit.Off;
                btnPoint.BackColor = Color.Gainsboro; lblPointMode.Text = "";
            }
            UpdateNavButtons();
            UpdateArrowColors();
            oglSelf.Invalidate();
        }

        private void UpdateNavButtons()
        {
            bool has = rows.Count > 0;
            btnPrevRow.Enabled = has;
            btnNextRow.Enabled = has;
            btnDeleteRow.Enabled = has;
            btnEdit.Enabled = has && mode == ViewMode.Browse;
            btnMakePath.Enabled = rows.Count >= 2;
            btnTreesOnRow.Enabled = !genMode && TargetRow() >= 0;
        }

        /// <summary>The row the row-level actions apply to: the edited one, else the browsed one.</summary>
        private int TargetRow()
        {
            int t = (mode == ViewMode.Build) ? workIdx : activeIdx;
            return (t >= 0 && t < rows.Count) ? t : -1;
        }

        private void btnNewRow_Click(object sender, EventArgs e)
        {
            // commit-by-reference already done live; just start a fresh empty row buffer
            pts.Clear(); workIdx = -1; selIndex = -1;
            tboxName.Text = "";
            SetMode(ViewMode.Build);
            lblActiveOp.Text = "New row — tap Add point, then place trees-line points";
        }

        private void btnEdit_Click(object sender, EventArgs e)
        {
            if (activeIdx < 0 || activeIdx >= rows.Count) return;
            ResetFilter();
            workIdx = activeIdx;
            pts.Clear();
            pts.AddRange(rows[activeIdx].pts);
            tboxName.Text = rows[activeIdx].name ?? "";
            SetMode(ViewMode.Build);
            lblActiveOp.Text = "Edit row";
        }

        private void btnPrevRow_Click(object sender, EventArgs e) => Navigate(-1);
        private void btnNextRow_Click(object sender, EventArgs e) => Navigate(1);

        private void Navigate(int dir)
        {
            if (rows.Count == 0) return;
            ResetFilter();
            if (mode != ViewMode.Browse) { SetMode(ViewMode.Browse); if (activeIdx < 0) activeIdx = 0; }
            else
            {
                activeIdx += dir;
                if (activeIdx < 0) activeIdx = rows.Count - 1;
                if (activeIdx >= rows.Count) activeIdx = 0;
            }
            UpdateNavButtons();
            RefreshActiveName();
            lblActiveOp.Text = (rows[activeIdx].name ?? "row") + "  (" + (activeIdx + 1) + "/" + rows.Count + ")"
                + (rows[activeIdx].trees.Count > 0 ? "  -  " + rows[activeIdx].trees.Count + " trees" : "");
            oglSelf.Invalidate();
        }

        private void RefreshActiveName()
        {
            if (activeIdx >= 0 && activeIdx < rows.Count) tboxName.Text = rows[activeIdx].name ?? "";
        }

        private void btnDeleteRow_Click(object sender, EventArgs e)
        {
            int target = (mode == ViewMode.Build) ? workIdx : activeIdx;
            if (target < 0 || target >= rows.Count) return;
            rows.RemoveAt(target);
            workIdx = -1; pts.Clear();
            midlines.Clear(); lblWarn.Text = "";
            if (activeIdx >= rows.Count) activeIdx = rows.Count - 1;
            if (rows.Count == 0) { SetMode(ViewMode.Build); lblActiveOp.Text = "no rows"; }
            else { SetMode(ViewMode.Browse); RefreshActiveName(); lblActiveOp.Text = "row deleted"; }
            oglSelf.Invalidate();
        }

        // ── Import KML (rows from LineStrings, trees from Points) ──────────
        private void btnImportKml_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                //t9: the same dialog restores the measurement - Orchard_recorded.txt holds the raw
                //recorded rows, so "back to what I drove" is an import, not a separate undo
                ofd.Filter = "Rows (*.kml;*.txt)|*.kml;*.txt|KML files (*.kml)|*.kml"
                           + "|Recorded rows (*.txt)|*.txt|All files (*.*)|*.*";
                ofd.Title = "Import orchard rows / trees (KML, or recorded rows)";
                ofd.InitialDirectory = FieldDir();
                if (ofd.ShowDialog(this) != DialogResult.OK) return;

                if (ofd.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    ImportRecordedRows(ofd.FileName);
                    return;
                }

                var newRows = COrchard.ImportKml(ofd.FileName, mf.AppModel.LocalPlane, trees, out string err);
                if (err != null) { lblActiveOp.Text = "KML: " + err; return; }
                foreach (var r in newRows) rows.Add(r);
                midlines.Clear(); lblWarn.Text = "";
                if (rows.Count > 0) { activeIdx = rows.Count - newRows.Count; if (activeIdx < 0) activeIdx = 0; SetMode(ViewMode.Browse); RefreshActiveName(); }
                UpdateNavButtons();
                lblActiveOp.Text = "KML: +" + newRows.Count + " rows, " + trees.Count + " trees";
                oglSelf.Invalidate();
            }
        }

        // ── New orchard (planting): generate rows + seedling spots ────────
        // The orchard does not exist yet: the driver draws ONE reference line inside the
        // boundary (2 points = straight, more = curve) and the layout settings turn it
        // into the full grid of rows and planting positions.
        /// <summary>
        /// Restore rows from a saved row file - in practice Orchard_recorded.txt, the raw rows kept
        /// when a recorded row was first filtered (t9). Rows are matched BY NAME: a row that exists
        /// is replaced by its measurement, one that no longer exists is added back, and everything
        /// else (generated, KML, hand-drawn) is left alone. Planting.txt is not touched, so what is
        /// already planted survives.
        /// </summary>
        private void ImportRecordedRows(string path)
        {
            List<OrchRow> saved;
            try
            {
                saved = COrchard.LoadRowsFrom(System.IO.Path.GetDirectoryName(path),
                                              System.IO.Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                Log.EventWriter("Orchard ImportRecordedRows: " + ex);
                lblActiveOp.Text = "Could not read that file";
                return;
            }

            if (saved.Count == 0) { lblActiveOp.Text = "No rows in that file"; return; }

            int replaced = 0, added = 0;
            foreach (var r in saved)
            {
                int at = -1;
                for (int i = 0; i < rows.Count; i++) if (rows[i].name == r.name) { at = i; break; }

                if (at >= 0) { rows[at] = new OrchRow(r); replaced++; }
                else { rows.Add(new OrchRow(r)); added++; }
            }

            ResetFilter();
            midlines.Clear();
            lblWarn.Text = "";
            if (rows.Count > 0) { activeIdx = 0; SetMode(ViewMode.Browse); RefreshActiveName(); }
            UpdateNavButtons();
            lblActiveOp.Text = "Recorded rows: " + replaced + " restored, " + added + " added";
            oglSelf.Invalidate();
        }

        private List<List<vec2>> FieldBounds()
        {
            var b = new List<List<vec2>>();
            for (int i = 0; i < mf.bnd.bndList.Count; i++)
            {
                var ring = mf.bnd.bndList[i].fenceLineEar;
                if (ring != null && ring.Count >= 3) b.Add(new List<vec2>(ring));
            }
            return b;
        }

        private void btnNewOrchard_Click(object sender, EventArgs e)
        {
            if (FieldBounds().Count == 0)
            {
                lblActiveOp.Text = "New orchard needs a field boundary first";
                return;
            }

            genMode = true;
            pnlGen.Visible = true;
            pnlGen.BringToFront();
            lblGenResult.Text = "";

            pts.Clear(); workIdx = -1; selIndex = -1;
            SetMode(ViewMode.Build);
            lblActiveOp.Text = "New orchard - set the spacings, then draw the reference line";
            oglSelf.Invalidate();
        }

        private void btnGenLine_Click(object sender, EventArgs e)
        {
            pts.Clear(); selIndex = -1;
            btnDeletePoint.Checked = false;
            SetMode(ViewMode.Build);
            addStage = 1;
            ApplyAddStageVisual();
            lblActiveOp.Text = "Draw the reference line - tap 2 points (more = curved rows)";
            oglSelf.Invalidate();
        }

        private void btnGenerate_Click(object sender, EventArgs e)
        {
            if (pts.Count < 2) { lblGenResult.Text = "Draw the reference line first"; return; }

            var bounds = FieldBounds();
            var made = COrchard.GenerateOrchard(pts, bounds,
                (double)nudGenRowSpace.Value, (double)nudGenTreeSpace.Value,
                (double)nudGenHeadland.Value, (double)nudGenSetback.Value,
                chkGenStagger.Checked, out string err);

            if (err != null && made.Count == 0) { lblGenResult.Text = err; return; }

            // regenerating replaces the previous layout, hand-drawn/imported rows stay
            rows.RemoveAll(r => r.source == RowSource.Generated);
            foreach (var r in made) rows.Add(r);

            int treeCount = 0;
            foreach (var r in made) treeCount += r.trees.Count;

            midlines.Clear(); lblWarn.Text = "";
            ResetAddStage();
            plantingTouched = true;
            UpdateNavButtons();
            lblGenResult.Text = made.Count + " rows, " + treeCount + " trees";
            lblActiveOp.Text = "Generated " + made.Count + " rows / " + treeCount + " trees";
            oglSelf.Invalidate();
        }

        // Seedlings along ONE row, at the current tree spacing / setback / headland.
        // Used after hand-editing a generated row and for rows drawn by hand to fill an
        // odd corner of the field.
        private void btnTreesOnRow_Click(object sender, EventArgs e)
        {
            int t = TargetRow();
            if (t < 0) { lblActiveOp.Text = "Pick a row first"; return; }

            COrchard.PlaceTreesOnRow(rows[t], FieldBounds(),
                (double)nudGenTreeSpace.Value, (double)nudGenSetback.Value,
                (double)nudGenHeadland.Value);

            lblWarn.Text = "";
            plantingTouched = true;
            lblActiveOp.Text = (rows[t].name ?? "row") + ": " + rows[t].trees.Count + " trees";
            oglSelf.Invalidate();
        }

        private void btnGenClose_Click(object sender, EventArgs e)
        {
            genMode = false;
            pnlGen.Visible = false;
            pts.Clear(); selIndex = -1;
            ResetAddStage();
            btnDeletePoint.Checked = false;

            if (rows.Count > 0) { activeIdx = 0; SetMode(ViewMode.Browse); RefreshActiveName(); }
            else SetMode(ViewMode.Build);
            UpdateNavButtons();
            lblActiveOp.Text = "";
            oglSelf.Invalidate();
        }

        // ── Make path (phase 2: passes from the IMPLEMENT settings) ───────
        // Tool type Orchard supplies working mode / spaces / offsets; any other
        // tool type falls back to the MVP single center pass per inter-row.
        private void btnMakePath_Click(object sender, EventArgs e)
        {
            if (rows.Count < 2) { lblActiveOp.Text = "Need at least 2 rows"; return; }
            midlines.Clear();
            double toolW = mf.tool.width > 0.1 ? mf.tool.width : 3.0;

            var ts = Properties.ToolSettings.Default;
            List<CTrk> passes = COrchard.BuildTracks(rows, COrchard.ResolvePathMode(), toolW,
                ts.setTool_orchardTreeWidth, ts.setTool_orchardCenterFirst,
                ts.setTool_orchardPassCount, out List<string> warns, FieldBounds());

            foreach (var t in passes) midlines.Add(t);
            lblWarn.Text = warns.Count > 0
                ? warns.Count + " warning(s): " + warns[0]
                : "";
            lblActiveOp.Text = midlines.Count + " passes built"
                + (mf.tool.isToolOrchard ? "" : " (basic - set tool type Orchard for multi-pass)");
            oglSelf.Invalidate();
        }

        // ── Cancel / Accept ───────────────────────────────────────────────
        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void btnAccept_Click(object sender, EventArgs e)
        {
            // name the relevant row
            int target = (mode == ViewMode.Browse) ? activeIdx : workIdx;
            string nm = tboxName.Text.Trim();
            if (nm.Length > 0 && target >= 0 && target < rows.Count) rows[target].name = nm;

            // persist rows + loose trees under the field (fixed reference lines)
            try { COrchard.SaveRows(FieldDir(), rows, trees); }
            catch (Exception ex) { Log.EventWriter("Orchard SaveRows: " + ex); }

            // hand the rows to the runtime: they are drawn on the map and the driving
            // path is derived from them (SIM 0729 t5/t7)
            mf.orchRows.Clear();
            foreach (var r in rows) mf.orchRows.Add(new OrchRow(r));

            // planting: the seedling spots become the driving job. Only when this really
            // is a planting orchard - a survey orchard (KML/recorded rows of trees that
            // already stand) must not start nagging the driver to plant them.
            if (plantingTouched || mf.planting.IsOn)
            {
                mf.planting.BuildFromRows(rows);
                mf.planting.Save(FieldDir());
            }

            // SIM 0729 t5: the path is derived, not committed by hand. Editing the rows is a
            // change of the definition, so the passes are rebuilt from the new rows + the
            // implement settings - hand-made tracks are untouched.
            mf.OrchardRebuildTracks("orchard rows accepted");

            int derived = 0;
            foreach (var t in mf.trk.gArr) if (t.isOrchardDerived) derived++;
            lblActiveOp.Text = "Saved: " + rows.Count + " rows, path = " + derived + " passes";

            UpdateNavButtons();
            oglSelf.Invalidate();
        }
    }
}
