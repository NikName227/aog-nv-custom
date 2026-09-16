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
        private Button btnLearn, btnLog, btnFolder, btnClose, btnInfo;
        private Panel panInfo;
        private bool infoOpen;
        private const int WNarrow = 440, WWide = 800;
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

            // ---------------- info (blue circle -> panel on the right) ----------------
            btnInfo = new Button
            {
                Text = "i", Location = new Point(14, y + 2), Size = new Size(26, 26),
                BackColor = Color.FromArgb(30, 110, 200), ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Font = new Font("Tahoma", 11f, FontStyle.Bold)
            };
            btnInfo.FlatAppearance.BorderSize = 0;
            btnInfo.Region = new Region(new System.Drawing.Drawing2D.GraphicsPath());
            using (var gp = new System.Drawing.Drawing2D.GraphicsPath())
            {
                gp.AddEllipse(0, 0, 26, 26);
                btnInfo.Region = new Region(gp);
            }
            Controls.Add(btnInfo);
            Lbl(hr ? "Info o parametrima" : "Parameter info", 46, y + 8, 160, 16, 8.5f, Color.FromArgb(120, 170, 220));

            panInfo = new Panel
            {
                Location = new Point(WNarrow, 0), Size = new Size(WWide - WNarrow, ClientSize.Height),
                BackColor = Color.FromArgb(20, 28, 25), AutoScroll = true, Visible = false
            };
            var lblInfo = new Label
            {
                Text = InfoText(), Location = new Point(10, 8), Size = new Size(WWide - WNarrow - 34, 2100),
                AutoSize = false, Font = new Font("Tahoma", 8.5f), ForeColor = Color.FromArgb(205, 218, 210)
            };
            panInfo.Controls.Add(lblInfo);
            Controls.Add(panInfo);
            btnInfo.Click += (s, e) => ToggleInfo();

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

        // ---------------------------------------------------------------- info panel
        private void ToggleInfo()
        {
            infoOpen = !infoOpen;
            int wide = Math.Min(WWide, Screen.FromControl(this).WorkingArea.Width - 20);
            ClientSize = new Size(infoOpen ? wide : WNarrow, ClientSize.Height);
            panInfo.Size = new Size(ClientSize.Width - WNarrow, ClientSize.Height);
            panInfo.Visible = infoOpen;
            btnInfo.BackColor = infoOpen ? Color.FromArgb(20, 80, 150) : Color.FromArgb(30, 110, 200);
        }

        private string InfoText()
        {
            if (!hr)
                return "Antenna height (Config > Vehicle) and invert roll (Config > Data) come from the existing "
                     + "AOG settings and are NOT entered here. The antenna height is also the lever used for terrain roll.\n\n"
                     + "Each item: what it does - typical range - how to get the value - what if it is wrong.";

            return
"Visina antene (Config → Vozilo) i invert roll (Config → Data) uzimaju se iz postojećih postavki\n" +
"AOG-a i ovdje se NE unose ponovno. Visina antene je ujedno poluga za nagib terena.\n\n" +
"── KOMPENZACIJA KABINE ──────────────────────────────\n\n" +
"k_fast [m]   tipično 0.5–2.5\n" +
"Poluga njihanja kabine: visina antene minus visina osi oko koje se kabina njiše. Govori koliko\n" +
"se antena stvarno pomakne kod brze promjene rolla.\n" +
"Kako: gumb Učenje (2–4 min vožnje po neravnom). Ručno: izmjeri visinu nosača ovjesa kabine i\n" +
"oduzmi je od visine antene.\n" +
"Krivo: premalo → njihanje ostaje nekompenzirano; previše (npr. puna visina antene) → lažni\n" +
"skokovi pozicije i trzaji volana.\n\n" +
"fc [Hz]   0 = filtar isključen, inače 0.5–1.5\n" +
"Granica bočnog filtra. Niže = mirnija linija, ali sporiji odziv na stvarni bočni pomak.\n" +
"Kako: podešava se na terenu. Kreni od 0 (isključen); ako je linija nemirna stavi 1.5, pa 1.0.\n" +
"Krivo: prenisko → trom odziv; previsoko → filtar ne radi ništa.\n\n" +
"lead [0–1]   tipično 0.3–0.7\n" +
"Koliko se kašnjenja filtra vraća natrag. 0 = najmirnije i najsporije, 1 = bez kašnjenja uz\n" +
"slabije glađenje. Nema učinka dok je fc = 0.\n" +
"Kako: podešava se zajedno s fc.\n\n" +
"τ_slow [s]   (napredno)   tipično 1–2\n" +
"Granica između nagiba terena i njihanja kabine. Spori dio rolla množi se punom visinom antene,\n" +
"brzi s k_fast.\n" +
"Kako: ostaviti zadano; kraće τ bolje hvata brz ulazak na nagib, dulje bolje razdvaja kabinu.\n\n" +
"── OBSERVER SMJERA ──────────────────────────────────\n\n" +
"Kp   tipično 0.2–0.5\n" +
"Koliko heading prati GPS smjer; vremenska konstanta je približno 1/Kp (0.3 → oko 3 s).\n" +
"Kako: podešavanje. Niže = mirnije, više = brže poravnanje uz više šuma.\n\n" +
"Ki   tipično 0.002–0.01\n" +
"Brzina kojom observer sam nalazi nulu WAS-a; radi na skali minute.\n" +
"Kako: ostaviti zadano; provjeriti da prikazana nula ne bježi.\n\n" +
"udio IMU-a [0–1]   tipično 0–0.5\n" +
"Koliko IMU brzine zakretanja ulazi u model. Pomaže na ravnom, odmaže u okretu jer se kabina\n" +
"zakreće i sama.\n" +
"Kako: podešavanje; povisiti na ravnom terenu s dobrim IMU-om.\n\n" +
"L_eff [m]   (naučeno)\n" +
"Efektivni međuosovinski razmak; uključuje klizanje kotača pa je veći od mehaničkog.\n" +
"Kako: gumb Učenje, uz okrete u obje strane i barem 30 s ravne vožnje.\n" +
"Bez učenja: mehanički razmak × 1.3–1.5.\n\n" +
"Nula WAS-a [°]   (naučeno)\n" +
"Odstupanje senzora kuta kako ga vidi model; dijelom upija i bočno klizanje na nagibu.\n" +
"Kako: uči se sama tijekom vožnje; provjera: pri ravnoj vožnji WAS treba biti blizu nule.\n\n" +
"U okretu i pri maloj brzini observer sam prelazi s GPS smjera na IMU heading (offset IMU-a uči\n" +
"dok se vozi ravno). Vožnja unatrag se prepoznaje i predznak se okreće.\n\n" +
"── RAW LOG ──────────────────────────────────────────\n\n" +
"Snima svaki fix (10 Hz) u Documents\\AgOpenGPS\\Logs kao AOG_AdvSensorFusion_<datum>_<vrijeme>.csv:\n" +
"sirovi ulazi i svi unutarnji signali lanca. Služi za analizu i ponovno učenje parametara.\n\n" +
"── ŽIVI PRIKAZ ──────────────────────────────────────\n\n" +
"roll sirovi / spori / brzi — sirovi roll i njegova dva dijela (teren i kabina)\n" +
"korekcija — koliko se pozicija pomiče, ukupno i po dijelovima\n" +
"bočno prije/poslije filtra — koliko filtar zapravo skida\n" +
"heading f2f / IMU / observer — tri izvora smjera i trenutni omjer\n" +
"dt — razmak između fixeva, normalno oko 100 ms\n";
        }

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

            double k, r2, leff, lbias, q;
            bool okK = mf.cabinComp.learn.Result(out k, out r2);
            bool okL = mf.headObs.learn.Result(out leff, out lbias, out q)
                       && mf.headObs.learn.HasBothTurns && mf.headObs.learn.HasStraight;

            string msg = "";
            msg += okK
                ? string.Format(hr ? "k_fast = {0:F2} m   (sada {1:F2} m, R² {2:F2}, n={3})\n"
                                   : "k_fast = {0:F2} m   (now {1:F2} m, R2 {2:F2}, n={3})\n",
                                Math.Abs(k), mf.cabinComp.kFast, r2, mf.cabinComp.learn.n)
                : (hr ? "k_fast: premalo podataka (treba vožnja po neravnom, 2-4 min)\n"
                      : "k_fast: not enough data (2-4 min on uneven ground)\n");
            msg += okL
                ? string.Format(hr ? "L_eff = {0:F2} m, nula WAS {1:+0.00;-0.00}°   (sada {2:F2} m / {3:+0.00;-0.00}°, n={4}, okreti L/D {5}/{6})"
                                   : "L_eff = {0:F2} m, WAS zero {1:+0.00;-0.00}°   (now {2:F2} m / {3:+0.00;-0.00}°, n={4}, turns L/R {5}/{6})",
                                leff, lbias, mf.headObs.Leff, mf.headObs.biasDeg, mf.headObs.learn.n,
                                mf.headObs.learn.turnsLeft, mf.headObs.learn.turnsRight)
                : (hr ? "L_eff: treba ravna vožnja (≥30 s) i okret lijevo i desno"
                      : "L_eff: needs straight driving (>=30 s) and a left and a right turn");

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
            if (okL) { mf.headObs.Leff = leff; mf.headObs.biasDeg = lbias; }
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
                    + "kabina {13}    observer {14}    IMU udio {16:P0}    dt {15:F0} ms"
                    : "roll raw {0,7:F2}°   slow {1,7:F2}°   fast {2,7:F2}°\n"
                    + "correction {3,7:F1} cm  (slow {4,5:F1} / fast {5,5:F1})\n"
                    + "lateral before/after {6,6:F1} / {7,6:F1} cm\n"
                    + "\n"
                    + "heading f2f {8,7:F2}°   IMU {9,7:F2}°\n"
                    + "observer    {10,7:F2}°   model {11,6:F2} °/s  IMU {12,6:F2} °/s\n"
                    + "\n"
                    + "cabin {13}    observer {14}    IMU share {16:P0}    dt {15:F0} ms"),
                mf.ahrs.imuRoll, cc.rollSlowDeg, cc.rollFastDeg,
                cc.corrTotal * 100, cc.corrSlow * 100, cc.corrFast * 100,
                cc.latBefore * 100, cc.latAfter * 100,
                glm.toDegrees(mf.gpsHeading), mf.ahrs.imuHeading,
                glm.toDegrees(ob.psiRad), ob.modelYaw, ob.imuYaw,
                cc.isOn ? "ON " : "off", ob.isOn ? (ob.isInitialized ? "ON " : "init") : "off",
                mf.sfDt * 1000, ob.imuWeight);
        }
    }
}
