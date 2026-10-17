// REQ 2026-10-17-1 (NV custom) — steering hold at a stop.
//
// When the machine stops and moves off again, the heading (and so the direction) is only
// re-established after some travel; until then the steer setpoint is computed from a heading
// that may still point the old way. Below steerHoldSpeed the last angle commanded while moving
// is held, and it is released once the pivot has moved steerHoldDistance from where it stopped,
// whichever way. A short stop and carry on does no harm either: the wheels just stay put.
// Autosteer stays on; the min-speed switch-off and the direction detection are untouched.
using System;

namespace AgOpenGPS
{
    public partial class FormGPS
    {
        private const double steerHoldSpeed = 0.3;      // km/h
        private const double steerHoldDistance = 0.5;   // m of pivot travel before release

        private bool steerHoldOn;            // holding right now
        private bool steerHoldReady = true;  // re-armed only after moving above steerHoldSpeed
        private short steerHoldAngle;        // the angle held (0.01 deg)
        private short steerMovingAngle;      // last angle commanded while moving
        private vec3 steerHoldAt;

        private void SteerHoldAtStop()
        {
            if (!isBtnAutoSteerOn)
            {
                steerHoldOn = false;
                steerHoldReady = true;
                return;
            }

            if (!steerHoldOn)
            {
                if (avgSpeed >= steerHoldSpeed)
                {
                    steerHoldReady = true;
                    steerMovingAngle = guidanceLineSteerAngle;
                    return;
                }
                if (!steerHoldReady) return;   // creeping on after a release - steer normally

                steerHoldOn = true;
                steerHoldReady = false;
                steerHoldAngle = steerMovingAngle;
                steerHoldAt = pivotAxlePos;
            }

            double dx = pivotAxlePos.easting - steerHoldAt.easting;
            double dn = pivotAxlePos.northing - steerHoldAt.northing;
            if (dx * dx + dn * dn >= steerHoldDistance * steerHoldDistance)
            {
                steerHoldOn = false;
                steerMovingAngle = guidanceLineSteerAngle;
                return;
            }

            guidanceLineSteerAngle = steerHoldAngle;
        }
    }
}
