using System;
using System.Drawing;
using GMap.NET;
using GMap.NET.WindowsForms;

namespace AgOpenGPS
{
    /// <summary>
    /// Small red rectangle marking the current vehicle position on the Map App
    /// preview (display only).
    /// </summary>
    [Serializable]
    public class VehicleMarker : GMapMarker
    {
        public VehicleMarker(PointLatLng pos) : base(pos)
        {
            Size = new Size(14, 14);
            Offset = new Point(-Size.Width / 2, -Size.Height / 2);
        }

        public override void OnRender(Graphics g)
        {
            Rectangle rect = new Rectangle(
                LocalPosition.X, LocalPosition.Y, Size.Width, Size.Height);

            using (SolidBrush brush = new SolidBrush(Color.Red))
                g.FillRectangle(brush, rect);
            using (Pen pen = new Pen(Color.Black, 1f))
                g.DrawRectangle(pen, rect);
        }
    }
}
