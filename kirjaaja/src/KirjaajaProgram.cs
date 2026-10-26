// AgPath Kirjaaja - enters AgPath runs into Agrineuvos.
// Copyright 2026 AgPath contributors. Licensed under the Apache License 2.0, see LICENSE.
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace AgPath
{
    /// <summary>
    /// A separate program for the computer where Agrineuvos is: reads the runs AgPath saved (for example in a
    /// shared OneDrive folder) and enters them into Agrineuvos. AgPath itself is not needed on this computer.
    /// </summary>
    static class Program
    {
        public static string AppDir;
        public const string Version = "0.9.0";

        [STAThread]
        static void Main(string[] args)
        {
            AppDir = Path.GetDirectoryName(Application.ExecutablePath);
            KSettings st = KSettings.Load();
            Lang.Load(st.Language);
            if (args.Length >= 2 && args[0] == "--agrineuvos")
            {
                // worker process that operates Agrineuvos (started by the main window)
                AgrineuvosAuto.RunJobFile(args[1]);
                return;
            }
            bool createdNew;
            using (Mutex m = new Mutex(true, "AgPathKirjaaja_single_instance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(Lang.T("app.alreadyRunning"), "AgPath Kirjaaja", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
                {
                    Log.Write("ERROR: " + e.Exception);
                    MessageBox.Show(Lang.T("app.error") + " " + e.Exception.Message, "AgPath Kirjaaja", MessageBoxButtons.OK, MessageBoxIcon.Error);
                };
                Application.Run(new AgrineuvosForm(st));
            }
        }
    }

    /// <summary>Settings of the Kirjaaja program (kirjaaja_settings.txt next to the exe).</summary>
    class KSettings
    {
        public string DataDir = "";
        public string Language = "auto";

        static string FilePath { get { return Path.Combine(Program.AppDir, "kirjaaja_settings.txt"); } }

        public static KSettings Load()
        {
            KSettings s = new KSettings();
            try
            {
                if (File.Exists(FilePath))
                    foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                    {
                        int i = line.IndexOf('=');
                        if (i <= 0 || line.StartsWith("#")) continue;
                        string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                        if (k == "data_folder") s.DataDir = v;
                        else if (k == "language") s.Language = v;
                    }
            }
            catch { }
            if (s.DataDir == "" || !Directory.Exists(s.DataDir))
            {
                // the usual place: the AgPath folder shared through OneDrive
                foreach (string env in new string[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
                {
                    string od = Environment.GetEnvironmentVariable(env);
                    if (string.IsNullOrEmpty(od)) continue;
                    foreach (string name in new string[] { "Agpath", "AgPath" })
                        if (Directory.Exists(Path.Combine(od, name))) { s.DataDir = Path.Combine(od, name); break; }
                    if (s.DataDir != "") break;
                }
            }
            return s;
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(FilePath, "# AgPath Kirjaaja\r\ndata_folder=" + DataDir + "\r\nlanguage=" + Language + "\r\n", new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Write("Settings: " + ex.Message); }
        }
    }

    static class Log
    {
        static readonly object locker = new object();

        public static void Write(string text)
        {
            try
            {
                lock (locker)
                {
                    string path = Path.Combine(Program.AppDir ?? ".", "log.txt");
                    if (File.Exists(path) && new FileInfo(path).Length > 1000000) File.Delete(path);
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
