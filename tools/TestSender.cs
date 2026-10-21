// AgPath test tool: sends a simulated run as AgOpenGPS would (UDP PGN 0x64 + 0xE5 to 127.255.255.255:17777),
// so AgPath can be tested without a tractor or AgOpenGPS.
// Usage: TestSender.exe [latitude longitude]   (default: a point in southern Finland)
// Drives 6 lanes of 80 m, 1.75 m apart; sections are off in the headland turns.
using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading;

class TestSender
{
    static void Send(Socket s, EndPoint ep, byte[] d)
    {
        int crc = 0;
        for (int i = 2; i < d.Length - 1; i++) crc += d[i];
        d[d.Length - 1] = (byte)crc;
        s.SendTo(d, ep);
    }

    static void Main(string[] args)
    {
        double lat0 = 60.80, lon0 = 24.00;
        if (args.Length >= 2)
        {
            lat0 = double.Parse(args[0], CultureInfo.InvariantCulture);
            lon0 = double.Parse(args[1], CultureInfo.InvariantCulture);
        }
        Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
        s.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        EndPoint ep = new IPEndPoint(IPAddress.Parse("127.255.255.255"), 17777);
        double k = Math.Cos(lat0 * Math.PI / 180), R = 6371008.8;
        Console.WriteLine("Sending a test run at " + lat0 + ", " + lon0 + " (6 lanes x 80 m)...");
        for (int lane = 0; lane < 6; lane++)
        {
            for (int step = 0; step <= 40; step++)
            {
                double x = lane * 1.75, y = (lane % 2 == 0) ? step * 2.0 : 80 - step * 2.0;
                Fix(s, ep, lat0 + y / R * 180 / Math.PI, lon0 + x / (R * k) * 180 / Math.PI, true);
                Thread.Sleep(100);
            }
            for (int t = 1; t <= 4; t++)
            {
                double y = (lane % 2 == 0) ? 80 + Math.Sin(t * Math.PI / 5) * 3 : -Math.Sin(t * Math.PI / 5) * 3;
                double x = lane * 1.75 + 1.75 * t / 5.0;
                Fix(s, ep, lat0 + y / R * 180 / Math.PI, lon0 + x / (R * k) * 180 / Math.PI, false);
                Thread.Sleep(100);
            }
        }
        Console.WriteLine("Done. Expected: about 0.08 ha with 1.75 m width when only sections-on is counted.");
    }

    static void Fix(Socket s, EndPoint ep, double lat, double lon, bool work)
    {
        byte[] p = new byte[30];
        p[0] = 0x80; p[1] = 0x81; p[2] = 0x7F; p[3] = 0x64; p[4] = 24;
        Buffer.BlockCopy(BitConverter.GetBytes(lon), 0, p, 5, 8);
        Buffer.BlockCopy(BitConverter.GetBytes(lat), 0, p, 13, 8);
        Send(s, ep, p);
        byte[] e = new byte[] { 0x80, 0x81, 0x7f, 0xE5, 10, (byte)(work ? 1 : 0), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        Send(s, ep, e);
    }
}
