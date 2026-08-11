// FormOrchardRecord.cs - the small orchard row recorder (F50, 2026-08-11).
//
// The big planner window is no place to be while driving: it covers the map and it steals the
// keyboard. Pressing "Record row" there hands over to this window - small, ALWAYS ON TOP, and
// deliberately never taking the focus (WS_EX_NOACTIVATE), so the main screen keeps working and
// a plugged-in keypad still reaches it.
//
// Built in code rather than in the designer: nine controls in a fixed grid, and a hand-written
// layout is easier to follow than a generated one for something this small.
//
// Spec: features/50_orchard-navigation/RECORD_ROW.md §3
using AgOpenGPS.Core.Translations;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AgOpenGPS
{
    public partial class FormOrchardRecord : Form
    {
        private readonly FormGPS mf;

        /// <summary>Rows recorded in this session - the planner picks them up on OK.</summary>
        public readonly List<OrchRow> recordedRows = new List<OrchRow>();

        /// <summary>True when the window was closed with the tick, not with the X.</summary>
        public bool isAccepted;

        private Button btnCancel, btnOk, btnSection, btnRow, btnDiscard, btnTree, btnDelTree, btnKey;
        private bool capturingKey;
        private NumericUpDown nudTreeMin;
        private Label lblTreeMin, lblStatus;
        private readonly Timer tick = new Timer();

        public FormOrchardRecord(FormGPS _f, double treeMinDist, List<OrchRow> existingRows)
        {
            mf = _f;
            existing = existingRows;
            BuildUi(treeMinDist);

            // every row starts on a clean map: the strips of this row are the only thing on it
            // (decision 2026-08-11 - rows are never recorded while the field is being worked)
            mf.DeleteAppliedArea();

            mf.orchRec.isArmed = true;
            mf.orchRec.StartRow();

            tick.Interval = 400;
            tick.Tick += (s, e) => UpdateUi();
            tick.Start();
        }

        private readonly List<OrchRow> existing;

        // Never steal the focus: the driver is working the main screen, this window only watches.
        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_NOACTIVATE = 0x08000000;
                const int WS_EX_TOOLWINDOW = 0x00000080;
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        private void BuildUi(double treeMinDist)
        {
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            Text = "";
            ControlBox = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ClientSize = new Size(300, 348);
            BackColor = Color.WhiteSmoke;

            var big = new Font("Tahoma", 12F, FontStyle.Bold);
            var mid = new Font("Tahoma", 10F, FontStyle.Bold);

            btnCancel = Btn("X", 232, 8, 60, 40, mid, Color.MistyRose);
            btnCancel.Click += (s, e) => Finish(false);

            btnOk = Btn("OK", 166, 8, 60, 40, mid, Color.PaleGreen);
            btnOk.Click += (s, e) => Finish(true);

            btnSection = Btn(gStr.gsOrchRecSection + ": OFF", 8, 56, 140, 46, mid, Color.Gainsboro);
            btnSection.Click += BtnSection_Click;

            btnRow = Btn(gStr.gsOrchRecNewRow, 156, 56, 136, 46, mid, Color.Gainsboro);
            btnRow.Click += BtnRow_Click;

            btnDiscard = Btn(gStr.gsOrchRecDiscardRow, 156, 108, 136, 40, mid, Color.MistyRose);
            btnDiscard.Click += BtnDiscard_Click;

            lblTreeMin = new Label
            {
                Text = gStr.gsOrchTreeMinDist,
                Font = new Font("Tahoma", 9F),
                Location = new Point(8, 116),
                Size = new Size(96, 32),
                TextAlign = ContentAlignment.MiddleLeft
            };

            nudTreeMin = new NumericUpDown
            {
                Location = new Point(104, 112),
                Size = new Size(44, 30),
                Font = new Font("Tahoma", 10F),
                DecimalPlaces = 1,
                Increment = 0.5M,
                Minimum = 0.5M,
                Maximum = 30M,
                Value = (decimal)Math.Min(30, Math.Max(0.5, treeMinDist))
            };

            btnDelTree = Btn(gStr.gsOrchDeleteLastTree, 8, 156, 284, 40, mid, Color.Gainsboro);
            btnDelTree.Click += (s, e) => { mf.orchRec.DeleteLastTree(); UpdateUi(); };

            btnKey = Btn("", 8, 202, 284, 30, new Font("Tahoma", 9F, FontStyle.Bold), Color.Gainsboro);
            btnKey.Click += BtnKey_Click;

            btnTree = Btn(gStr.gsOrchTree, 8, 238, 284, 76, big, Color.Khaki);
            btnTree.Click += (s, e) => { mf.orchRec.MarkTree(); UpdateUi(); };

            lblStatus = new Label
            {
                Text = gStr.gsOrchRecStatusIdle,
                Font = new Font("Tahoma", 9F),
                Location = new Point(8, 318),
                Size = new Size(284, 24),
                TextAlign = ContentAlignment.MiddleLeft
            };

            Controls.AddRange(new Control[] { btnCancel, btnOk, btnSection, btnRow, btnDiscard,
                lblTreeMin, nudTreeMin, btnDelTree, btnKey, btnTree, lblStatus });

            // no title bar to grab, so the window is dragged by its own background - and by the
            // labels, which would otherwise be dead spots (field call 2026-08-11)
            MouseDown += Drag_MouseDown;
            lblStatus.MouseDown += Drag_MouseDown;
            lblTreeMin.MouseDown += Drag_MouseDown;

            UpdateKeyLabel();
        }

        private static Button Btn(string text, int x, int y, int w, int h, Font f, Color back)
        {
            return new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                Font = f,
                FlatStyle = FlatStyle.Flat,
                BackColor = back,
                UseVisualStyleBackColor = false
            };
        }

        // ── Buttons ───────────────────────────────────────────────────────

        // The ON/OFF button is the ordinary manual section master, not a private buffer: what
        // gets painted is what gets recorded, and the driver sees it in the HUD (call 20:06).
        private void BtnSection_Click(object sender, EventArgs e)
        {
            mf.btnSectionMasterManual.PerformClick();
            UpdateUi();
        }

        // One button: "New row" until something is recorded, "Finish row" from then on -
        // finishing one row IS the start of the next (decision 2026-08-11).
        private void BtnRow_Click(object sender, EventArgs e)
        {
            if (!mf.orchRec.HasData)
            {
                mf.orchRec.StartRow();
                lblStatus.Text = gStr.gsOrchRecDriveFirst;
                UpdateUi();
                return;
            }

            var all = new List<OrchRow>(existing);
            all.AddRange(recordedRows);

            OrchRow row = mf.orchRec.BuildRow(COrchard.NextRowName(all),
                                              (double)nudTreeMin.Value, out string err);
            if (row == null)
            {
                lblStatus.Text = err;
                return;
            }

            recordedRows.Add(row);
            mf.DeleteAppliedArea();       // clean map for the next row, as asked 2026-08-11
            mf.orchRec.StartRow();
            lblStatus.Text = row.name + " OK (" + row.trees.Count + " " + gStr.gsOrchTrees + ")";
            UpdateUi();
        }

        private void BtnDiscard_Click(object sender, EventArgs e)
        {
            mf.DeleteAppliedArea();
            mf.orchRec.DiscardRow();
            lblStatus.Text = gStr.gsOrchRecStatusIdle;
            UpdateUi();
        }

        private void Finish(bool accept)
        {
            isAccepted = accept;
            if (!accept)
            {
                mf.DeleteAppliedArea();
                mf.orchRec.DiscardRow();
                recordedRows.Clear();
            }
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            tick.Stop();
            tick.Dispose();
            mf.orchRec.isArmed = false;
            base.OnFormClosed(e);
        }

        // ── Dragging ──────────────────────────────────────────────────────

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private void Drag_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            const int WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 0x2;
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
        }

        // ── Trigger key ───────────────────────────────────────────────────
        // A keypad key is easier to hit than the screen while driving. This window never takes
        // the focus, so the main form catches the key and hands it here (ProcessCmdKey).

        private void BtnKey_Click(object sender, EventArgs e)
        {
            capturingKey = true;
            btnKey.Text = gStr.gsOrchPressKey;
            btnKey.BackColor = Color.Orange;
        }

        /// <summary>Key seen by the main form. True when this window used it.</summary>
        public bool HandleTriggerKey(Keys keyData)
        {
            if (capturingKey)
            {
                if (keyData == Keys.Escape) { capturingKey = false; UpdateKeyLabel(); return true; }
                Properties.Settings.Default.setDisplay_orchardTreeKey = (int)keyData;
                try { Properties.Settings.Default.Save(); } catch { }
                capturingKey = false;
                UpdateKeyLabel();
                return true;
            }

            int k = Properties.Settings.Default.setDisplay_orchardTreeKey;
            if (k != 0 && (int)keyData == k)
            {
                mf.orchRec.MarkTree();
                UpdateUi();
                return true;
            }
            return false;
        }

        private void UpdateKeyLabel()
        {
            int k = Properties.Settings.Default.setDisplay_orchardTreeKey;
            if (k == 0)
            {
                // never set before - Space is the one key every keypad has
                k = (int)Keys.Space;
                Properties.Settings.Default.setDisplay_orchardTreeKey = k;
                try { Properties.Settings.Default.Save(); } catch { }
            }
            btnKey.Text = gStr.gsOrchKeyTrigger + ": " + ((Keys)k);
            btnKey.BackColor = Color.Gainsboro;
        }

        // ── Live state ────────────────────────────────────────────────────

        private void UpdateUi()
        {
            bool on = mf.manualBtnState == btnStates.On;
            btnSection.Text = gStr.gsOrchRecSection + (on ? ": ON" : ": OFF");
            btnSection.BackColor = on ? Color.PaleGreen : Color.Gainsboro;

            bool has = mf.orchRec.HasData;
            btnRow.Text = has ? gStr.gsOrchRecFinishRow : gStr.gsOrchRecNewRow;
            btnRow.BackColor = has ? Color.Khaki : Color.Gainsboro;
            btnDiscard.Enabled = has;

            if (has)
            {
                lblStatus.Text = gStr.gsOrchRecPass + " " + mf.orchRec.PassCount
                    + " - " + mf.orchRec.CurrentLength().ToString("0") + " m, "
                    + mf.orchRec.treeMarks.Count + " " + gStr.gsOrchTrees;
            }
        }
    }
}
