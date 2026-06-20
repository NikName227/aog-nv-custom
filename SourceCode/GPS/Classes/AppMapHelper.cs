using GMap.NET.MapProviders;

namespace AgOpenGPS
{
    /// <summary>
    /// Resolves the background map provider used by the field map (FormMap),
    /// based on the "App Map" setting chosen by the user.
    /// </summary>
    public static class AppMapHelper
    {
        public const int ModeDefault = 0; // Bing - keep AOG default behaviour
        public const int ModeGoogle = 1;  // Google satellite
        public const int ModeCustom = 2;  // Custom WMS (default: DGU Geoportal)

        static AppMapHelper()
        {
            // WMS tiles can respond slowly; give them more time so they don't time
            // out and show as blank on the first load.
            GMapProvider.TimeoutMs = 12000;
        }

        public static GMapProvider GetProvider()
        {
            switch (Properties.Settings.Default.setMap_appMapMode)
            {
                case ModeGoogle:
                    return GMapProviders.GoogleSatelliteMap;

                case ModeCustom:
                    GeoportalWmsProvider.Instance.SetBaseUrl(
                        Properties.Settings.Default.setMap_customWmsUrl);
                    return GeoportalWmsProvider.Instance;

                default:
                    return GMapProviders.BingHybridMap;
            }
        }
    }
}
