using AgOpenGPS.Core.Models;

namespace AgOpenGPS
{
    /// <summary>
    /// Cadastral parcel outlines for the captured field area, drawn as crisp vector
    /// lines on the main map (sharp at any zoom, unlike a baked raster overlay).
    ///
    /// Performance: all parcel edges are flattened ONCE into a single line-segment
    /// array, so the render draws them in one batched call per frame (like one
    /// boundary) instead of one VBO per parcel. The set is bounded to the captured
    /// area, so it does not grow while driving; the background texture handling is
    /// unchanged.
    /// </summary>
    public class CParcels
    {
        // Flattened line segments (pairs of points) for all parcels.
        public GeoCoord[] Segments = new GeoCoord[0];

        public void Clear()
        {
            Segments = new GeoCoord[0];
        }
    }
}
