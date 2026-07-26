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
        private Panel panSteps;
        private Button btnStraight, btnCircle, btnSkip, btnFinish, btnClose;

        public FormIntegrationTest(FormGPS callingForm)
        {
            mf = callingForm;
            hr = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "hr";

            Name = "FormIntegrationTest";
            Text = hr ? "Integration Test" : "Integration Test";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(440, 556);
            BackColor = Color.FromArgb(24, 33, 29);
            ForeColor = Color.Gainsboro;
            Font = new Font("Tahoma", 9.75f);

            lblTitle = new Label
            {
                Text = hr ? "Validator senzorike — odaberi test" : "Sensor validator — choose a test",
                AutoSize = false, Location = new Point(16, 12), Size = new Size(398, 24),
                Font = new Font("Tahoma", 11f, FontStyle.Bold), ForeColor = Color.White
            };

            btnStraight = MakeBtn(hr ? "Test pravac\n(AB linija, tamo i natrag)" : "Straight test\n(AB line, there and back)",
                                  new Point(16, 48), new Size(196, 78), Color.FromArgb(15, 131, 149));
            btnCircle = MakeBtn(hr ? "Test kružnica\n(konstantan kut, 2 kruga)" : "Circle test\n(constant angle, 2 laps)",
                                new Point(220, 48), new Size(196, 78), Color.FromArgb(18, 90, 110));
            btnStraight.Click += (s, e) => StartTest(ITMode.Straight);
            btnCircle.Click += (s, e) => StartTest(ITMode.Circle);

            // 3-line driver hint: action / BIG value (target km/h) / progress — big + centered so it reads on the tablet while driving
            // step checklist — filled in once a test starts
            panSteps = new Panel
            {
                Location = new Point(16, 132), Size = new Size(408, 214),
                BackColor = Color.FromArgb(20, 28, 25), Visible = false, AutoScroll = true
            };

            lblHintTop = new Label
            {
                Text = hr ? "Preporuka: prvo pravac (geometrija), pa kružnica (heading)."
                          : "Recommended: straight first (geometry), then circle (heading).",
                AutoSize = false, Location = new Point(16, 356), Size = new Size(408, 40),
                TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Tahoma", 10.5f),
                ForeColor = Color.FromArgb(200, 214, 206)
            };
            lblHintBig = new Label
            {
                Text = "", AutoSize = false, Location = new Point(16, 398), Size = new Size(408, 44),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Tahoma", 26f, FontStyle.Bold), ForeColor = Color.FromArgb(90, 200, 220)
            };
            lblHintSub = new Label
            {
                Text = "", AutoSize = false, Location = new Point(16, 444), Size = new Size(408, 20),
                TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Tahoma", 9.5f),
                ForeColor = Color.FromArgb(140, 160, 150)
            };

            bar = new ProgressBar { Location = new Point(16, 468), Size = new Size(408, 16), Minimum = 0, Maximum = 100, Visible = false };

            btnSkip = MakeBtn(hr ? "Preskoči korak" : "Skip step", new Point(16, 496), new Size(150, 46), Color.FromArgb(120, 96, 24));
            btnSkip.Visible = false;
            btnSkip.Click += (s, e) => { test.SkipStep(); BuildStepList(); if (test.IsComplete()) Finish(); };

            btnFinish = MakeBtn(hr ? "Završi + report" : "Finish + report", new Point(174, 496), new Size(160, 46), Color.FromArgb(46, 139, 79));
            btnFinish.Enabled = false;
            btnFinish.Click += (s, e) => Finish();

            btnClose = MakeBtn(hr ? "Zatvori" : "Close", new Point(342, 496), new Size(82, 46), Color.FromArgb(60, 71, 64));
            btnClose.Click += (s, e) => Close();

            Controls.AddRange(new Control[] { lblTitle, btnStraight, btnCircle, panSteps,
                                              lblHintTop, lblHintBig, lblHintSub, bar, btnSkip, btnFinish, btnClose });

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

            test.Start(m, hr);
            btnStraight.Enabled = btnCircle.Enabled = false;
            btnFinish.Enabled = true;
            btnSkip.Visible = true;
            bar.Visible = true; bar.Value = 0;
            panSteps.Visible = true;
            lblHintBig.Text = ""; lblHintSub.Text = "";
            lblTitle.Text = m == ITMode.Straight
                ? (hr ? "Test pravac — 3 koraka" : "Straight test — 3 steps")
                : (hr ? "Test kružnica — 2 kruga" : "Circle test — 2 laps");
            BuildStepList();
            poll.Start();
        }

        // Redraw the checklist: done steps go green with a tick, the current one is
        // highlighted, the rest stay dim. Cheap enough to rebuild on each change.
        private void BuildStepList()
        {
            panSteps.SuspendLayout();
            panSteps.Controls.Clear();
            int y = 4;
            var steps = test.Steps;
            for (int i = 0; i < steps.Count; i++)
            {
                ITStep st = steps[i];
                if (!string.IsNullOrEmpty(st.head))
                {
                    panSteps.Controls.Add(new Label
                    {
                        Text = st.head, AutoSize = false, Location = new Point(6, y), Size = new Size(390, 20),
                        Font = new Font("Tahoma", 9.75f, FontStyle.Bold),
                        ForeColor = Color.FromArgb(150, 168, 158)
                    });
                    y += 21;
                }

                bool current = i == test.CurrentStep;
                string mark = st.done ? "✓" : (st.skipped ? "–" : (current ? "▶" : "○"));
                Color col = st.done ? Color.FromArgb(96, 200, 130)
                          : st.skipped ? Color.FromArgb(120, 132, 124)
                          : current ? Color.White
                          : Color.FromArgb(118, 132, 124);

                string text = "   " + mark + "  " + st.label;
                if (st.optional) text += hr ? "   ⓘ opc. — kontrola offseta" : "   (i) optional — offset cross-check";
                if (st.skipped) text += hr ? "   — preskočeno" : "   — skipped";

                panSteps.Controls.Add(new Label
                {
                    Text = text, AutoSize = false, Location = new Point(6, y), Size = new Size(390, 19),
                    Font = new Font("Tahoma", 9.5f, current ? FontStyle.Bold : FontStyle.Regular),
                    ForeColor = col
                });
                y += 20;
            }
            panSteps.ResumeLayout();
        }

        private void Poll_Tick(object sender, EventArgs e)
        {
            int before = test.CurrentStep;
            test.RecordFrom(mf);
            if (test.CurrentStep != before) BuildStepList();

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
            btnSkip.Visible = false;
            BuildStepList();

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
