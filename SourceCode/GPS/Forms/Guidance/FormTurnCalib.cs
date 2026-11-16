using AgOpenGPS.Core.Translations;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace AgOpenGPS
{
    /// <summary>
    /// Turn calibration window (REQ 2026-11-16-1). Small, not modal, on the left half of the
    /// screen: the test drives are done while driving, so the navigation behind it stays visible
    /// and usable. OK saves the two steering numbers and brings the Turn window back.
    /// </summary>
    public class FormTurnCalib : Form
    {
        private readonly FormGPS mf;
        private readonly NumericUpDown nudRate, nudLead, nudAccel, nudFilter, nudSAngle, nudSRate, nudSMin;
        private readonly Button btnTeach, btnRadius;
        private readonly Label lblLive, lblTeach, lblRadius;
        private readonly ListBox lstRadius;
        private readonly Timer timer;
        private bool teachSeen = true;
        private int resultsShown = -1;

        private static readonly Font fText = new Font("Tahoma", 10.5F);
        private static readonly Font fBold = new Font("Tahoma", 11.25F, FontStyle.Bold);

        public FormTurnCalib(FormGPS callingForm)
        {
            mf = callingForm;

            Text = gStr.gsCalibTab;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            ControlBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(520, 912);
            AutoScroll = true;
            BackColor = Color.WhiteSmoke;
            Font = fText;

            //what the driver needs to see while doing this, in big type
            lblLive = new Label
            {
                Font = new Font("Tahoma", 17F, FontStyle.Bold), Location = new Point(14, 10), Size = new Size(492, 36),
                TextAlign = ContentAlignment.MiddleLeft
            };
            Controls.Add(lblLive);

            // ---- steering: the two numbers + Teach
            var gSteer = new GroupBox { Text = gStr.gsTeachStart, Font = fBold, Location = new Point(10, 54), Size = new Size(500, 332) };
            Controls.Add(gSteer);
            nudRate = AddRow(gSteer, gStr.gsSteerRate, 30, 5, 60, 0, 1M);
            nudLead = AddRow(gSteer, gStr.gsLeadTime, 76, 0, 1, 2, 0.05M);
            nudAccel = AddRow(gSteer, gStr.gsSteerAccel, 122, 0, 600, 0, 10M);
            nudFilter = AddRow(gSteer, gStr.gsFbFilter, 168, 0, 0.5M, 2, 0.05M);
            btnTeach = BigButton(gStr.gsTeachStart, 12, 220, 170);
            btnTeach.Click += (s, e) =>
            {
                if (mf.turnCal.mode == CTurnCalib.Mode.Teach) mf.turnCal.Stop();
                else if (mf.turnCal.StartTeach()) teachSeen = false;
                else lblTeach.Text = gStr.gsTeachFail;
            };
            lblTeach = new Label { Font = fText, Location = new Point(194, 218), Size = new Size(296, 106), Text = gStr.gsTeachHint };
            gSteer.Controls.Add(btnTeach);
            gSteer.Controls.Add(lblTeach);

            // ---- the counter-turn back onto the row (user 16.11.)
            var gS = new GroupBox { Text = gStr.gsSGroup, Font = fBold, Location = new Point(10, 394), Size = new Size(500, 168) };
            Controls.Add(gS);
            nudSAngle = AddRow(gS, gStr.gsSMaxAngle, 30, 3, 35, 0, 1M);
            nudSRate = AddRow(gS, gStr.gsSSteerRate, 76, 1, 60, 0, 1M);
            nudSMin = AddRow(gS, gStr.gsSMinOffset, 122, 0, 1, 2, 0.05M);

            // ---- radius check
            var gRad = new GroupBox { Text = gStr.gsRadiusTitle, Font = fBold, Location = new Point(10, 570), Size = new Size(500, 272) };
            Controls.Add(gRad);
            btnRadius = BigButton(gStr.gsRadiusStart, 12, 30, 230);
            btnRadius.Click += (s, e) =>
            {
                if (mf.turnCal.mode == CTurnCalib.Mode.Radius) mf.turnCal.Stop();
                else if (!mf.turnCal.StartRadius()) lblRadius.Text = gStr.gsRadiusNeedAngle;
            };
            lblRadius = new Label
            {
                Font = fText, Location = new Point(12, 84), Size = new Size(478, 44),
                Text = string.Format(gStr.gsRadiusHint, (mf.vehicle.maxSteerAngle * 0.85).ToString("N0"))
            };
            lstRadius = new ListBox { Font = fText, Location = new Point(12, 132), Size = new Size(476, 96), IntegralHeight = false };
            var note = new Label { Font = new Font("Tahoma", 9F), ForeColor = Color.DimGray, Location = new Point(12, 232), Size = new Size(478, 34), Text = gStr.gsRadiusNote };
            gRad.Controls.Add(btnRadius);
            gRad.Controls.Add(lblRadius);
            gRad.Controls.Add(lstRadius);
            gRad.Controls.Add(note);

            // ---- OK / Cancel
            var ok = BigButton("OK", 290, 852, 105);
            ok.BackColor = Color.LightGreen;
            ok.Click += (s, e) =>
            {
                var veh = Properties.VehicleSettings.Default;
                veh.setTurn_steerRate = (double)nudRate.Value;
                veh.setTurn_leadTime = (double)nudLead.Value;
                veh.setTurn_steerAccel = (double)nudAccel.Value;
                veh.setTurn_fbFilter = (double)nudFilter.Value;
                veh.setTurn_S_maxAngle = (double)nudSAngle.Value;
                veh.setTurn_S_steerRate = (double)nudSRate.Value;
                veh.setTurn_S_minOffset = (double)nudSMin.Value;
                veh.Save();
                Close();
            };
            var cancel = BigButton(gStr.gsCancel, 405, 852, 105);
            cancel.Click += (s, e) => Close();
            Controls.Add(ok);
            Controls.Add(cancel);

            SetNud(nudRate, Properties.VehicleSettings.Default.setTurn_steerRate);
            SetNud(nudLead, Properties.VehicleSettings.Default.setTurn_leadTime);
            SetNud(nudAccel, Properties.VehicleSettings.Default.setTurn_steerAccel);
            SetNud(nudFilter, Properties.VehicleSettings.Default.setTurn_fbFilter);
            SetNud(nudSAngle, Properties.VehicleSettings.Default.setTurn_S_maxAngle);
            SetNud(nudSRate, Properties.VehicleSettings.Default.setTurn_S_steerRate);
            SetNud(nudSMin, Properties.VehicleSettings.Default.setTurn_S_minOffset);

            timer = new Timer { Interval = 250 };
            timer.Tick += (s, e) => UpdateLive();
            Load += (s, e) =>
            {
                //left half of the main window, under its top row of buttons
                Rectangle r = mf.Bounds;
                //never taller than the screen (a small tablet): the rest scrolls
                Rectangle wa = Screen.FromControl(mf).WorkingArea;
                if (Height > wa.Height - 20) Height = wa.Height - 20;
                Location = new Point(r.Left + 6, Math.Max(wa.Top, Math.Min(r.Top + Math.Max(60, (r.Height - Height) / 2), wa.Bottom - Height)));
                UpdateLive();
                timer.Start();
            };
            FormClosing += (s, e) =>
            {
                //a run never outlives the window it was started from
                timer.Stop();
                if (mf.turnCal.IsBusy) mf.turnCal.Stop();
            };
        }

        private static void SetNud(NumericUpDown nud, double v)
        {
            decimal d = (decimal)v;
            if (d < nud.Minimum) d = nud.Minimum;
            if (d > nud.Maximum) d = nud.Maximum;
            nud.Value = d;
        }

        private static Button BigButton(string text, int x, int y, int w)
        {
            return new Button
            {
                Text = text, Font = new Font("Tahoma", 12F, FontStyle.Bold), FlatStyle = FlatStyle.Flat,
                BackColor = Color.Gainsboro, UseVisualStyleBackColor = false,
                Location = new Point(x, y), Size = new Size(w, 46)
            };
        }

        /// <summary>label, value (tap = keypad) and the two step buttons</summary>
        private NumericUpDown AddRow(Control host, string text, int y, decimal min, decimal max, int decimals, decimal step)
        {
            host.Controls.Add(new Label { Text = text, Font = fText, Location = new Point(12, y + 4), Size = new Size(268, 30), TextAlign = ContentAlignment.MiddleLeft });
            var nud = new NumericUpDown
            {
                Minimum = min, Maximum = max, DecimalPlaces = decimals, Increment = step, ReadOnly = true,
                Font = new Font("Tahoma", 13F), Location = new Point(286, y), Size = new Size(96, 34), TextAlign = HorizontalAlignment.Center
            };
            if (nud.Controls.Count > 0) nud.Controls[0].Visible = false;
            host.Controls.Add(nud);

            EventHandler keypad = (s, e) => BeginInvoke((Action)(() => OpenKeypad(nud)));
            nud.Click += keypad;
            foreach (Control ch in nud.Controls) if (ch.Visible) ch.MouseUp += (s, e) => keypad(s, e);

            var minus = BigButton("◀", 390, y - 4, 46); minus.Size = new Size(46, 40);
            var plus = BigButton("▶", 442, y - 4, 46); plus.Size = new Size(46, 40);
            minus.Click += (s, e) => { if (nud.Value - step >= nud.Minimum) nud.Value -= step; };
            plus.Click += (s, e) => { if (nud.Value + step <= nud.Maximum) nud.Value += step; };
            host.Controls.Add(minus);
            host.Controls.Add(plus);
            return nud;
        }

        private bool keypadOpen;
        private DateTime keypadClosed = DateTime.MinValue;

        private void OpenKeypad(NumericUpDown nud)
        {
            if (keypadOpen || (DateTime.UtcNow - keypadClosed).TotalMilliseconds < 400) return;
            keypadOpen = true;
            try
            {
                using (var f = new FormNumeric((double)nud.Minimum, (double)nud.Maximum, (double)nud.Value))
                {
                    if (f.ShowDialog(this) == DialogResult.OK) SetNud(nud, f.ReturnValue);
                }
            }
            finally { keypadOpen = false; keypadClosed = DateTime.UtcNow; }
        }

        private void UpdateLive()
        {
            var c = mf.turnCal;
            lblLive.Text = string.Format(gStr.gsCalLive, mf.mc.actualSteerAngleDegrees.ToString("N1"),
                Math.Abs(mf.avgSpeed).ToString("N1"));

            btnTeach.Text = c.mode == CTurnCalib.Mode.Teach ? gStr.gsStop : gStr.gsTeachStart;
            btnRadius.Text = c.mode == CTurnCalib.Mode.Radius ? gStr.gsStop : gStr.gsRadiusStart;
            btnTeach.Enabled = c.mode != CTurnCalib.Mode.Radius;
            btnRadius.Enabled = c.mode != CTurnCalib.Mode.Teach;
            btnTeach.BackColor = c.mode == CTurnCalib.Mode.Teach ? Color.Gold : Color.Gainsboro;
            btnRadius.BackColor = c.mode == CTurnCalib.Mode.Radius ? Color.Gold : Color.Gainsboro;

            if (c.mode == CTurnCalib.Mode.Teach)
                lblTeach.Text = string.Format(gStr.gsTeachRunning, (c.TeachProgress * 100).ToString("N0"));
            else if (c.teachDone && !teachSeen)
            {
                teachSeen = true;
                if (c.teachOk)
                {
                    SetNud(nudRate, Math.Round(c.teachRate));
                    SetNud(nudLead, Math.Round(c.teachLead / 0.05) * 0.05);
                    if (c.teachAccel > 0) SetNud(nudAccel, Math.Round(c.teachAccel / 10) * 10);
                    lblTeach.Text = string.Format(gStr.gsTeachResult, c.teachRate.ToString("N0"),
                        c.teachLead.ToString("N2"), c.teachGain.ToString("N2"));
                }
                else lblTeach.Text = c.teachMeasured ? gStr.gsTeachUnsure : gStr.gsTeachFail;
            }

            if (c.mode == CTurnCalib.Mode.Radius)
                lblRadius.Text = string.Format(gStr.gsRadiusProgress, c.holdAngle.ToString("N1"),
                    (c.sweep / glm.twoPI * 100).ToString("N0"));

            if (resultsShown != c.resultsVersion)
            {
                resultsShown = c.resultsVersion;
                lstRadius.Items.Clear();
                foreach (var r in c.results)
                {
                    double pct = (r.measured - r.expected) / r.expected * 100;
                    string row = string.Format(gStr.gsRadiusRow, r.angle.ToString("N1"), r.expected.ToString("N2"),
                        r.measured.ToString("N2"), (pct >= 0 ? "+" : "") + pct.ToString("N0"));
                    if (r.why == CTurnCalib.Unreliable.ShortCircle)
                        row += "  - " + string.Format(gStr.gsRadiusWhyShort, (r.sweepDeg / 3.6).ToString("N0"));
                    else if (r.why == CTurnCalib.Unreliable.AngleMoved) row += "  - " + gStr.gsRadiusWhyAngle;
                    else if (r.why == CTurnCalib.Unreliable.Gps) row += "  - " + gStr.gsRadiusWhyGps;
                    lstRadius.Items.Add(row);
                }
                if (lstRadius.Items.Count > 0) lstRadius.TopIndex = lstRadius.Items.Count - 1;
            }
        }
    }
}
