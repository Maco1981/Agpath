using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AgPath
{
    /// <summary>
    /// Writes map\data.js: settings for the map page (language, base map), field boundaries and all
    /// saved recordings, so that map\index.html can show them straight from disk (file://).
    /// Also writes a single self-contained HTML file (map + Leaflet + data) into the save folder,
    /// so the map can be shared, e.g. through a OneDrive folder, and opened on any computer.
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
                sb.AppendLine("AGPATH.units = " + Exporter.J(Units.Imperial ? "imperial" : "metric") + ";");
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
                string data = sb.ToString();
                WriteAtomic(Path.Combine(MapDir, "data.js"), data);
                try { WriteShared(s, html, data); }
                catch (Exception ex) { Log.Write("Writing shared map: " + ex.Message); }
            }
            catch (Exception ex) { Log.Write("Writing map data: " + ex.Message); }
            return html;
        }

        public static string SharedPath(Settings s)
        {
            string name = Exporter.SafeFileName(Lang.T("map.sharedFile"));
            if (name == "") name = "Map.html";
            return Path.Combine(s.SaveDir, name);
        }

        static void WriteAtomic(string target, string text)
        {
            string tmp = target + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(target)) File.Delete(target);
            File.Move(tmp, target);
        }

        static string DataUri(string file, string mime)
        {
            return "data:" + mime + ";base64," + Convert.ToBase64String(File.ReadAllBytes(file));
        }

        /// <summary>One HTML file with everything inlined, written to the save folder.</summary>
        static void WriteShared(Settings s, string indexHtml, string data)
        {
            if (!Directory.Exists(s.SaveDir)) Directory.CreateDirectory(s.SaveDir);
            string page = File.ReadAllText(indexHtml, Encoding.UTF8);
            string css = File.ReadAllText(Path.Combine(MapDir, "leaflet.css"), Encoding.UTF8);
            string img = Path.Combine(MapDir, "images");
            foreach (string png in new string[] { "layers-2x.png", "layers.png", "marker-icon.png" })
            {
                string f = Path.Combine(img, png);
                if (File.Exists(f)) css = css.Replace("url(images/" + png + ")", "url(" + DataUri(f, "image/png") + ")");
            }
            string js = File.ReadAllText(Path.Combine(MapDir, "leaflet.js"), Encoding.UTF8).Replace("</script", "<\\/script");
            page = page.Replace("<link rel=\"stylesheet\" href=\"leaflet.css\">", "<style>\n" + css + "\n</style>");
            page = page.Replace("<script src=\"leaflet.js\"></script>", "<script>\n" + js + "\n</script>");
            page = page.Replace("<script src=\"data.js\" onerror=\"window.AGPATH_NO_DATA=true\"></script>",
                                "<script>\n" + data + "\n</script>");
            WriteAtomic(SharedPath(s), page);
        }
    }
}
