using AgLibrary.Settings;
using System.Drawing;
using System.IO;

namespace AgOpenGPS.Properties
{
    public sealed class ToolSettings
    {
        private static ToolSettings settings_ = new ToolSettings();

        public static ToolSettings Default
        {
            get { return settings_; }
        }

        // Tool dimensions
        public double setVehicle_toolWidth = 4.0;
        public double setVehicle_toolOverlap = 0.0;
        public double setVehicle_toolOffset = 0;
        public double setVehicle_tankTrailingHitchLength = 3;
        public double setVehicle_toolTrailingHitchLength = -2.5;
        public double setVehicle_toolLookAheadOn = 1;
        public double setVehicle_toolLookAheadOff = 0.5;
        public double setVehicle_hitchLength = -1;

        public bool setTool_isToolTrailing = true;
        public bool setTool_isToolRearFixed = false;
        public bool setTool_isToolTBT = false;
        public bool setTool_isToolFront = false;

        // Orchard tool type (F50): the processing settings belong to the IMPLEMENT -
        // changing the machine changes the pass pattern, the orchard rows stay put
        public bool setTool_isToolOrchard = false;
        // How the AB lines are derived from the rows (SIM feedback 2026-07-29):
        //   0 = one pass  - a single line down the middle of each inter-row
        //   1 = multi pass- several passes per inter-row (the settings below)
        //   2 = on row    - the line IS the row + planter offset (planting a new orchard)
        // -1 = not chosen yet -> migrated once from setTool_orchardOnePass. That old flag
        // is kept (never renamed): settings are serialized BY FIELD NAME, so renaming it
        // would silently throw away what is already saved on the machine (F05 lesson).
        public int setTool_orchardPathMode = -1;
        // Multi pass only: how many passes per inter-row. 0 = work it out from the tool
        // width; >= 3 = exactly that many, whatever the width and the overlap. 1 and 2 are
        // refused by the UI - they would be the one-pass and two-pass modes.
        // Orchard window reference point (SIM 0730): where the thing that MEASURES sits -
        // a pole on the front, the recorder's own offset - relative to the tractor. Lateral is
        // + to the right of travel, longitudinal + forward from the rear axle. Deliberately not
        // the implement geometry: mapping an orchard with a pole must not mean rewriting the
        // implement profile (which would change coverage and the planting path).
        public double setTool_orchardRefLateral = 2.0;
        public double setTool_orchardRefLong = 0;

        // Marking trees while recording: a press closer than this to the last tree is ignored,
        // so a double tap does not put two trees on one trunk.
        public double setTool_orchardTreeMinDist = 1.0;

        public int setTool_orchardPassCount = 0;
        // DEAD since F50 fix 2026-08-01 - nothing reads it. The implement side is the SIGN of
        // setVehicle_toolOffset (Tool -> Tool Offset); this was a second definition of the same
        // fact that nothing kept in sync. Kept only so an older build still finds its value.
        public int setTool_orchardWorkMode = 0;        // 0 = center, 1 = left, 2 = right
        // Full width of the band around a row line that is NOT worked (SIM 0730: measured
        // in the field as the untouched strip around the trees, which is easier to judge than
        // a one-sided margin). Half of it is the clearance the tool edge keeps from the row.
        public double setTool_orchardTreeWidth = 1.0;   // m, the whole unworked band
        public bool setTool_orchardOnePass = false;    // force 1 center pass per inter-row (spraying)
        // Orders the pass BUILD (numbering, and the sequence handed to the field plan). Off the
        // settings panel since 2026-08-01 - the driver picks a pass by steering onto it, so on
        // screen it did nothing; the sequencing gets designed in the turn plan.
        public bool setTool_orchardCenterFirst = true; // odd passes: center first, then outward

        // Largest steering angle allowed while the machine is between the trees (deg, 0 = no cap).
        // A lost or wobbling fix must not turn into a big wheel angle in a row only as wide as the
        // gap between two trunks. Lifted for the last 10 m at each row end, where the machine
        // lines itself up and turns - see COrchard.BuildLimitZone.
        public double setTool_orchardMaxSteerAngle = 8.0;

        // Planter geometry (F50 planting): where the seedling actually lands, relative
        // to the tractor. Guidance still steers the pivot - only the "next tree" HUD and
        // the planted marking use this point.
        public bool setTool_isSectionsNotZones = true;
        public bool setTool_isSectionOffWhenOut = true;
        public bool setTool_isDisplayTramControl = true;
        public bool setTool_isTramOuterInverted = false;
        public bool setTool_isDirectionMarkers = true;
        public double setTool_trailingToolToPivotLength = 0;

        // Sections
        public int setVehicle_numSections = 3;
        public int setTool_numSectionsMulti = 20;
        public bool setSection_isFast = true;
        public double setTool_defaultSectionWidth = 2;
        public double setTool_sectionWidthMulti = 0.5;
        public string setTool_zones = "2,10,20,0,0,0,0,0,0";

        // ===== TURN, IMPLEMENT SIDE (F51, 2026-08-10) =====
        // Extra distance kept from a drawn turn limit line, ON TOP of the wheel-safety margin
        // (which is the outer front wheel's swept path). A wide implement may not let the
        // tractor drive right up to the line, and how much room it needs is a property of the
        // implement - so it travels with the implement profile, as do the trigger tolerances.
        public double setTurn_boundaryOffset = 0.5;
        public double setTurn_triggerTolerance = 3.0;        // window before the start (m)
        public double setTurn_triggerToleranceAfter = 1.0;   // window past the start (m)

        // Turn SHAPE, moved off the vehicle 2026-08-12. A pulled implement changes how the turn
        // actually runs far more than the difference between two tractors does, and what is left
        // of that difference shows up as a slightly wider overshoot - harmless, because nobody
        // plans a turn at the tractor's minimum radius. Keeping the whole set on the implement
        // means swapping implements brings its own numbers along, with nothing to retype.
        // The vehicle still has the last word through its geometric minimum radius (CYouTurn).
        public double setTurn_youTurnRadius = 8.1;
        public double setTurn_K_exitRadius = 8;         // K: exit arc radius
        public double setTurn_K_entryStartDist = 2;     // K: metres before the new line the entry begins
        public double setTurn_K_entryAngle = 40;        // K: entry straight angle to the new line (deg)
        public double setTurn_K_transitionRadius = 1.0; // K: unused (rigid K - covered by path smoothing)
        public double setTurn_U_exitRadius = 8;         // U: exit (outer) radius
        public double setTurn_U_entryRadius = 8;        // U: entry (inner) radius
        public double setTurn_U_overshootDist = 0.5;    // U: virtual line offset beyond AB

        /// <summary>false = this implement never had the turn shape, take it off the vehicle once</summary>
        public bool setTurn_shapeFromVehicle = false;

        // Section positions
        public decimal setSection_position1 = -2;
        public decimal setSection_position2 = -1;
        public decimal setSection_position3 = 1;
        public decimal setSection_position4 = 2;
        public decimal setSection_position5 = 0;
        public decimal setSection_position6 = 0;
        public decimal setSection_position7 = 0;
        public decimal setSection_position8 = 0;
        public decimal setSection_position9 = 0;
        public decimal setSection_position10 = 0;
        public decimal setSection_position11 = 0;
        public decimal setSection_position12 = 0;
        public decimal setSection_position13 = 0;
        public decimal setSection_position14 = 0;
        public decimal setSection_position15 = 0;
        public decimal setSection_position16 = 0;
        public decimal setSection_position17 = 0;

        // Section colors
        public Color setColor_sec01 = Color.FromArgb(249, 22, 10);
        public Color setColor_sec02 = Color.FromArgb(68, 84, 254);
        public Color setColor_sec03 = Color.FromArgb(8, 243, 8);
        public Color setColor_sec04 = Color.FromArgb(233, 6, 233);
        public Color setColor_sec05 = Color.FromArgb(200, 191, 86);
        public Color setColor_sec06 = Color.FromArgb(0, 252, 246);
        public Color setColor_sec07 = Color.FromArgb(144, 36, 246);
        public Color setColor_sec08 = Color.FromArgb(232, 102, 21);
        public Color setColor_sec09 = Color.FromArgb(255, 160, 170);
        public Color setColor_sec10 = Color.FromArgb(205, 204, 246);
        public Color setColor_sec11 = Color.FromArgb(213, 239, 190);
        public Color setColor_sec12 = Color.FromArgb(247, 200, 247);
        public Color setColor_sec13 = Color.FromArgb(253, 241, 144);
        public Color setColor_sec14 = Color.FromArgb(187, 250, 250);
        public Color setColor_sec15 = Color.FromArgb(227, 201, 249);
        public Color setColor_sec16 = Color.FromArgb(247, 229, 215);
        public bool setColor_isMultiColorSections = false;

        // DeadZone settings (from Vehicle)
        public int setAS_deadZoneDistance = 1;
        public int setAS_deadZoneHeading = 10;
        public int setAS_deadZoneDelay = 5;

        // Nudge snap distances
        public double setAS_snapDistance = 20.0;
        public double setAS_snapDistanceRef = 5.0;

        // Headland
        public bool setHeadland_isSectionControlled = true;

        // Arduino Machine
        public byte setArdMac_setting0 = 0;
        public byte setArdMac_isHydEnabled = 0;
        public byte setArdMac_hydRaiseTime = 3;
        public byte setArdMac_hydLowerTime = 4;
        public byte setArdMac_user1 = 1;
        public byte setArdMac_user2 = 2;
        public byte setArdMac_user3 = 3;
        public byte setArdMac_user4 = 4;

        // Relay
        public string setRelay_pinConfig = "1,2,3,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0";

        // Guidance algorithm
        public double purePursuitIntegralGainAB = 0;
        public double stanleyDistanceErrorGain = 1;
        public double stanleyHeadingErrorGain = 1;
        public double stanleyIntegralGainAB = 0;
        public bool setVehicle_isStanleyUsed = false;

        // Tool specific Steer Parameters
        public double setVehicle_goalPointLookAheadHold = 3;
        public double setVehicle_toolOffDelay = 0;
        public double setVehicle_goalPointLookAheadMult = 1.5;
        public double setVehicle_goalPointAcquireFactor = 0.9;
        public double setVehicle_slowSpeedCutoff = 0.5;
        public double setVehicle_minCoverage = 100;
        public double setVehicle_hydraulicLiftLookAhead = 2;
        public bool setF_isSteerWorkSwitchEnabled = false;

        public LoadResult Load(string toolFileName)
        {
            string path = Path.Combine(RegistrySettings.toolsDirectory, toolFileName + ".xml");
            var result = XmlSettingsHandler.LoadXMLFile(path, this);
            if (result == LoadResult.MissingFile)
            {
                // Try loading from old format and migrate
                return CSettingsMigration.MigrateTool(toolFileName, this);
            }

            // Update registry with the loaded tool file name
            if (result == LoadResult.Ok)
            {
                RegistrySettings.toolProfileName = toolFileName;
            }

            return result;
        }

        /// <summary>
        /// Save tool settings to file using the file name from registry.
        /// Uses "DefaultTool" as fallback if registry value is empty.
        /// </summary>
        public void Save()
        {
            // Read file name from registry, use fallback if empty
            string fileName = string.IsNullOrEmpty(RegistrySettings.toolProfileName)
                ? "DefaultTool"
                : RegistrySettings.toolProfileName;

            string path = Path.Combine(RegistrySettings.toolsDirectory, fileName + ".xml");
            XmlSettingsHandler.SaveXMLFile(path, this);
        }

        /// <summary>
        /// Save tool settings to a specific file (used during migration).
        /// This overload saves to a custom file name without updating the registry.
        /// </summary>
        /// <param name="fileName">The file name to save to (without extension)</param>
        public void Save(string fileName)
        {
            string path = Path.Combine(RegistrySettings.toolsDirectory, fileName + ".xml");
            XmlSettingsHandler.SaveXMLFile(path, this);
        }

        public void Reset()
        {
            settings_ = new ToolSettings();
        }
    }
}
