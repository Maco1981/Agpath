using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace AgPath
{
    /// <summary>Reads AgOpenGPS field boundaries (Field.kml, read only) and detects the current field.</summary>
    class FieldDetector
    {
        class FieldPoly
        {
            public string Name;
            public double[] Lon, Lat;
            public double MinLat, MaxLat, MinLon, MaxLon;
        }

        readonly List<FieldPoly> polys = new List<FieldPoly>();
        public readonly List<string> FieldNames = new List<string>();

        public void Load(string aogBase)
        {
            polys.Clear();
            FieldNames.Clear();
            try
            {
                string dir = Path.Combine(aogBase, "Fields");
                if (!Directory.Exists(dir)) return;
                foreach (string fd in Directory.GetDirectories(dir))
                {
                    string name = Path.GetFileName(fd);
                    FieldNames.Add(name);
                    string kml = Path.Combine(fd, "Field.kml");
                    if (File.Exists(kml)) ReadKml(name, kml);
                }
                FieldNames.Sort(StringComparer.CurrentCultureIgnoreCase);
            }
            catch (Exception ex) { Log.Write("Reading fields: " + ex.Message); }
        }

        void ReadKml(string name, string path)
        {
            try
            {
                string text = File.ReadAllText(path);
                int b = text.IndexOf("<name>Boundaries</name>", StringComparison.Ordinal);
                if (b < 0) return;
                int e = text.IndexOf("</Folder>", b, StringComparison.Ordinal);
                if (e < 0) e = text.Length;
                string section = text.Substring(b, e - b);
                // outer boundaries only
                foreach (Match m in Regex.Matches(section,
                    @"<outerBoundaryIs>\s*<LinearRing>\s*<coordinates>([^<]*)</coordinates>", RegexOptions.Singleline))
                {
                    string[] pts = m.Groups[1].Value.Split(new char[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    List<double> lon = new List<double>(), lat = new List<double>();
                    foreach (string p in pts)
                    {
                        string[] c = p.Split(',');
                        if (c.Length < 2) continue;
                        double x, y;
                        if (double.TryParse(c[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) &&
                            double.TryParse(c[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y))
                        { lon.Add(x); lat.Add(y); }
                    }
                    if (lon.Count < 3) continue;
                    FieldPoly fp = new FieldPoly();
                    fp.Name = name; fp.Lon = lon.ToArray(); fp.Lat = lat.ToArray();
                    fp.MinLat = double.MaxValue; fp.MaxLat = double.MinValue; fp.MinLon = double.MaxValue; fp.MaxLon = double.MinValue;
                    for (int i = 0; i < fp.Lat.Length; i++)
                    {
                        fp.MinLat = Math.Min(fp.MinLat, fp.Lat[i]); fp.MaxLat = Math.Max(fp.MaxLat, fp.Lat[i]);
                        fp.MinLon = Math.Min(fp.MinLon, fp.Lon[i]); fp.MaxLon = Math.Max(fp.MaxLon, fp.Lon[i]);
                    }
                    polys.Add(fp);
                }
            }
            catch (Exception ex) { Log.Write("Field.kml " + path + ": " + ex.Message); }
        }

        static bool Inside(FieldPoly f, double lat, double lon)
        {
            if (lat < f.MinLat || lat > f.MaxLat || lon < f.MinLon || lon > f.MaxLon) return false;
            bool c = false;
            int n = f.Lat.Length;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                if (((f.Lat[i] > lat) != (f.Lat[j] > lat)) &&
                    (lon < (f.Lon[j] - f.Lon[i]) * (lat - f.Lat[i]) / (f.Lat[j] - f.Lat[i]) + f.Lon[i]))
                    c = !c;
            }
            return c;
        }

        /// <summary>Field boundaries as GeoJSON for the map.</summary>
        public string ToGeoJson()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("{\"type\":\"FeatureCollection\",\"features\":[");
            for (int k = 0; k < polys.Count; k++)
            {
                FieldPoly f = polys[k];
                if (k > 0) sb.Append(",\n");
                sb.Append("{\"type\":\"Feature\",\"properties\":{\"field\":").Append(Exporter.J(f.Name))
                  .Append("},\"geometry\":{\"type\":\"Polygon\",\"coordinates\":[[");
                for (int i = 0; i < f.Lat.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('[').Append(f.Lon[i].ToString("0.0000000", CultureInfo.InvariantCulture)).Append(',')
                      .Append(f.Lat[i].ToString("0.0000000", CultureInfo.InvariantCulture)).Append(']');
                }
                sb.Append("]]}}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>Centre {lat, lon} of a field's boundary, or null if it has none.</summary>
        public double[] Center(string name)
        {
            foreach (FieldPoly f in polys)
                if (f.Name == name) return new double[] { (f.MinLat + f.MaxLat) / 2, (f.MinLon + f.MaxLon) / 2 };
            return null;
        }

        public string FieldAt(double lat, double lon)
        {
            foreach (FieldPoly f in polys) if (Inside(f, lat, lon)) return f.Name;
            return "";
        }

        /// <summary>The field containing most of the points.</summary>
        public string FieldForPath(List<PathPoint> pts)
        {
            if (pts.Count == 0) return "";
            Dictionary<string, int> counts = new Dictionary<string, int>();
            int step = Math.Max(1, pts.Count / 200);
            for (int i = 0; i < pts.Count; i += step)
            {
                string n = FieldAt(pts[i].Lat, pts[i].Lon);
                if (n == "") continue;
                int c;
                counts.TryGetValue(n, out c);
                counts[n] = c + 1;
            }
            string best = ""; int bestC = 0;
            foreach (KeyValuePair<string, int> kv in counts)
                if (kv.Value > bestC) { best = kv.Key; bestC = kv.Value; }
            return best;
        }
    }
}
