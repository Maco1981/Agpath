// AgPath - records driven paths alongside AgOpenGPS.
// Copyright 2026 AgPath contributors. Licensed under the Apache License 2.0, see LICENSE.
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace AgPath
{
    static class Program
    {
        public static string AppDir;
        public const string Version = "1.2.1-agrineuvos";

        [STAThread]
        static void Main(string[] args)
        {
            AppDir = Path.GetDirectoryName(Application.ExecutablePath);
            if (args.Length >= 3 && args[0] == "--parcel-test")
            {
                // diagnostics: around lat,lon (args[1]) create fields for all parcels into folder args[2]\Fields
                Lang.Load(Settings.Load().Language);
                StringBuilder sb = new StringBuilder();
                try
                {
                    if (args[1].EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        FieldDetector fv = new FieldDetector();
                        fv.Load(Settings.Load().AogBaseDir);
                        Directory.CreateDirectory(Path.Combine(args[2], "Fields"));
                        foreach (bool kasvu in new bool[] { false, true })
                        {
                            System.Collections.Generic.List<Parcel> vs = ParcelImport.ReadVipu(args[1], kasvu);
                            sb.AppendLine((kasvu ? "KASVU " : "PERUS ") + vs.Count);
                            foreach (Parcel p in vs)
                            {
                                sb.AppendLine(p.Tunnus + "\t" + p.KlTunnus + "\t" + p.Name + "\t" + p.AreaHa + "\t" + p.Crops + "\t" + p.Ring.Count + "\t" + p.Holes.Count + "\t" + p.Source + "\t" + fv.FieldAt(p.CLat, p.CLon));
                                if (!kasvu && p.Holes.Count > 0 && !Directory.Exists(Path.Combine(Path.Combine(args[2], "Fields"), "V" + p.Tunnus)))
                                    ParcelImport.CreateField(args[2], "V" + p.Tunnus, p);
                            }
                        }
                        File.WriteAllText(Path.Combine(args[2], "parcel_test.txt"), sb.ToString());
                        return;
                    }
                    string[] ll = args[1].Split(',');
                    double la = double.Parse(ll[0], System.Globalization.CultureInfo.InvariantCulture);
                    double lo = double.Parse(ll[1], System.Globalization.CultureInfo.InvariantCulture);
                    double e, n;
                    Tm35.ToTm(la, lo, out e, out n);
                    int yr = ParcelImport.LatestYear();
                    sb.AppendLine("year " + yr + " tm " + e + " " + n);
                    System.Collections.Generic.List<Parcel> ps = ParcelImport.Fetch(yr, new double[] { e - 400, n - 400, e + 400, n + 400 }, null);
                    FieldDetector fd = new FieldDetector();
                    fd.Load(Settings.Load().AogBaseDir);
                    Directory.CreateDirectory(Path.Combine(args[2], "Fields"));
                    foreach (Parcel p in ps)
                    {
                        sb.AppendLine(p.Tunnus + "\t" + p.AreaHa + "\t" + p.Crops + "\t" + p.Ring.Count + "\t" + p.CLat + "," + p.CLon + "\t" + fd.FieldAt(p.CLat, p.CLon));
                        ParcelImport.CreateField(args[2], "T" + p.Tunnus, p);
                    }
                }
                catch (Exception ex) { sb.AppendLine("ERROR " + ex); }
                File.WriteAllText(Path.Combine(args[2], "parcel_test.txt"), sb.ToString());
                return;
            }
            if (args.Length >= 2 && args[0] == "--agrineuvos")
            {
                // separate worker process that enters runs into Agrineuvos (started by AgrineuvosForm)
                Lang.Load(Settings.Load().Language);
                AgrineuvosAuto.RunJobFile(args[1]);
                return;
            }
            bool createdNew;
            using (Mutex m = new Mutex(true, "AgPath_single_instance", out createdNew))
            {
                Settings settings = Settings.Load();
                Lang.Load(settings.Language);
                Units.Load(settings);
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
