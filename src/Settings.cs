using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using Microsoft.Win32;

namespace AgPath
{
    /// <summary>Program settings, stored in settings.txt (key=value).</summary>
    class Settings
    {
        public double ToolWidth = 0;          // 0 = read from the AgOpenGPS tool profile
        public bool OnlyWhenSectionsOn = false;
        public string AogBaseDir = "";        // e.g. C:\Users\x\Documents\AgOpenGPS
        public string SaveDir = "";
        public string ListenIp = "127.77.0.1";
        public double MinStep = 0.3;          // m, minimum distance between points
        public bool TopMost = true;
        public string MinFix = "all";        // all | float | fix : which GPS quality is recorded
        public int WinX = -1, WinY = -1;
        public bool Compact = false;
        public string Language = "auto";      // auto = same as AgOpenGPS
        public string MapProvider = "";       // osm | mml | custom ("" = mml if key given, else osm)
        public string MmlApiKey = "";
        public string CustomTileUrl = "";
        public string CustomAttribution = "";

        static string FilePath { get { return Path.Combine(Program.AppDir, "settings.txt"); } }

        public static Settings Load()
        {
            Settings s = new Settings();
            string path = FilePath;
            if (!File.Exists(path))
            {
                string legacy = Path.Combine(Program.AppDir, "asetukset.txt"); // AOG Kerrokset (early Finnish version)
                if (File.Exists(legacy)) path = legacy;
            }
            try
            {
                if (File.Exists(path))
                {
                    foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                    {
                        if (line.TrimStart().StartsWith("#")) continue;
                        int i = line.IndexOf('=');
                        if (i <= 0) continue;
                        string k = line.Substring(0, i).Trim();
                        string v = line.Substring(i + 1).Trim();
                        switch (k)
                        {
                            case "tool_width": case "tyoleveys": s.ToolWidth = ParseD(v, 0); break;
                            case "only_when_sections_on": case "vain_lohkot_paalla": s.OnlyWhenSectionsOn = v == "1"; break;
                            case "agopengps_folder": case "agopengps_kansio": s.AogBaseDir = v; break;
                            case "save_folder": case "tallennuskansio": s.SaveDir = v; break;
                            case "listen_ip": case "kuunteluosoite": s.ListenIp = v; break;
                            case "min_point_distance": case "pisteväli": s.MinStep = ParseD(v, 0.3); break;
                            case "always_on_top": case "aina_paallimmaisena": s.TopMost = v != "0"; break;
                            case "window_x": case "ikkuna_x": s.WinX = (int)ParseD(v, -1); break;
                            case "window_y": case "ikkuna_y": s.WinY = (int)ParseD(v, -1); break;
                            case "compact": case "pieni_tila": s.Compact = v == "1"; break;
                            case "language": s.Language = v; break;
                            case "min_fix": s.MinFix = (v == "fix" || v == "float") ? v : "all"; break;
                            case "map_provider": s.MapProvider = v; break;
                            case "mml_api_key": s.MmlApiKey = v; break;
                            case "custom_tile_url": s.CustomTileUrl = v; break;
                            case "custom_attribution": s.CustomAttribution = v; break;
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Write("Reading settings: " + ex.Message); }

            if (string.IsNullOrEmpty(s.AogBaseDir) || !Directory.Exists(s.AogBaseDir))
                s.AogBaseDir = DetectAogBaseDir();
            if (string.IsNullOrEmpty(s.SaveDir))
                s.SaveDir = Path.Combine(Program.AppDir, "Recordings");
            if (s.MinStep < 0.05) s.MinStep = 0.05;
            if (s.MapProvider == "") s.MapProvider = s.MmlApiKey != "" ? "mml" : "osm";
            if (s.Language == "") s.Language = "auto";
            return s;
        }

        public void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# AgPath settings");
                sb.AppendLine("language=" + Language);
                sb.AppendLine("tool_width=" + D(ToolWidth));
                sb.AppendLine("only_when_sections_on=" + (OnlyWhenSectionsOn ? "1" : "0"));
                sb.AppendLine("agopengps_folder=" + AogBaseDir);
                sb.AppendLine("save_folder=" + SaveDir);
                sb.AppendLine("listen_ip=" + ListenIp);
                sb.AppendLine("min_point_distance=" + D(MinStep));
                sb.AppendLine("min_fix=" + MinFix);
                sb.AppendLine("always_on_top=" + (TopMost ? "1" : "0"));
                sb.AppendLine("window_x=" + WinX);
                sb.AppendLine("window_y=" + WinY);
                sb.AppendLine("compact=" + (Compact ? "1" : "0"));
                sb.AppendLine("map_provider=" + MapProvider);
                sb.AppendLine("mml_api_key=" + MmlApiKey);
                sb.AppendLine("custom_tile_url=" + CustomTileUrl);
                sb.AppendLine("custom_attribution=" + CustomAttribution);
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Write("Saving settings: " + ex.Message); }
        }

        /// <summary>
        /// Is a fix of this quality recorded? q: NMEA fix quality from AgIO (4 = RTK fix, 5 = RTK float,
        /// 8 = simulator, -1 = unknown). Unknown and simulator are always accepted.
        /// </summary>
        public bool AcceptsFix(int q)
        {
            if (MinFix == "all" || q < 0 || q == 8) return true;
            if (MinFix == "fix") return q == 4;
            return q == 4 || q == 5;
        }

        /// <summary>Working width: own setting or the active AgOpenGPS tool.</summary>
        public double EffectiveWidth()
        {
            if (ToolWidth > 0) return ToolWidth;
            double w = ReadAogToolWidth(AogBaseDir);
            return w > 0 ? w : 3.0;
        }

        // ---------------------------------------------------------------
        // Reading AgOpenGPS data (read only, nothing is ever written)
        // ---------------------------------------------------------------

        public static string DetectAogBaseDir()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\AgOpenGPS"))
                {
                    if (k != null)
                    {
                        object wd = k.GetValue("WorkingDirectory");
                        if (wd != null)
                        {
                            string s = wd.ToString();
                            if (s != "" && s != "Default")
                            {
                                string p = Path.Combine(s, "AgOpenGPS");
                                if (Directory.Exists(p)) return p;
                            }
                        }
                    }
                }
            }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AgOpenGPS");
        }

        public static string ActiveToolProfile()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\AgOpenGPS"))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("ToolProfileName");
                        if (v != null) return v.ToString();
                    }
                }
            }
            catch { }
            return "";
        }

        public static double ReadAogToolWidth(string aogBase)
        {
            string name = ActiveToolProfile();
            if (name == "") return 0;
            return ReadToolWidthFromFile(Path.Combine(Path.Combine(aogBase, "ToolProfiles"), name + ".xml"));
        }

        public static double ReadToolWidthFromFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return 0;
                XmlDocument doc = new XmlDocument();
                doc.Load(path);
                XmlNode n = doc.SelectSingleNode("//setting[@name='setVehicle_toolWidth']/value");
                if (n != null) return ParseD(n.InnerText, 0);
            }
            catch { }
            return 0;
        }

        public static List<KeyValuePair<string, double>> ListToolProfiles(string aogBase)
        {
            List<KeyValuePair<string, double>> list = new List<KeyValuePair<string, double>>();
            try
            {
                string dir = Path.Combine(aogBase, "ToolProfiles");
                if (Directory.Exists(dir))
                {
                    foreach (string f in Directory.GetFiles(dir, "*.xml"))
                    {
                        double w = ReadToolWidthFromFile(f);
                        if (w > 0) list.Add(new KeyValuePair<string, double>(Path.GetFileNameWithoutExtension(f), w));
                    }
                }
            }
            catch { }
            return list;
        }

        public static double ParseD(string s, double def)
        {
            double d;
            if (s == null) return def;
            s = s.Trim().Replace(',', '.');
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return def;
        }

        public static string D(double d)
        {
            return d.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
