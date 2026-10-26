// AgPath - records driven paths alongside AgOpenGPS.
// Copyright 2026 AgPath contributors. Licensed under the Apache License 2.0, see LICENSE.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace AgPath
{
    /// <summary>A basic parcel (peruslohko) from the open Finnish Land Parcel Register.</summary>
    class Parcel
    {
        public string Tunnus = "";      // PERUSLOHKOTUNNUS, 10 digits
        public double AreaHa;
        public int Year;
        public string Crops = "";       // from the agricultural parcels (kasvulohkot) of the same year
        public List<double[]> Ring = new List<double[]>(); // outer ring, {lat, lon} WGS84 / ETRS89
        public double CLat, CLon;       // centre
        public double DistanceM;
        public string Overlaps = "";    // existing AgOpenGPS field that covers its centre

        public string TunnusPretty
        {
            get { return Tunnus.Length == 10 ? Tunnus.Substring(0, 3) + "-" + Tunnus.Substring(3, 5) + "-" + Tunnus.Substring(8) : Tunnus; }
        }
    }

    /// <summary>
    /// Reads basic parcels from the Finnish Food Authority's open INSPIRE WFS service (CC BY 4.0) and
    /// creates new AgOpenGPS fields from them. Existing AgOpenGPS fields are never changed.
    /// </summary>
    static class ParcelImport
    {
        const string Wfs = "https://inspire.ruokavirasto-awsa.com/geoserver/wfs";
        public const string Attribution = "Ruokavirasto, peltolohkorekisteri (CC BY 4.0)";

        static string Get(string url)
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { } // TLS 1.2
            using (WebClient wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.UserAgent] = "AgPath/" + Program.Version;
                return wc.DownloadString(url);
            }
        }

        /// <summary>The newest year that has a parcel layer in the service.</summary>
        public static int LatestYear()
        {
            string caps = Get(Wfs + "?service=WFS&version=2.0.0&request=GetCapabilities");
            int best = 0;
            foreach (Match m in Regex.Matches(caps, @"LC\.LandCoverSurfaces\.LPIS\.(\d{4})<"))
                best = Math.Max(best, int.Parse(m.Groups[1].Value));
            if (best == 0) throw new Exception(Lang.T("pi.err.noLayer"));
            return best;
        }

        /// <summary>Parcels inside a box (TM35FIN metres) or with the given IDs.</summary>
        public static List<Parcel> Fetch(int year, double[] boxTm, List<string> ids)
        {
            string filter;
            if (ids != null && ids.Count > 0)
            {
                List<string> q = new List<string>();
                foreach (string id in ids) q.Add("'" + id.Replace("'", "") + "'");
                filter = "&CQL_FILTER=" + Uri.EscapeDataString("PERUSLOHKOTUNNUS IN (" + string.Join(",", q.ToArray()) + ")");
            }
            else
            {
                filter = "&bbox=" + string.Format(CultureInfo.InvariantCulture, "{0:0},{1:0},{2:0},{3:0}", boxTm[0], boxTm[1], boxTm[2], boxTm[3]) +
                         ",urn:ogc:def:crs:EPSG::3067";
            }
            string common = Wfs + "?service=WFS&version=2.0.0&request=GetFeature&outputFormat=application/json&srsName=EPSG:3067&count=2000";
            List<Parcel> list = new List<Parcel>();
            Dictionary<string, Parcel> byId = new Dictionary<string, Parcel>();
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;

            Dictionary<string, object> fc = js.Deserialize<Dictionary<string, object>>(
                Get(common + "&typeNames=inspire:LC.LandCoverSurfaces.LPIS." + year + filter));
            foreach (object fo in (ArrayList)fc["features"])
            {
                Dictionary<string, object> f = (Dictionary<string, object>)fo;
                Dictionary<string, object> p = (Dictionary<string, object>)f["properties"];
                Parcel pc = new Parcel();
                pc.Tunnus = Convert.ToString(p["PERUSLOHKOTUNNUS"], CultureInfo.InvariantCulture);
                pc.AreaHa = p["PINTA_ALA"] == null ? 0 : Convert.ToDouble(p["PINTA_ALA"], CultureInfo.InvariantCulture);
                pc.Year = year;
                ReadRing(f, pc);
                if (pc.Ring.Count < 3 || byId.ContainsKey(pc.Tunnus)) continue;
                byId[pc.Tunnus] = pc;
                list.Add(pc);
            }

            // crops of the same year, only to help recognise the parcels
            try
            {
                Dictionary<string, object> gc = js.Deserialize<Dictionary<string, object>>(
                    Get(common + "&typeNames=inspire:LandUse.ExistingLandUse.GSAAAgriculturalParcel." + year + filter));
                foreach (object fo in (ArrayList)gc["features"])
                {
                    Dictionary<string, object> p = (Dictionary<string, object>)((Dictionary<string, object>)fo)["properties"];
                    Parcel pc;
                    if (!byId.TryGetValue(Convert.ToString(p["PERUSLOHKOTUNNUS"], CultureInfo.InvariantCulture), out pc)) continue;
                    string crop = Convert.ToString(p[Lang.Code == "sv" ? "KASVIKOODI_SELITE_SV" : "KASVIKOODI_SELITE_FI"]) ?? "";
                    if (crop != "" && pc.Crops.IndexOf(crop, StringComparison.OrdinalIgnoreCase) < 0)
                        pc.Crops += (pc.Crops == "" ? "" : ", ") + crop;
                }
            }
            catch (Exception ex) { Log.Write("Crops: " + ex.Message); }
            return list;
        }

        static void ReadRing(Dictionary<string, object> f, Parcel pc)
        {
            Dictionary<string, object> g = f["geometry"] as Dictionary<string, object>;
            if (g == null) return;
            string type = Convert.ToString(g["type"]);
            ArrayList coords = (ArrayList)g["coordinates"];
            ArrayList poly = null;
            if (type == "Polygon") poly = coords;
            else if (type == "MultiPolygon")
            {
                // the largest part
                double bestA = -1;
                foreach (object po in coords)
                {
                    ArrayList cand = (ArrayList)po;
                    double a = Math.Abs(RingArea((ArrayList)cand[0]));
                    if (a > bestA) { bestA = a; poly = cand; }
                }
            }
            if (poly == null || poly.Count == 0) return;
            foreach (object pt in (ArrayList)poly[0])
            {
                ArrayList c = (ArrayList)pt;
                double e = Convert.ToDouble(c[0], CultureInfo.InvariantCulture), n = Convert.ToDouble(c[1], CultureInfo.InvariantCulture);
                double lat, lon;
                Tm35.ToLatLon(e, n, out lat, out lon);
                pc.Ring.Add(new double[] { lat, lon });
            }
            double sl = 0, so = 0;
            foreach (double[] p in pc.Ring) { sl += p[0]; so += p[1]; }
            pc.CLat = sl / pc.Ring.Count; pc.CLon = so / pc.Ring.Count;
        }

        static double RingArea(ArrayList ring)
        {
            double a = 0;
            for (int i = 0; i + 1 < ring.Count; i++)
            {
                ArrayList p = (ArrayList)ring[i], q = (ArrayList)ring[i + 1];
                a += Convert.ToDouble(p[0]) * Convert.ToDouble(q[1]) - Convert.ToDouble(q[0]) * Convert.ToDouble(p[1]);
            }
            return a / 2;
        }

        // ------------------------------------------------------------ AgOpenGPS field files
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const double D = Math.PI / 180.0;

        public static bool ValidName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
            return name.Trim() == name && !name.EndsWith(".");
        }

        /// <summary>Creates a new AgOpenGPS field folder with this parcel as its outer boundary.</summary>
        public static void CreateField(string aogBase, string name, Parcel pc)
        {
            string dir = Path.Combine(Path.Combine(aogBase, "Fields"), name);
            if (Directory.Exists(dir)) throw new Exception(Lang.T("pi.err.exists", name));
            double lat0 = pc.CLat, lon0 = pc.CLon;

            // local plane exactly as AgOpenGPS 6 (CNMEA): metres north/east of the start fix
            double mLat = 111132.92 - 559.82 * Math.Cos(2.0 * lat0 * D) + 1.175 * Math.Cos(4.0 * lat0 * D) - 0.0023 * Math.Cos(6.0 * lat0 * D);
            List<double[]> pts = new List<double[]>();
            foreach (double[] p in pc.Ring)
            {
                double mLon = 111412.84 * Math.Cos(p[0] * D) - 93.5 * Math.Cos(3.0 * p[0] * D) + 0.118 * Math.Cos(5.0 * p[0] * D);
                pts.Add(new double[] { (p[1] - lon0) * mLon, (p[0] - lat0) * mLat });
            }
            if (pts.Count > 1 && Math.Abs(pts[0][0] - pts[pts.Count - 1][0]) < 0.001 && Math.Abs(pts[0][1] - pts[pts.Count - 1][1]) < 0.001)
                pts.RemoveAt(pts.Count - 1);
            // counter-clockwise like the boundaries AgOpenGPS records
            double area = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                double[] a = pts[i], b = pts[(i + 1) % pts.Count];
                area += a[0] * b[1] - b[0] * a[1];
            }
            if (area < 0) pts.Reverse();
            // about one point per metre, like a driven boundary
            List<double[]> dense = new List<double[]>();
            for (int i = 0; i < pts.Count; i++)
            {
                double[] a = pts[i], b = pts[(i + 1) % pts.Count];
                double len = Math.Sqrt((b[0] - a[0]) * (b[0] - a[0]) + (b[1] - a[1]) * (b[1] - a[1]));
                int n = Math.Max(1, (int)Math.Floor(len / 1.0));
                for (int k = 0; k < n; k++) dense.Add(new double[] { a[0] + (b[0] - a[0]) * k / n, a[1] + (b[1] - a[1]) * k / n });
            }
            dense.Add(dense[0]);

            Directory.CreateDirectory(dir);
            string stamp = DateTime.Now.ToString("yyyy-MMMM-dd hh:mm:ss tt", Inv);
            string fix = lat0.ToString("0.0000000000000", Inv) + "," + lon0.ToString("0.0000000000000", Inv);
            string header = stamp + "\r\n$FieldDir\r\n{0}\r\n$Offsets\r\n0,0\r\nConvergence\r\n0\r\nStartFix\r\n" + fix + "\r\n";
            W(dir, "Field.txt", string.Format(header, "FieldNew"));
            W(dir, "Elevation.txt", string.Format(header, "Elevation") + "Latitude,Longitude,Elevation,Quality,Easting,Northing,Heading,Roll\r\n");

            StringBuilder b2 = new StringBuilder("$Boundary\r\nFalse\r\n");
            b2.Append(dense.Count.ToString(Inv)).Append("\r\n");
            for (int i = 0; i < dense.Count; i++)
            {
                double[] p = dense[i], q = dense[(i + 1) % dense.Count];
                if (i == dense.Count - 1) q = dense[1];
                double h = Math.Atan2(q[0] - p[0], q[1] - p[1]);
                if (h < 0) h += 2 * Math.PI;
                b2.Append(p[0].ToString("0.000", Inv)).Append(',').Append(p[1].ToString("0.000", Inv)).Append(',')
                  .Append(h.ToString("0.#####", Inv)).Append("\r\n");
            }
            W(dir, "Boundary.txt", b2.ToString());
            W(dir, "TrackLines.txt", "$TrackLines\r\n");
            W(dir, "Flags.txt", "$Flags\r\n0\r\n");
            W(dir, "Contour.txt", "$Contour\r\n");
            W(dir, "RecPath.txt", "$RecPath\r\n0\r\n");
            W(dir, "Sections.txt", "");

            // Field.kml in the same layout AgOpenGPS writes, so AgPath (and Google Earth) see the boundary at once
            StringBuilder k = new StringBuilder();
            k.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<kml xmlns=\"http://www.opengis.net/kml/2.2\">\r\n<Document>\r\n");
            k.Append("<Folder>\r\n<name>Field Stats</name>\r\n<description>Field: ").Append(Esc(name)).Append("\r\nPeruslohko: ")
             .Append(pc.TunnusPretty).Append("\r\nSource: ").Append(Attribution).Append(" ").Append(pc.Year).Append("\r\n</description>\r\n</Folder>\r\n");
            k.Append("<Folder>\r\n<name>Boundaries</name>\r\n<Placemark>\r\n<name>").Append(Esc(name)).Append("</name>\r\n");
            k.Append("<Polygon>\r\n<tessellate>1</tessellate>\r\n<outerBoundaryIs>\r\n<LinearRing>\r\n<coordinates>");
            List<double[]> ring = new List<double[]>(pc.Ring);
            if (ring.Count > 0 && (ring[0][0] != ring[ring.Count - 1][0] || ring[0][1] != ring[ring.Count - 1][1])) ring.Add(ring[0]);
            foreach (double[] p in ring)
                k.Append(p[1].ToString("0.0000000", Inv)).Append(',').Append(p[0].ToString("0.0000000", Inv)).Append(",0 ");
            k.Append("</coordinates>\r\n</LinearRing>\r\n</outerBoundaryIs>\r\n</Polygon>\r\n</Placemark>\r\n</Folder>\r\n</Document>\r\n</kml>\r\n");
            W(dir, "Field.kml", k.ToString());
            Log.Write("Created AgOpenGPS field " + dir + " from parcel " + pc.Tunnus + " (" + pc.Year + ")");
        }

        static string Esc(string s) { return System.Security.SecurityElement.Escape(s); }

        static void W(string dir, string file, string text)
        {
            File.WriteAllText(Path.Combine(dir, file), text, new UTF8Encoding(false));
        }
    }

    /// <summary>ETRS-TM35FIN (EPSG:3067) to geographic coordinates (GRS80), Krüger series.</summary>
    static class Tm35
    {
        const double A = 6378137.0, F = 1 / 298.257222101, K0 = 0.9996, E0 = 500000.0, LON0 = 27.0 * Math.PI / 180;

        public static void ToLatLon(double e, double n, out double lat, out double lon)
        {
            double nn = F / (2 - F);
            double a1 = A / (1 + nn) * (1 + nn * nn / 4 + Math.Pow(nn, 4) / 64);
            double e2 = F * (2 - F), ee = Math.Sqrt(e2);
            double h1 = nn / 2 - 2 * nn * nn / 3 + 37 * Math.Pow(nn, 3) / 96 - Math.Pow(nn, 4) / 360;
            double h2 = nn * nn / 48 + Math.Pow(nn, 3) / 15 - 437 * Math.Pow(nn, 4) / 1440;
            double h3 = 17 * Math.Pow(nn, 3) / 480 - 37 * Math.Pow(nn, 4) / 840;
            double h4 = 4397 * Math.Pow(nn, 4) / 161280;
            double xi = n / (a1 * K0), eta = (e - E0) / (a1 * K0);
            double xi1 = xi - h1 * Math.Sin(2 * xi) * Math.Cosh(2 * eta) - h2 * Math.Sin(4 * xi) * Math.Cosh(4 * eta)
                            - h3 * Math.Sin(6 * xi) * Math.Cosh(6 * eta) - h4 * Math.Sin(8 * xi) * Math.Cosh(8 * eta);
            double eta1 = eta - h1 * Math.Cos(2 * xi) * Math.Sinh(2 * eta) - h2 * Math.Cos(4 * xi) * Math.Sinh(4 * eta)
                             - h3 * Math.Cos(6 * xi) * Math.Sinh(6 * eta) - h4 * Math.Cos(8 * xi) * Math.Sinh(8 * eta);
            double beta = Math.Asin(Math.Sin(xi1) / Math.Cosh(eta1));
            double l = Math.Asin(Math.Tanh(eta1) / Math.Cos(beta));
            double q = Asinh(Math.Tan(beta)), q1 = q;
            for (int i = 0; i < 6; i++) q1 = q + ee * Atanh(ee * Math.Tanh(q1));
            lat = Math.Atan(Math.Sinh(q1)) * 180 / Math.PI;
            lon = (LON0 + l) * 180 / Math.PI;
        }

        static double Asinh(double x) { return Math.Log(x + Math.Sqrt(x * x + 1)); }
        static double Atanh(double x) { return 0.5 * Math.Log((1 + x) / (1 - x)); }

        /// <summary>Geographic to TM35FIN (for the search box).</summary>
        public static void ToTm(double lat, double lon, out double e, out double n)
        {
            double nn = F / (2 - F);
            double a1 = A / (1 + nn) * (1 + nn * nn / 4 + Math.Pow(nn, 4) / 64);
            double e2 = F * (2 - F), ee = Math.Sqrt(e2);
            double h1 = nn / 2 - 2 * nn * nn / 3 + 5 * Math.Pow(nn, 3) / 16 + 41 * Math.Pow(nn, 4) / 180;
            double h2 = 13 * nn * nn / 48 - 3 * Math.Pow(nn, 3) / 5 + 557 * Math.Pow(nn, 4) / 1440;
            double h3 = 61 * Math.Pow(nn, 3) / 240 - 103 * Math.Pow(nn, 4) / 140;
            double h4 = 49561 * Math.Pow(nn, 4) / 161280;
            double phi = lat * Math.PI / 180, l = lon * Math.PI / 180 - LON0;
            double q = Asinh(Math.Tan(phi)) - ee * Atanh(ee * Math.Sin(phi));
            double beta = Math.Atan(Math.Sinh(q));
            double eta1 = Atanh(Math.Cos(beta) * Math.Sin(l));
            double xi1 = Math.Asin(Math.Sin(beta) * Math.Cosh(eta1));
            double xi = xi1 + h1 * Math.Sin(2 * xi1) * Math.Cosh(2 * eta1) + h2 * Math.Sin(4 * xi1) * Math.Cosh(4 * eta1)
                            + h3 * Math.Sin(6 * xi1) * Math.Cosh(6 * eta1) + h4 * Math.Sin(8 * xi1) * Math.Cosh(8 * eta1);
            double eta = eta1 + h1 * Math.Cos(2 * xi1) * Math.Sinh(2 * eta1) + h2 * Math.Cos(4 * xi1) * Math.Sinh(4 * eta1)
                              + h3 * Math.Cos(6 * xi1) * Math.Sinh(6 * eta1) + h4 * Math.Cos(8 * xi1) * Math.Sinh(8 * eta1);
            n = a1 * xi * K0;
            e = a1 * eta * K0 + E0;
        }
    }
}
