using GMap.NET;
using GMap.NET.WindowsForms;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace AgOpenGPS
{
    /// <summary>
    /// Draws the captured field background (BackPic) inside a GMap view, in its true
    /// geographic place (2026-08-10).
    ///
    /// Windows built on a GMapControl show live tiles, which are coarser than the imagery the
    /// Map App downloads for the field - so the turn window was showing none of the resolution
    /// that had just been captured. The OpenGL windows (Smart AB, Orchard Planner) already draw
    /// the captured picture; this marker gives the tile-based windows the same thing without
    /// rewriting them: it renders under every other overlay, and outside its box the live tiles
    /// remain visible so the surroundings do not disappear.
    /// </summary>
    [Serializable]
    public class BackPicMarker : GMapMarker
    {
        private readonly Bitmap bitmap;
        private readonly PointLatLng topLeft;
        private readonly PointLatLng bottomRight;

        public BackPicMarker(Bitmap bitmap, PointLatLng topLeft, PointLatLng bottomRight)
            : base(topLeft)
        {
            this.bitmap = bitmap;
            this.topLeft = topLeft;
            this.bottomRight = bottomRight;
            IsHitTestVisible = false;
        }

        public override void OnRender(Graphics g)
        {
            if (bitmap == null || Overlay?.Control == null) return;

            GPoint tl = Overlay.Control.FromLatLngToLocal(topLeft);
            GPoint br = Overlay.Control.FromLatLngToLocal(bottomRight);

            float w = br.X - tl.X;
            float h = br.Y - tl.Y;
            if (w < 1 || h < 1) return;

            InterpolationMode oldMode = g.InterpolationMode;
            //zoomed out the picture is minified, zoomed in it is magnified - bilinear keeps
            //both honest without the cost of a high-quality resample on every repaint
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.DrawImage(bitmap, new RectangleF(tl.X, tl.Y, w, h));
            g.InterpolationMode = oldMode;
        }
    }
}
