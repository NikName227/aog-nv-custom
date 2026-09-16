// F10 Advanced Sensor Fusion (NV custom) — Wizards dialog.
// Two independent functions (cabin compensation, heading observer), one learning run for
// both parameters, a raw logger and a live view of every signal in the chain.
// Code-built UI (no designer), same style/size as the F07 Integration Test window.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace AgOpenGPS
{
    public class FormSensorFusion : Form
    {
        private readonly FormGPS mf;
        private readonly bool hr;
        private readonly Timer poll = new Timer();

        private CheckBox cbCabin, cbObs, cbAdvanced, cbAutoLog;
        private NumericUpDown nKFast, nFc, nLead, nTau, nKp, nKi, nImuW;
        private Label lblLearned, lblLearnStatus, lblLogInfo, lblLive, lblStatusTop;
        private Button btnLearn, btnLog, btnFolder, btnClose;
        private bool learning;
        private DateTime learnStart;

        public FormSensorFusion(FormGPS callingForm)
        {
            mf = callingForm;
            hr = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "hr";

            Name = "FormSensorFusion";
            Text = "Advanced Sensor Fusion";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(440, 588);
            BackColor = Color.FromArgb(24, 33, 29);
            ForeColor = Color.Gainsboro;
            Font = new Font("Tahoma", 9f);

            int y = 8;
            lblStatusTop = Lbl("", 12, y, 416, 18, 9f, Color.FromArgb(150, 200, 180)); y += 22;

            // ---------------- cabin compensation ----------------
            Sep(y - 2);
            cbCabin = Chk(hr ? "KOMPENZACIJA KABINE" : "CABIN COMPENSATION", 12, y); y += 24;
            Lbl("k_fast [m]", 20, y + 3, 70, 16);
            nKFast = Num(96, y, 0, 6, 0.05m, 2);
            Lbl("fc [Hz]", 190, y + 3, 52, 16);
            nFc = Num(244, y, 0, 3, 0.1m, 2);
            Lbl("lead", 330, y + 3, 34, 16);
            nLead = Num(366, y, 0, 1, 0.05m, 2); y += 28;
            cbAdvanced = Chk(hr ? "napredno: τ_slow [s]" : "advanced: tau_slow [s]", 20, y);
            nTau = Num(180, y - 2, 0.2m, 5, 0.1m, 1); y += 28;
            btnLearn = Btn(hr ? "Učenje (k_fast + model)" : "Learning (k_fast + model)", 20, y, 250, 30, Color.FromArgb(15, 131, 149));
            lblLearnStatus = Lbl("—", 278, y + 8, 150, 16, 8.5f, Color.FromArgb(160, 180, 170)); y += 36;

            // ---------------- heading observer ----------------
            Sep(y - 4);
            cbObs = Chk(hr ? "OBSERVER SMJERA" : "HEADING OBSERVER", 12, y); y += 24;
            Lbl("Kp", 20, y + 3, 24, 16);
            nKp = Num(46, y, 0.05m, 2, 0.05m, 2);
            Lbl("Ki", 140, y + 3, 20, 16);
            nKi = Num(162, y, 0, 0.05m, 0.001m, 3);
            Lbl(hr ? "udio IMU" : "IMU share", 268, y + 3, 62, 16);
            nImuW = Num(336, y, 0, 1, 0.05m, 2); y += 28;
            lblLearned = Lbl("", 20, y, 400, 16, 8.5f, Color.FromArgb(160, 180, 170)); y += 24;

            // ---------------- raw log ----------------
            Sep(y - 4);
            Lbl("RAW LOG", 12, y, 120, 18, 9.5f, Color.White); y += 22;
            btnLog = Btn(hr ? "● Log start" : "● Log start", 20, y, 130, 30, Color.FromArgb(120, 50, 50));
            btnFolder = Btn(hr ? "Otvori folder" : "Open folder", 158, y, 120, 30, Color.FromArgb(60, 71, 64));
            cbAutoLog = Chk(hr ? "auto" : "auto", 290, y + 6); y += 34;
            lblLogInfo = Lbl("—", 20, y, 400, 16, 8.5f, Color.FromArgb(160, 180, 170)); y += 22;

            // ---------------- live ----------------
            Sep(y - 4);
            Lbl(hr ? "ŽIVI PRIKAZ" : "LIVE", 12, y, 120, 18, 9.5f, Color.White); y += 20;
            lblLive = new Label
            {
                Location = new Point(16, y), Size = new Size(408, 150), AutoSize = false,
                Font = new Font("Consolas", 8.5f), ForeColor = Color.FromArgb(200, 214, 206)
            };
            Controls.Add(lblLive); y += 156;

            btnClose = Btn(hr ? "Zatvori" : "Close", 340, y, 84, 30, Color.FromArgb(60, 71, 64));

            // ---------------- wire up ----------------
            LoadToControls();

            cbCabin.CheckedChanged += (s, e) => { mf.cabinComp.isOn = cbCabin.Checked; mf.cabinComp.Reset(); Store(); };
            cbObs.CheckedChanged += (s, e) => { mf.headObs.isOn = cbObs.Checked; mf.headObs.Reset(); Store(); };
            cbAdvanced.CheckedChanged += (s, e) => nTau.Visible = cbAdvanced.Checked;
            nTau.Visible = false;
            cbAutoLog.CheckedChanged += (s, e) =>
            {
                Properties.VehicleSettings.Default.setSF_autoLog = cbAutoLog.Checked;
                Properties.VehicleSettings.Default.Save();
            };
            nKFast.ValueChanged += (s, e) => { mf.cabinComp.kFast = (double)nKFast.Value; Store(); };
            nFc.ValueChanged += (s, e) => { mf.cabinComp.fc = (double)nFc.Value; mf.cabinComp.Reset(); Store(); };
            nLead.ValueChanged += (s, e) => { mf.cabinComp.lead = (double)nLead.Value; Store(); };
            nTau.ValueChanged += (s, e) => { mf.cabinComp.tauSlow = (double)nTau.Value; mf.cabinComp.Reset(); Store(); };
            nKp.ValueChanged += (s, e) => { mf.headObs.Kp = (double)nKp.Value; Store(); };
            nKi.ValueChanged += (s, e) => { mf.headObs.Ki = (double)nKi.Value; Store(); };
            nImuW.ValueChanged += (s, e) => { mf.headObs.wImu = (double)nImuW.Value; Store(); };

            btnLearn.Click += (s, e) => ToggleLearn();
            btnLog.Click += (s, e) => ToggleLog();
            btnFolder.Click += (s, e) =>
            {
                try { Process.Start(RegistrySettings.logsDirectory); } catch { }
            };
            btnClose.Click += (s, e) => Close();

            poll.Interval = 200;
            poll.Tick += (s, e) => UpdateLive();
            poll.Start();
            FormClosing += (s, e) => { poll.Stop(); if (learning) ToggleLearn(); };
        }

        // ---------------------------------------------------------------- UI helpers
        private Label Lbl(string t, int x, int y, int w, int h, float size = 9f, Color? c = null)
        {
            var l = new Label
            {
                Text = t, Location = new Point(x, y), Size = new Size(w, h), AutoSize = false,
                Font = new Font("Tahoma", size), ForeColor = c ?? Color.Gainsboro
            };
            Controls.Add(l); return l;
        }

        private void Sep(int y)
        {
            Controls.Add(new Label { Location = new Point(8, y), Size = new Size(424, 2), BorderStyle = BorderStyle.Fixed3D });
        }

        private CheckBox Chk(string t, int x, int y)
        {
            var c = new CheckBox
            {
                Text = t, Location = new Point(x, y), AutoSize = true,
                Font = new Font("Tahoma", 9.5f, FontStyle.Bold), ForeColor = Color.White
            };
            Controls.Add(c); return c;
        }

        private NumericUpDown Num(int x, int y, decimal min, decimal max, decimal step, int dec)
        {
            var n = new NumericUpDown
            {
                Location = new Point(x, y), Size = new Size(84, 24), Minimum = min, Maximum = max,
                Increment = step, DecimalPlaces = dec, Font = new Font("Tahoma", 10f),
                BackColor = Color.FromArgb(38, 48, 43), ForeColor = Color.White, TextAlign = HorizontalAlignment.Center
            };
            Controls.Add(n); return n;
        }

        private Button Btn(string t, int x, int y, int w, int h, Color back)
        {
            var b = new Button
            {
                Text = t, Location = new Point(x, y), Size = new Size(w, h), BackColor = back,
                ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Tahoma", 9f, FontStyle.Bold)
            };
            b.FlatAppearance.BorderSize = 0;
            Controls.Add(b); return b;
        }

        private static decimal Clamp(double v, NumericUpDown n)
        {
            decimal d = (decimal)v;
            if (d < n.Minimum) d = n.Minimum;
            if (d > n.Maximum) d = n.Maximum;
            return d;
        }

        private void LoadToControls()
        {
            cbCabin.Checked = mf.cabinComp.isOn;
            nKFast.Value = Clamp(mf.cabinComp.kFast, nKFast);
            nFc.Value = Clamp(mf.cabinComp.fc, nFc);
            nLead.Value = Clamp(mf.cabinComp.lead, nLead);
            nTau.Value = Clamp(mf.cabinComp.tauSlow, nTau);
            cbObs.Checked = mf.headObs.isOn;
            nKp.Value = Clamp(mf.headObs.Kp, nKp);
            nKi.Value = Clamp(mf.headObs.Ki, nKi);
            nImuW.Value = Clamp(mf.headObs.wImu, nImuW);
            cbAutoLog.Checked = Properties.VehicleSettings.Default.setSF_autoLog;
        }

        private void Store() { mf.SaveSensorFusionSettings(); }

        // ---------------------------------------------------------------- learning
        private void ToggleLearn()
        {
            if (!learning)
            {
                mf.cabinComp.learn.Reset(); mf.headObs.learn.Reset();
                mf.cabinComp.learn.running = true; mf.headObs.learn.running = true;
                learning = true; learnStart = DateTime.Now;
                btnLearn.Text = hr ? "Gotovo (izračunaj)" : "Finish (compute)";
                btnLearn.BackColor = Color.FromArgb(46, 139, 79);
                return;
            }

            mf.cabinComp.learn.running = false; mf.headObs.learn.running = false;
            learning = false;
            btnLearn.Text = hr ? "Učenje (k_fast + model)" : "Learning (k_fast + model)";
            btnLearn.BackColor = Color.FromArgb(15, 131, 149);

            double k, r2, leff, q;
            bool okK = mf.cabinComp.learn.Result(out k, out r2);
            bool okL = mf.headObs.learn.Result(out leff, out q) && mf.headObs.learn.HasBothTurns;

            string msg = "";
            msg += okK
                ? string.Format(hr ? "k_fast = {0:F2} m   (sada {1:F2} m, R² {2:F2}, n={3})\n"
                                   : "k_fast = {0:F2} m   (now {1:F2} m, R2 {2:F2}, n={3})\n",
                                Math.Abs(k), mf.cabinComp.kFast, r2, mf.cabinComp.learn.n)
                : (hr ? "k_fast: premalo podataka (treba vožnja po neravnom, 2-4 min)\n"
                      : "k_fast: not enough data (2-4 min on uneven ground)\n");
            msg += okL
                ? string.Format(hr ? "L_eff = {0:F2} m   (sada {1:F2} m, n={2}, okreti L/D {3}/{4})"
                                   : "L_eff = {0:F2} m   (now {1:F2} m, n={2}, turns L/R {3}/{4})",
                                leff, mf.headObs.Leff, mf.headObs.learn.n,
                                mf.headObs.learn.turnsLeft, mf.headObs.learn.turnsRight)
                : (hr ? "L_eff: treba barem jedan okret lijevo i jedan desno"
                      : "L_eff: needs at least one left and one right turn");

            if (!okK && !okL)
            {
                MessageBox.Show(this, msg, hr ? "Učenje" : "Learning");
                return;
            }

            var res = MessageBox.Show(this, msg + (hr ? "\n\nPrihvatiti izračunate vrijednosti?"
                                                     : "\n\nAccept the computed values?"),
                                      hr ? "Učenje" : "Learning", MessageBoxButtons.YesNo);
            if (res != DialogResult.Yes) return;

            if (okK) { mf.cabinComp.kFast = Math.Min(6, Math.Abs(k)); nKFast.Value = Clamp(mf.cabinComp.kFast, nKFast); }
            if (okL) mf.headObs.Leff = leff;
            Store();
        }

        // ---------------------------------------------------------------- log
        private void ToggleLog()
        {
            if (mf.rawLog.isLogging) { mf.rawLog.Stop(); }
            else if (!mf.rawLog.Start())
                MessageBox.Show(this, hr ? "Ne mogu otvoriti log datoteku." : "Cannot open the log file.");

            btnLog.Text = mf.rawLog.isLogging ? (hr ? "■ Log stop" : "■ Log stop") : (hr ? "● Log start" : "● Log start");
            btnLog.BackColor = mf.rawLog.isLogging ? Color.FromArgb(46, 139, 79) : Color.FromArgb(120, 50, 50);
        }

        // ---------------------------------------------------------------- live
        private void UpdateLive()
        {
            var cc = mf.cabinComp;
            var ob = mf.headObs;

            lblStatusTop.Text = string.Format(CultureInfo.InvariantCulture,
                "fix {0} · {1:F1} km/h · roll {2:F2}° · WAS {3:F2}°",
                mf.pn.fixQuality, mf.pn.speed, mf.ahrs.imuRoll, mf.mc.actualSteerAngleDegrees);

            lblLearned.Text = string.Format(CultureInfo.InvariantCulture,
                hr ? "naučeno:  L_eff {0:F2} m    nula WAS {1:+0.00;-0.00}°"
                   : "learned:  L_eff {0:F2} m    WAS zero {1:+0.00;-0.00}°", ob.Leff, ob.biasDeg);

            if (learning)
            {
                lblLearnStatus.Text = string.Format(hr ? "{0:F0} s · n={1} · okreti {2}/{3}"
                                                       : "{0:F0} s · n={1} · turns {2}/{3}",
                    (DateTime.Now - learnStart).TotalSeconds, cc.learn.n,
                    ob.learn.turnsLeft, ob.learn.turnsRight);
            }
            else lblLearnStatus.Text = "—";

            lblLogInfo.Text = mf.rawLog.isLogging
                ? string.Format("{0}  ·  {1:mm\\:ss}  ·  {2} {3}  ·  {4:F1} MB",
                    mf.rawLog.FileName, mf.rawLog.Elapsed, mf.rawLog.rows,
                    hr ? "redaka" : "rows", mf.rawLog.bytes / 1048576.0)
                : (string.IsNullOrEmpty(mf.rawLog.FileName) ? "—" : (hr ? "spremljeno: " : "saved: ") + mf.rawLog.FileName);

            lblLive.Text = string.Format(CultureInfo.InvariantCulture,
                (hr ? "roll sirovi {0,7:F2}°   spori {1,7:F2}°   brzi {2,7:F2}°\n"
                    + "korekcija   {3,7:F1} cm  (spori {4,5:F1} / brzi {5,5:F1})\n"
                    + "bočno prije/poslije filtra {6,6:F1} / {7,6:F1} cm\n"
                    + "\n"
                    + "heading f2f {8,7:F2}°   IMU {9,7:F2}°\n"
                    + "observer    {10,7:F2}°   model {11,6:F2} °/s  IMU {12,6:F2} °/s\n"
                    + "\n"
                    + "kabina {13}    observer {14}    dt {15:F0} ms"
                    : "roll raw {0,7:F2}°   slow {1,7:F2}°   fast {2,7:F2}°\n"
                    + "correction {3,7:F1} cm  (slow {4,5:F1} / fast {5,5:F1})\n"
                    + "lateral before/after {6,6:F1} / {7,6:F1} cm\n"
                    + "\n"
                    + "heading f2f {8,7:F2}°   IMU {9,7:F2}°\n"
                    + "observer    {10,7:F2}°   model {11,6:F2} °/s  IMU {12,6:F2} °/s\n"
                    + "\n"
                    + "cabin {13}    observer {14}    dt {15:F0} ms"),
                mf.ahrs.imuRoll, cc.rollSlowDeg, cc.rollFastDeg,
                cc.corrTotal * 100, cc.corrSlow * 100, cc.corrFast * 100,
                cc.latBefore * 100, cc.latAfter * 100,
                glm.toDegrees(mf.gpsHeading), mf.ahrs.imuHeading,
                glm.toDegrees(ob.psiRad), ob.modelYaw, ob.imuYaw,
                cc.isOn ? "ON " : "off", ob.isOn ? (ob.isInitialized ? "ON " : "init") : "off",
                mf.sfDt * 1000);
        }
    }
}
