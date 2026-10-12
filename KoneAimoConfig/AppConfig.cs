using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

/// App preferences, the "start with Windows" entry, and the last settings saved to the mouse (%APPDATA%\KoneAimoConfig).
static class AppConfig
{
    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KoneAimoConfig");
    static string F(string name) { Directory.CreateDirectory(Dir); return Path.Combine(Dir, name); }
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", RunName = "Kone AIMO";

    public static bool StartWithWindows
    {
        get { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k?.GetValue(RunName) is string v && v.Contains(Application.ExecutablePath); }
        set
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                if (value) k.SetValue(RunName, $"\"{Application.ExecutablePath}\" --tray");
                else k.DeleteValue(RunName, false);
        }
    }

    public static bool RestoreAtStartup
    {
        get => !File.Exists(F("no-restore"));
        set { if (value) File.Delete(F("no-restore")); else File.WriteAllText(F("no-restore"), ""); }
    }

    public static MouseState LastSaved
    {
        get { try { return File.Exists(F("last_saved.json")) ? MouseState.FromJson(File.ReadAllText(F("last_saved.json"))) : null; } catch { return null; } }
        set => File.WriteAllText(F("last_saved.json"), value.ToJson());
    }

    /// Which multi-step macro each spare key F13-F24 (0-11) plays.
    public static System.Collections.Generic.Dictionary<int, string> MacroSlots
    {
        get
        {
            var d = new System.Collections.Generic.Dictionary<int, string>();
            if (File.Exists(F("macros.txt")))
                foreach (var line in File.ReadAllLines(F("macros.txt")))
                {
                    var p = line.Split('=');
                    if (p.Length == 2 && int.TryParse(p[0], out int n) && n >= 0 && n < 12) d[n] = p[1].Trim();
                }
            return d;
        }
        set => File.WriteAllLines(F("macros.txt"), System.Linq.Enumerable.Select(value, kv => $"{kv.Key}={kv.Value}"));
    }

    public static System.Collections.Generic.List<System.Drawing.Color> RecentColors =>
        File.Exists(F("recent_colors.txt"))
            ? System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(System.Linq.Enumerable.Where(File.ReadAllLines(F("recent_colors.txt")), l => l.Length == 6),
                l => System.Drawing.Color.FromArgb(Convert.ToInt32(l, 16) | unchecked((int)0xFF000000))))
            : new System.Collections.Generic.List<System.Drawing.Color>();
    public static void AddRecentColor(System.Drawing.Color c)
    {
        var hex = $"{c.R:X2}{c.G:X2}{c.B:X2}";
        var list = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(RecentColors, x => $"{x.R:X2}{x.G:X2}{x.B:X2}"));
        list.Remove(hex); list.Insert(0, hex);
        File.WriteAllLines(F("recent_colors.txt"), System.Linq.Enumerable.Take(list, 7));
    }

    /// What the mouse held the first time this app saw it, kept once and never overwritten.
    public static string OriginalBackup => F("original_settings.json");
    public static void KeepOriginal(MouseState s) { if (!File.Exists(OriginalBackup)) File.WriteAllText(OriginalBackup, s.ToJson()); }

    public static string LastCheck
    {
        get => File.Exists(F("last_check.txt")) ? File.ReadAllText(F("last_check.txt")) : "Not run yet.";
        set => File.WriteAllText(F("last_check.txt"), value);
    }
}

/// Proves the mouse keeps a change without power: change one value, unplug + replug, read it back, put it back.
static class PersistTest
{
    public class Marker { public MouseState Original; public int Profile, Dpi; }

    /// Moves DPI stage 1 of a profile that is not in use by 50, so nothing you feel changes.
    public static Marker Mark()
    {
        return KoneAimo.Use(m =>
        {
            var original = MouseState.Read(m);
            int p = (original.Active + 1) % KoneAimo.Profiles;
            var t = original.Clone();
            for (int axis = 0; axis < 2; axis++) { int d = t.P[p].Dpi(axis, 0); t.P[p].SetDpi(axis, 0, d <= 15950 ? d + 50 : d - 50); }
            MouseState.Apply(m, t, original);
            return new Marker { Original = original, Profile = p, Dpi = t.P[p].Dpi(0, 0) };
        });
    }

    public static bool StillThere(Marker mk) => KoneAimo.Use(m => MouseState.Read(m).P[mk.Profile].Dpi(0, 0) == mk.Dpi);

    public static void Restore(Marker mk) => KoneAimo.Use(m => MouseState.Apply(m, mk.Original.Clone(), MouseState.Read(m)));

    /// Blocks until the mouse has been unplugged and plugged back in (or the time runs out).
    public static bool WaitForReplug(TimeSpan timeout, Action<string> say, Func<bool> cancelled = null)
    {
        var end = DateTime.Now + timeout; cancelled = cancelled ?? (() => false);
        say("Unplug the mouse now.");
        while (KoneAimo.IsPresent) { if (DateTime.Now > end || cancelled()) return false; Thread.Sleep(250); }
        say("Unplugged. Wait a few seconds, then plug it back in.");
        while (!KoneAimo.IsPresent) { if (DateTime.Now > end || cancelled()) return false; Thread.Sleep(250); }
        say("Plugged back in. Checking.");
        Thread.Sleep(2000);   // let the firmware start up
        return true;
    }
}
