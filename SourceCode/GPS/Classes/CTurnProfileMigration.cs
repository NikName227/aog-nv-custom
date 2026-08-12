using AgLibrary.Logging;
using System;

namespace AgOpenGPS
{
    /// <summary>
    /// One-time move of the turn parameters out of the application settings and into the
    /// machine profiles (F51, 2026-08-10).
    ///
    /// They used to sit in Settings (user.config), which tied them to the TABLET: swapping to
    /// another tractor kept the previous one's turn radii, and swapping implements kept the
    /// previous one's trigger tolerances. Now the turn shape follows the vehicle profile and
    /// the tolerances plus the extra boundary offset follow the implement profile.
    ///
    /// The values the user already had are copied into whichever profiles are loaded at the
    /// time this runs - i.e. the pair that was in use when they upgraded. Other profiles start
    /// from the defaults, which is the honest outcome: nobody ever entered those numbers for
    /// them in the first place.
    /// </summary>
    public static class CTurnProfileMigration
    {
        /// <summary>
        /// 2026-08-12: the turn SHAPE moved from the vehicle profile to the implement profile.
        /// Runs whenever an implement without its own numbers is loaded - it inherits whatever
        /// the vehicle carries, so nothing changes under the driver on the first run.
        /// </summary>
        public static void ShapeToTool()
        {
            var t = Properties.ToolSettings.Default;
            if (t.setTurn_shapeFromVehicle) return;

            try
            {
                var v = Properties.VehicleSettings.Default;
                t.setTurn_youTurnRadius = v.setTurn_youTurnRadius;
                t.setTurn_K_exitRadius = v.setTurn_K_exitRadius;
                t.setTurn_K_entryStartDist = v.setTurn_K_entryStartDist;
                t.setTurn_K_entryAngle = v.setTurn_K_entryAngle;
                t.setTurn_K_transitionRadius = v.setTurn_K_transitionRadius;
                t.setTurn_U_exitRadius = v.setTurn_U_exitRadius;
                t.setTurn_U_entryRadius = v.setTurn_U_entryRadius;
                t.setTurn_U_overshootDist = v.setTurn_U_overshootDist;
                t.setTurn_shapeFromVehicle = true;
                t.Save();

                Log.EventWriter("Turn shape copied from the vehicle to the implement profile");
            }
            catch (Exception ex)
            {
                Log.EventWriter("CTurnProfileMigration.ShapeToTool: " + ex);
            }
        }

        public static void RunOnce()
        {
            var s = Properties.Settings.Default;
            if (s.set_turnParamsMovedToProfiles) return;

            try
            {
                var v = Properties.VehicleSettings.Default;
                v.setTurn_youTurnRadius = s.set_youTurnRadius;
                v.setTurn_K_exitRadius = s.set_turnK_exitRadius;
                v.setTurn_K_entryStartDist = s.set_turnK_entryStartDist;
                v.setTurn_K_entryAngle = s.set_turnK_entryAngle;
                v.setTurn_K_transitionRadius = s.set_turnK_transitionRadius;
                v.setTurn_U_exitRadius = s.set_turnU_exitRadius;
                v.setTurn_U_entryRadius = s.set_turnU_entryRadius;
                v.setTurn_U_overshootDist = s.set_turnU_overshootDist;
                v.Save();

                var t = Properties.ToolSettings.Default;
                t.setTurn_triggerTolerance = s.set_turnTriggerTolerance;
                t.setTurn_triggerToleranceAfter = s.set_turnTriggerToleranceAfter;
                //setTurn_boundaryOffset is new - it keeps its 0.5 m default
                t.Save();

                s.set_turnParamsMovedToProfiles = true;
                s.Save();

                Log.EventWriter("F51: turn parameters moved into the vehicle/implement profiles");
            }
            catch (Exception e)
            {
                //a failed migration must not stop the program - it simply runs again next start
                Log.EventWriter("F51 turn parameter migration failed: " + e.Message);
            }
        }
    }
}
