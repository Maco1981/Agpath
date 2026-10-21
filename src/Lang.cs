using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace AgPath
{
    /// <summary>
    /// Translations. Text files lang\xx.txt (UTF-8), one "key = text" per line, \n = new line.
    /// The language follows AgOpenGPS (registry HKCU\SOFTWARE\AgOpenGPS\Language) unless set in settings.
    /// Missing texts fall back to English (en.txt).
    /// </summary>
    static class Lang
    {
        static readonly Dictionary<string, string> texts = new Dictionary<string, string>();
        static readonly Dictionary<string, string> fallback = new Dictionary<string, string>();
        public static string Code = "en";
        public static CultureInfo Culture = CultureInfo.InvariantCulture;

        public static string Dir { get { return Path.Combine(Program.AppDir, "lang"); } }

        public static string AogLanguage()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\AgOpenGPS"))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("Language");
                        if (v != null && v.ToString() != "") return v.ToString();
                    }
                }
            }
            catch { }
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        }

        public static void Load(string setting)
        {
            texts.Clear();
            fallback.Clear();
            string want = (string.IsNullOrEmpty(setting) || setting == "auto") ? AogLanguage() : setting;
            ReadFile(Path.Combine(Dir, "en.txt"), fallback);

            string file = Path.Combine(Dir, want + ".txt");
            if (!File.Exists(file) && want.Contains("-"))
            {
                want = want.Substring(0, want.IndexOf('-'));
                file = Path.Combine(Dir, want + ".txt");
            }
            if (!File.Exists(file)) { want = "en"; file = Path.Combine(Dir, "en.txt"); }
            Code = want;
            ReadFile(file, texts);

            try { Culture = CultureInfo.CreateSpecificCulture(Code); }
            catch { Culture = CultureInfo.CurrentCulture; }
        }

        static void ReadFile(string path, Dictionary<string, string> into)
        {
            try
            {
                if (!File.Exists(path)) return;
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.TrimStart();
                    if (line == "" || line.StartsWith("#")) continue;
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i).Trim();
                    string v = line.Substring(i + 1).Trim().Replace("\\n", "\n");
                    into[k] = v;
                }
            }
            catch (Exception ex) { Log.Write("Language file " + path + ": " + ex.Message); }
        }

        public static string T(string key)
        {
            string v;
            if (texts.TryGetValue(key, out v) && v != "") return v;
            if (fallback.TryGetValue(key, out v) && v != "") return v;
            return key;
        }

        public static string T(string key, params object[] args)
        {
            try { return string.Format(Culture, T(key), args); }
            catch { return T(key); }
        }

        /// <summary>Texts for the map page (keys starting with "web.").</summary>
        public static Dictionary<string, string> WebTexts()
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> kv in fallback) if (kv.Key.StartsWith("web.")) d[kv.Key.Substring(4)] = kv.Value;
            foreach (KeyValuePair<string, string> kv in texts) if (kv.Key.StartsWith("web.") && kv.Value != "") d[kv.Key.Substring(4)] = kv.Value;
            return d;
        }

        /// <summary>Available languages: code -> language name (from the file's "language.name").</summary>
        public static List<KeyValuePair<string, string>> Available()
        {
            List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
            try
            {
                foreach (string f in Directory.GetFiles(Dir, "*.txt"))
                {
                    Dictionary<string, string> d = new Dictionary<string, string>();
                    ReadFile(f, d);
                    string code = Path.GetFileNameWithoutExtension(f);
                    string name;
                    if (!d.TryGetValue("language.name", out name)) name = code;
                    list.Add(new KeyValuePair<string, string>(code, name));
                }
            }
            catch { }
            list.Sort(delegate(KeyValuePair<string, string> a, KeyValuePair<string, string> b) { return string.Compare(a.Value, b.Value, StringComparison.CurrentCultureIgnoreCase); });
            return list;
        }

        public static string N(double d, string fmt) { return d.ToString(fmt, Culture); }
    }
}
