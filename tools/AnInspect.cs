// Diagnostic tool: dumps the window and UI Automation tree of the running Agrineuvos client
// into agrineuvos_dump.txt next to this exe. Read only: it never clicks or types anything.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

class AnInspect
{
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumProc f, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, StringBuilder l, uint flags, uint timeout, out IntPtr res);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    static StringBuilder o = new StringBuilder();

    static string Cls(IntPtr h) { StringBuilder s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }
    static string Txt(IntPtr h)
    {
        StringBuilder s = new StringBuilder(512);
        IntPtr r;
        SendMessageTimeout(h, 0x000D, (IntPtr)512, s, 2, 500, out r); // WM_GETTEXT, SMTO_ABORTIFHUNG
        string t = s.ToString().Replace("\r", "\\r").Replace("\n", "\\n");
        return t.Length > 80 ? t.Substring(0, 80) + "..." : t;
    }
    static string Rect(IntPtr h) { RECT r; GetWindowRect(h, out r); return r.L + "," + r.T + " " + (r.R - r.L) + "x" + (r.B - r.T); }

    static void DumpHwnd(IntPtr h, int depth)
    {
        o.AppendLine(new string(' ', depth * 2) + "0x" + h.ToString("X") + " [" + Cls(h) + "] \"" + Txt(h) + "\" " + Rect(h) +
                     " id=" + GetDlgCtrlID(h) + (IsWindowVisible(h) ? "" : " HIDDEN") + (IsWindowEnabled(h) ? "" : " DISABLED"));
        List<IntPtr> kids = new List<IntPtr>();
        EnumChildWindows(h, delegate(IntPtr c, IntPtr l) { if (GetParent(c) == h) kids.Add(c); return true; }, IntPtr.Zero);
        foreach (IntPtr c in kids) DumpHwnd(c, depth + 1);
    }

    static void DumpUia(AutomationElement e, int depth, int maxDepth)
    {
        string line;
        try
        {
            AutomationElement.AutomationElementInformation i = e.Current;
            System.Windows.Rect b = i.BoundingRectangle;
            List<string> pats = new List<string>();
            foreach (AutomationPattern p in e.GetSupportedPatterns()) pats.Add(p.ProgrammaticName.Replace("PatternIdentifiers.Pattern", ""));
            string val = "";
            object vp;
            if (e.TryGetCurrentPattern(ValuePattern.Pattern, out vp)) { try { val = " value=\"" + ((ValuePattern)vp).Current.Value + "\""; } catch { } }
            line = i.ControlType.ProgrammaticName.Replace("ControlType.", "") + " name=\"" + i.Name + "\" aid=\"" + i.AutomationId +
                   "\" class=\"" + i.ClassName + "\" hwnd=0x" + i.NativeWindowHandle.ToString("X") +
                   (b.IsEmpty ? "" : " rect=" + (int)b.X + "," + (int)b.Y + " " + (int)b.Width + "x" + (int)b.Height) +
                   (i.IsOffscreen ? " OFFSCREEN" : "") + val + " [" + string.Join(",", pats.ToArray()) + "]";
        }
        catch (Exception ex) { line = "error: " + ex.Message; }
        o.AppendLine(new string(' ', depth * 2) + line);
        if (depth >= maxDepth) return;
        try
        {
            AutomationElement c = TreeWalker.RawViewWalker.GetFirstChild(e);
            int n = 0;
            while (c != null && n < 400) { DumpUia(c, depth + 1, maxDepth); c = TreeWalker.RawViewWalker.GetNextSibling(c); n++; }
        }
        catch (Exception ex) { o.AppendLine(new string(' ', depth * 2 + 2) + "children error: " + ex.Message); }
    }

    static int Main(string[] args)
    {
        string outFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "agrineuvos_dump.txt");
        try
        {
            o.AppendLine("AnInspect " + DateTime.Now.ToString("s"));
            Process[] ps = Process.GetProcessesByName("AgrineuvosClient");
            if (ps.Length == 0) o.AppendLine("Agrineuvos is not running.");
            foreach (Process p in ps)
            {
                o.AppendLine("Process " + p.Id + " " + p.MainWindowTitle);
                List<IntPtr> tops = new List<IntPtr>();
                uint pid = (uint)p.Id;
                EnumWindows(delegate(IntPtr h, IntPtr l) { uint wp; GetWindowThreadProcessId(h, out wp); if (wp == pid && IsWindowVisible(h)) tops.Add(h); return true; }, IntPtr.Zero);
                o.AppendLine("== Win32 tree ==");
                foreach (IntPtr h in tops) DumpHwnd(h, 0);
                o.AppendLine("== UI Automation tree ==");
                foreach (IntPtr h in tops)
                {
                    try { DumpUia(AutomationElement.FromHandle(h), 0, 14); }
                    catch (Exception ex) { o.AppendLine("UIA error: " + ex.Message); }
                }
            }
        }
        catch (Exception ex) { o.AppendLine("ERROR " + ex); }
        File.WriteAllText(outFile, o.ToString(), new UTF8Encoding(true));
        return 0;
    }
}
