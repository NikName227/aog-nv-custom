// REQ 2026-11-06-1 (NV custom) - turn log for calibrating the turn-entry model.
//
// One row per fix from the moment a U/K turn is triggered until 20 m of driving after it has
// handed over to the line, appended to TurnLog.csv in the field folder (it travels with the field
// zip). Each turn starts with a "#" line of the parameters it was driven with. State: T = on the
// turn, R = on the straight on the new row, A = after the hand-over (line guidance).
using AgLibrary.Logging;
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace AgOpenGPS
{
    public partial class FormGPS
    {
        private bool tlActive, tlAfter, tlFailed;
        private double tlDistAfter;
        private vec3 tlLastPos;
        private DateTime tlStart, tlLastFlush = DateTime.MinValue;
        private int tlTurnId;
        private readonly StringBuilder tlBuf = new StringBuilder();

        private const double tlAfterDistance = 20.0;   // m logged after the hand-over

        private static string Tl(double v, string f = "0.000")
        {
            return v.ToString(f, CultureInfo.InvariantCulture);
        }

        private const string tlHeader = "t_s,state,pivot_e,pivot_n,heading_deg,speed_kmh,steer_set_deg,steer_actual_deg,goal_e,goal_n,goal_dist_m,xte_cm,idx,points,tool_e,tool_n,curv_on,k_cmd\n";

        private void TurnLogFix()
        {
            if (tlFailed) return;
            //a debug option since REQ 2026-11-16-1: written only when the config file switches it on
            if (!CFeatureLock.IsOn(CFeatureLock.DebugTurnLog))
            {
                if (tlActive) TurnLogEnd();
                return;
            }
            try
            {
                bool trig = yt.isYouTurnTriggered && yt.ytList.Count > 3;

                if (!tlActive)
                {
                    if (!trig) return;
                    TurnLogBegin();
                }
                else if (!trig)
                {
                    if (!tlAfter) { tlAfter = true; tlDistAfter = 0; tlLastPos = pivotAxlePos; }
                    else
                    {
                        tlDistAfter += glm.Distance(tlLastPos, pivotAxlePos);
                        tlLastPos = pivotAxlePos;
                    }
                    if (tlDistAfter > tlAfterDistance) { TurnLogEnd(); return; }
                }
                else tlAfter = false;     // triggered again

                //standing still adds nothing but rows (a stop in mid-turn used to fill the file)
                if (Math.Abs(avgSpeed) < 0.1) return;

                char state = tlAfter ? 'A'
                    : (yt.ytTailStart > 0 && yt.onA >= yt.ytTailStart ? 'R' : 'T');

                vec2 goal = yt.goalPointYT;
                if (tlAfter && trk.idx > -1)
                    goal = trk.gArr[trk.idx].mode == TrackMode.AB ? ABLine.goalPointAB : curve.goalPointCu;

                tlBuf.Append(Tl((DateTime.UtcNow - tlStart).TotalSeconds, "0.00")).Append(',')
                    .Append(state).Append(',')
                    .Append(Tl(pivotAxlePos.easting)).Append(',').Append(Tl(pivotAxlePos.northing)).Append(',')
                    .Append(Tl(glm.toDegrees(fixHeading), "0.0")).Append(',')
                    .Append(Tl(avgSpeed, "0.00")).Append(',')
                    .Append(Tl(guidanceLineSteerAngle * 0.01, "0.00")).Append(',')
                    .Append(Tl(mc.actualSteerAngleDegrees, "0.00")).Append(',')
                    .Append(Tl(goal.easting)).Append(',').Append(Tl(goal.northing)).Append(',')
                    .Append(Tl(vehicle.goalDistance, "0.00")).Append(',')
                    .Append(Tl(guidanceLineDistanceOff * 0.1, "0.0")).Append(',')
                    .Append(yt.onA).Append(',').Append(yt.ytList.Count).Append(',')
                    .Append(Tl(toolPivotPos.easting)).Append(',').Append(Tl(toolPivotPos.northing)).Append(',')
                    .Append(!tlAfter && yt.turnCurvActive ? 1 : 0).Append(',')
                    .Append(Tl(!tlAfter && yt.turnCurvActive ? yt.turnCurvCmd : 0, "0.0000")).Append('\n');

                //on disk every 5 s as well: a report taken right after a turn holds that turn (16.11.)
                if (tlBuf.Length > 60000 || (DateTime.UtcNow - tlLastFlush).TotalSeconds > 5) TurnLogFlush();
            }
            catch (Exception ex)
            {
                tlFailed = true;
                Log.EventWriter("TurnLog disabled: " + ex.Message);
            }
        }

        private void TurnLogBegin()
        {
            tlActive = true;
            tlAfter = false;
            tlTurnId++;
            tlStart = DateTime.UtcNow;

            var tool = Properties.ToolSettings.Default;
            tlBuf.Append("#turn ").Append(tlTurnId).Append(' ').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                .Append(" style=").Append(yt.isLineApproach ? "A" : (yt.activeTurnStyle == 0 ? "U" : "K"))
                .Append(" hold=").Append(Tl(vehicle.goalPointLookAheadHold, "0.00"))
                .Append(" mult=").Append(Tl(vehicle.goalPointLookAheadMult, "0.00"))
                .Append(" acquire=").Append(Tl(vehicle.goalPointAcquireFactor, "0.00"))
                .Append(" overshoot=").Append(Tl(yt.lastOvershootAsked, "0.00"))
                .Append(" applied=").Append(Tl(yt.lastOvershootApplied, "0.00"))
                .Append(" rOut=").Append(Tl(yt.UOutRadius, "0.00"))
                .Append(" rIn=").Append(Tl(yt.UInRadius, "0.00"))
                .Append(" tailLen=").Append(Tl(yt.lastTailLen, "0.00"))
                .Append(" tailAngle=").Append(Tl(yt.lastTailAngle, "0.0"))
                .Append(" steerRate=").Append(Tl(Properties.VehicleSettings.Default.setTurn_steerRate, "0.0"))
                .Append(" lead=").Append(Tl(Properties.VehicleSettings.Default.setTurn_leadTime, "0.00"))
                .Append(" transition=").Append(Tl(yt.lastTransitionLen, "0.00"))
                .Append(" uWidth=").Append(Tl(yt.lastTurnWidthNeeded, "0.00"))
                .Append(" curv=").Append(yt.HasTurnCurvature() ? 1 : 0)
                .Append(" wheelbase=").Append(Tl(vehicle.VehicleConfig.Wheelbase, "0.00"))
                .Append(" trailing=").Append(this.tool.isToolTrailing ? 1 : 0)
                .Append(" highPWM=").Append(p_252.pgn[p_252.highPWM])
                .Append(" points=").Append(yt.ytList.Count)
                .Append(" trailExtra=").Append(Tl(yt.lastTrailExtra, "0.00"))
                .Append('\n');

            string dir = Path.Combine(RegistrySettings.fieldsDirectory, currentFieldDirectory ?? "");
            if (!File.Exists(Path.Combine(dir, "TurnLog.csv")) && Directory.Exists(dir))
                tlBuf.Insert(0, tlHeader);
        }

        private void TurnLogEnd()
        {
            tlActive = false;
            tlAfter = false;
            TurnLogFlush();
        }

        private void TurnLogFlush()
        {
            tlLastFlush = DateTime.UtcNow;
            if (tlBuf.Length == 0) return;
            try
            {
                string dir = Path.Combine(RegistrySettings.fieldsDirectory, currentFieldDirectory ?? "");
                if (Directory.Exists(dir))
                {
                    string file = Path.Combine(dir, "TurnLog.csv");
                    //never more than about 10 MB per field: at 5 MB the file becomes TurnLog_old.csv
                    //(replacing the previous one) and a new one starts
                    var fi = new FileInfo(file);
                    if (fi.Exists && fi.Length > 5 * 1024 * 1024)
                    {
                        string old = Path.Combine(dir, "TurnLog_old.csv");
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(file, old);
                        tlBuf.Insert(0, tlHeader);
                    }
                    File.AppendAllText(file, tlBuf.ToString());
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("TurnLog write: " + ex.Message);
            }
            tlBuf.Clear();
        }
    }
}
