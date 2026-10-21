using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AgPath
{
    /// <summary>
    /// Writes map\data.js: settings for the map page (language, base map), field boundaries and all
    /// saved recordings, so that map\index.html can show them straight from disk (file://).
    /// </summary>
    static class MapData
    {
        public static string MapDir { get { return Path.Combine(Program.AppDir, "map"); } }

        /// <summary>Returns the path of index.html, or null if the map folder is missing.</summary>
        public static string Write(Settings s, FieldDetector fields)
        {
            string html = Path.Combine(MapDir, "index.html");
            if (!File.Exists(html)) return null;
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("// Written automatically by AgPath. Do not edit (changes are overwritten).");
                sb.AppendLine("window.AGPATH = {};");
                sb.AppendLine("AGPATH.updated = " + Exporter.J(DateTime.Now.ToString("g", Lang.Culture)) + ";");
                sb.AppendLine("AGPATH.lang = " + Exporter.J(Lang.Code) + ";");
                sb.Append("AGPATH.texts = {");
                bool first = true;
                foreach (KeyValuePair<string, string> kv in Lang.WebTexts())
                {
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append("\n  ").Append(Exporter.J(kv.Key)).Append(":").Append(Exporter.J(kv.Value));
                }
                sb.AppendLine("\n};");
                sb.AppendLine("AGPATH.map = {provider:" + Exporter.J(s.MapProvider) +
                              ",mmlKey:" + Exporter.J(s.MmlApiKey) +
                              ",url:" + Exporter.J(s.CustomTileUrl) +
                              ",attribution:" + Exporter.J(s.CustomAttribution) + "};");
                sb.Append("AGPATH.fields = ").Append(fields.ToGeoJson()).AppendLine(";");
                sb.AppendLine("AGPATH.runs = [];");
                if (Directory.Exists(s.SaveDir))
                {
                    foreach (string f in Directory.GetFiles(s.SaveDir, "*.geojson", SearchOption.AllDirectories))
                    {
                        string txt;
                        try { txt = File.ReadAllText(f, Encoding.UTF8).Trim(); }
                        catch (Exception ex) { Log.Write("Map: " + f + ": " + ex.Message); continue; }
                        if (!txt.StartsWith("{")) continue;
                        // keep the generated file safe to load as a script
                        txt = txt.Replace("</", "<\\/").Replace(((char)0x2028).ToString(), "\\u2028").Replace(((char)0x2029).ToString(), "\\u2029");
                        string rel = f.Substring(s.SaveDir.Length).TrimStart('\\', '/');
                        sb.Append("AGPATH.runs.push({file:").Append(Exporter.J(rel)).Append(",data:")
                          .Append(txt).AppendLine("});");
                    }
                }
                string target = Path.Combine(MapDir, "data.js");
                string tmp = target + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(target)) File.Delete(target);
                File.Move(tmp, target);
            }
            catch (Exception ex) { Log.Write("Writing map data: " + ex.Message); }
            return html;
        }
    }
}
