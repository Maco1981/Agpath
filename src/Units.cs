using System;
using System.IO;
using System.Xml;

namespace AgPath
{
    /// <summary>
    /// Display units. By default the same as AgOpenGPS (setMenu_isMetric in the environment settings),
    /// or chosen in AgPath settings. Files are always saved in metric units.
    /// </summary>
    static class Units
    {
        public static bool Imperial;

        const double M_PER_MI = 1609.344, M2_PER_AC = 4046.8564224, M_PER_FT = 0.3048, KMH_PER_MPH = 1.609344;

        public static void Load(Settings s)
        {
            if (s.Units == "metric") Imperial = false;
            else if (s.Units == "imperial") Imperial = true;
            else Imperial = !AogIsMetric(s.AogBaseDir);
        }

        /// <summary>Reads setMenu_isMetric from AgOpenGPS (read only). Defaults to metric.</summary>
        public static bool AogIsMetric(string aogBase)
        {
            try
            {
                string dir = Path.Combine(aogBase, "Environment");
                foreach (string name in new string[] { "environment.xml", "DefaultEnvironment.xml" })
                {
                    string f = Path.Combine(dir, name);
                    if (!File.Exists(f)) continue;
                    XmlDocument doc = new XmlDocument();
                    doc.Load(f);
                    XmlNode n = doc.SelectSingleNode("//setting[@name='setMenu_isMetric']/value");
                    if (n != null) return !string.Equals(n.InnerText.Trim(), "False", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }
            return true;
        }

        public static string DistUnit { get { return Imperial ? "mi" : "km"; } }
        public static string AreaUnit { get { return Imperial ? "ac" : "ha"; } }
        public static string SpeedUnit { get { return Imperial ? "mph" : "km/h"; } }
        public static string WidthUnit { get { return Imperial ? "ft" : "m"; } }

        public static double DistValue(double m) { return Imperial ? m / M_PER_MI : m / 1000.0; }
        public static double AreaValue(double m2) { return Imperial ? m2 / M2_PER_AC : m2 / 10000.0; }
        public static double SpeedValue(double kmh) { return Imperial ? kmh / KMH_PER_MPH : kmh; }
        public static double WidthValue(double m) { return Imperial ? m / M_PER_FT : m; }
        public static double WidthToMeters(double v) { return Imperial ? v * M_PER_FT : v; }

        public static string Dist(double m) { return DistValue(m).ToString("0.00", Lang.Culture) + " " + DistUnit; }
        public static string Area(double m2) { return AreaValue(m2).ToString("0.00", Lang.Culture) + " " + AreaUnit; }
        public static string Speed(double kmh) { return SpeedValue(kmh).ToString("0.0", Lang.Culture) + " " + SpeedUnit; }
        public static string Width(double m) { return WidthValue(m).ToString("0.##", Lang.Culture) + " " + WidthUnit; }
    }
}
