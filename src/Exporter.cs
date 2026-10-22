using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;

namespace AgPath
{
    class SaveInfo
    {
        public string Name = "";
        public string Product = "";
        public string Variety = "";
        public string Field = "";
        public string Notes = "";
    }

    /// <summary>
    /// Saves a recording as GeoJSON and KML and appends it to recordings.csv.
    /// GeoJSON property names are fixed English keys (see README) so other tools can rely on them.
    /// </summary>
    static class Exporter
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static string LastWarning = "";

        public static string FormatDuration(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return ((int)t.TotalHours).ToString() + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00");
        }

        public static string SafeFileName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            s = s.Trim().Trim('.');
            if (s.Length > 60) s = s.Substring(0, 60);
            return s;
        }

        public static string J(string s)
        {
            if (s == null) return "\"\"";
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    case '\t': sb.Append("\\t"); break;
                    case '<': sb.Append("\\u003c"); break; // safe inside <script>
                    default:
                        if (c < 32 || c == (char)0x2028 || c == (char)0x2029) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        static string N(double d, string fmt) { return d.ToString(fmt, Inv); }

        static string Iso(DateTime t)
        {
            return t.ToString("yyyy-MM-dd'T'HH:mm:ss", Inv) + t.ToString("zzz", Inv);
        }

        static string LocalDate(DateTime t) { return t.ToString("d", Lang.Culture); }
        static string LocalTime(DateTime t) { return t.ToString("HH:mm:ss", Inv); }

        static List<List<PathPoint>> Segments(Recorder r)
        {
            List<List<PathPoint>> segs = new List<List<PathPoint>>();
            List<PathPoint> cur = null;
            int s = -1;
            foreach (PathPoint p in r.Points)
            {
                if (cur == null || p.Seg != s) { cur = new List<PathPoint>(); segs.Add(cur); s = p.Seg; }
                cur.Add(p);
            }
            // a single point is not a valid line -> duplicate it
            foreach (List<PathPoint> l in segs) if (l.Count == 1) l.Add(l[0]);
            return segs;
        }

        /// <summary>Saves and returns the path of the GeoJSON file.</summary>
        public static string Save(Recorder r, SaveInfo info, string saveRoot)
        {
            string fieldDir = SafeFileName(info.Field);
            if (fieldDir == "") fieldDir = "_" + SafeFileName(Lang.T("export.noField"));
            string dir = Path.Combine(saveRoot, fieldDir);
            Directory.CreateDirectory(dir);

            string baseName = r.StartTime.ToString("yyyy-MM-dd_HHmm", Inv);
            string nm = SafeFileName(info.Name);
            if (nm != "") baseName += "_" + nm.Replace(' ', '_');
            string path = Path.Combine(dir, baseName);
            int k = 2;
            while (File.Exists(path + ".geojson")) { path = Path.Combine(dir, baseName + "_" + k); k++; }

            string geo = path + ".geojson";
            File.WriteAllText(geo, GeoJson(r, info), new UTF8Encoding(false));
            File.WriteAllText(path + ".kml", Kml(r, info), new UTF8Encoding(false));
            LastWarning = "";
            try { AppendIndex(r, info, saveRoot, geo); }
            catch (Exception ex)
            {
                LastWarning = Lang.T("export.csvLocked") + "\n" + ex.Message;
                Log.Write(LastWarning);
            }
            return geo;
        }

        static void Props(StringBuilder sb, Recorder r, SaveInfo info)
        {
            TimeSpan dur = r.EndTime - r.StartTime;
            sb.Append("\"name\":").Append(J(info.Name)).Append(',');
            sb.Append("\"product\":").Append(J(info.Product)).Append(',');
            sb.Append("\"variety\":").Append(J(info.Variety)).Append(',');
            sb.Append("\"field\":").Append(J(info.Field)).Append(',');
            sb.Append("\"date\":").Append(J(r.StartTime.ToString("yyyy-MM-dd", Inv))).Append(',');
            sb.Append("\"start\":").Append(J(Iso(r.StartTime))).Append(',');
            sb.Append("\"end\":").Append(J(Iso(r.EndTime))).Append(',');
            sb.Append("\"duration\":").Append(J(FormatDuration(dur))).Append(',');
            sb.Append("\"duration_s\":").Append(((long)dur.TotalSeconds).ToString(Inv)).Append(',');
            sb.Append("\"distance_m\":").Append(N(r.Distance, "0.0")).Append(',');
            sb.Append("\"width_m\":").Append(N(r.Width, "0.###")).Append(',');
            sb.Append("\"area_ha\":").Append(N(r.DrivenArea / 10000.0, "0.0000")).Append(',');
            sb.Append("\"covered_area_ha\":").Append(N(r.Cover.AreaM2 / 10000.0, "0.0000")).Append(',');
            sb.Append("\"only_sections_on\":").Append(r.OnlyWork ? "true" : "false").Append(',');
            sb.Append("\"gps_filter\":").Append(J(r.MinFix)).Append(',');
            sb.Append("\"skipped_fixes\":").Append(r.SkippedFixes.ToString(Inv)).Append(',');
            sb.Append("\"notes\":").Append(J(info.Notes)).Append(',');
            sb.Append("\"points\":").Append(r.Points.Count.ToString(Inv)).Append(',');
            sb.Append("\"app\":").Append(J("AgPath " + Program.Version));
        }

        static string GeoJson(Recorder r, SaveInfo info)
        {
            List<List<PathPoint>> segs = Segments(r);
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"type\":\"FeatureCollection\",\"features\":[{\"type\":\"Feature\",\"properties\":{");
            Props(sb, r, info);
            // point times (seconds from start) in the same order as the coordinates
            sb.Append(",\"point_times_s\":[");
            bool first = true;
            foreach (List<PathPoint> seg in segs)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('[');
                for (int i = 0; i < seg.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(N((seg[i].Time - r.StartTime).TotalSeconds, "0.0"));
                }
                sb.Append(']');
            }
            sb.Append("]},\n\"geometry\":{");
            if (segs.Count == 1)
            {
                sb.Append("\"type\":\"LineString\",\"coordinates\":");
                Coords(sb, segs[0]);
            }
            else
            {
                sb.Append("\"type\":\"MultiLineString\",\"coordinates\":[");
                for (int i = 0; i < segs.Count; i++)
                {
                    if (i > 0) sb.Append(",\n");
                    Coords(sb, segs[i]);
                }
                sb.Append(']');
            }
            sb.Append("}}]}\n");
            return sb.ToString();
        }

        static void Coords(StringBuilder sb, List<PathPoint> seg)
        {
            sb.Append('[');
            for (int i = 0; i < seg.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('[').Append(N(seg[i].Lon, "0.00000000")).Append(',').Append(N(seg[i].Lat, "0.00000000")).Append(']');
            }
            sb.Append(']');
        }

        static string X(string s) { return SecurityElement.Escape(s ?? ""); }

        static string Kml(Recorder r, SaveInfo info)
        {
            TimeSpan dur = r.EndTime - r.StartTime;
            CultureInfo c = Lang.Culture;
            string title = info.Name != "" ? info.Name : Lang.T("export.run");
            StringBuilder desc = new StringBuilder();
            if (info.Product != "") desc.AppendLine(Lang.T("f.product") + ": " + info.Product);
            if (info.Variety != "") desc.AppendLine(Lang.T("f.variety") + ": " + info.Variety);
            if (info.Field != "") desc.AppendLine(Lang.T("f.field") + ": " + info.Field);
            desc.AppendLine(Lang.T("f.date") + ": " + LocalDate(r.StartTime));
            desc.AppendLine(Lang.T("f.start") + ": " + LocalTime(r.StartTime) + "  " + Lang.T("f.end") + ": " + LocalTime(r.EndTime));
            desc.AppendLine(Lang.T("f.duration") + ": " + FormatDuration(dur));
            desc.AppendLine(Lang.T("f.distance") + ": " + (r.Distance / 1000.0).ToString("0.00", c) + " km");
            desc.AppendLine(Lang.T("f.width") + ": " + r.Width.ToString("0.###", c) + " m");
            desc.AppendLine(Lang.T("f.area") + ": " + (r.DrivenArea / 10000.0).ToString("0.00", c) + " ha");
            desc.AppendLine(Lang.T("f.covered") + ": " + (r.Cover.AreaM2 / 10000.0).ToString("0.00", c) + " ha");
            if (info.Notes != "") desc.AppendLine(Lang.T("f.notes") + ": " + info.Notes);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<kml xmlns=\"http://www.opengis.net/kml/2.2\"><Document>");
            sb.AppendLine("<name>" + X(title + " " + LocalDate(r.StartTime)) + "</name>");
            sb.AppendLine("<Style id=\"path\"><LineStyle><color>ff00a0ff</color><width>3</width></LineStyle></Style>");
            sb.AppendLine("<Placemark><name>" + X(title) + "</name>");
            sb.AppendLine("<description>" + X(desc.ToString()) + "</description>");
            sb.AppendLine("<TimeSpan><begin>" + Iso(r.StartTime) + "</begin><end>" + Iso(r.EndTime) + "</end></TimeSpan>");
            sb.AppendLine("<styleUrl>#path</styleUrl>");
            sb.Append("<ExtendedData>");
            ED(sb, "name", info.Name); ED(sb, "product", info.Product); ED(sb, "variety", info.Variety);
            ED(sb, "field", info.Field); ED(sb, "date", r.StartTime.ToString("yyyy-MM-dd", Inv));
            ED(sb, "start", Iso(r.StartTime)); ED(sb, "end", Iso(r.EndTime));
            ED(sb, "duration", FormatDuration(dur)); ED(sb, "distance_m", N(r.Distance, "0.0"));
            ED(sb, "width_m", N(r.Width, "0.###")); ED(sb, "area_ha", N(r.DrivenArea / 10000.0, "0.0000"));
            ED(sb, "covered_area_ha", N(r.Cover.AreaM2 / 10000.0, "0.0000")); ED(sb, "notes", info.Notes);
            sb.AppendLine("</ExtendedData>");
            sb.AppendLine("<MultiGeometry>");
            foreach (List<PathPoint> seg in Segments(r))
            {
                sb.Append("<LineString><tessellate>1</tessellate><coordinates>");
                foreach (PathPoint p in seg)
                    sb.Append(N(p.Lon, "0.00000000")).Append(',').Append(N(p.Lat, "0.00000000")).Append(",0 ");
                sb.AppendLine("</coordinates></LineString>");
            }
            sb.AppendLine("</MultiGeometry></Placemark></Document></kml>");
            return sb.ToString();
        }

        static void ED(StringBuilder sb, string name, string value)
        {
            sb.Append("<Data name=\"").Append(name).Append("\"><value>").Append(X(value)).Append("</value></Data>");
        }

        static string Sep()
        {
            // Excel uses ';' where the decimal separator is ','
            return Lang.Culture.NumberFormat.NumberDecimalSeparator == "," ? ";" : ",";
        }

        static string C(string s, string sep)
        {
            s = s ?? "";
            if (s.IndexOfAny(new char[] { sep[0], '"', '\n', '\r' }) >= 0)
                return "\"" + s.Replace("\"", "\"\"").Replace("\r", "").Replace("\n", " ") + "\"";
            return s;
        }

        static void AppendIndex(Recorder r, SaveInfo info, string saveRoot, string geoPath)
        {
            string csv = Path.Combine(saveRoot, "recordings.csv");
            bool header = !File.Exists(csv);
            string sep = Sep();
            CultureInfo c = Lang.Culture;
            TimeSpan dur = r.EndTime - r.StartTime;
            StringBuilder sb = new StringBuilder();
            if (header)
            {
                string[] h = { "f.date", "f.start", "f.end", "f.duration", "f.field", "f.name", "f.product", "f.variety",
                               "f.distanceKm", "f.widthM", "f.areaHa", "f.coveredHa", "f.notes", "f.file" };
                for (int i = 0; i < h.Length; i++) { if (i > 0) sb.Append(sep); sb.Append(C(Lang.T(h[i]), sep)); }
                sb.AppendLine();
            }
            string[] v = {
                r.StartTime.ToString("yyyy-MM-dd", Inv), LocalTime(r.StartTime), LocalTime(r.EndTime), FormatDuration(dur),
                info.Field, info.Name, info.Product, info.Variety,
                (r.Distance / 1000.0).ToString("0.000", c), r.Width.ToString("0.###", c),
                (r.DrivenArea / 10000.0).ToString("0.0000", c), (r.Cover.AreaM2 / 10000.0).ToString("0.0000", c),
                info.Notes, MakeRelative(saveRoot, geoPath) };
            for (int i = 0; i < v.Length; i++) { if (i > 0) sb.Append(sep); sb.Append(C(v[i], sep)); }
            sb.AppendLine();
            // UTF-8 BOM so that Excel shows accented letters correctly
            File.AppendAllText(csv, sb.ToString(), new UTF8Encoding(header));
        }

        static string MakeRelative(string root, string path)
        {
            if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return path.Substring(root.Length).TrimStart('\\', '/');
            return path;
        }
    }
}
