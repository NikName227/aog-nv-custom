using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace AgOpenGPS
{
    /// <summary>
    /// Pictograms for the three plan modes (wizard step 3), drawn at runtime so no
    /// binary resources are needed. Geometry mirrors the design the user confirmed
    /// (features/05_turn-zone/icons, v5): solid green = worked/active passes,
    /// dashed green + a small tangent arrowhead = the next pass, gray = untouched,
    /// and an order number in a disc on the middle of every pass.
    /// Mode 1 = row by row (serpentine), 2 = contour (middle out), 3 = zones/skip.
    /// </summary>
    public static class TurnModeIcons
    {
        private static readonly Color Green = Color.FromArgb(47, 158, 68);
        private static readonly Color Gray = Color.FromArgb(168, 176, 168);
        private static readonly Color Frame = Color.FromArgb(122, 138, 122);

        /// <summary>Icon for the given mode, rendered into a w x h bitmap
        /// (the native design space is 80 x 72 and scales uniformly).</summary>
        public static Bitmap Make(int mode, int w, int h)
        {
            var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                float sc = Math.Min(w / 80f, h / 72f);
                g.TranslateTransform((w - 80f * sc) / 2f, (h - 72f * sc) / 2f);
                g.ScaleTransform(sc, sc);

                using (var frame = new Pen(Frame, 2.5f))
                    g.DrawPath(frame, RoundedRect(6, 6, 68, 60, 8));

                if (mode == 1) DrawRows(g);
                else if (mode == 2) DrawContour(g);
                else DrawZones(g);
            }
            return bmp;
        }

        private static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
        {
            var p = new GraphicsPath();
            p.AddArc(x, y, 2 * r, 2 * r, 180, 90);
            p.AddArc(x + w - 2 * r, y, 2 * r, 2 * r, 270, 90);
            p.AddArc(x + w - 2 * r, y + h - 2 * r, 2 * r, 2 * r, 0, 90);
            p.AddArc(x, y + h - 2 * r, 2 * r, 2 * r, 90, 90);
            p.CloseFigure();
            return p;
        }

        private static void Pass(Graphics g, float x, Color c, float width = 4f, bool dashed = false)
        {
            using (var pen = new Pen(c, width) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                if (dashed) pen.DashPattern = new[] { 1.4f, 1.1f };
                g.DrawLine(pen, x, 22, x, 52);
            }
        }

        private static void NumDisc(Graphics g, float x, Color c, string n)
        {
            using (var b = new SolidBrush(c))
                g.FillEllipse(b, x - 6.2f, 38 - 6.2f, 12.4f, 12.4f);
            using (var f = new Font("Tahoma", 6.5f, FontStyle.Bold))
            {
                SizeF sz = g.MeasureString(n, f);
                g.DrawString(n, f, Brushes.White, x - sz.Width / 2f, 38 - sz.Height / 2f + 0.5f);
            }
        }

        // small arrowhead rotated onto the arc tangent (dives onto the target pass)
        private static void ArrowHeadDown(Graphics g, float x, float y, Color c)
        {
            using (var pen = new Pen(c, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                g.DrawLine(pen, x + 3.1f, y - 2.6f, x, y);
                g.DrawLine(pen, x, y, x - 0.3f, y - 3.8f);
            }
        }

        private static void DrawRows(Graphics g)
        {
            using (var pen = new Pen(Green, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var dash = new Pen(Frame, 1.5f) { DashPattern = new[] { 2.6f, 2f } })
            using (var head = new Pen(Green, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                g.DrawLine(dash, 6, 16, 74, 16);
                g.DrawLine(dash, 6, 56, 74, 56);

                var path = new GraphicsPath();
                path.AddLine(18, 54, 18, 24);
                path.AddArc(18, 18, 12, 12, 180, 180);
                path.AddLine(30, 24, 30, 48);
                path.AddArc(30, 42, 12, 12, 180, -180);
                path.AddLine(42, 48, 42, 24);
                path.AddArc(42, 18, 12, 12, 180, 180);
                path.AddLine(54, 24, 54, 48);
                path.AddArc(54, 42, 12, 12, 180, -180);
                path.AddLine(66, 48, 66, 22);
                g.DrawPath(pen, path);

                g.DrawLine(head, 62.5f, 26, 66, 20);
                g.DrawLine(head, 66, 20, 69.5f, 26);
            }
            string[] nums = { "1", "2", "3", "4", "5" };
            float[] xs = { 18, 30, 42, 54, 66 };
            for (int i = 0; i < 5; i++) NumDisc(g, xs[i], Green, nums[i]);
        }

        private static void DrawContour(Graphics g)
        {
            Pass(g, 16, Gray); Pass(g, 64, Gray);
            Pass(g, 40, Green, 5f);
            Pass(g, 52, Green);
            Pass(g, 28, Green, 3.5f, dashed: true);

            using (var arc = new Pen(Green, 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawBezier(arc, 52, 17, 46, 8, 34, 8, 28, 17);
            ArrowHeadDown(g, 28, 17, Green);

            NumDisc(g, 40, Green, "1");
            NumDisc(g, 52, Green, "2");
            NumDisc(g, 28, Green, "3");
            NumDisc(g, 64, Gray, "4");
            NumDisc(g, 16, Gray, "5");
        }

        private static void DrawZones(Graphics g)
        {
            Pass(g, 16, Green);
            Pass(g, 28, Gray);
            Pass(g, 40, Gray);
            Pass(g, 52, Green);
            Pass(g, 64, Gray);

            using (var arc = new Pen(Green, 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawBezier(arc, 52, 17, 46, 8, 34, 8, 28, 17);
            ArrowHeadDown(g, 28, 17, Green);

            //thin gray arrow below: where the sequence goes after that (2nd -> 5th)
            using (var arc = new Pen(Gray, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawBezier(arc, 28, 56, 38, 64, 54, 64, 64, 56);
            using (var head = new Pen(Gray, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                g.DrawLine(head, 61, 58.7f, 64, 56);
                g.DrawLine(head, 64, 56, 64.4f, 60);
            }

            NumDisc(g, 16, Green, "1");
            NumDisc(g, 28, Gray, "3");
            NumDisc(g, 40, Gray, "5");
            NumDisc(g, 52, Green, "2");
            NumDisc(g, 64, Gray, "4");
        }
    }
}
