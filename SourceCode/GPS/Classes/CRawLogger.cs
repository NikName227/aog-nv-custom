// F10 Advanced Sensor Fusion (NV custom) — raw logger.
//
// One row per fix (10 Hz) into <Documents>\AgOpenGPS\Logs\AOG_AdvSensorFusion_<date>_<time>.csv:
// raw inputs (position, roll, heading, WAS, XTE...) plus every internal signal of the new
// chain (roll split, correction, position before/after the lateral filter, all heading
// sources). Column names follow the external analysis tool where possible so the same
// Python scripts can read these logs.
//
// Units: every angle is in degrees and every rate in deg/s, including pitch and yaw rate,
// which AOG itself keeps as raw deg*10 integers. xte_cm is the real cross track error and
// is logged with autosteer off as well - see the note at the column. Blank cell = the value
// does not exist this fix (no guidance line, lateral filter not running), never a fake 0.
// The geometry columns at the end (wheelbase, antenna, L_eff, fusion weight) repeat on every
// row so a log can be analysed without the vehicle settings next to it.
using AgOpenGPS.Core.Models;
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace AgOpenGPS
{
    public class CRawLogger
    {
        private readonly FormGPS mf;
        private StreamWriter sw;
        private DateTime startTime;
        private readonly StringBuilder sb = new StringBuilder(512);

        public bool isLogging;
        public string filePath = "";
        public long rows;
        public long bytes;

        private const long MaxBytes = 300L * 1024 * 1024;   // safety cap, ~300 MB

        public CRawLogger(FormGPS callingForm) { mf = callingForm; }

        public TimeSpan Elapsed { get { return isLogging ? DateTime.Now - startTime : TimeSpan.Zero; } }

        public string FileName { get { return string.IsNullOrEmpty(filePath) ? "" : Path.GetFileName(filePath); } }

        public bool Start()
        {
            Stop();
            try
            {
                string dir = RegistrySettings.logsDirectory;
                Directory.CreateDirectory(dir);
                startTime = DateTime.Now;
                filePath = Path.Combine(dir, "AOG_AdvSensorFusion_" + startTime.ToString("yyyy-MM-dd_HH-mm-ss") + ".csv");
                sw = new StreamWriter(filePath, false, Encoding.UTF8) { AutoFlush = true };
                sw.WriteLine(
                    "pc_time,t_s,type,fix_time,lat,lon,fix_q,sats,hdop,alt,age,speed_kn,heading_deg,roll_deg," +
                    "pitch_deg,yaw_rate,was_deg,label,switch,pwm,was_set_deg,xte_cm,steer_status,aog_speed_kmh," +
                    "raw_east,raw_north,fix_east,fix_north,roll_slow_deg,roll_fast_deg,corr_slow_m,corr_fast_m," +
                    "lat_before_m,lat_after_m,gps_heading_deg,imu_corrected_deg,obs_heading_deg,obs_bias_deg," +
                    "obs_model_yaw,obs_imu_yaw,obs_imu_w,cab_on,obs_on," +
                    "roll_raw_deg,fix_heading_deg,avg_speed_kmh,pivot_east,pivot_north," +
                    "dt_s,xte_valid,xte_pgn_cm,head_err_deg,kin_yaw_rate,fusion_w,roll_filter," +
                    "wheelbase_m,ant_pivot_m,ant_height_m,obs_leff_m,is_sim," +
                    "mdl_on,mdl_force,mdl_act,mdl_innov_cm,mdl_reject,mdl_bias,mdl_yaw,mdl_kin,mdl_vtg");
                rows = 0; bytes = 0;
                isLogging = true;
                return true;
            }
            catch (Exception ex)
            {
                AgLibrary.Logging.Log.EventWriter("AdvSensorFusion logger: " + ex.Message);
                sw = null; isLogging = false;
                return false;
            }
        }

        public void Stop()
        {
            isLogging = false;
            if (sw != null)
            {
                try { sw.Flush(); sw.Dispose(); } catch { }
                sw = null;
            }
        }

        public void Mark(string label)
        {
            if (!isLogging || sw == null) return;
            try
            {
                sw.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    //69 fields, label in the 18th - keep in sync with the header in Start()
                    "{0},{1:F3},MARK,,,,,,,,,,,,,,,{2},,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,",
                    DateTime.Now.ToString("HH:mm:ss.fff"), Elapsed.TotalSeconds, label));
            }
            catch { }
        }

        /// <summary>Called once per fix from UpdateFixPosition, after the whole chain has run.</summary>
        public void Sample(double rawEast, double rawNorth)
        {
            if (!isLogging || sw == null) return;

            try
            {
                double lat = 0, lon = 0;
                try
                {
                    Wgs84 ll = mf.AppModel.LocalPlane.ConvertGeoCoordToWgs84(
                        new GeoCoord(mf.pn.fix.northing, mf.pn.fix.easting));
                    lat = ll.Latitude; lon = ll.Longitude;
                }
                catch { }

                //is a guidance line actually being computed this fix? Same gate as the autosteer
                //region in Position.designer.cs:858,891 - without it modeActualXTE would keep the
                //last value from before the line was dropped.
                bool xteValid = mf.ct.isContourBtnOn
                    || (mf.trk.gArr != null && mf.trk.gArr.Count > 0
                        && mf.trk.idx >= 0 && mf.trk.idx < mf.trk.gArr.Count);

                sb.Length = 0;
                sb.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(',');
                sb.Append(Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture)).Append(",PANDA,,");
                sb.Append(lat.ToString("F9", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(lon.ToString("F9", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(mf.pn.fixQuality).Append(',');
                sb.Append(mf.pn.satellitesTracked).Append(',');
                N(mf.pn.hdop, 2); N(mf.pn.altitude, 2); N(mf.pn.age, 2);
                N(mf.pn.vtgSpeed < 9999 ? mf.pn.vtgSpeed : 0, 3);
                N(mf.ahrs.imuHeading, 2); N(mf.ahrs.imuRoll, 2);
                //AOG scales heading and roll by 0.1 on the PANDA input but leaves pitch and yaw
                //rate as the raw deg*10 integers (UDPComm.Designer.cs:152-162) - they are only
                //shown on a display there. Scale them here so the CSV is in real units throughout.
                N(mf.ahrs.imuPitch * 0.1, 2); N(mf.ahrs.imuYawRate * 0.1, 2);
                N(mf.mc.actualSteerAngleDegrees, 2);
                sb.Append(',');                                   // label
                sb.Append(',');                                   // switch (not in AOG)
                sb.Append(mf.mc.pwmDisplay).Append(',');
                N(mf.guidanceLineSteerAngle * 0.01, 2);
                //Real cross track error. guidanceLineDistanceOff is NOT usable for analysis: it is
                //forced to 32020 whenever the autosteer button is off (Position.designer.cs:948-950),
                //so a manually driven pass logs a constant. vehicle.modeActualXTE is written by every
                //guidance path on every fix, autosteer or not. Blank when no line is active.
                if (xteValid) N(mf.vehicle.modeActualXTE * 100.0, 2);
                else sb.Append(',');
                sb.Append(mf.isBtnAutoSteerOn ? 1 : 0).Append(',');
                N(mf.pn.speed, 2);
                N(rawEast, 4); N(rawNorth, 4);
                N(mf.pn.fix.easting, 4); N(mf.pn.fix.northing, 4);
                N(mf.cabinComp.rollSlowDeg, 3); N(mf.cabinComp.rollFastDeg, 3);
                N(mf.cabinComp.corrSlow, 4); N(mf.cabinComp.corrFast, 4);
                N(mf.cabinComp.latBefore, 4); N(mf.cabinComp.latAfter, 4);
                N(glm.toDegrees(mf.gpsHeading), 3);
                N(glm.toDegrees(mf.imuCorrected), 3);
                N(glm.toDegrees(mf.headObs.psiRad), 3);
                N(mf.headObs.biasDeg, 3);
                N(mf.headObs.modelYaw, 3); N(mf.headObs.imuYaw, 3); N(mf.headObs.imuWeight, 2);
                sb.Append(mf.cabinComp.isOn ? 1 : 0).Append(',');
                sb.Append(mf.headObs.isOn ? 1 : 0).Append(',');
                N(mf.ahrs.imuRollRaw, 2);
                N(glm.toDegrees(mf.fixHeading), 3);
                N(mf.avgSpeed, 2);
                N(mf.pivotAxlePos.easting, 4);
                N(mf.pivotAxlePos.northing, 4);

                N(mf.sfDt, 4);
                sb.Append(xteValid ? 1 : 0).Append(',');
                N(mf.guidanceLineDistanceOff * 0.1, 1);           // what the module got, mm -> cm
                N(mf.vehicle.modeActualHeadingError, 3);
                //yaw rate the geometry allows at this speed and steer angle - the ceiling any
                //heading signal has to stay under
                double wb = mf.vehicle.VehicleConfig.Wheelbase;
                N(wb > 0.1
                    ? glm.toDegrees(Math.Abs(mf.avgSpeed) / 3.6 * Math.Tan(glm.toRadians(mf.mc.actualSteerAngleDegrees)) / wb)
                    : double.NaN, 3);
                N(mf.ahrs.fusionWeight, 3);
                N(mf.ahrs.rollFilter, 3);
                N(wb, 3);
                N(mf.vehicle.VehicleConfig.AntennaPivot, 3);
                N(mf.vehicle.VehicleConfig.AntennaHeight, 3);
                N(mf.headObs.Leff, 3);
                sb.Append(mf.timerSim.Enabled ? 1 : 0).Append(',');

                //F10: kinematic model state - what the window shows, so the log tells the same story
                sb.Append(mf.kinModel.isOn ? 1 : 0).Append(',');
                sb.Append(mf.kinModel.forceOn ? 1 : 0).Append(',');
                N(mf.kinModel.activeFrac, 3);
                N(mf.kinModel.innovCm, 2);
                N(mf.kinModel.rejectPct, 1);
                N(mf.kinModel.biasDeg, 3);
                N(mf.kinModel.usedYaw, 3);
                N(mf.kinModel.kinLimit, 3);
                sb.Append(mf.kinModel.vtgShare.ToString("F2", CultureInfo.InvariantCulture));

                string line = sb.ToString();
                sw.WriteLine(line);
                rows++;
                bytes += line.Length + 2;
                if (bytes > MaxBytes) Stop();
            }
            catch { }
        }

        private void N(double v, int dec)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) sb.Append(',');
            else sb.Append(v.ToString("F" + dec, CultureInfo.InvariantCulture)).Append(',');
        }
    }
}
