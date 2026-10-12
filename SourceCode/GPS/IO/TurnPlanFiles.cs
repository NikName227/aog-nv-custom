// TurnPlanFiles.cs - per-field pass plan.
// One file per implement working width (TurnPlan_W<width>.txt) so switching the
// implement keeps the old plan on disk and it comes back with the same implement.
// Header flags + one line per pass entry (offsetIdx,order,zone,sameWay,worked[,track]).
//
// F51 (2026-08-10): the width alone was never enough. The plan's geometry is shaped by the
// turn radii and by the extra boundary offset just as much - drive the same field with a
// different tractor and the stored plan no longer fits. Every file therefore carries a
// SIGNATURE of the values it was built with, and a plan whose signature does not match the
// current machine is ignored (a new one is generated). Actual values, not profile names: a
// profile can be renamed, and its numbers can be edited without renaming it.
// Plans written before this change have no signature line and are ignored as well.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace AgOpenGPS.IO
{
    public class TurnPlanData
    {
        public bool followPlan;
        public int mode = CTurnPlan.ModeZones;
        public int zoneCount = 2;
        public string zoneOrder = "";
        public string trackName = "";
        public bool clipHeadland = true;
        // working width the plan was built for; 0 = the file did not say (old file)
        public double builtWidth;
        // orchard: first inter-row the plan starts from (1 = from the beginning)
        public int startZone = 1;
        // REQ 2026-10-11-2: automatic zone borders on/off + the driver's own zone cuts
        public bool autoZones = true;
        public List<vec2> zoneCuts = new List<vec2>();
        // REQ 2026-10-12-1: the most the field end may step along inside a zone (m)
        public double maxEndStep = CTurnPlan.defaultMaxEndStep;
        public List<CPlanEntry> entries = new List<CPlanEntry>();
    }

    public static class TurnPlanFiles
    {
        // REQ 2026-10-12-1: entries became parts of a pass ("offsetIdx:piece"). A file without
        // this version was laid out without the parts and is ignored - a new plan is made.
        private const int planVersion = 2;

        /// <summary>
        /// The machine values the plan geometry depends on, as one comparable string.
        /// </summary>
        public static string Signature(double workWidth)
        {
            // 2026-08-12: the whole turn shape lives on the implement, so the signature is
            // read from one profile instead of two
            var v = AgOpenGPS.Properties.ToolSettings.Default;
            var t = AgOpenGPS.Properties.ToolSettings.Default;

            return string.Join("/", new[]
            {
                workWidth.ToString("0.000", CultureInfo.InvariantCulture),
                v.setTurn_youTurnRadius.ToString("0.000", CultureInfo.InvariantCulture),
                v.setTurn_K_exitRadius.ToString("0.000", CultureInfo.InvariantCulture),
                v.setTurn_K_entryStartDist.ToString("0.000", CultureInfo.InvariantCulture),
                v.setTurn_K_entryAngle.ToString("0.000", CultureInfo.InvariantCulture),
                v.setTurn_K_transitionRadius.ToString("0.000", CultureInfo.InvariantCulture),
                v.setTurn_U_exitRadius.ToString("0.000", CultureInfo.InvariantCulture),
                v.setTurn_U_entryRadius.ToString("0.000", CultureInfo.InvariantCulture),
                v.setTurn_U_overshootDist.ToString("0.000", CultureInfo.InvariantCulture),
                t.setTurn_boundaryOffset.ToString("0.000", CultureInfo.InvariantCulture)
            });
        }

        // e.g. TurnPlan_W3.00.txt - one file per width, signature checked inside
        private static string FileName(double workWidth)
        {
            return "TurnPlan_W" + workWidth.ToString("0.00", CultureInfo.InvariantCulture) + ".txt";
        }

        // Returns null when the field has no plan for this working width.
        public static TurnPlanData Load(string fieldDirectory, double workWidth)
        {
            var path = Path.Combine(fieldDirectory, FileName(workWidth));

            // fall back to the legacy single-plan file
            if (!File.Exists(path)) path = Path.Combine(fieldDirectory, "TurnPlan.txt");
            if (!File.Exists(path)) return null;

            var data = new TurnPlanData();
            string wantSig = Signature(workWidth);
            bool sigSeen = false;
            bool verOk = false;

            using (var reader = new StreamReader(path))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0 || line.StartsWith("$", StringComparison.Ordinal)) continue;

                    if (line.StartsWith("Sig,", StringComparison.Ordinal))
                    {
                        sigSeen = true;
                        //built for another machine -> not this plan
                        if (line.Substring(4).Trim() != wantSig) return null;
                        continue;
                    }

                    // track name may contain commas - take everything after the key
                    if (line.StartsWith("Track,", StringComparison.Ordinal))
                    {
                        data.trackName = line.Substring(6).Trim();
                        continue;
                    }

                    // zone order is free text ("3 | 2 | 1 || 4 | 5") - same treatment
                    if (line.StartsWith("ZoneOrder,", StringComparison.Ordinal))
                    {
                        data.zoneOrder = line.Substring(10).Trim();
                        continue;
                    }

                    var kv = line.Split(',');

                    // ZoneCut,<easting>,<northing> - one driver's zone border
                    if (kv.Length == 3 && kv[0] == "ZoneCut")
                    {
                        if (double.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double ce) &&
                            double.TryParse(kv[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double cn))
                            data.zoneCuts.Add(new vec2(ce, cn));
                        continue;
                    }

                    if (kv.Length == 2)
                    {
                        switch (kv[0].Trim())
                        {
                            case "PlanVer":
                                verOk = int.TryParse(kv[1].Trim(), NumberStyles.Integer,
                                    CultureInfo.InvariantCulture, out int pv) && pv == planVersion;
                                continue;
                            case "MaxEndStep":
                                if (double.TryParse(kv[1].Trim(), NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out double mes)) data.maxEndStep = mes;
                                continue;
                            case "AutoZones":
                                if (bool.TryParse(kv[1].Trim(), out bool az)) data.autoZones = az;
                                continue;
                            case "FollowPlan":
                                if (bool.TryParse(kv[1].Trim(), out bool f)) data.followPlan = f;
                                continue;
                            case "ClipHeadland":
                                if (bool.TryParse(kv[1].Trim(), out bool c)) data.clipHeadland = c;
                                continue;
                            case "Mode":
                                if (int.TryParse(kv[1].Trim(), NumberStyles.Integer,
                                    CultureInfo.InvariantCulture, out int m)) data.mode = m;
                                continue;
                            case "ZoneCount":
                                if (int.TryParse(kv[1].Trim(), NumberStyles.Integer,
                                    CultureInfo.InvariantCulture, out int z)) data.zoneCount = z;
                                continue;
                            //working width the plan was built for. The file name carries it
                            //too, but not for the legacy single-file fallback below - and the
                            //width is what decides whether the plan still fits the machine.
                            case "StartZone":
                                if (int.TryParse(kv[1].Trim(), NumberStyles.Integer,
                                    CultureInfo.InvariantCulture, out int sz)) data.startZone = sz;
                                continue;
                            case "Width":
                                if (double.TryParse(kv[1].Trim(), NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out double bw)) data.builtWidth = bw;
                                continue;
                        }
                    }

                    // pass entry: offsetIdx[:piece],order,zone,sameWay,worked[,skipped][,trackName]
                    // (skipped added 2026-07-11; old files without it still load)
                    string idxText = kv[0];
                    int piece = 0;
                    int colon = idxText.IndexOf(':');
                    if (colon > 0)
                    {
                        if (!int.TryParse(idxText.Substring(colon + 1), NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out piece)) continue;
                        idxText = idxText.Substring(0, colon);
                    }
                    if (kv.Length >= 5 &&
                        int.TryParse(idxText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx) &&
                        int.TryParse(kv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int order) &&
                        int.TryParse(kv[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int zone) &&
                        bool.TryParse(kv[3], out bool sameWay) &&
                        bool.TryParse(kv[4], out bool worked))
                    {
                        bool skipped = false;
                        int trackFrom = 5;
                        if (kv.Length > 5 && bool.TryParse(kv[5], out bool sk))
                        {
                            skipped = sk;
                            trackFrom = 6;
                        }

                        // rejoin any remaining fields: the manual-pass track name
                        string track = kv.Length > trackFrom
                            ? string.Join(",", kv, trackFrom, kv.Length - trackFrom).Trim()
                            : "";

                        data.entries.Add(new CPlanEntry
                        {
                            offsetIdx = idx,
                            piece = piece,
                            order = order,
                            zone = zone,
                            isSameWay = sameWay,
                            isWorked = worked,
                            isSkipped = skipped,
                            trackName = track
                        });
                    }
                }
            }

            //a plan from before signatures existed cannot be trusted to fit this machine
            if (!sigSeen) return null;
            //nor one laid out before passes were split into parts
            if (!verOk) return null;

            return data;
        }

        public static void Save(string fieldDirectory, CTurnPlan plan, double workWidth)
        {
            var path = Path.Combine(fieldDirectory, FileName(workWidth));

            // no plan -> no file for this width (keeps the field folder clean)
            if (!plan.HasPlan)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }

            using (var writer = new StreamWriter(path, false))
            {
                writer.WriteLine("$TurnPlan");
                writer.WriteLine("Sig," + Signature(workWidth));
                writer.WriteLine("PlanVer," + planVersion.ToString(CultureInfo.InvariantCulture));
                writer.WriteLine("FollowPlan," + plan.isFollowPlan);
                writer.WriteLine("ClipHeadland," + plan.clipToHeadland);
                writer.WriteLine("Mode," + plan.mode.ToString(CultureInfo.InvariantCulture));
                writer.WriteLine("ZoneCount," + plan.zoneCount.ToString(CultureInfo.InvariantCulture));
                writer.WriteLine("Width," + workWidth.ToString("0.000", CultureInfo.InvariantCulture));
                writer.WriteLine("StartZone," + plan.orchardStartZone.ToString(CultureInfo.InvariantCulture));
                if (!string.IsNullOrEmpty(plan.zoneOrder))
                    writer.WriteLine("ZoneOrder," + plan.zoneOrder);
                if (!string.IsNullOrEmpty(plan.trackName))
                    writer.WriteLine("Track," + plan.trackName);
                writer.WriteLine("AutoZones," + plan.autoZones);
                writer.WriteLine("MaxEndStep," + FileIoUtils.FormatDouble(plan.maxEndStep, 1));
                foreach (var c in plan.zoneCuts)
                    writer.WriteLine("ZoneCut," + FileIoUtils.FormatDouble(c.easting, 3) + ","
                        + FileIoUtils.FormatDouble(c.northing, 3));

                foreach (var p in plan.entries)
                {
                    writer.WriteLine(
                        p.offsetIdx.ToString(CultureInfo.InvariantCulture) +
                        (p.piece > 0 ? ":" + p.piece.ToString(CultureInfo.InvariantCulture) : "") + "," +
                        p.order.ToString(CultureInfo.InvariantCulture) + "," +
                        p.zone.ToString(CultureInfo.InvariantCulture) + "," +
                        p.isSameWay + "," +
                        p.isWorked + "," +
                        p.isSkipped +
                        (p.IsManual ? "," + p.trackName : ""));
                }
            }
        }
    }
}
