// F07 Integration Test (NV custom) — Wizards dialog. Two guided drives (straight
// multi-speed / circle constant-angle), timer-sampled from FormGPS, analysed by
// CIntegrationTest, and written as a self-contained HTML report into the AOG
// documents folder (\Reports). Code-built UI (no designer) to keep it self
// contained. Gated behind the F06 'integrationtest' unlock.
using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace AgOpenGPS
{
    public class FormIntegrationTest : Form
    {
        private readonly FormGPS mf;
        private readonly bool hr;
        private readonly CIntegrationTest test = new CIntegrationTest();
        private readonly Timer poll = new Timer();

        private Label lblTitle, lblHintTop, lblHintBig, lblHintSub;
        private ProgressBar bar;
        private Button btnStraight, btnCircle, btnFinish, btnClose;

        public FormIntegrationTest(FormGPS callingForm)
        {
            mf = callingForm;
            hr = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "hr";

            Name = "FormIntegrationTest";
            Text = hr ? "Integration Test" : "Integration Test";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(440, 348);
            BackColor = Color.FromArgb(24, 33, 29);
            ForeColor = Color.Gainsboro;
            Font = new Font("Tahoma", 9.75f);

            lblTitle = new Label
            {
                Text = hr ? "Validator senzorike — odaberi test" : "Sensor validator — choose a test",
                AutoSize = false, Location = new Point(16, 12), Size = new Size(398, 24),
                Font = new Font("Tahoma", 11f, FontStyle.Bold), ForeColor = Color.White
            };

            btnStraight = MakeBtn(hr ? "Test pravac\n(3 · 7 · 10 km/h)" : "Straight test\n(3 · 7 · 10 km/h)",
                                  new Point(16, 48), new Size(196, 78), Color.FromArgb(15, 131, 149));
            btnCircle = MakeBtn(hr ? "Test kružnica\n(konstantan kut)" : "Circle test\n(constant angle)",
                                new Point(220, 48), new Size(196, 78), Color.FromArgb(18, 90, 110));
            btnStraight.Click += (s, e) => StartTest(ITMode.Straight);
            btnCircle.Click += (s, e) => StartTest(ITMode.Circle);

            // 3-line driver hint: action / BIG value (target km/h) / progress — big + centered so it reads on the tablet while driving
            lblHintTop = new Label
            {
                Text = hr ? "Preporuka: prvo pravac (geometrija), pa kružnica (heading)."
                          : "Recommended: straight first (geometry), then circle (heading).",
                AutoSize = false, Location = new Point(16, 126), Size = new Size(408, 26),
                TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Tahoma", 11.5f),
                ForeColor = Color.FromArgb(200, 214, 206)
            };
            lblHintBig = new Label
            {
                Text = "", AutoSize = false, Location = new Point(16, 152), Size = new Size(408, 44),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Tahoma", 26f, FontStyle.Bold), ForeColor = Color.FromArgb(90, 200, 220)
            };
            lblHintSub = new Label
            {
                Text = "", AutoSize = false, Location = new Point(16, 198), Size = new Size(408, 20),
                TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Tahoma", 9.5f),
                ForeColor = Color.FromArgb(140, 160, 150)
            };

            bar = new ProgressBar { Location = new Point(16, 224), Size = new Size(408, 16), Minimum = 0, Maximum = 100, Visible = false };

            btnFinish = MakeBtn(hr ? "Završi + report" : "Finish + report", new Point(16, 252), new Size(250, 46), Color.FromArgb(46, 139, 79));
            btnFinish.Enabled = false;
            btnFinish.Click += (s, e) => Finish();

            btnClose = MakeBtn(hr ? "Zatvori" : "Close", new Point(274, 252), new Size(150, 46), Color.FromArgb(60, 71, 64));
            btnClose.Click += (s, e) => Close();

            Controls.AddRange(new Control[] { lblTitle, btnStraight, btnCircle, lblHintTop, lblHintBig, lblHintSub, bar, btnFinish, btnClose });

            poll.Interval = 100;   // ~10 Hz
            poll.Tick += Poll_Tick;

            FormClosing += (s, e) => { poll.Stop(); test.Stop(); };
        }

        private Button MakeBtn(string text, Point p, Size sz, Color back)
        {
            var b = new Button
            {
                Text = text, Location = p, Size = sz, BackColor = back, ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Font = new Font("Tahoma", 10f, FontStyle.Bold)
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private void StartTest(ITMode m)
        {
            // The straight test measures the lateral residual against the AB line.
            // Without one there is no reference worth reporting, so refuse up front
            // rather than produce a confident wrong verdict after the drive.
            if (m == ITMode.Straight && !HasAbLine())
            {
                ShowMsg(hr ? "Treba aktivna AB linija — postavi je pa pokreni test."
                           : "An AB line is required — set one, then start the test.");
                return;
            }

            test.Start(m);
            btnStraight.Enabled = btnCircle.Enabled = false;
            btnFinish.Enabled = true;
            bar.Visible = true; bar.Value = 0;
            lblHintBig.Text = ""; lblHintSub.Text = "";
            lblTitle.Text = m == ITMode.Straight
                ? (hr ? "Test pravac — vozi" : "Straight test — drive")
                : (hr ? "Test kružnica — vozi" : "Circle test — drive");
            poll.Start();
        }

        private void Poll_Tick(object sender, EventArgs e)
        {
            test.RecordFrom(mf);
            string action, big;
            double p = test.Progress(mf, out action, out big, hr);
            bar.Value = (int)(p * 100);
            lblHintTop.Text = action;
            lblHintBig.Text = big;
            lblHintSub.Text = (hr ? "uzoraka " : "samples ") + test.SampleCount + " · " + (p * 100).ToString("0") + "%";
            if (test.IsComplete()) Finish();
        }

        private void Finish()
        {
            poll.Stop();
            test.Stop();
            btnFinish.Enabled = false;

            ITResult r = test.Analyze(mf, hr);
            if (!r.ok)
            {
                ShowMsg(r.message);
                // allow retry
                btnStraight.Enabled = btnCircle.Enabled = true;
                bar.Visible = false;
                return;
            }

            try
            {
                string dir = Path.Combine(RegistrySettings.baseDirectory, "Reports");
                Directory.CreateDirectory(dir);
                string modeTag = r.mode == ITMode.Straight ? (hr ? "pravac" : "straight") : (hr ? "kruznica" : "circle");
                string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm", CultureInfo.InvariantCulture);
                string baseName = "Report_" + stamp + "_" + modeTag;

                File.WriteAllText(Path.Combine(dir, baseName + ".html"), BuildReportHtml(r), Encoding.UTF8);
                File.WriteAllText(Path.Combine(dir, baseName + ".csv"), test.BuildCsv(mf), Encoding.UTF8);

                ShowMsg((hr ? "Report spremljen: " : "Report saved: ") + baseName + ".html");
                System.Diagnostics.Process.Start(Path.Combine(dir, baseName + ".html"));
            }
            catch (Exception ex)
            {
                ShowMsg((hr ? "Greška pri spremanju: " : "Save error: ") + ex.Message);
            }

            btnStraight.Enabled = btnCircle.Enabled = true;
            bar.Visible = false;
        }

        private void ShowMsg(string s) { lblHintTop.Text = s; lblHintBig.Text = ""; lblHintSub.Text = ""; }

        private bool HasAbLine()
        {
            return mf.trk.idx >= 0 && mf.trk.idx < mf.trk.gArr.Count
                && mf.trk.gArr[mf.trk.idx].mode == TrackMode.AB;
        }

        // ---------------------------------------------------------------- report
        private string BuildReportHtml(ITResult r)
        {
            string title = r.mode == ITMode.Straight
                ? (hr ? "Integration Test — Pravac" : "Integration Test — Straight")
                : (hr ? "Integration Test — Kružnica" : "Integration Test — Circle");
            string verdictColor = r.okCount == r.total ? "#2e8b4f" : (r.okCount == 0 ? "#c23a2b" : "#b06f12");

            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang='").Append(hr ? "hr" : "en").Append("'><head><meta charset='utf-8'>");
            sb.Append("<meta name='viewport' content='width=device-width,initial-scale=1'><title>").Append(title).Append("</title>");
            sb.Append("<style>")
              .Append("body{margin:0;background:#eceff0;color:#18211d;font-family:system-ui,'Segoe UI',Roboto,sans-serif;line-height:1.5}")
              .Append(".wrap{max-width:760px;margin:0 auto;padding:28px 20px 60px}")
              .Append("h1{font-size:22px;margin:0 0 2px}.sub{color:#5b6a63;font-family:ui-monospace,Consolas,monospace;font-size:12px;margin:0 0 18px}")
              .Append(".rep{background:#fff;border:1px solid #d7ddd9;border-radius:14px;overflow:hidden}")
              .Append(".rhead{display:flex;justify-content:space-between;align-items:center;padding:14px 18px;background:#f5f7f6;border-bottom:1px solid #d7ddd9}")
              .Append(".rhead b{font-size:16px}.score{font-family:ui-monospace,Consolas,monospace;text-align:right}.score .n{font-size:20px;font-weight:700}")
              .Append(".chk{display:grid;grid-template-columns:22px 1fr auto;gap:10px;align-items:center;padding:11px 18px;border-top:1px solid #eef1f0}")
              .Append(".ci{width:19px;height:19px;border-radius:6px;display:grid;place-items:center;font-weight:800;font-size:12px}")
              .Append(".g{background:#e4f1e8;color:#2e8b4f}.w{background:#f6ecd8;color:#b06f12}.c{background:#f7e2de;color:#c23a2b}")
              .Append(".n{background:#eceff0;color:#8b968f}.chk.na{opacity:.62}")
              .Append(".nm{font-weight:600}.ds{color:#5b6a63;font-size:12.5px}.vl{font-family:ui-monospace,Consolas,monospace;font-size:12px;font-weight:700;white-space:nowrap}")
              .Append(".nt{color:#8b968f;font-family:ui-monospace,Consolas,monospace;font-size:11px}")
              .Append(".chart{padding:16px 18px;border-top:1px solid #eef1f0}.sum{padding:14px 18px;border-top:1px solid #eef1f0;font-size:13.5px}")
              .Append(".foot{color:#5b6a63;font-size:11.5px;margin-top:16px}")
              .Append("</style></head><body><div class='wrap'>");

            sb.Append("<h1>").Append(title).Append("</h1>");
            sb.Append("<p class='sub'>").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append(" · ").Append(r.subtitle).Append("</p>");

            sb.Append("<div class='rep'><div class='rhead'><span><span style='display:inline-block;width:11px;height:11px;border-radius:50%;background:")
              .Append(verdictColor).Append(";margin-right:8px'></span><b>")
              .Append(hr ? "Rezultat" : "Result").Append("</b></span><span class='score'><span class='n' style='color:")
              .Append(verdictColor).Append("'>").Append(r.okCount).Append(" / ").Append(r.total).Append("</span><br>")
              .Append(hr ? "provjere OK" : "checks OK").Append("</span></div>");

            foreach (var c in r.checks)
            {
                // status 3 = not measured: greyed out, and the note says what is missing
                string cls = c.status == 0 ? "g" : (c.status == 1 ? "w" : (c.status == 3 ? "n" : "c"));
                string sym = c.status == 0 ? "✓" : (c.status == 1 ? "~" : (c.status == 3 ? "–" : "!"));
                sb.Append("<div class='chk").Append(c.status == 3 ? " na" : "").Append("'><span class='ci ")
                  .Append(cls).Append("'>").Append(sym).Append("</span>")
                  .Append("<span><span class='nm'>").Append(Esc(c.name)).Append("</span><br><span class='ds'>").Append(Esc(c.desc)).Append("</span>");
                if (!string.IsNullOrEmpty(c.note))
                    sb.Append("<br><span class='nt'>").Append(Esc(c.note)).Append("</span>");
                sb.Append("</span><span class='vl'>").Append(Esc(c.value)).Append("</span></div>");
            }

            if (!string.IsNullOrEmpty(r.svg))
                sb.Append("<div class='chart'>").Append(r.svg).Append("</div>");

            sb.Append("<div class='sum'><b>").Append(hr ? "Zaključak: " : "Summary: ").Append("</b>").Append(r.summary).Append("</div>");
            sb.Append("</div>");

            sb.Append("<p class='foot'>").Append(hr
                ? "F07 Integration Test · dijagnostika (ništa nije mijenjano na stroju). Preporuke primijeni ručno pa ponovi test."
                : "F07 Integration Test · diagnostic only (nothing applied to the machine). Apply suggestions manually, then re-test.")
              .Append("</p>");

            sb.Append("</div></body></html>");
            return sb.ToString();
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}
