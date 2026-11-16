using AgOpenGPS.Core.Models;
using System;

namespace AgOpenGPS
{
    public class CSim
    {
        private readonly FormGPS mf;

        #region properties sim

        public Wgs84 CurrentLatLon { get; set; }

        public double headingTrue, stepDistance = 0.0, steerAngle, steerangleAve = 0.0;
        public double steerAngleScrollBar = 0;

        public bool isAccelForward, isAccelBack;

        //steer rate at High Steer PWM 255 [deg/s]
        private const double simFullSteerRate = 60.0;

        #endregion properties sim

        public CSim(FormGPS _f)
        {
            mf = _f;
            CurrentLatLon = new Wgs84(
                Properties.Settings.Default.setGPS_SimLatitude,
                Properties.Settings.Default.setGPS_SimLongitude);
        }

        // REQ 2026-11-16-1 (simulator like the tractor): the wheels follow the command after the
        // dead time, speed and acceleration typed in the turn calibration (VehicleSettings) - the
        // same numbers the turn steering predicts with, so a changed value (by hand or Teach + OK)
        // is driven at once. CurrentLatLon is the REAR AXLE; the antenna is worked out from it.
        private readonly System.Diagnostics.Stopwatch simClock = System.Diagnostics.Stopwatch.StartNew();
        private double simTime, lastReal = -1, steerRate;
        private readonly System.Collections.Generic.List<SimCmd> cmdHist = new System.Collections.Generic.List<SimCmd>();
        private struct SimCmd { public double t, v; }

        //sim seconds that passed since the last tick: real time (F6 fast mode keeps running faster)
        private double SimStep()
        {
            double real = simClock.Elapsed.TotalSeconds;
            double nominal = mf.timerSim.Interval * 0.001;
            double dt = lastReal < 0 ? nominal : real - lastReal;
            lastReal = real;
            if (dt < 0.005) dt = 0.005;
            if (dt > 0.25) dt = 0.25;
            if (mf.timerSim.Interval < 20) dt *= 93.0 / mf.timerSim.Interval;
            return dt;
        }

        public void DoSimTick(double _st)
        {
            steerAngle = _st;
            double dt = SimStep();
            simTime += dt;
            var veh = Properties.VehicleSettings.Default;

            //dead time: the wheels go for the angle asked for lead seconds ago
            double lead = Math.Max(0, Math.Min(1.0, veh.setTurn_leadTime));
            cmdHist.Add(new SimCmd { t = simTime, v = steerAngle });
            while (cmdHist.Count > 1 && cmdHist[1].t <= simTime - lead) cmdHist.RemoveAt(0);
            double target = cmdHist[0].t <= simTime - lead ? cmdHist[0].v : steerangleAve;
            if (cmdHist.Count > 400) cmdHist.RemoveAt(0);

            //top speed = typed steer rate at High Steer PWM 255 (REQ 2026-10-17-1: lower PWM = slower
            //valve, live PGN 252 value), acceleration = typed, 0 = 4 x rate (as the turn limiter)
            double pwm = Math.Max(1, (int)mf.p_252.pgn[mf.p_252.highPWM]);
            double vMax = Math.Max(1.0, Math.Min(simFullSteerRate, veh.setTurn_steerRate)) * pwm / 255.0;
            double aMax = veh.setTurn_steerAccel > 1.0 ? Math.Min(600.0, veh.setTurn_steerAccel) : 4.0 * vMax;
            double err = target - steerangleAve;
            double vWant = Math.Sign(err) * Math.Min(vMax, Math.Min(Math.Sqrt(2.0 * aMax * Math.Abs(err)), Math.Abs(err) / dt));
            double dv = vWant - steerRate, dvMax = aMax * dt;
            if (dv > dvMax) dv = dvMax; else if (dv < -dvMax) dv = -dvMax;
            steerRate += dv;
            double step = steerRate * dt;
            //never past the target
            if ((err >= 0 && step > err) || (err <= 0 && step < err)) { step = err; steerRate = step / dt; }
            steerangleAve += step;

            mf.mc.actualSteerAngleDegrees = steerangleAve;

            //bicycle model on the rear axle with the vehicle's own wheelbase
            //(stepDistance stays "per 0.1 s" as the speed buttons and recorded path set it)
            double dist = stepDistance * dt / 0.1;
            double wb = Math.Max(0.5, Math.Abs(mf.vehicle.VehicleConfig.Wheelbase));
            double dHead = dist * Math.Tan(glm.toRadians(steerangleAve)) / wb;
            CurrentLatLon = CurrentLatLon.CalculateNewPostionFromBearingDistance(headingTrue + dHead * 0.5, dist);
            headingTrue += dHead;
            if (headingTrue > glm.twoPI) headingTrue -= glm.twoPI;
            if (headingTrue < 0) headingTrue += glm.twoPI;

            mf.pn.vtgSpeed = Math.Abs(Math.Round(4 * stepDistance * 10, 2));
            mf.pn.AverageTheSpeed();

            //antenna: ahead of the axle by antenna pivot, and to the side so that AOG's own antenna
            //offset correction brings it back onto the centre line
            var cfg = mf.vehicle.VehicleConfig;
            Wgs84 antenna = CurrentLatLon;
            if (cfg.AntennaPivot != 0) antenna = antenna.CalculateNewPostionFromBearingDistance(headingTrue, cfg.AntennaPivot);
            if (cfg.AntennaOffset != 0) antenna = antenna.CalculateNewPostionFromBearingDistance(headingTrue - glm.PIBy2, cfg.AntennaOffset);

            GeoCoord fixCoord = mf.AppModel.LocalPlane.ConvertWgs84ToGeoCoord(antenna);
            mf.pn.fix.northing = fixCoord.Northing;
            mf.pn.fix.easting = fixCoord.Easting;
            mf.pn.headingTrue = mf.pn.headingTrueDual = glm.toDegrees(headingTrue);
            mf.ahrs.imuHeading = mf.pn.headingTrue;
            if (mf.ahrs.imuHeading >= 360) mf.ahrs.imuHeading -= 360;

            mf.AppModel.CurrentLatLon = antenna;

            mf.pn.hdop = 0.7;

            mf.pn.altitude = SimulateAltitude(mf.AppModel.CurrentLatLon);

            mf.pn.satellitesTracked = 12;

            mf.sentenceCounter = 0;

            mf.UpdateFixPosition();

            if (isAccelForward)
            {
                isAccelBack = false;
                stepDistance += 0.02;
                if (stepDistance > 0.12) isAccelForward = false;
            }
            if (isAccelBack)
            {
                isAccelForward = false;
                stepDistance -= 0.01;
                if (stepDistance < -0.06) isAccelBack = false;
            }
        }

        private double SimulateAltitude(Wgs84 latLon)
        {
            double temp = Math.Abs(latLon.Latitude * 100);
            temp -= ((int)(temp));
            temp *= 100;
            double altitude = temp + 200;

            temp = Math.Abs(latLon.Longitude * 100);
            temp -= ((int)(temp));
            temp *= 100;
            altitude += temp;
            return altitude;
        }

    }
}