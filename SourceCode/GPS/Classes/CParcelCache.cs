using AgOpenGPS.Properties;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace AgOpenGPS
{
    /// <summary>
    /// Shared cadastral parcel cache (field test 2026-07-10). One ParcelCache folder
    /// next to Fields serves ALL fields, split into 1x1 km tiles on the EPSG:3765
    /// meter grid - one file per tile (tile_E_N.txt). Coverage follows the vehicle:
    /// tiles within ~1 km of a position load from disk or, when missing, fetch from
    /// the WFS one tile-bbox at a time (a 1 km bbox stays well under the WFS
    /// COUNT=800 cap that a big bbox trips). Everything runs in a background task,
    /// so the UI never blocks, and the cache grows by itself while driving.
    /// The old per-field Parcels.txt files are ignored (experimental phase, no
    /// migration - decision 2026-07-10).
    /// </summary>
    public static class CParcelCache
    {
        public const double TileSize = 1000.0;     // m, EPSG:3765 grid
        public const double CoverRadius = 1000.0;  // m around the given position

        private static readonly object gate = new object();
        private static readonly Dictionary<(int e, int n), List<CadastralParcel>> tiles
            = new Dictionary<(int e, int n), List<CadastralParcel>>();
        private static bool busy;

        /// <summary>Bumped on every cache change - consumers redraw when it moves.</summary>
        public static int Version { get; private set; }

        private static string CacheDir
        {
            get
            {
                return Path.Combine(
                    Path.GetDirectoryName(RegistrySettings.fieldsDirectory) ?? RegistrySettings.fieldsDirectory,
                    "ParcelCache");
            }
        }

        private static string TilePath(int te, int tn)
        {
            return Path.Combine(CacheDir,
                "tile_" + te.ToString(CultureInfo.InvariantCulture) + "_"
                        + tn.ToString(CultureInfo.InvariantCulture) + ".txt");
        }

        /// <summary>
        /// Make sure every tile within CoverRadius of the WGS84 position is loaded:
        /// from disk when the file exists, else fetched from the WFS and saved.
        /// Runs in the background; a pass already underway is not doubled. force
        /// re-downloads the tiles around the position and overwrites their files
        /// (the Refresh button - for when the cadastre changed).
        /// onPass (optional) runs on the worker when the pass ends, changed or not.
        /// </summary>
        public static void EnsureCoverage(double lat, double lon, string wfsUrl,
            bool force = false, Action onPass = null)
        {
            lock (gate)
            {
                if (busy) return;
                busy = true;
            }

            Task.Run(() =>
            {
                try
                {
                    Directory.CreateDirectory(CacheDir);
                    var (e, n) = CadastralWfsClient.ToEN3765(lat, lon);

                    foreach (var key in TilesAround(e, n))
                    {
                        bool have;
                        lock (gate) have = tiles.ContainsKey(key);
                        if (have && !force) continue;

                        string file = TilePath(key.e, key.n);
                        List<CadastralParcel> list = null;

                        if (!force && File.Exists(file))
                            list = CadastralWfsClient.LoadTile(file, out _);

                        if (list == null)
                        {
                            list = CadastralWfsClient.GetParcelsBbox3765(wfsUrl,
                                key.e * TileSize, key.n * TileSize,
                                (key.e + 1) * TileSize, (key.n + 1) * TileSize, out string err);
                            if (err != null) continue;   // offline etc. - next pass retries
                            CadastralWfsClient.SaveTile(file, list, out _);
                        }

                        lock (gate)
                        {
                            tiles[key] = list;
                            Version++;
                        }
                    }
                }
                catch { }
                finally
                {
                    lock (gate) busy = false;
                    onPass?.Invoke();
                }
            });
        }

        // tiles whose 1x1 km square comes within CoverRadius of the position
        private static IEnumerable<(int e, int n)> TilesAround(double e, double n)
        {
            int minE = (int)Math.Floor((e - CoverRadius) / TileSize);
            int maxE = (int)Math.Floor((e + CoverRadius) / TileSize);
            int minN = (int)Math.Floor((n - CoverRadius) / TileSize);
            int maxN = (int)Math.Floor((n + CoverRadius) / TileSize);

            for (int te = minE; te <= maxE; te++)
            {
                for (int tn = minN; tn <= maxN; tn++)
                {
                    // nearest point of the tile square to the position
                    double dx = Math.Max(0, Math.Max(te * TileSize - e, e - (te + 1) * TileSize));
                    double dy = Math.Max(0, Math.Max(tn * TileSize - n, n - (tn + 1) * TileSize));
                    if (dx * dx + dy * dy <= CoverRadius * CoverRadius)
                        yield return (te, tn);
                }
            }
        }

        /// <summary>
        /// All loaded parcels, deduped across tile borders (a parcel intersecting
        /// two tiles is in both files; the id keeps one copy).
        /// </summary>
        public static List<CadastralParcel> Snapshot()
        {
            var outList = new List<CadastralParcel>();
            var seen = new HashSet<string>();
            lock (gate)
            {
                foreach (var kv in tiles)
                {
                    foreach (var p in kv.Value)
                    {
                        if (p.Outer.Count < 3) continue;
                        if (seen.Add(p.DedupKey())) outList.Add(p);
                    }
                }
            }
            return outList;
        }

        /// <summary>Drop the in-memory tiles (files stay) - e.g. on field close.</summary>
        public static void ClearMemory()
        {
            lock (gate)
            {
                tiles.Clear();
                Version++;
            }
        }
    }
}
