using AgLibrary.Settings;
using AgOpenGPS.Core.Models;
using System.IO;

namespace AgOpenGPS.Properties
{
    public sealed class VehicleSettings
    {
        private static VehicleSettings settings_ = new VehicleSettings();

        public static VehicleSettings Default
        {
            get { return settings_; }
        }

        // Vehicle dimensions
        public double setVehicle_wheelbase = 3.3;
        public double setVehicle_antennaHeight = 3;
        public double setVehicle_antennaPivot = 0.1;
        public double setVehicle_antennaOffset = 0;
        public double setVehicle_maxSteerAngle = 30;
        public double setVehicle_maxAngularVelocity = 0.64;
        public double setVehicle_trackWidth = 1.9;

        // AutoSteer settings
        public byte setAS_Kp = 50;
        public byte setAS_countsPerDegree = 110;
        public byte setAS_minSteerPWM = 25;
        public byte setAS_highSteerPWM = 180;
        public byte setAS_lowSteerPWM = 30;
        public int setAS_wasOffset = 3;
        public double setAS_sideHillComp = 0.0;
        public byte setAS_ackerman = 100;
        public double setAS_ModeXTE = 0.1;
        public int setAS_ModeTime = 1;
        public double setAS_functionSpeedLimit = 12;
        public double setAS_maxSteerSpeed = 15;
        public double setAS_minSteerSpeed = 0;
        public bool setAS_isSteerInReverse = false;

        // IMU settings
        public double setIMU_rollZero = 0.0;
        public double setIMU_rollFilter = 0.0;
        public bool setIMU_invertRoll = false;
        public bool setIMU_isDualAsIMU = false;
        public double setIMU_fusionWeight2 = 0.06;

        // F10 Advanced Sensor Fusion — cabin compensation (independent of the observer)
        public bool setSF_cabinOn = false;
        public double setSF_kFast = 1.4;        // [m] lever of the fast (cabin) roll part
        public double setSF_fc = 0.0;           // [Hz] lateral filter cutoff (0 = off)
        public double setSF_lead = 0.7;         // [0..1] how much filter delay is given back
        public double setSF_tauSlow = 1.0;      // [s] terrain / cabin split

        // F10 Advanced Sensor Fusion — kinematic model (replaces the heading observer)
        public bool setSF_modelOn = false;
        public double setSF_modelImuW = 0.25;   // [0..0.6] slider WAS <-> IMU
        public double setSF_modelFloor = 0.30;  // gain floor; tau/floor is the effective time const
        public double setSF_modelTau = 0.4;     // [s] correction time constant
        public double setSF_modelHFloor = 0.35; // same, for the heading correction
        public double setSF_modelHTau = 0.8;    // [s]

        // F10 — heading observer: retired from the window, kept only for L_eff learning
        public bool setSF_obsOn = false;
        public double setSF_obsKp = 0.3;
        public double setSF_obsKi = 0.004;
        public double setSF_obsImuWeight = 0.25;
        public double setSF_obsLeff = 4.2;      // [m] effective wheelbase (incl. slip), learned
        public double setSF_obsWasBias = 0.0;   // [deg] WAS zero, learned / kept between runs
        public double setSF_obsKpTurn = 1.0;    // correction strength while the IMU is the measurement
        public double setSF_obsWasLo = 5.0;     // [deg] below: measurement = course
        public double setSF_obsWasHi = 15.0;    // [deg] above: measurement = IMU
        public bool setSF_autoLog = false;      // start the raw logger with AOG

        // GPS settings
        public string setGPS_headingFromWhichSource = "Fix";
        public double setGPS_forwardComp = 0.15;
        public double setGPS_reverseComp = 0.3;
        public double setGPS_dualHeadingOffset = 0.0;
        public double setGPS_dualReverseDetectionDistance = 0.25;
        public double setGPS_minimumStepLimit = 0.05;

        // Arduino Steer
        public byte setArdSteer_setting0 = 56;
        public byte setArdSteer_setting1 = 0;
        public byte setArdSteer_maxPulseCounts = 3;
        public bool setArdMac_isDanfoss = false;

        // Brands
        public TractorBrand setBrand_TBrand = TractorBrand.AGOpenGPS;
        public HarvesterBrand setBrand_HBrand = HarvesterBrand.AgOpenGPS;
        public ArticulatedBrand setBrand_WDBrand = ArticulatedBrand.AgOpenGPS;

        // Vehicle type
        public int setVehicle_vehicleType = 0;
        public double setVehicle_panicStopSpeed = 0;

        // ===== TURN SHAPE (F51, 2026-08-10) =====
        // How THIS tractor turns. These used to live in the application settings, which tied
        // them to the tablet: swapping to another tractor kept the previous one's radii.
        // A radius of 0 still means "use the general turn radius".
        public double setTurn_youTurnRadius = 8.1;
        public double setTurn_K_exitRadius = 8;         // K: exit arc radius
        public double setTurn_K_entryStartDist = 2;     // K: metres before the new line the entry begins
        public double setTurn_K_entryAngle = 40;        // K: entry straight angle to the new line (deg)
        public double setTurn_K_transitionRadius = 1.0; // K: unused (rigid K - covered by path smoothing)
        public double setTurn_U_exitRadius = 8;         // U: exit (outer) radius
        public double setTurn_U_entryRadius = 8;        // U: entry (inner) radius
        public double setTurn_U_overshootDist = 0.5;    // U: virtual line offset beyond AB

        public LoadResult Load(string vehicleFileName)
        {
            string path = Path.Combine(RegistrySettings.vehiclesDirectory, vehicleFileName + ".xml");
            var result = XmlSettingsHandler.LoadXMLFile(path, this);
            if (result == LoadResult.MissingFile)
            {
                // Try loading from old format and migrate
                return CSettingsMigration.MigrateVehicle(vehicleFileName, this);
            }

            // Update registry with the loaded vehicle file name
            if (result == LoadResult.Ok)
            {
                RegistrySettings.vehicleProfileName = vehicleFileName;
            }

            return result;
        }

        /// <summary>
        /// Save vehicle settings to file using the file name from registry.
        /// Uses "DefaultVehicle" as fallback if registry value is empty.
        /// </summary>
        public void Save()
        {
            // Read file name from registry, use fallback if empty
            string fileName = string.IsNullOrEmpty(RegistrySettings.vehicleProfileName)
                ? "DefaultVehicle"
                : RegistrySettings.vehicleProfileName;

            string path = Path.Combine(RegistrySettings.vehiclesDirectory, fileName + ".xml");
            XmlSettingsHandler.SaveXMLFile(path, this);
        }

        /// <summary>
        /// Save vehicle settings to a specific file (used during migration).
        /// This overload saves to a custom file name without updating the registry.
        /// </summary>
        /// <param name="fileName">The file name to save to (without extension)</param>
        public void Save(string fileName)
        {
            string path = Path.Combine(RegistrySettings.vehiclesDirectory, fileName + ".xml");
            XmlSettingsHandler.SaveXMLFile(path, this);
        }

        public void Reset()
        {
            settings_ = new VehicleSettings();
        }
    }
}
