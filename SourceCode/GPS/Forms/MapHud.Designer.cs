using AgLibrary.Logging;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Streamers;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgOpenGPS
{
    // F01 — HUD map button (bottom left, above the grid "x2"): one tap shows/hides the
    // satellite background AND the cadastral parcels, without the Map App window.
    // Off only hides (the field's background file stays), so the next tap is instant.
    // A field without a background gets one at Low resolution, cut off after 3 s.
    public partial class FormGPS
    {
        private const int MapHudCaptureTimeoutMs = 3000;

        private BingMap mapHudHidden;     // background taken off the screen by the button
        private int mapHudCaptureId;      // bumped to drop a late or stale capture
        public bool mapHudCapturing;

        public bool IsMapHudVisible =>
            isJobStarted && CFeatureLock.IsOn(CFeatureLock.FeatureGeoportal);

        public bool IsMapHudOn =>
            worldGrid.HasBingMap && Properties.Settings.Default.setMap_showParcelsOnMain;

        // Field opened or closed: forget the hidden picture and any capture in flight.
        public void MapHudReset()
        {
            mapHudHidden = null;
            mapHudCaptureId++;
            mapHudCapturing = false;
        }

        public void MapHudToggle()
        {
            if (mapHudCapturing) return;

            if (IsMapHudOn)
            {
                mapHudHidden = worldGrid.BingMap;
                worldGrid.BingMap = null;
                MapHudParcels(false);
                TimedMessageBox(1500, "Mapa", "Mapa i čestice isključeni");
                return;
            }

            if (!worldGrid.HasBingMap && mapHudHidden != null)
            {
                worldGrid.BingMap = mapHudHidden;
                mapHudHidden = null;
            }

            if (worldGrid.HasBingMap)
            {
                MapHudParcels(true);
                TimedMessageBox(1500, "Mapa", "Mapa i čestice uključeni");
                return;
            }

            if (!CMapCapture.HasBoundary(this))
            {
                TimedMessageBox(3000, "Mapa", "Polje nema granicu – otvori Map App za snimanje pozadine");
                return;
            }

            MapHudCapture();
        }

        private void MapHudParcels(bool on)
        {
            Properties.Settings.Default.setMap_showParcelsOnMain = on;
            if (!on)
            {
                parcels.Clear();
                return;
            }

            // show what the cache already has now; the 4 s tick keeps filling it
            CParcelCache.EnsureCoverage(AppModel.CurrentLatLon.Latitude, AppModel.CurrentLatLon.Longitude,
                Properties.Settings.Default.setMap_parcelWfsUrl);
            parcelCacheDrawnVersion = CParcelCache.Version;
            RebuildParcelSegments();
        }

        // Low resolution regardless of the Map App setting. The download cannot be aborted;
        // after the timeout its result is simply dropped and nothing is saved.
        private void MapHudCapture()
        {
            int id = ++mapHudCaptureId;
            mapHudCapturing = true;
            TimedMessageBox(1500, "Mapa", "Skidam mapu…");
            var sw = Stopwatch.StartNew();

            var work = Task.Run(() => CMapCapture.CaptureField(this, null, out string error, out bool tooLarge, forceLow: true));

            Task.WhenAny(work, Task.Delay(MapHudCaptureTimeoutMs)).ContinueWith(t =>
            {
                bool inTime = t.Result == work && work.Status == TaskStatus.RanToCompletion;
                BingMap map = inTime ? work.Result : null;
                try { BeginInvoke((MethodInvoker)(() => MapHudCaptureDone(id, map, inTime, sw))); }
                catch { }
            });

            // log how long the download really took, also when it came too late
            work.ContinueWith(t => Log.EventWriter("Map HUD: capture took " + sw.ElapsedMilliseconds + " ms"
                + (t.Status == TaskStatus.RanToCompletion && t.Result != null ? "" : " (failed)")));
        }

        private void MapHudCaptureDone(int id, BingMap map, bool inTime, Stopwatch sw)
        {
            if (id != mapHudCaptureId) return;   // field changed meanwhile
            mapHudCapturing = false;

            if (map == null || !isJobStarted)
            {
                Log.EventWriter("Map HUD: no background after " + sw.ElapsedMilliseconds + " ms"
                    + (inTime ? " (download failed)" : " (timeout)"));
                TimedMessageBox(3000, "Mapa", "Mapa nije skinuta u 3 s – pokušaj ponovo ili otvori Map App");
                return;
            }

            worldGrid.BingMap = map;
            var streamer = new BingMapStreamer();
            streamer.JpegQuality = CMapCapture.JpegQuality(false);
            streamer.TryWrite(map, AppCore.ActiveField.FieldDirectory);

            MapHudParcels(true);
            TimedMessageBox(1500, "Mapa", "Mapa i čestice uključeni");
        }
    }
}
