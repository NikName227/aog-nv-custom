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

            // Anchor on LocalPosition, which GMap keeps in step with every other overlay, and
            // take only the SIZE from the projection at the current zoom. Asking the control
            // where a lat/lon lands at render time (FromLatLngToLocal) misses the offset GMap
            // applies while the map is being dragged, so the picture used to slide away from
            // the boundary lines mid-drag.
            var proj = Overlay.Control.MapProvider?.Projection;
            if (proj == null) return;

            int zoom = (int)Overlay.Control.Zoom;
            GPoint p0 = proj.FromLatLngToPixel(topLeft, zoom);
            GPoint p1 = proj.FromLatLngToPixel(bottomRight, zoom);

            float w = p1.X - p0.X;
            float h = p1.Y - p0.Y;
            if (w < 1 || h < 1) return;

            Point tl = LocalPosition;

            InterpolationMode oldMode = g.InterpolationMode;
            //zoomed out the picture is minified, zoomed in it is magnified - bilinear keeps
            //both honest without the cost of a high-quality resample on every repaint
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.DrawImage(bitmap, new RectangleF(tl.X, tl.Y, w, h));
            g.InterpolationMode = oldMode;
        }
    }
}
