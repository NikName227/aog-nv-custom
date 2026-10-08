// F10 Advanced Sensor Fusion (NV custom) — glue between FormGPS and the two independent
// functions (cabin compensation, heading observer) plus the raw logger.
//
// The two functions never depend on each other: a fixed-cab tractor only turns the observer
// on, a tractor without a usable WAS only turns the cabin compensation on, and with both off
// the position/heading code runs exactly as before.
using System;

namespace AgOpenGPS
{
    public partial class FormGPS
    {
        public double sfDt = 0.1;                  // measured fix interval [s]
        private double sfRawEast, sfRawNorth;      // antenna position before any correction
        private double sfImuUnwrapped = double.NaN; // IMU heading, unwrapped for the model
        private double sfElapsed;                   // running time for the model [s]

        public void LoadSensorFusionSettings()
        {
            var s = Properties.VehicleSettings.Default;

            kinModel.isOn = s.setSF_modelOn;
            kinModel.mixPos = s.setSF_modelMixPos;
            kinModel.mixHdg = s.setSF_modelMixHdg;

            //sway compensation step: a customer setting, so it is read on every install, not only
            //in service mode. ApplyPreset() is what puts it into posFloor.
            kinModel.ApplyPreset((CKinematicModel.SwayPreset)s.setSF_swayPreset);

            // The tunables are release constants unless this install is in service mode. Reading
            // them from the profile only in debug means a machine behaves identically on every
            // install regardless of what an earlier session left in its vehicle XML, and that a
            // tuned machine falls back to standard the moment the debug flag is taken away.
            if (CFeatureLock.IsDebug)
            {
                kinModel.posFloor = s.setSF_modelFloor;
                kinModel.posTau = s.setSF_modelTau;
                kinModel.hdgFloor = s.setSF_modelHFloor;
                kinModel.hdgTau = s.setSF_modelHTau;
                kinModel.leff = s.setSF_modelLeff;
                kinModel.anchorTau = s.setSF_modelATau;
            }
            else kinModel.ReleaseDefaults();

            kinModel.Reset();

            rollCorr.isOn = s.setSF_rollCorrOn;
            rollCorr.reactionSec = Math.Max(1.0, Math.Min(8.0, s.setSF_rollReactionSec));
            rollCorr.Reset();

            tm171.useRoll = s.setSF_tmUseRoll;
            tm171.portName = s.setSF_tmPort ?? "";
            tm171.baud = s.setSF_tmBaud > 0 ? s.setSF_tmBaud : 115200;
            tm171.usePitchAsRoll = s.setSF_tmPitchAsRoll;
            tm171.invert = s.setSF_tmInvert;
            tm171.zeroDeg = s.setSF_tmZero;
            //the port is read whenever one is set, so the window and the log show the chassis
            //roll before it is switched into the position
            if (tm171.portName.Length > 0) tm171.Start(); else tm171.Stop();

            //raw logger can start with AOG (handy when chasing a problem in the field)
            if (s.setSF_autoLog && !rawLog.isLogging) rawLog.Start();
        }

        public void SaveSensorFusionSettings()
        {
            var s = Properties.VehicleSettings.Default;

            s.setSF_modelOn = kinModel.isOn;
            s.setSF_swayPreset = (int)kinModel.preset;
            s.setSF_modelFloor = kinModel.posFloor;
            s.setSF_modelTau = kinModel.posTau;
            s.setSF_modelHFloor = kinModel.hdgFloor;
            s.setSF_modelHTau = kinModel.hdgTau;
            s.setSF_modelLeff = kinModel.leff;
            s.setSF_modelATau = kinModel.anchorTau;
            s.setSF_modelMixPos = kinModel.mixPos;
            s.setSF_modelMixHdg = kinModel.mixHdg;
            s.setSF_rollCorrOn = rollCorr.isOn;
            s.setSF_rollReactionSec = rollCorr.reactionSec;
            s.setSF_tmUseRoll = tm171.useRoll;
            s.setSF_tmPort = tm171.portName;
            s.setSF_tmBaud = tm171.baud;
            s.setSF_tmPitchAsRoll = tm171.usePitchAsRoll;
            s.setSF_tmInvert = tm171.invert;
            s.setSF_tmZero = tm171.zeroDeg;
            s.Save();
        }

        public double sfCabRoll = double.NaN;      // PANDA roll (BNO in the cab), for the log
        public double sfCabHeading = double.NaN;   // PANDA heading (BNO in the cab), for the log
        public int sfRollSource;                    // 0 PANDA, 1 TM171, 2 TM171 on but stale -> roll 0

        /// <summary>
        /// End of the PANDA block, before UpdateFixPosition (REQ 2026-10-02-1, 2026-10-03-1). With
        /// the TM171 switch on, its chassis roll and heading replace the PANDA ones; never a quiet
        /// fall back to the BNO - another mounting, another zero. Testing only, so no more checks:
        /// stale data means roll 0, the heading keeps the last value the TM171 sent.
        /// With the switch off nothing here touches ahrs.
        /// </summary>
        public void SensorFusionChassisRoll()
        {
            sfCabRoll = ahrs.imuRollRaw;
            sfCabHeading = ahrs.imuHeading;
            if (!tm171.useRoll)
            {
                //just switched back: the PANDA path blended its filter into our values (or 0),
                //so restart it from the BNO's own reading
                if (sfRollSource != 0) ahrs.imuRoll = ahrs.imuRollRaw;
                sfRollSource = 0; sfTmFiltered = double.NaN;
                return;
            }

            if (tm171.IsFresh)
            {
                //own filter state: ahrs.imuRoll was just blended with the BNO by the PANDA path
                double r = tm171.Roll;
                sfTmFiltered = double.IsNaN(sfTmFiltered) ? r
                    : sfTmFiltered * ahrs.rollFilter + r * (1 - ahrs.rollFilter);
                ahrs.imuRollRaw = r;
                ahrs.imuRoll = sfTmFiltered;
                sfRollSource = 1;
            }
            else
            {
                ahrs.imuRollRaw = 0;
                ahrs.imuRoll = 0;
                sfTmFiltered = double.NaN;
                sfRollSource = 2;
            }

            //heading: turns the same way as AOG's, its zero is lined up by imuGPS_Offset
            if (tm171.HasData)
            {
                double h = tm171.Yaw % 360.0;
                if (h < 0) h += 360.0;
                ahrs.imuHeading = h;
            }
        }
        private double sfTmFiltered = double.NaN;

        /// <summary>
        /// Roll correction distance for the Fix/VTG/Dual paths (REQ 2026-09-30-1). Same sign and
        /// meaning as AOG's own "Math.Sin(roll) * -AntennaHeight": with the switch off it returns
        /// exactly that, with it on the correction checked against the raw antenna. Both are
        /// computed on every fix so the raw log always carries the old and the new value.
        /// </summary>
        public double SensorFusionRollDistance(double headingRad)
        {
            double h = vehicle.VehicleConfig.AntennaHeight;
            if (!rollCorr.isOn)
            {
                //keep the log columns alive, but return AOG's expression untouched
                rollCorr.Update(pn.fix.easting, pn.fix.northing, ahrs.imuRoll, headingRad, h, sfDt, sfElapsed);
                return Math.Sin(glm.toRadians(ahrs.imuRoll)) * -h;
            }
            return -rollCorr.Update(pn.fix.easting, pn.fix.northing, ahrs.imuRoll, headingRad, h, sfDt, sfElapsed);
        }

        /// <summary>Start of every fix: measure dt and remember the raw antenna position.</summary>
        public void SensorFusionNewFix()
        {
            sfElapsed += Math.Max(0.0, Math.Min(1.0, timeSliceOfLastFix));
            double dt = timeSliceOfLastFix;             // measured at the top of UpdateFixPosition
            if (dt < 0.02 || dt > 1.0) dt = 0.1;        // dropped packet / first fix
            sfDt = dt;

            sfRawEast = pn.fix.easting;
            sfRawNorth = pn.fix.northing;
        }

        /// <summary>
        /// Kinematic model: runs after AOG's own chain has produced its position and heading,
        /// because it uses that chain both as the passive solution and as the reference the
        /// output is blended against. Replaces the fix and the heading when active - by separate
        /// shares, since the field says the model earns its keep on position, not on heading.
        /// </summary>
        public double SensorFusionModel(double headingRad)
        {
            if (!kinModel.isOn) return headingRad;

            //IMU heading unwrapped, so the 5-point slope does not jump at the 360 seam
            double ih = ahrs.imuHeading;
            if (ih > 65000) ih = glm.toDegrees(headingRad);
            if (double.IsNaN(sfImuUnwrapped)) sfImuUnwrapped = ih;
            else
            {
                double d = ih - sfImuUnwrapped;
                while (d > 180) d -= 360;
                while (d < -180) d += 360;
                sfImuUnwrapped += d;
            }

            //AOG's chain, at the pivot axle - the model works on the vehicle, not the antenna
            double aogE = pn.fix.easting - Math.Sin(headingRad) * vehicle.VehicleConfig.AntennaPivot;
            double aogN = pn.fix.northing - Math.Cos(headingRad) * vehicle.VehicleConfig.AntennaPivot;

            kinModel.Update(aogE, aogN, headingRad, mc.actualSteerAngleDegrees,
                            Math.Abs(pn.vtgSpeed < 9999 ? pn.vtgSpeed : pn.speed) / 3.6,
                            sfImuUnwrapped, gpsHeading,
                            avgSpeed > startSpeed && isFirstHeadingSet,
                            isBtnAutoSteerOn, sfElapsed, sfDt,
                            vehicle.VehicleConfig.Wheelbase);

            double oE, oN, oP;
            kinModel.Output(aogE, aogN, headingRad, out oE, out oN, out oP);

            //back to the antenna frame, so the rest of AOG is untouched
            pn.fix.easting = oE + Math.Sin(oP) * vehicle.VehicleConfig.AntennaPivot;
            pn.fix.northing = oN + Math.Cos(oP) * vehicle.VehicleConfig.AntennaPivot;
            return oP;
        }

        /// <summary>
        /// One log row per fix. Called at the very end of the autosteer block in UpdateFixPosition,
        /// not from SensorFusionHeading: the heading is known long before pivotAxlePos is rebuilt
        /// and the guidance line is evaluated, so logging there put pivot_east/north and the XTE
        /// one fix (100 ms) behind everything else in the same row.
        /// </summary>
        public void SensorFusionLog()
        {
            if (rawLog.isLogging) rawLog.Sample(sfRawEast, sfRawNorth);
        }
    }
}
