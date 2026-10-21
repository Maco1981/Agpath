using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace AgPath
{
    class PathPoint
    {
        public double Lat, Lon;
        public DateTime Time;
        public bool Work;     // sections on (or unknown = true)
        public int Seg;       // part number (a GPS gap starts a new part)
        public double X, Y;   // local metric coordinates
    }

    /// <summary>Covered area on a grid: overlapping passes are counted once.</summary>
    class Coverage
    {
        public double Cell = 0.3;
        readonly HashSet<long> cells = new HashSet<long>();

        public void Clear() { cells.Clear(); }

        public double AreaM2 { get { return cells.Count * Cell * Cell; } }

        /// <summary>Adds a capsule of radius r (working width / 2) around segment a-b.</summary>
        public void AddCapsule(double ax, double ay, double bx, double by, double r)
        {
            if (r <= 0) return;
            double minX = Math.Min(ax, bx) - r, maxX = Math.Max(ax, bx) + r;
            double minY = Math.Min(ay, by) - r, maxY = Math.Max(ay, by) + r;
            int i0 = (int)Math.Floor(minX / Cell), i1 = (int)Math.Floor(maxX / Cell);
            int j0 = (int)Math.Floor(minY / Cell), j1 = (int)Math.Floor(maxY / Cell);
            double dx = bx - ax, dy = by - ay;
            double len2 = dx * dx + dy * dy;
            double r2 = r * r;
            for (int i = i0; i <= i1; i++)
            {
                double cx = (i + 0.5) * Cell;
                for (int j = j0; j <= j1; j++)
                {
                    double cy = (j + 0.5) * Cell;
                    double t = 0;
                    if (len2 > 1e-9) t = ((cx - ax) * dx + (cy - ay) * dy) / len2;
                    if (t < 0) t = 0; else if (t > 1) t = 1;
                    double px = ax + t * dx - cx, py = ay + t * dy - cy;
                    if (px * px + py * py <= r2)
                        cells.Add(((long)i << 32) ^ (uint)j);
                }
            }
        }
    }

    /// <summary>Recording of one run.</summary>
    class Recorder
    {
        public readonly List<PathPoint> Points = new List<PathPoint>();
        public DateTime StartTime, EndTime;
        public bool Active;
        public double Width = 3;
        public bool OnlyWork = false;
        public double MinStep = 0.3;
        public double MaxJump = 25;        // m: a bigger jump = GPS gap, not connected
        public double MaxGapSeconds = 10;  // s: a longer pause between points = new part

        public double Distance;   // m, total distance
        public double WorkDistance; // m, distance counted for area
        public double DrivenArea; // m2, length x width
        public readonly Coverage Cover = new Coverage();

        double lat0, lon0, cosLat0;
        bool hasOrigin;
        int seg;
        PathPoint last;
        DateTime lastFixTime;

        public void Start()
        {
            Points.Clear();
            Cover.Clear();
            Distance = WorkDistance = DrivenArea = 0;
            hasOrigin = false;
            last = null;
            seg = 0;
            StartTime = DateTime.Now;
            EndTime = StartTime;
            Active = true;
        }

        public void Stop()
        {
            Active = false;
            EndTime = DateTime.Now;
        }

        public TimeSpan Elapsed
        {
            get { return (Active ? DateTime.Now : EndTime) - StartTime; }
        }

        void ToLocal(double lat, double lon, out double x, out double y)
        {
            const double R = 6371008.8;
            x = (lon - lon0) * Math.PI / 180.0 * R * cosLat0;
            y = (lat - lat0) * Math.PI / 180.0 * R;
        }

        public void AddFix(double lat, double lon, bool work, DateTime time)
        {
            if (!Active) return;
            AddPointInternal(lat, lon, work, time, -1, true);
        }

        /// <param name="forcedSeg">-1 = automatic</param>
        void AddPointInternal(double lat, double lon, bool work, DateTime time, int forcedSeg, bool filter)
        {
            if (!hasOrigin)
            {
                lat0 = lat; lon0 = lon; cosLat0 = Math.Cos(lat * Math.PI / 180.0);
                hasOrigin = true;
            }
            double x, y;
            ToLocal(lat, lon, out x, out y);

            if (last != null)
            {
                double dx = x - last.X, dy = y - last.Y;
                double d = Math.Sqrt(dx * dx + dy * dy);
                bool newSeg;
                if (forcedSeg >= 0) newSeg = forcedSeg != last.Seg;
                else newSeg = d > MaxJump || (time - lastFixTime).TotalSeconds > MaxGapSeconds;

                if (!newSeg && filter && d < MinStep && work == last.Work)
                {
                    lastFixTime = time;
                    return; // too close to the previous point
                }

                if (newSeg)
                {
                    seg = forcedSeg >= 0 ? forcedSeg : seg + 1;
                }
                else
                {
                    Distance += d;
                    bool count = !OnlyWork || (work && last.Work);
                    if (count)
                    {
                        WorkDistance += d;
                        DrivenArea += d * Width;
                        Cover.AddCapsule(last.X, last.Y, x, y, Width / 2.0);
                    }
                }
            }
            else if (forcedSeg >= 0) seg = forcedSeg;

            PathPoint p = new PathPoint();
            p.Lat = lat; p.Lon = lon; p.Time = time; p.Work = work; p.Seg = seg; p.X = x; p.Y = y;
            Points.Add(p);
            last = p;
            lastFixTime = time;
        }

        /// <summary>Recomputes distance and areas (e.g. after the width changes).</summary>
        public void Recompute()
        {
            List<PathPoint> copy = new List<PathPoint>(Points);
            bool wasActive = Active;
            Points.Clear();
            Cover.Clear();
            Distance = WorkDistance = DrivenArea = 0;
            hasOrigin = false;
            last = null;
            seg = 0;
            foreach (PathPoint p in copy) AddPointInternal(p.Lat, p.Lon, p.Work, p.Time, p.Seg, false);
            Active = wasActive;
        }

        public int SegmentCount
        {
            get { return Points.Count == 0 ? 0 : Points[Points.Count - 1].Seg + 1; }
        }

        // ------------------------------------------------------------------
        // Backup of a running recording (in case of power loss)
        // ------------------------------------------------------------------

        public static string BackupPath { get { return Path.Combine(Program.AppDir, "recording_backup.txt"); } }

        public void WriteBackup()
        {
            try
            {
                CultureInfo ci = CultureInfo.InvariantCulture;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("start=" + StartTime.Ticks);
                sb.AppendLine("width=" + Width.ToString(ci));
                sb.AppendLine("onlywork=" + (OnlyWork ? "1" : "0"));
                foreach (PathPoint p in Points)
                {
                    sb.Append(p.Lat.ToString("0.000000000", ci)).Append(';')
                      .Append(p.Lon.ToString("0.000000000", ci)).Append(';')
                      .Append(p.Time.Ticks).Append(';')
                      .Append(p.Work ? '1' : '0').Append(';')
                      .Append(p.Seg).AppendLine();
                }
                string tmp = BackupPath + ".tmp";
                File.WriteAllText(tmp, sb.ToString());
                if (File.Exists(BackupPath)) File.Delete(BackupPath);
                File.Move(tmp, BackupPath);
            }
            catch (Exception ex) { Log.Write("Backup: " + ex.Message); }
        }

        public static void DeleteBackup()
        {
            try { if (File.Exists(BackupPath)) File.Delete(BackupPath); } catch { }
        }

        public static Recorder ReadBackup()
        {
            try
            {
                if (!File.Exists(BackupPath)) return null;
                CultureInfo ci = CultureInfo.InvariantCulture;
                Recorder r = new Recorder();
                List<PathPoint> pts = new List<PathPoint>();
                foreach (string line in File.ReadAllLines(BackupPath))
                {
                    if (line.StartsWith("start=")) { r.StartTime = new DateTime(long.Parse(line.Substring(6))); continue; }
                    if (line.StartsWith("width=")) { r.Width = double.Parse(line.Substring(6), ci); continue; }
                    if (line.StartsWith("onlywork=")) { r.OnlyWork = line.Substring(9) == "1"; continue; }
                    string[] f = line.Split(';');
                    if (f.Length < 5) continue;
                    PathPoint p = new PathPoint();
                    p.Lat = double.Parse(f[0], ci);
                    p.Lon = double.Parse(f[1], ci);
                    p.Time = new DateTime(long.Parse(f[2]));
                    p.Work = f[3] == "1";
                    p.Seg = int.Parse(f[4]);
                    pts.Add(p);
                }
                if (pts.Count == 0) return null;
                r.Points.AddRange(pts);
                r.EndTime = pts[pts.Count - 1].Time;
                r.Recompute();
                r.Active = false;
                return r;
            }
            catch (Exception ex)
            {
                Log.Write("Reading backup: " + ex.Message);
                return null;
            }
        }

        /// <summary>Continue a restored recording.</summary>
        public void Resume()
        {
            Active = true;
            if (Points.Count > 0) lastFixTime = Points[Points.Count - 1].Time;
        }
    }
}
