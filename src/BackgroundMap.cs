// AgPath - records driven paths alongside AgOpenGPS.
// Copyright 2026 AgPath contributors. Licensed under the Apache License 2.0, see LICENSE.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace AgPath
{
    /// <summary>
    /// Adds an aerial photo (National Land Survey of Finland, ortokuva, CC BY 4.0) as the AgOpenGPS background
    /// map of a field: BackPic.png + BackPic.txt, the same files AgOpenGPS's own map tool writes.
    /// Only those two files are written; the field itself is not changed.
    /// </summary>
    static class BackgroundMap
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const double D = Math.PI / 180.0;
        const int MaxPixels = 2048, MaxZoom = 17, Margin = 25;

        public static bool HasBackground(string fieldDir)
        {
            string txt = Path.Combine(fieldDir, "BackPic.txt");
            if (!File.Exists(txt) || !File.Exists(Path.Combine(fieldDir, "BackPic.png"))) return false;
            try
            {
                string[] l = File.ReadAllLines(txt);
                return l.Length > 1 && l[1].Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>Downloads the aerial photo for the field's boundary and saves it as the background.</summary>
        public static void Add(string fieldDir, string mmlKey)
        {
            if (string.IsNullOrEmpty(mmlKey)) throw new Exception(Lang.T("bg.err.noKey"));
            double lat0, lon0;
            ReadStartFix(Path.Combine(fieldDir, "Field.txt"), out lat0, out lon0);
            double minE = double.MaxValue, maxE = double.MinValue, minN = double.MaxValue, maxN = double.MinValue;
            foreach (string line in File.ReadAllLines(Path.Combine(fieldDir, "Boundary.txt")))
            {
                string[] p = line.Split(',');
                if (p.Length < 3) continue;
                double e, n;
                if (!double.TryParse(p[0], NumberStyles.Float, Inv, out e) || !double.TryParse(p[1], NumberStyles.Float, Inv, out n)) continue;
                minE = Math.Min(minE, e); maxE = Math.Max(maxE, e); minN = Math.Min(minN, n); maxN = Math.Max(maxN, n);
            }
            if (minE > maxE) throw new Exception(Lang.T("bg.err.noBoundary"));
            minE -= Margin; maxE += Margin; minN -= Margin; maxN += Margin;

            // local plane (AgOpenGPS) -> WGS84 corners
            double mLat = 111132.92 - 559.82 * Math.Cos(2.0 * lat0 * D) + 1.175 * Math.Cos(4.0 * lat0 * D) - 0.0023 * Math.Cos(6.0 * lat0 * D);
            double latTop = lat0 + maxN / mLat, latBottom = lat0 + minN / mLat;
            double latMid = (latTop + latBottom) / 2;
            double mLon = 111412.84 * Math.Cos(latMid * D) - 93.5 * Math.Cos(3.0 * latMid * D) + 0.118 * Math.Cos(5.0 * latMid * D);
            double lonLeft = lon0 + minE / mLon, lonRight = lon0 + maxE / mLon;

            int z = MaxZoom;
            double x0, x1, y0, y1;
            while (true)
            {
                x0 = PixX(lonLeft, z); x1 = PixX(lonRight, z); y0 = PixY(latTop, z); y1 = PixY(latBottom, z);
                if ((x1 - x0 <= MaxPixels && y1 - y0 <= MaxPixels) || z <= 12) break;
                z--;
            }
            int w = Math.Max(1, (int)Math.Round(x1 - x0)), h = Math.Max(1, (int)Math.Round(y1 - y0));
            int ox = (int)Math.Round(x0), oy = (int)Math.Round(y0);
            int tx0 = (int)Math.Floor(x0 / 256), tx1 = (int)Math.Floor(x1 / 256), ty0 = (int)Math.Floor(y0 / 256), ty1 = (int)Math.Floor(y1 / 256);

            using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb))
            using (Graphics g = Graphics.FromImage(bmp))
            using (WebClient wc = new WebClient())
            {
                try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
                wc.Headers[HttpRequestHeader.UserAgent] = "AgPath/" + Program.Version;
                g.Clear(Color.DarkGray);
                for (int tx = tx0; tx <= tx1; tx++)
                    for (int ty = ty0; ty <= ty1; ty++)
                    {
                        string url = "https://avoin-karttakuva.maanmittauslaitos.fi/avoin/wmts/1.0.0/ortokuva/default/WGS84_Pseudo-Mercator/" +
                                     z + "/" + ty + "/" + tx + ".jpg?api-key=" + Uri.EscapeDataString(mmlKey);
                        byte[] data = wc.DownloadData(url);
                        using (MemoryStream ms = new MemoryStream(data))
                        using (Image tile = Image.FromStream(ms))
                            g.DrawImageUnscaled(tile, tx * 256 - ox, ty * 256 - oy); // whole pixels: no seams
                    }
                string png = Path.Combine(fieldDir, "BackPic.png");
                if (File.Exists(png)) File.Delete(png);
                bmp.Save(png, ImageFormat.Png);
            }
            // same layout as AgOpenGPS: max easting, min easting, max northing, min northing
            File.WriteAllText(Path.Combine(fieldDir, "BackPic.txt"),
                "$BackPic\r\nTrue\r\n" + maxE.ToString("0.000", Inv) + "\r\n" + minE.ToString("0.000", Inv) + "\r\n" +
                maxN.ToString("0.000", Inv) + "\r\n" + minN.ToString("0.000", Inv) + "\r\n", new UTF8Encoding(false));
            Log.Write("Background map z" + z + " " + w + "x" + h + " for " + fieldDir);
        }

        static double PixX(double lon, int z) { return (lon + 180.0) / 360.0 * 256.0 * Math.Pow(2, z); }
        static double PixY(double lat, int z)
        {
            double s = Math.Log(Math.Tan(Math.PI / 4 + lat * D / 2));
            return (1 - s / Math.PI) / 2 * 256.0 * Math.Pow(2, z);
        }

        static void ReadStartFix(string fieldTxt, out double lat, out double lon)
        {
            string[] l = File.ReadAllLines(fieldTxt);
            for (int i = 0; i < l.Length - 1; i++)
                if (l[i].Trim() == "StartFix")
                {
                    string[] p = l[i + 1].Split(',');
                    lat = double.Parse(p[0], Inv); lon = double.Parse(p[1], Inv);
                    return;
                }
            throw new Exception("StartFix missing in " + fieldTxt);
        }

        /// <summary>AgOpenGPS fields that have a boundary but no background map.</summary>
        public static List<string> FieldsWithout(string aogBase)
        {
            List<string> list = new List<string>();
            string dir = Path.Combine(aogBase, "Fields");
            if (!Directory.Exists(dir)) return list;
            foreach (string fd in Directory.GetDirectories(dir))
            {
                if (HasBackground(fd)) continue;
                string b = Path.Combine(fd, "Boundary.txt");
                if (File.Exists(b) && File.Exists(Path.Combine(fd, "Field.txt")) && new FileInfo(b).Length > 40) list.Add(fd);
            }
            list.Sort(StringComparer.CurrentCultureIgnoreCase);
            return list;
        }
    }
}
