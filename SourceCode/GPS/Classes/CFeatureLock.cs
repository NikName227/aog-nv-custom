using AgLibrary.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AgOpenGPS
{
    /// <summary>
    /// Feature tiering (NV custom, F06): optional features ship in the build but
    /// stay hidden unless an encrypted per-install config unlocks them. The point
    /// is a simpler app for basic users plus hand-issued unlocks - friction, not
    /// protection (the data endpoints are public). Without a valid config the app
    /// behaves like plain basic AOG; Smart AB is always available.
    /// </summary>
    public static class CFeatureLock
    {
        public const string FeatureGeoportal = "geoportal";  // Map App (F01) + Geoportal boundary (F02)
        public const string FeatureTurnApp = "turnapp";      // Turn lines + pass plan (F05)
        public const string FeatureOrchard = "orchard";      // Orchard planner (F50)
        public const string FeatureIntegrationTest = "integrationtest"; // Integration Test wizard (F07)
        public const string FeatureSensorFusion = "sensorfusion";       // Advanced Sensor Fusion (F10)

        // unremarkable file name on purpose (not "license"/"premium")
        private static readonly string configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AgOpenGPS", "Config.json");

        // key phrase lives only in the nv-custom build - never in a public repo
        private const string keyPhrase = "NV-AOG-2026.tilling&turns.8407";

        private static JObject config;

        /// <summary>
        /// Read + decrypt the config file. Any failure (missing, tampered, bad
        /// Base64/JSON) silently means all optional features stay off.
        /// </summary>
        public static void Load()
        {
            config = null;
            try
            {
                if (!File.Exists(configPath)) return;

                byte[] blob = Convert.FromBase64String(File.ReadAllText(configPath).Trim());
                if (blob.Length < 17) return;

                using (var aes = Aes.Create())
                using (var sha = SHA256.Create())
                {
                    aes.Key = sha.ComputeHash(Encoding.UTF8.GetBytes(keyPhrase));
                    byte[] iv = new byte[16];
                    Array.Copy(blob, iv, 16);
                    aes.IV = iv;

                    using (var dec = aes.CreateDecryptor())
                    {
                        byte[] plain = dec.TransformFinalBlock(blob, 16, blob.Length - 16);
                        config = JObject.Parse(Encoding.UTF8.GetString(plain));
                    }
                }
                Log.EventWriter("FeatureLock: config loaded, features unlocked");
            }
            catch (Exception)
            {
                config = null;
            }
        }

        public static bool IsOn(string feature)
        {
            return config?["features"]?[feature]?.Value<bool>() ?? false;
        }

        /// <summary>Endpoint URL from the config; "" when not provided.</summary>
        public static string Endpoint(string name)
        {
            return config?["endpoints"]?[name]?.Value<string>() ?? "";
        }
    }
}
