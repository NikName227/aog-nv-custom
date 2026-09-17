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

        private CheckBox cbCabin, cbModel, cbForce, cbAutoLog;
        private NumericUpDown nKFast, nFc, nLead, nImuW, nFloor, nTau, nLeff, nHFloor, nHTau;
        private Label lblLearned, lblLearnStatus, lblLogInfo, lblLive, lblLiveModel, lblStatusTop;
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
            btnLearn = Btn(hr ? "Učenje (k_fast + L_eff)" : "Learning (k_fast + L_eff)", 20, y, 250, 30, Color.FromArgb(15, 131, 149));
            lblLearnStatus = Lbl("—", 278, y + 8, 150, 16, 8.5f, Color.FromArgb(160, 180, 170)); y += 36;

            // ---------------- kinematic model ----------------
            Sep(y - 4);
            cbModel = Chk(hr ? "KINEMATIČKI MODEL" : "KINEMATIC MODEL", 12, y);
            cbForce = Chk(hr ? "force on (test)" : "force on (test)", 300, y + 1);
            cbForce.Font = new Font("Tahoma", 8f);
            cbForce.ForeColor = Color.FromArgb(220, 150, 70);
            cbForce.Width = 130; y += 24;
            Lbl(hr ? "WAS ◀──────▶ IMU" : "WAS ◀──────▶ IMU", 20, y + 3, 130, 16);
            nImuW = Num(156, y, 0, 0.6m, 0.05m, 2);
            y += 28;
            Lbl("floor", 20, y + 3, 40, 16);
            nFloor = Num(62, y, 0.05m, 0.90m, 0.05m, 2);
            Lbl("τ [s]", 152, y + 3, 36, 16);
            nTau = Num(190, y, 0.1m, 2.0m, 0.1m, 1);
            Lbl("L_eff [m]", 286, y + 3, 58, 16);
            nLeff = Num(348, y, 2.0m, 8.0m, 0.1m, 2); y += 26;
            Lbl("h_floor", 20, y + 3, 50, 16);
            nHFloor = Num(72, y, 0.05m, 0.90m, 0.05m, 2);
            Lbl("h_τ [s]", 152, y + 3, 44, 16);
            nHTau = Num(198, y, 0.1m, 3.0m, 0.1m, 1); y += 26;
            Lbl(hr ? "ugađanje na terenu — τ/floor = efektivna konstanta (gore pozicija, dolje smjer)"
                   : "field tuning — tau/floor = effective time constant (position above, heading below)",
                20, y, 406, 14, 8f, Color.FromArgb(140, 160, 150)); y += 20;
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
            Lbl(hr ? "kompenzacija kabine" : "cabin compensation", 18, y, 200, 14, 8f,
                Color.FromArgb(130, 155, 142)); y += 16;
            lblLive = new Label
            {
                Location = new Point(16, y), Size = new Size(408, 54), AutoSize = false,
                Font = new Font("Consolas", 8.5f), ForeColor = Color.FromArgb(200, 214, 206)
            };
            Controls.Add(lblLive); y += 58;
            Lbl(hr ? "model" : "model", 18, y, 200, 14, 8f, Color.FromArgb(130, 155, 142)); y += 16;
            lblLiveModel = new Label
            {
                Location = new Point(16, y), Size = new Size(408, 86), AutoSize = false,
                Font = new Font("Consolas", 8.5f), ForeColor = Color.FromArgb(200, 214, 206)
            };
            Controls.Add(lblLiveModel); y += 90;

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
            cbModel.CheckedChanged += (s, e) => { mf.kinModel.isOn = cbModel.Checked; mf.kinModel.Reset(); Store(); };
            //force is a test aid: never stored, so it cannot be left on at a customer
            cbForce.CheckedChanged += (s, e) => { mf.kinModel.forceOn = cbForce.Checked; Store(); };
            cbAutoLog.CheckedChanged += (s, e) =>
            {
                Properties.VehicleSettings.Default.setSF_autoLog = cbAutoLog.Checked;
                Properties.VehicleSettings.Default.Save();
            };
            nKFast.ValueChanged += (s, e) => { mf.cabinComp.kFast = (double)nKFast.Value; Store(); };
            nFc.ValueChanged += (s, e) => { mf.cabinComp.fc = (double)nFc.Value; mf.cabinComp.Reset(); Store(); };
            nLead.ValueChanged += (s, e) => { mf.cabinComp.lead = (double)nLead.Value; Store(); };
            nImuW.ValueChanged += (s, e) => { mf.kinModel.imuShare = (double)nImuW.Value; Store(); };
            nFloor.ValueChanged += (s, e) => { mf.kinModel.posFloor = (double)nFloor.Value; Store(); };
            nTau.ValueChanged += (s, e) => { mf.kinModel.posTau = (double)nTau.Value; Store(); };
            nLeff.ValueChanged += (s, e) => { mf.kinModel.leff = (double)nLeff.Value; Store(); };
            nHFloor.ValueChanged += (s, e) => { mf.kinModel.hdgFloor = (double)nHFloor.Value; Store(); };
            nHTau.ValueChanged += (s, e) => { mf.kinModel.hdgTau = (double)nHTau.Value; Store(); };

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
            cbModel.Checked = mf.kinModel.isOn;
            cbForce.Checked = mf.kinModel.forceOn;
            nImuW.Value = Clamp(mf.kinModel.imuShare, nImuW);
            nFloor.Value = Clamp(mf.kinModel.posFloor, nFloor);
            nTau.Value = Clamp(mf.kinModel.posTau, nTau);
            nLeff.Value = Clamp(mf.kinModel.leff, nLeff);
            nHFloor.Value = Clamp(mf.kinModel.hdgFloor, nHFloor);
            nHTau.Value = Clamp(mf.kinModel.hdgTau, nHTau);
            cbAutoLog.Checked = Properties.VehicleSettings.Default.setSF_autoLog;
        }

        private void Store()
        {
            mf.SaveSensorFusionSettings();
            // a mark in the raw log, so a parameter change is visible in the replay/analysis
            if (mf.rawLog.isLogging)
                mf.rawLog.Mark(string.Format(CultureInfo.InvariantCulture,
                    "SET cab={0} k_fast={1:F2} fc={2:F2} lead={3:F2} model={4} force={5} imuW={6:F2} " +
                    "floor={7:F2} tau={8:F1} Leff={9:F2} hfloor={10:F2} htau={11:F1}",
                    mf.cabinComp.isOn ? 1 : 0, mf.cabinComp.kFast, mf.cabinComp.fc, mf.cabinComp.lead,
                    mf.kinModel.isOn ? 1 : 0, mf.kinModel.forceOn ? 1 : 0, mf.kinModel.imuShare,
                    mf.kinModel.posFloor, mf.kinModel.posTau, mf.kinModel.leff,
                    mf.kinModel.hdgFloor, mf.kinModel.hdgTau));
        }

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
                     + "AOG settings and are NOT entered here.\n\n"
                     + "Each item: what it does - typical range - how to get the value - what if it is wrong.";

            return
"Visina antene (Config → Vozilo) i invert roll (Config → Data) uzimaju se iz postojećih\n" +
"postavki AOG-a i ovdje se NE unose ponovno. Visina antene je ujedno poluga za nagib terena.\n" +
"\n" +
"── KOMPENZACIJA KABINE ──────────────────────────────\n" +
"\n" +
"k_fast [m]   tipično 0.5–2.5\n" +
"Poluga njihanja kabine: visina antene minus visina osi oko koje se kabina njiše. Govori\n" +
"koliko se antena stvarno pomakne kod brze promjene rolla.\n" +
"Kako: gumb Učenje (2–4 min vožnje po neravnom). Ručno: izmjeri visinu nosača ovjesa\n" +
"kabine i oduzmi je od visine antene.\n" +
"Krivo: premalo → njihanje ostaje nekompenzirano; previše → lažni skokovi pozicije i\n" +
"trzaji volana.\n" +
"Izmjereno: poluga varira i unutar jedne vožnje (0.66–1.92 m), pa je 1.40 kompromis, ne\n" +
"istina. Greška u oba smjera ostaje ispod ±1.2 cm/°, naspram −4.9 cm/° bez kompenzacije.\n" +
"\n" +
"fc [Hz]   0 = filtar isključen, inače 0.2–1.0\n" +
"Granica bočnog filtra. Niže = mirnija linija, ali sporiji odziv na stvarni bočni pomak.\n" +
"Kako: kreni od 0. Ako je linija nemirna, stavi 0.5 pa 0.3 — i OBAVEZNO lead na 0.\n" +
"Krivo: prenisko → trom odziv (fc 0.15 unosi 1.06 s kašnjenja); previsoko → ne radi ništa.\n" +
"\n" +
"lead [0–1]   preporučeno 0 kad je fc uključen\n" +
"Koliko se kašnjenja filtra vraća natrag. Nema učinka dok je fc = 0.\n" +
"Izmjereno: sa zadanih 0.70 filtar vrati 70 % onoga što je uklonio — skok 8.2 cm naspram\n" +
"5.9 cm uz lead 0. Ako uključiš fc, stavi lead na 0.\n" +
"\n" +
"τ_slow je hardkodiran na 1.0 s — razdvaja nagib terena (puna visina antene) od njihanja\n" +
"kabine (k_fast). Nije se pokazao kao nešto što se ugađa na terenu.\n" +
"\n" +
"── KINEMATIČKI MODEL ────────────────────────────────\n" +
"\n" +
"Model predviđa gdje traktor MOŽE biti iz kuta kotača i brzine, pa GPS-u vjeruje onoliko\n" +
"koliko je mjerenje fizički moguće. Skok koji vozilo ne može napraviti dobiva malu težinu;\n" +
"stvarno gibanje prolazi bez kašnjenja jer dolazi iz predikcije.\n" +
"\n" +
"Slider WAS ◀──▶ IMU [0–0.6]   tipično 0.25\n" +
"Koliko brzine zakretanja model uzima iz žiroskopa umjesto iz kuta kotača:\n" +
"   zakretanje = (1 − slider) · v·tan(WAS − nula)/L_eff  +  slider · yaw iz IMU-a\n" +
"\n" +
"Zašto postoji: s prednjim priključkom (npr. tanjurača) prednji kraj gura i traktor skrene\n" +
"manje nego što WAS kaže. To je greška predikcije i ispravlja se upravo ovdje.\n" +
"\n" +
"Kako: 0 na traktoru bez prednjeg priključka. Podigni ako u živom prikazu red „zakretanje\"\n" +
"stalno pokazuje omjer manji od ~0.8× — model tada zaostaje za stvarnim zakretanjem.\n" +
"\n" +
"Krivo: previsoko → u model ulazi zakretanje same kabine. IMU je na kabini i mjeri 1.15×\n" +
"kinematičkog limita, a roll to ne objašnjava (r² 0.045), pa se ne može oduzeti. Zato je\n" +
"gornja granica 0.6, a ne 1.0.\n" +
"\n" +
"Napomena: stalno klizanje hvata L_eff (uči se). Slider pokriva samo promjenjivi dio —\n" +
"tvrdo naspram mekog tla, dubina zahvata.\n" +
"\n" +
"── ŠTO MODEL RAČUNA SAM ─────────────────────────────\n" +
"\n" +
"Ovo se NE unosi. Izvedeno je iz geometrije vozila i iz mjerenja, pa nema što ugađati:\n" +
"\n" +
"prag pozicije 0.079 m\n" +
"  Izmjerena amplituda njihanja kabine (p99 ±6.6 cm) + 20 % rezerve. Ispod toga je\n" +
"  njihanje, iznad je stvarni pomak. Ovjes fizički ne dopušta više.\n" +
"prag smjera  v·tan(WAS)/L_eff · τ × 1.20\n" +
"  Kinematički moguća promjena smjera kroz horizont korekcije.\n" +
"τ pozicije 1.0 s · τ smjera 2.0 s\n" +
"yaw iz IMU-a\n" +
"  Nagib pravca kroz zadnjih 5 uzoraka (najmanji kvadrati). Dvije točke bi dale 0.408 °/s\n" +
"  šuma na signal od 0.56 °/s — 70 % šuma. Pet točaka: 0.091 °/s uz 0.20 s kašnjenja.\n" +
"nula WAS-a\n" +
"  Uči se svaku sesiju (Ki 0.15), i to SAMO dok je zakretanje ispod 1 °/s — iznad toga\n" +
"  odnos WAS → zakretanje nije siguran (proklizavanje, Ackermann). Sidro je AOG-ov heading,\n" +
"  ne modelov vlastiti (inače bi procjena bila kružna). NE sprema se jer se Keya\n" +
"  inicijalizira pri svakom paljenju. Traje ~12 s vožnje naprijed.\n" +
"L_eff   NIJE kritičan za model\n" +
"  Iz gumba Učenje (u odjeljku kompenzacije kabine, uči k_fast i L_eff zajedno).\n" +
"  Izmjereno: greška od −30 % do +60 % mijenja XTE za 0.1–1.5 cm, jer L_eff samo SKALIRA\n" +
"  predviđeno zakretanje, a korekcija smjera tu skalu ispravi. Zadanih 4.20 m radi na\n" +
"  praktički svakom traktoru i bez učenja.\n" +
"  (Nula WAS-a je druga priča — ona je konstantni pomak i mora se procjenjivati.)\n" +
"  Vrata od 1 °/s usput razbijaju degeneraciju L_eff/nula: nula se uči samo u ravnoj\n" +
"  vožnji, gdje zakretanje je ~0 pa skala L_eff množi gotovo ništa.\n" +
"\n" +
"── KAD JE MODEL PASIVAN ─────────────────────────────\n" +
"\n" +
"Pasivno znači: model je isključen kao izvor, autosteer dobiva AOG-ov današnji lanac. Model\n" +
"se i dalje računa u pozadini i puni se tim podacima, pa preuzima bez skoka.\n" +
"\n" +
"  • autosteer isključen — vozač vozi, model samo prati\n" +
"  • WAS iznad 10° — okret. WAS je pouzdan unutar ±10°; iznad toga ulaze greške od\n" +
"    Ackermannove geometrije i montaže senzora, a model stoji ili pada na WAS-u.\n" +
"    Vraća se ispod 8° nakon 1 s (histereza), rampa 3 s.\n" +
"  • uči nulu — prvih ~12 s vožnje u svakoj sesiji\n" +
"  • reset (razilaženje) — razlika veća od 25 cm dulje od 2 s znači da je MODEL kriv,\n" +
"    pa se odbacuje i postavlja nanovo iz GPS-a\n" +
"\n" +
"force on (test)\n" +
"  Drži model aktivnim bez obzira na sve gore — i s isključenim autosteerom, i u okretu,\n" +
"  i prije nego je nula WAS-a ustaljena. Služi za testiranje s data replayom, gdje\n" +
"  autosteer nije uključen pa model inače ne bi ni krenuo.\n" +
"  NE sprema se — pri svakom pokretanju AOG-a je isključena, da ne ostane slučajno.\n" +
"  Nadzor razilaženja radi i pod force: bez njega bi model koji luta izgledao kao da radi.\n" +
"  Dok je uključena, gore piše ⚠ FORCE ON i u živom prikazu stanje je AKTIVNO (FORCE).\n" +
"\n" +
"── RAW LOG ──────────────────────────────────────────\n" +
"\n" +
"Snima svaki fix (10 Hz) u Documents\\AgOpenGPS\\Logs kao\n" +
"AOG_AdvSensorFusion_<datum>_<vrijeme>.csv: sirovi ulazi i svi unutarnji signali lanca.\n" +
"Kvačica „auto\" pokreće log pri sljedećem pokretanju AOG-a i ne gasi ga sama.\n" +
"\n" +
"── ŽIVI PRIKAZ: KOMPENZACIJA KABINE ─────────────────\n" +
"\n" +
"roll sirovi / spori / brzi — sirovi roll i njegova dva dijela (teren i kabina)\n" +
"korekcija — koliko se pozicija pomiče, ukupno i po dijelovima\n" +
"bočno prije/poslije filtra — koliko filtar zapravo skida; „—\" znači fc = 0\n" +
"\n" +
"── ŽIVI PRIKAZ: MODEL ───────────────────────────────\n" +
"\n" +
"stanje   PASIVNO / PRIJELAZ / AKTIVNO\n" +
"  Tko trenutno vodi. Kad je pasivno, piše i razlog.\n" +
"razlika   model − GPS, u cm\n" +
"  Jedina brojka po kojoj se vidi je li model zdrav. Mali i miran = model prati. Raste i\n" +
"  ostaje velik = model luta i slijedi tvrdi reset na GPS.\n" +
"odbačeno   koliko je vaga odrezala, u %\n" +
"  Pokazuje da model radi. Na neravnom skoči preko 90 %, na glatkom padne blizu nule.\n" +
"nula WAS-a   procjena i je li ustaljena\n" +
"  Ako vrijednost stalno bježi, WAS ima problem koji se inače ne bi primijetio.\n" +
"zakretanje   °/s i omjer prema kinematici\n" +
"  Ispod 1× znači da je smjer unutar fizički mogućeg. Iznad — nešto propušta šum.\n" +
"brzina   iznos, izvor i dt\n" +
"  Izvor je VTG iznad 2 km/h, ispod toga pozicija (VTG ondje pokazuje do 0.73 m/s lažne\n" +
"  brzine). dt je razmak između fixeva, normalno oko 100 ms.\n" +
"\n";
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
                "fix {0} · {1:F1} km/h · roll {2:F2}° · WAS {3:F2}°{4}",
                mf.pn.fixQuality, mf.pn.speed, mf.ahrs.imuRoll, mf.mc.actualSteerAngleDegrees,
                mf.kinModel.forceOn ? "        ⚠ FORCE ON" : "");
            lblStatusTop.ForeColor = mf.kinModel.forceOn
                ? Color.FromArgb(230, 150, 60) : Color.FromArgb(150, 200, 180);

            lblLearned.Text = hr ? "nula WAS-a se više ne uči — trovala ju je buka sidra (REQ 2026-09-17-2)"
                                 : "WAS zero learning removed — it was poisoned by anchor noise";

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
                (hr ? "roll  sirovi {0,7:F2}°   spori {1,7:F2}°   brzi {2,7:F2}°\n"
                    + "korekcija    {3,7:F1} cm   (spori {4,5:F1} / brzi {5,5:F1})\n"
                    + "bočno prije/poslije filtra  {6,6} / {7,6} cm"
                    : "roll  raw {0,7:F2}°   slow {1,7:F2}°   fast {2,7:F2}°\n"
                    + "correction {3,7:F1} cm   (slow {4,5:F1} / fast {5,5:F1})\n"
                    + "lateral before/after filter {6,6} / {7,6} cm"),
                mf.ahrs.imuRoll, cc.rollSlowDeg, cc.rollFastDeg,
                cc.corrTotal * 100, cc.corrSlow * 100, cc.corrFast * 100,
                LatTxt(cc.latBefore), LatTxt(cc.latAfter));

            var km = mf.kinModel;
            string state;
            if (!km.isOn) state = hr ? "ISKLJUČEN" : "OFF";
            else if (km.activeFrac >= 0.999)
                state = km.forceOn ? (hr ? "AKTIVNO  (FORCE)" : "ACTIVE  (FORCE)")
                                   : (hr ? "AKTIVNO  (rampa 100 %)" : "ACTIVE  (ramp 100 %)");
            else if (km.activeFrac <= 0.001)
                state = (hr ? "PASIVNO" : "PASSIVE") + (km.passiveReason.Length > 0 ? "  — " + km.passiveReason : "");
            else state = string.Format(hr ? "PRIJELAZ  (rampa {0:F0} %)" : "HANDOVER  (ramp {0:F0} %)",
                                       km.activeFrac * 100);

            double ratio = km.kinLimit > 0.02 ? Math.Abs(km.usedYaw) / km.kinLimit : 0;
            lblLiveModel.Text = string.Format(CultureInfo.InvariantCulture,
                (hr ? "stanje      {0}\n"
                    + "razlika     {1,6:F1} cm      odbačeno {2,3:F0} %\n"
                    + "sidro       {3,6:+0.00;-0.00}°       τ_ef {4}\n"
                    + "zakretanje  {5,6:F2} °/s     kinematika {6:F2} → {7:F2}×\n"
                    + "brzina      {8,6:F1} km/h    {9}   dt {10:F0} ms"
                    : "state       {0}\n"
                    + "innovation  {1,6:F1} cm      rejected {2,3:F0} %\n"
                    + "anchor      {3,6:+0.00;-0.00}°       tau_ef {4}\n"
                    + "yaw rate    {5,6:F2} °/s     kinematic {6:F2} → {7:F2}×\n"
                    + "speed       {8,6:F1} km/h    {9}   dt {10:F0} ms"),
                state, km.innovCm, km.rejectPct, km.anchorDeg,
                string.Format(CultureInfo.InvariantCulture, "{0:F1} s",
                    km.posTau / Math.Max(0.02, km.rejectPct < 99.9 ? 1.0 - km.rejectPct / 100.0 : 0.02)),
                km.usedYaw, km.kinLimit, ratio, km.speedUsed * 3.6,
                km.vtgShare > 0.99 ? "VTG" : (km.vtgShare < 0.01 ? (hr ? "pozicija" : "position")
                    : string.Format(hr ? "VTG {0:F0} %" : "VTG {0:F0} %", km.vtgShare * 100)),
                mf.sfDt * 1000);
        }

        /// <summary>Lateral offset in cm, or a dash while the lateral filter is not running.</summary>
        private static string LatTxt(double m)
        {
            return double.IsNaN(m) ? "—" : (m * 100).ToString("F1", CultureInfo.InvariantCulture);
        }
    }
}
