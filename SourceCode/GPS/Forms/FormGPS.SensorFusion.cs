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
            cabinComp.isOn = s.setSF_cabinOn;
            cabinComp.kFast = s.setSF_kFast;
            cabinComp.fc = s.setSF_fc;
            cabinComp.lead = s.setSF_lead;
            cabinComp.tauSlow = s.setSF_tauSlow;

            kinModel.isOn = s.setSF_modelOn;
            kinModel.imuShare = s.setSF_modelImuW;
            kinModel.Reset();

            //the observer stays instantiated only for the L_eff learning; it no longer drives
            //the heading - the kinematic model does (REQ 2026-09-17-1)
            headObs.isOn = false;
            headObs.Kp = s.setSF_obsKp;
            headObs.Ki = s.setSF_obsKi;
            headObs.wImu = s.setSF_obsImuWeight;
            headObs.Leff = s.setSF_obsLeff;
            headObs.biasDeg = s.setSF_obsWasBias;
            headObs.KpTurn = s.setSF_obsKpTurn;
            headObs.wasLo = s.setSF_obsWasLo;
            headObs.wasHi = s.setSF_obsWasHi;

            cabinComp.Reset();
            headObs.Reset();

            //raw logger can start with AOG (handy when chasing a problem in the field)
            if (s.setSF_autoLog && !rawLog.isLogging) rawLog.Start();
        }

        public void SaveSensorFusionSettings()
        {
            var s = Properties.VehicleSettings.Default;
            s.setSF_cabinOn = cabinComp.isOn;
            s.setSF_kFast = cabinComp.kFast;
            s.setSF_fc = cabinComp.fc;
            s.setSF_lead = cabinComp.lead;
            s.setSF_tauSlow = cabinComp.tauSlow;

            s.setSF_modelOn = kinModel.isOn;
            s.setSF_modelImuW = kinModel.imuShare;
            s.setSF_obsOn = false;
            s.setSF_obsKp = headObs.Kp;
            s.setSF_obsKi = headObs.Ki;
            s.setSF_obsImuWeight = headObs.wImu;
            s.setSF_obsLeff = headObs.Leff;
            s.setSF_obsWasBias = headObs.biasDeg;
            s.setSF_obsKpTurn = headObs.KpTurn;
            s.setSF_obsWasLo = headObs.wasLo;
            s.setSF_obsWasHi = headObs.wasHi;
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

        /// <summary>Roll correction distance - new chain when on, old formula when off.</summary>
        public double SensorFusionRollCorrection(double rollDeg)
        {
            return cabinComp.RollCorrection(rollDeg, vehicle.VehicleConfig.AntennaHeight, sfDt);
        }

        /// <summary>Lateral filter on the corrected fix (once per fix, after the roll correction).</summary>
        public void SensorFusionFilterFix(double headingRad)
        {
            double e = pn.fix.easting, n = pn.fix.northing;
            cabinComp.FilterFix(ref e, ref n, headingRad, Math.Abs(pn.speed) / 3.6, sfDt);
            pn.fix.easting = e;
            pn.fix.northing = n;
        }

        /// <summary>
        /// Heading observer + learning + raw log. Called once per fix after the heading is known.
        /// Returns the heading that guidance should use [rad].
        /// </summary>
        public double SensorFusionHeading(double courseRad, bool courseValid, double fallbackHeadingRad)
        {
            double speedMs = Math.Abs(pn.speed) / 3.6;

            // learning always runs from the RAW signals, so it is independent of what is enabled
            if (cabinComp.learn.running)
                cabinComp.learn.Sample(sfRawEast, sfRawNorth, ahrs.imuRoll, courseRad, speedMs, sfDt);
            if (headObs.learn.running && courseValid)
                headObs.learn.Sample(mc.actualSteerAngleDegrees, speedMs, courseRad, isReverse, sfDt);

            headObs.psiRad = fallbackHeadingRad;
            return fallbackHeadingRad;
        }

        /// <summary>
        /// Kinematic model: runs after AOG's own chain has produced its position and heading,
        /// because it uses that chain both as the passive solution and as the independent
        /// anchor for the WAS zero. Replaces the fix and the heading when active.
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

            kinModel.mfLeff = headObs.Leff;
            kinModel.Update(aogE, aogN, headingRad, mc.actualSteerAngleDegrees,
                            Math.Abs(pn.vtgSpeed < 9999 ? pn.vtgSpeed : pn.speed) / 3.6,
                            sfImuUnwrapped, gpsHeading,
                            avgSpeed > startSpeed && isFirstHeadingSet,
                            isBtnAutoSteerOn, sfElapsed, sfDt);

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
