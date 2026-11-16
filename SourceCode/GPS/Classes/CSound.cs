using System;
using System.IO;
using System.Media;

namespace AgOpenGPS
{
    public class CSound
    {
        //sound objects - wave files in resources
        public readonly SoundPlayer sndBoundaryAlarm = new SoundPlayer(Properties.Resources.Alarm10);
        public readonly SoundPlayer sndUTurnTooClose = new SoundPlayer(Properties.Resources.TF012);

        public readonly SoundPlayer sndAutoSteerOn = new SoundPlayer(Properties.Resources.SteerOn);
        public readonly SoundPlayer sndAutoSteerOff = new SoundPlayer(Properties.Resources.SteerOff);
        public readonly SoundPlayer sndHydLiftUp = new SoundPlayer(Properties.Resources.HydUp);
        public readonly SoundPlayer sndHydLiftDn = new SoundPlayer(Properties.Resources.HydDown);
        public readonly SoundPlayer sndRTKAlarm = new SoundPlayer(Properties.Resources.rtk_lost);
        public readonly SoundPlayer sndSectionOn = new SoundPlayer(Properties.Resources.SectionOn);
        public readonly SoundPlayer sndSectionOff = new SoundPlayer(Properties.Resources.SectionOff);
        public readonly SoundPlayer sndHeadland = new SoundPlayer(Properties.Resources.Headland);
        public readonly SoundPlayer sndRTKRecoverd = new SoundPlayer(Properties.Resources.rtk_back);

        // Turn maneuver signals (REQ 2026-11-16-1), generated here - no sound files:
        // in the zone where the turn may start, turn started (rising), turn finished (falling)
        public readonly SoundPlayer sndTurnZone = Tone(new[] { 880.0 }, 90);
        public readonly SoundPlayer sndTurnStart = Tone(new[] { 660.0, 990.0 }, 120);
        public readonly SoundPlayer sndTurnEnd = Tone(new[] { 990.0, 660.0 }, 120);

        /// <summary>Short sine tones one after another as an in-memory WAV (16 bit mono, 22.05 kHz).</summary>
        private static SoundPlayer Tone(double[] freqs, int msEach)
        {
            const int rate = 22050;
            int nEach = rate * msEach / 1000, gap = rate * 30 / 1000;
            int total = freqs.Length * nEach + (freqs.Length - 1) * gap;
            var ms = new MemoryStream(44 + total * 2);
            var w = new BinaryWriter(ms);
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + total * 2);
            w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' }); w.Write(16);
            w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(total * 2);
            int fade = rate * 8 / 1000;
            for (int f = 0; f < freqs.Length; f++)
            {
                for (int i = 0; i < nEach; i++)
                {
                    double env = Math.Min(1.0, Math.Min(i, nEach - 1 - i) / (double)fade);
                    w.Write((short)(Math.Sin(2 * Math.PI * freqs[f] * i / rate) * env * 14000));
                }
                if (f < freqs.Length - 1) for (int i = 0; i < gap; i++) w.Write((short)0);
            }
            w.Flush();
            ms.Position = 0;
            return new SoundPlayer(ms);
        }


        public bool isBoundAlarming, isRTKAlarming;
        public bool RTKWasAlarming = false;


        public bool isSteerSoundOn, isTurnSoundOn, isHydLiftSoundOn, isSectionsSoundOn;

        public bool isHydLiftChange;

        public CSound()
        {
            isSteerSoundOn = Properties.Settings.Default.setSound_isAutoSteerOn;
            isHydLiftSoundOn = Properties.Settings.Default.setSound_isHydLiftOn;
            isTurnSoundOn = Properties.Settings.Default.setSound_isUturnOn;
            isSectionsSoundOn = Properties.Settings.Default.setSound_isSectionsOn;
        }
    }
}