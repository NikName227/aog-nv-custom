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

        public void LoadSensorFusionSettings()
        {
            var s = Properties.VehicleSettings.Default;
            cabinComp.isOn = s.setSF_cabinOn;
            cabinComp.kFast = s.setSF_kFast;
            cabinComp.fc = s.setSF_fc;
            cabinComp.lead = s.setSF_lead;
            cabinComp.tauSlow = s.setSF_tauSlow;

            headObs.isOn = s.setSF_obsOn;
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

            s.setSF_obsOn = headObs.isOn;
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

            double outHeading = fallbackHeadingRad;
            if (headObs.isOn)
            {
                double psi = headObs.Update(mc.actualSteerAngleDegrees, speedMs, courseRad, courseValid,
                                            ahrs.imuHeading, isReverse, sfDt);
                if (headObs.isInitialized) outHeading = psi;
            }
            else
            {
                headObs.psiRad = fallbackHeadingRad;
            }

            if (rawLog.isLogging) rawLog.Sample(sfRawEast, sfRawNorth);
            return outHeading;
        }
    }
}
