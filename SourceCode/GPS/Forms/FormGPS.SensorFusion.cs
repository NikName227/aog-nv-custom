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


            //raw logger can start with AOG (handy when chasing a problem in the field)
            if (s.setSF_autoLog && !rawLog.isLogging) rawLog.Start();
        }

        public void SaveSensorFusionSettings()
        {
            var s = Properties.VehicleSettings.Default;

            s.setSF_modelOn = kinModel.isOn;
            s.setSF_modelFloor = kinModel.posFloor;
            s.setSF_modelTau = kinModel.posTau;
            s.setSF_modelHFloor = kinModel.hdgFloor;
            s.setSF_modelHTau = kinModel.hdgTau;
            s.setSF_modelLeff = kinModel.leff;
            s.setSF_modelATau = kinModel.anchorTau;
            s.setSF_modelMixPos = kinModel.mixPos;
            s.setSF_modelMixHdg = kinModel.mixHdg;
            s.Save();
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
