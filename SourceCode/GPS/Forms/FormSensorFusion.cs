// F10 Advanced Sensor Fusion (NV custom) — Wizards dialog.
// The kinematic model, a raw logger and a live view of the chain. Two sliders is all a normal
// install shows; the tuning parameters appear only when the install config carries the debug
// flag (REQ 2026-09-18-1), so a machine can be serviced without a new build.
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

        private CheckBox cbModel, cbForce, cbAutoLog;
        private NumericUpDown nATau, nFloor, nTau, nLeff, nHFloor, nHTau, nMixPos, nMixHdg;
        private Label lblLogInfo, lblLiveModel, lblStatusTop;
        private Button btnLog, btnFolder, btnClose, btnInfo, btnDocs;
        private Panel panInfo;
        private bool infoOpen;
        private readonly bool dbg = CFeatureLock.IsDebug;
        private const int WNarrow = 440, WWide = 800;

        public FormSensorFusion(FormGPS callingForm)
        {
            mf = callingForm;
            hr = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "hr";

            Name = "FormSensorFusion";
            Text = "Advanced Sensor Fusion";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            //height is set at the end of the constructor - the service-mode rows change it
            BackColor = Color.FromArgb(24, 33, 29);
            ForeColor = Color.Gainsboro;
            Font = new Font("Tahoma", 9f);

            int y = 8;
            lblStatusTop = Lbl("", 12, y, 416, 18, 9f, Color.FromArgb(150, 200, 180)); y += 22;

            // ---------------- kinematic model ----------------
            Sep(y - 2);
            cbModel = Chk(hr ? "KINEMATIČKI MODEL" : "KINEMATIC MODEL", 12, y); y += 24;
            Lbl(hr ? "udio modela — pozicija" : "model share — position", 20, y + 3, 150, 16);
            nMixPos = Num(176, y, 0, 1.0m, 0.05m, 2); y += 26;
            Lbl(hr ? "udio modela — smjer" : "model share — heading", 20, y + 3, 150, 16);
            nMixHdg = Num(176, y, 0, (decimal)CKinematicModel.MixHdgMax, 0.05m, 2); y += 28;

            // Service mode only. Built, not merely hidden, so the layout closes up without a gap
            // and a customer install cannot reach these at all.
            if (dbg)
            {
                cbForce = Chk("force on (test)", 300, y - 52);
                cbForce.Font = new Font("Tahoma", 8f);
                cbForce.ForeColor = Color.FromArgb(220, 150, 70);
                cbForce.Width = 130;
                Lbl("floor", 20, y + 3, 40, 16);
                nFloor = Num(62, y, 0.05m, 0.90m, 0.05m, 2);
                Lbl("τ [s]", 152, y + 3, 36, 16);
                nTau = Num(190, y, 0.1m, 2.0m, 0.1m, 1);
                Lbl("L_eff [m]", 286, y + 3, 58, 16);
                nLeff = Num(348, y, 0m, 8.0m, 0.1m, 2); y += 26;
                Lbl("h_floor", 20, y + 3, 50, 16);
                nHFloor = Num(72, y, 0.05m, 0.90m, 0.05m, 2);
                Lbl("h_τ [s]", 152, y + 3, 44, 16);
                nHTau = Num(198, y, 0.1m, 3.0m, 0.1m, 1);
                Lbl("sidro τ", 292, y + 3, 50, 16);
                nATau = Num(348, y, 0.5m, 6.0m, 0.5m, 1); y += 26;
                Lbl(hr ? "SERVISNI MOD — τ/floor = efektivna konstanta; L_eff 0 = iz postavki vozila"
                       : "SERVICE MODE — tau/floor = effective time constant; L_eff 0 = from vehicle settings",
                    20, y, 430, 14, 8f, Color.FromArgb(220, 150, 70)); y += 20;
            }
            y += 4;

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
            lblLiveModel = new Label
            {
                Location = new Point(16, y), Size = new Size(416, 102), AutoSize = false,
                Font = new Font("Consolas", 8.5f), ForeColor = Color.FromArgb(200, 214, 206)
            };
            Controls.Add(lblLiveModel); y += 106;

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
            Lbl(hr ? "Info o parametrima" : "Parameter info", 46, y + 8, 122, 16, 8.5f, Color.FromArgb(120, 170, 220));
            btnDocs = Btn(hr ? "Dokumentacija" : "Documentation", 174, y, 124, 30, Color.FromArgb(40, 84, 96));
            btnDocs.Font = new Font("Tahoma", 8.5f);
            btnDocs.Click += (s, e) => OpenDocs();

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
            ClientSize = new Size(WNarrow, y + 44);
            btnInfo.Click += (s, e) => ToggleInfo();

            // ---------------- wire up ----------------
            LoadToControls();

            cbModel.CheckedChanged += (s, e) => { mf.kinModel.isOn = cbModel.Checked; mf.kinModel.Reset(); Store(); };
            //force is a test aid: never stored, so it cannot be left on at a customer
            if (dbg) cbForce.CheckedChanged += (s, e) => { mf.kinModel.forceOn = cbForce.Checked; Store(); };
            cbAutoLog.CheckedChanged += (s, e) =>
            {
                Properties.VehicleSettings.Default.setSF_autoLog = cbAutoLog.Checked;
                Properties.VehicleSettings.Default.Save();
            };
            nMixPos.ValueChanged += (s, e) => { mf.kinModel.mixPos = (double)nMixPos.Value; Store(); };
            nMixHdg.ValueChanged += (s, e) => { mf.kinModel.mixHdg = (double)nMixHdg.Value; Store(); };
            if (dbg)
            {
                nFloor.ValueChanged += (s, e) => { mf.kinModel.posFloor = (double)nFloor.Value; Store(); };
                nTau.ValueChanged += (s, e) => { mf.kinModel.posTau = (double)nTau.Value; Store(); };
                nLeff.ValueChanged += (s, e) => { mf.kinModel.leff = (double)nLeff.Value; Store(); };
                nHFloor.ValueChanged += (s, e) => { mf.kinModel.hdgFloor = (double)nHFloor.Value; Store(); };
                nHTau.ValueChanged += (s, e) => { mf.kinModel.hdgTau = (double)nHTau.Value; Store(); };
                nATau.ValueChanged += (s, e) => { mf.kinModel.anchorTau = (double)nATau.Value; Store(); };
            }

            btnLog.Click += (s, e) => ToggleLog();
            btnFolder.Click += (s, e) =>
            {
                try { Process.Start(RegistrySettings.logsDirectory); } catch { }
            };
            btnClose.Click += (s, e) => Close();

            poll.Interval = 200;
            poll.Tick += (s, e) => UpdateLive();
            poll.Start();
            FormClosing += (s, e) => poll.Stop();
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
            cbModel.Checked = mf.kinModel.isOn;
            if (dbg) cbForce.Checked = mf.kinModel.forceOn;
            nMixPos.Value = Clamp(mf.kinModel.mixPos, nMixPos);
            nMixHdg.Value = Clamp(mf.kinModel.mixHdg, nMixHdg);
            if (dbg)
            {
                nFloor.Value = Clamp(mf.kinModel.posFloor, nFloor);
                nTau.Value = Clamp(mf.kinModel.posTau, nTau);
                nLeff.Value = Clamp(mf.kinModel.leff, nLeff);
                nHFloor.Value = Clamp(mf.kinModel.hdgFloor, nHFloor);
                nHTau.Value = Clamp(mf.kinModel.hdgTau, nHTau);
                nATau.Value = Clamp(mf.kinModel.anchorTau, nATau);
            }
            cbAutoLog.Checked = Properties.VehicleSettings.Default.setSF_autoLog;
        }

        private void Store()
        {
            mf.SaveSensorFusionSettings();
            // a mark in the raw log, so a parameter change is visible in the replay/analysis
            if (mf.rawLog.isLogging)
                mf.rawLog.Mark(string.Format(CultureInfo.InvariantCulture,
                    "SET model={0} force={1} mixpos={2:F2} mixhdg={3:F2} " +
                    "floor={4:F2} tau={5:F1} Leff={6:F2} hfloor={7:F2} htau={8:F1} atau={9:F1} dbg={10}",
                    mf.kinModel.isOn ? 1 : 0, mf.kinModel.forceOn ? 1 : 0,
                    mf.kinModel.mixPos, mf.kinModel.mixHdg,
                    mf.kinModel.posFloor, mf.kinModel.posTau, mf.kinModel.leff,
                    mf.kinModel.hdgFloor, mf.kinModel.hdgTau, mf.kinModel.anchorTau,
                    dbg ? 1 : 0));
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

        /// <summary>Opens the shipped HTML documentation next to the exe.</summary>
        private void OpenDocs()
        {
            try
            {
                string path = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath),
                                           "Docs", "sensor-fusion.html");
                if (File.Exists(path)) Process.Start(path);
                else MessageBox.Show(this, (hr ? "Dokumentacija nije nađena:\n" : "Documentation not found:\n") + path);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message); }
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

            lblStatusTop.Text = string.Format(CultureInfo.InvariantCulture,
                "fix {0} · {1:F1} km/h · roll {2:F2}° · WAS {3:F2}°{4}",
                mf.pn.fixQuality, mf.pn.speed, mf.ahrs.imuRoll, mf.mc.actualSteerAngleDegrees,
                mf.kinModel.forceOn ? "        ⚠ FORCE ON" : "");
            lblStatusTop.ForeColor = mf.kinModel.forceOn
                ? Color.FromArgb(230, 150, 60) : Color.FromArgb(150, 200, 180);


            lblLogInfo.Text = mf.rawLog.isLogging
                ? string.Format("{0}  ·  {1:mm\\:ss}  ·  {2} {3}  ·  {4:F1} MB",
                    mf.rawLog.FileName, mf.rawLog.Elapsed, mf.rawLog.rows,
                    hr ? "redaka" : "rows", mf.rawLog.bytes / 1048576.0)
                : (string.IsNullOrEmpty(mf.rawLog.FileName) ? "—" : (hr ? "spremljeno: " : "saved: ") + mf.rawLog.FileName);

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
                    + "ΔWAS        {11,6:+0.00;-0.00} °/s   L_eff {12:F2} m {13}\n"
                    + "brzina      {8,6:F1} km/h    {9}   dt {10:F0} ms"
                    : "state       {0}\n"
                    + "innovation  {1,6:F1} cm      rejected {2,3:F0} %\n"
                    + "anchor      {3,6:+0.00;-0.00}°       tau_ef {4}\n"
                    + "yaw rate    {5,6:F2} °/s     kinematic {6:F2} → {7:F2}×\n"
                    + "dWAS        {11,6:+0.00;-0.00} deg/s L_eff {12:F2} m {13}\n"
                    + "speed       {8,6:F1} km/h    {9}   dt {10:F0} ms"),
                state, km.innovCm, km.rejectPct, km.anchorDeg,
                string.Format(CultureInfo.InvariantCulture, "{0:F1} s",
                    km.posTau / Math.Max(0.02, km.rejectPct < 99.9 ? 1.0 - km.rejectPct / 100.0 : 0.02)),
                km.usedYaw, km.kinLimit, ratio, km.speedUsed * 3.6,
                km.vtgShare > 0.99 ? "VTG" : (km.vtgShare < 0.01 ? (hr ? "pozicija" : "position")
                    : string.Format(hr ? "VTG {0:F0} %" : "VTG {0:F0} %", km.vtgShare * 100)),
                mf.sfDt * 1000,
                km.dWasYaw, km.leffUsed,
                km.leff > 0.1 ? (hr ? "(ručno)" : "(manual)") : (hr ? "(iz vozila)" : "(vehicle)"));
        }

        /// <summary>Lateral offset in cm, or a dash while the lateral filter is not running.</summary>
        private static string LatTxt(double m)
        {
            return double.IsNaN(m) ? "—" : (m * 100).ToString("F1", CultureInfo.InvariantCulture);
        }
    }
}
