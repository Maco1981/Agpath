// AgPath - records driven paths alongside AgOpenGPS.
// Copyright 2026 AgPath contributors. Licensed under the Apache License 2.0, see LICENSE.
using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace AgPath
{
    static class Program
    {
        public static string AppDir;
        public const string Version = "1.1.0";

        [STAThread]
        static void Main()
        {
            AppDir = Path.GetDirectoryName(Application.ExecutablePath);
            bool createdNew;
            using (Mutex m = new Mutex(true, "AgPath_single_instance", out createdNew))
            {
                Settings settings = Settings.Load();
                Lang.Load(settings.Language);
                if (!createdNew)
                {
                    MessageBox.Show(Lang.T("app.alreadyRunning"), "AgPath", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
                {
                    Log.Write("ERROR: " + e.Exception);
                    MessageBox.Show(Lang.T("app.error") + " " + e.Exception.Message, "AgPath",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                };
                Application.Run(new MainForm(settings));
            }
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
                    if (File.Exists(path) && new FileInfo(path).Length > 1000000)
                        File.Delete(path);
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
