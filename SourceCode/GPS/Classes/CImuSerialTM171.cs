// F10 Advanced Sensor Fusion (NV custom) — TM171 IMU read straight from a USB COM port
// (REQ 2026-10-02-1).
//
// The BNO085 sits in the cabin and its roll carries the cabin rocking on the suspension. A TM171 on
// the CHASSIS gives the roll of the tractor itself. It is read here, by AOG, over USB, so the BNO
// can stay on the Teensy: the PANDA roll (cabin) and this roll (chassis) end up in the same log.
//
// Protocol (AIO_v4_Firmware_WebConfig/TM171_IMU.cpp, verified on a laptop 02.10.2026):
//   AA 55 | LEN | PAYLOAD (LEN bytes) | CRC16-Modbus over LEN+PAYLOAD, little endian
//   LEN 20: +4 u32 device time [us], +8 roll, +12 pitch, +16 yaw - float32 LE, degrees
//   LEN 16 also arrives (probably temperature) and is skipped.
// Measured: 50 Hz with no gaps once raised in the TM171 tool (factory default is 25 Hz), CRC errors
// 0, noise 0.013 deg at rest. Over USB the packets arrive in bursts every ~50 ms, so the newest
// value is 0-50 ms old - fine at 10 Hz, and the age is logged.
//
// The reader runs on its own background thread and reconnects every 2 s while enabled, so a pulled
// cable never reaches the UI thread as an exception. Axis, invert and zero are its own - the AOG
// roll settings keep describing the BNO.
using System;
using System.IO.Ports;
using System.Threading;

namespace AgOpenGPS
{
    public class CImuSerialTM171
    {
        // ---- settings -------------------------------------------------------------------
        public bool useRoll;                            // chassis roll replaces the PANDA roll
        public string portName = "";
        public int baud = 115200;
        public bool usePitchAsRoll;                     // mounted turned 90 deg
        public bool invert;
        public double zeroDeg;                          // subtracted after axis + invert

        public const double FreshMs = 250;             // older than this = no roll
        public const double LowRateHz = 45;            // below: warn (factory output is 25 Hz)

        // ---- state shared with the reader thread ----------------------------------------
        private readonly object sync = new object();
        private Thread reader;
        private volatile bool running;
        private double rollRaw, pitchRaw, yawRaw;       // as sent by the device
        private long lastRxTicks;                       // Stopwatch ticks of the newest packet
        private uint lastDevUs;
        private bool haveDev;
        private int gaps, crcErrors;
        private double rateHz;
        private string state = "off";

        private static readonly double TickMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        public bool IsRunning { get { return running; } }

        /// <summary>Opens the port (again) with the current settings. Safe to call repeatedly.</summary>
        public void Start()
        {
            Stop();
            if (string.IsNullOrEmpty(portName)) { lock (sync) state = "no port"; return; }
            running = true;
            reader = new Thread(ReadLoop) { IsBackground = true, Name = "TM171 reader" };
            reader.Start();
        }

        public void Stop()
        {
            running = false;
            Thread t = reader;
            reader = null;
            if (t != null && t.IsAlive) t.Join(500);
            lock (sync) { state = "off"; rateHz = 0; }
        }

        // ---- what AOG reads ---------------------------------------------------------------

        /// <summary>Age of the newest angle packet [ms], infinity before the first one.</summary>
        public double AgeMs
        {
            get
            {
                lock (sync)
                    return lastRxTicks == 0 ? double.PositiveInfinity
                        : (System.Diagnostics.Stopwatch.GetTimestamp() - lastRxTicks) * TickMs;
            }
        }

        public bool IsFresh { get { return AgeMs < FreshMs; } }

        /// <summary>Roll with axis, invert and zero applied [deg]. Right side down = positive
        /// once invert is set right for the mounting.</summary>
        public double Roll { get { lock (sync) return Mounted(); } }

        public double Pitch { get { lock (sync) return usePitchAsRoll ? rollRaw : pitchRaw; } }
        public double Yaw { get { lock (sync) return yawRaw; } }
        public double RateHz { get { lock (sync) return rateHz; } }
        public int Gaps { get { lock (sync) return gaps; } }
        public int CrcErrors { get { lock (sync) return crcErrors; } }
        public string State { get { lock (sync) return state; } }

        /// <summary>Takes the current mounted roll as zero (tractor standing on level ground).</summary>
        public void SetZero()
        {
            lock (sync)
            {
                double r = usePitchAsRoll ? pitchRaw : rollRaw;
                zeroDeg = invert ? -r : r;
            }
        }

        private double Mounted()
        {
            double r = usePitchAsRoll ? pitchRaw : rollRaw;
            if (invert) r = -r;
            return r - zeroDeg;
        }

        // ---- reader thread ------------------------------------------------------------------
        private void ReadLoop()
        {
            byte[] buf = new byte[4096];
            int len = 0;
            byte[] chunk = new byte[512];
            long rateWindowStart = System.Diagnostics.Stopwatch.GetTimestamp();
            int rateCount = 0;

            while (running)
            {
                SerialPort sp = null;
                try
                {
                    sp = new SerialPort(portName, baud) { ReadTimeout = 200 };
                    sp.Open();
                    lock (sync) { state = "open"; haveDev = false; }
                    len = 0;

                    while (running)
                    {
                        int n;
                        try { n = sp.Read(chunk, 0, chunk.Length); }
                        catch (TimeoutException) { n = 0; }

                        if (n > 0)
                        {
                            if (len + n > buf.Length) len = 0;  // garbage flood - start over
                            Buffer.BlockCopy(chunk, 0, buf, len, n);
                            len += n;
                            rateCount += Parse(buf, ref len);
                        }

                        long now = System.Diagnostics.Stopwatch.GetTimestamp();
                        double win = (now - rateWindowStart) * TickMs;
                        if (win >= 1000)
                        {
                            lock (sync) rateHz = rateCount * 1000.0 / win;
                            rateCount = 0; rateWindowStart = now;
                        }
                    }
                }
                catch (Exception ex)
                {
                    lock (sync) { state = "error: " + ex.Message; rateHz = 0; }
                }
                finally
                {
                    try { if (sp != null && sp.IsOpen) sp.Close(); } catch { }
                    if (sp != null) sp.Dispose();
                }

                //port gone or failed to open - try again in 2 s while still enabled
                for (int i = 0; i < 20 && running; i++) Thread.Sleep(100);
            }
        }

        /// <summary>Takes every complete packet out of buf, returns how many angle packets.</summary>
        private int Parse(byte[] buf, ref int len)
        {
            int found = 0, i = 0;
            while (i + 3 <= len)
            {
                if (buf[i] != 0xAA || buf[i + 1] != 0x55) { i++; continue; }
                int n = buf[i + 2];
                int size = 2 + 1 + n + 2;
                if (i + size > len) break;                  // wait for the rest

                ushort crc = Crc16(buf, i + 2, 1 + n);
                ushort got = (ushort)(buf[i + size - 2] | (buf[i + size - 1] << 8));
                if (crc != got) { lock (sync) crcErrors++; i += 2; continue; }

                if (n == 20)
                {
                    int p = i + 3;
                    uint devUs = BitConverter.ToUInt32(buf, p + 4);
                    float r = BitConverter.ToSingle(buf, p + 8);
                    float pt = BitConverter.ToSingle(buf, p + 12);
                    float yw = BitConverter.ToSingle(buf, p + 16);
                    lock (sync)
                    {
                        //a step of more than 1.5 packets on the device clock = lost packet(s)
                        if (haveDev && rateHz > 1)
                        {
                            double step = unchecked(devUs - lastDevUs);
                            if (step > 1.5e6 / rateHz) gaps++;
                        }
                        lastDevUs = devUs; haveDev = true;
                        rollRaw = r; pitchRaw = pt; yawRaw = yw;
                        lastRxTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                        state = "ok";
                    }
                    found++;
                }
                i += size;
            }

            if (i > 0)
            {
                Buffer.BlockCopy(buf, i, buf, 0, len - i);
                len -= i;
            }
            return found;
        }

        private static ushort Crc16(byte[] data, int start, int count)
        {
            ushort crc = 0xFFFF;
            for (int k = start; k < start + count; k++)
            {
                crc ^= data[k];
                for (int b = 0; b < 8; b++)
                    crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
            }
            return crc;
        }
    }
}
