using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace AgPath
{
    class Fix
    {
        public double Lat, Lon;
        public DateTime Time;
        public bool FromAog;
    }

    /// <summary>
    /// Listens to the local UDP traffic between AgIO and AgOpenGPS (listen only, never sends anything).
    /// AgIO -> AgOpenGPS: 127.255.255.255:15555 (PGN 0xD6, GPS position)
    /// AgOpenGPS -> AgIO: 127.255.255.255:17777 (PGN 0x64 corrected position, 0xE5/0xEF sections)
    /// Binds to its own 127.x address so AgOpenGPS's own ports are not disturbed
    /// (same approach as AgOpenGPS's GPS_Out program).
    /// </summary>
    class UdpReceiver
    {
        public readonly ConcurrentQueue<Fix> Fixes = new ConcurrentQueue<Fix>();
        readonly object locker = new object();

        Socket sockAog, sockAgio;
        volatile bool running;

        DateTime aogTime = DateTime.MinValue, agioTime = DateTime.MinValue, sectionsTime = DateTime.MinValue;
        double lastLat, lastLon;
        int fixQuality = -1;
        float speedKmh;
        ulong sections;
        bool hasE5;

        public string Error = "";

        public void Start(string ip)
        {
            running = true;
            Error = "";
            IPAddress addr;
            if (!IPAddress.TryParse(ip, out addr)) addr = IPAddress.Parse("127.77.0.1");
            sockAog = Open(addr, 17777);
            sockAgio = Open(addr, 15555);
            if (sockAog != null) StartThread(sockAog, "AOG");
            if (sockAgio != null) StartThread(sockAgio, "AgIO");
        }

        Socket Open(IPAddress addr, int port)
        {
            try
            {
                Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
                s.Bind(new IPEndPoint(addr, port));
                s.ReceiveTimeout = 1000;
                Log.Write("Listening " + addr + ":" + port);
                return s;
            }
            catch (Exception ex)
            {
                Error = Lang.T("gps.portError", port, ex.Message);
                Log.Write(Error);
                return null;
            }
        }

        void StartThread(Socket s, string name)
        {
            Thread t = new Thread(delegate() { Loop(s); });
            t.IsBackground = true;
            t.Name = "UDP " + name;
            t.Start();
        }

        public void Stop()
        {
            running = false;
            try { if (sockAog != null) sockAog.Close(); } catch { }
            try { if (sockAgio != null) sockAgio.Close(); } catch { }
        }

        void Loop(Socket s)
        {
            byte[] buf = new byte[2048];
            while (running)
            {
                EndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                int n;
                try { n = s.ReceiveFrom(buf, ref ep); }
                catch (SocketException) { if (!running) break; continue; }
                catch (ObjectDisposedException) { break; }
                try { Parse(buf, n); } catch { }
            }
        }

        static bool GoodCrc(byte[] d, int total)
        {
            int crc = 0;
            for (int i = 2; i < total - 1; i++) crc += d[i];
            return (byte)crc == d[total - 1];
        }

        static bool ValidLatLon(double lat, double lon)
        {
            if (double.IsNaN(lat) || double.IsNaN(lon)) return false;
            if (Math.Abs(lat) > 90 || Math.Abs(lon) > 180) return false;
            if (Math.Abs(lat) < 1e-6 && Math.Abs(lon) < 1e-6) return false;
            return true;
        }

        void Parse(byte[] d, int n)
        {
            if (n < 6 || d[0] != 0x80 || d[1] != 0x81) return;
            int total = d[4] + 6;
            if (n < total) return;
            byte pgn = d[3];
            DateTime now = DateTime.Now;

            if (pgn == 0x64 && d[4] >= 16) // AgOpenGPS: corrected position (lon, lat)
            {
                if (!GoodCrc(d, total)) return;
                double lon = BitConverter.ToDouble(d, 5);
                double lat = BitConverter.ToDouble(d, 13);
                if (!ValidLatLon(lat, lon)) return;
                lock (locker) { aogTime = now; lastLat = lat; lastLon = lon; }
                Enqueue(lat, lon, now, true);
            }
            else if (pgn == 0xD6 && d[4] >= 0x33) // AgIO: GPS position
            {
                double lon = BitConverter.ToDouble(d, 5);
                double lat = BitConverter.ToDouble(d, 13);
                if (!ValidLatLon(lat, lon)) return;
                float spd = BitConverter.ToSingle(d, 29);
                int q = d[43];
                bool useIt;
                lock (locker)
                {
                    agioTime = now;
                    fixQuality = q;
                    if (!float.IsNaN(spd)) speedKmh = spd;
                    useIt = (now - aogTime).TotalSeconds > 1.5; // use only if AgOpenGPS is not sending
                    if (useIt) { lastLat = lat; lastLon = lon; }
                }
                if (useIt) Enqueue(lat, lon, now, false);
            }
            else if (pgn == 0xE5 && d[4] >= 8) // AgOpenGPS: state of 64 sections
            {
                if (!GoodCrc(d, total)) return;
                ulong v = 0;
                for (int i = 0; i < 8; i++) v |= ((ulong)d[5 + i]) << (8 * i);
                lock (locker) { sections = v; sectionsTime = now; hasE5 = true; }
            }
            else if (pgn == 0xEF && d[4] >= 8) // AgOpenGPS: machine data, sections 1-16
            {
                if (!GoodCrc(d, total)) return;
                lock (locker)
                {
                    if (!hasE5 || (now - sectionsTime).TotalSeconds > 3)
                    {
                        sections = (ulong)d[11] | ((ulong)d[12] << 8);
                        sectionsTime = now;
                    }
                }
            }
        }

        void Enqueue(double lat, double lon, DateTime t, bool fromAog)
        {
            Fix f = new Fix();
            f.Lat = lat; f.Lon = lon; f.Time = t; f.FromAog = fromAog;
            Fixes.Enqueue(f);
            while (Fixes.Count > 5000) { Fix dummy; Fixes.TryDequeue(out dummy); }
        }

        // -------- state for the UI --------

        public bool AogAlive { get { lock (locker) return (DateTime.Now - aogTime).TotalSeconds < 2; } }
        public bool AgioAlive { get { lock (locker) return (DateTime.Now - agioTime).TotalSeconds < 2; } }
        public bool HasPosition { get { return AogAlive || AgioAlive; } }
        public int FixQuality { get { lock (locker) return AgioAlive ? fixQuality : -1; } }
        public float SpeedKmh { get { lock (locker) return speedKmh; } }

        /// <summary>Section state: null = unknown (no field open in AgOpenGPS).</summary>
        public bool? SectionsOn
        {
            get
            {
                lock (locker)
                {
                    if ((DateTime.Now - sectionsTime).TotalSeconds > 2) return null;
                    return sections != 0;
                }
            }
        }

        public bool LastPosition(out double lat, out double lon)
        {
            lock (locker) { lat = lastLat; lon = lastLon; }
            return HasPosition;
        }

        public static string FixQualityText(int q)
        {
            switch (q)
            {
                case 0: return Lang.T("gps.q0");
                case 1: return "GPS";
                case 2: return "DGPS";
                case 4: return "RTK fix";
                case 5: return "RTK float";
                case 8: return Lang.T("gps.q8");
                default: return q < 0 ? "" : Lang.T("gps.qOther", q);
            }
        }
    }
}
