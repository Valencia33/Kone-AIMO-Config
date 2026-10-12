using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string mode = args.FirstOrDefault() ?? "";
        if (mode == "--dump" && args.Length == 2) return Run(args[1], Dump);
        if (mode == "--selftest" && args.Length == 2) return Run(args[1], SelfTest);
        if (mode == "--persist-test" && args.Length >= 2) return PersistCli(args[1], args.Length > 2 ? int.Parse(args[2]) : 600);
        if (mode == "--screenshot" && args.Length == 2) { Screenshots(args[1]); return 0; }
        if (mode == "--macro-test" && args.Length == 2) return MacroTest(args[1]);
        if (mode == "--light-test" && args.Length == 2) return Run(args[1], LightTest);
        if (mode == "--startup" && args.Length == 2) { AppConfig.StartWithWindows = args[1] == "on"; return 0; }

        // one copy at a time: starting it again just brings the running one forward
        using (var mutex = new Mutex(true, @"Local\KoneAimoConfig", out bool first))
        using (var open = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\KoneAimoConfig-Open"))
        {
            if (!first) { open.Set(); return 0; }
            var form = new MainForm(trayStart: mode == "--tray");
            new Thread(() => { while (open.WaitOne()) try { form.BeginInvoke((Action)form.OpenWindow); } catch { } }) { IsBackground = true }.Start();
            Application.Run(form);
        }
        return 0;
    }

    // test modes: results go to a text file (a window app has no console)
    static int Run(string outFile, Action<KoneAimo, StringBuilder> test)
    {
        var log = new StringBuilder();
        try { using (var m = KoneAimo.Open()) test(m, log); log.AppendLine("RESULT OK"); }
        catch (Exception ex) { log.AppendLine("RESULT FAILED: " + ex); }
        File.WriteAllText(outFile, log.ToString());
        return log.ToString().Contains("RESULT OK") ? 0 : 1;
    }

    static void Dump(KoneAimo m, StringBuilder log)
    {
        var st = MouseState.Read(m);
        log.AppendLine($"active profile {st.Active + 1}");
        for (int i = 0; i < KoneAimo.Profiles; i++)
        {
            var p = st.P[i];
            log.AppendLine($"Profile {i + 1}: checksum {(p.ChecksumOk ? "ok" : "BAD")}, sensitivity {p.Sensitivity}, polling {Profile.PollingHz[p.Polling]} Hz, angle snapping {p.AngleSnapping}");
            log.AppendLine("  DPI " + string.Join(", ", Enumerable.Range(0, 5).Select(k => $"{(k == p.ActiveStage ? "*" : "")}{p.Dpi(0, k)}x{p.Dpi(1, k)}{(p.StageEnabled(k) ? "" : " (off)")}")));
            log.AppendLine($"  lighting effect {p.Effect}, speed {p.EffectSpeed}, brightness {p.Brightness}, theme 0x{p.Theme:X2}; LEDs " + string.Join(" ", Enumerable.Range(0, 11).Select(k => $"#{p.Led(k).R:X2}{p.Led(k).G:X2}{p.Led(k).B:X2}")));
            log.AppendLine("  buttons: " + string.Join(" | ", Enumerable.Range(0, 24).Select(s => $"{(s < 12 ? "" : "ES ")}{Actions.Slots[s % 12]}={Describe(p, s)}")));
        }
        log.AppendLine(st.ToJson());
    }

    static string Describe(Profile p, int s) => p.Action(s) == Actions.Shortcut ? Keys2Hid.Label(p.Shortcut(s).mods, p.Shortcut(s).key) : Actions.Name(p.Action(s));

    /// Changes a few things in profile 1 through the same path the window uses, checks them, then puts everything back.
    static void SelfTest(KoneAimo m, StringBuilder log)
    {
        var original = MouseState.Read(m);
        log.AppendLine("read original, checksums " + string.Join(",", original.P.Select(p => p.ChecksumOk)));
        var t = original.Clone(); var p = t.P[0];
        p.SetDpi(0, 4, 3150); p.SetDpi(1, 4, 3150); p.Sensitivity = 1; p.EffectSpeed = 5; p.SetLed(0, Color.FromArgb(10, 20, 30));
        p.SetAction(12, Actions.Shortcut, 0x01, 0x06);   // Easy-Shift + left button = Ctrl+C
        var after = MouseState.Apply(m, t, original);
        log.AppendLine($"changed + read back identical: {after.SameAs(t)}; DPI 5 = {after.P[0].Dpi(0, 4)}, sens {after.P[0].Sensitivity}, wheel {after.P[0].Led(0)}, theme 0x{after.P[0].Theme:X2}, ES+left = {Describe(after.P[0], 12)}");
        var restored = MouseState.Apply(m, original.Clone(), after);
        log.AppendLine($"restored, identical to original: {restored.SameAs(original)}");
        if (!restored.SameAs(original)) throw new Exception("restore mismatch");
    }

    /// The unplug test without the window; writes progress line by line.
    static int PersistCli(string outFile, int seconds)
    {
        void Log(string s) => File.AppendAllText(outFile, $"{DateTime.Now:HH:mm:ss} {s}\r\n");
        File.WriteAllText(outFile, "");
        PersistTest.Marker mk = null;
        try
        {
            mk = PersistTest.Mark();
            Log($"marked: profile {mk.Profile + 1} DPI stage 1 = {mk.Dpi} (was {mk.Original.P[mk.Profile].Dpi(0, 0)})");
            if (!PersistTest.WaitForReplug(TimeSpan.FromSeconds(seconds), Log)) { Log("TIMEOUT: the mouse was not unplugged and replugged"); }
            else Log(PersistTest.StillThere(mk) ? "RESULT KEPT: the change survived the power loss" : "RESULT LOST: the mouse forgot the change");
        }
        catch (Exception ex) { Log("ERROR " + ex.Message); }
        try { if (mk != null) { PersistTest.Restore(mk); Log("restored: identical to before = " + KoneAimo.Use(MouseState.Read).SameAs(mk.Original)); } }
        catch (Exception ex) { Log("RESTORE ERROR " + ex.Message); return 1; }
        return 0;
    }

    /// Checks the macro list fits the mouse, then plays a harmless test macro (F15, Shift+F16) through the real
    /// hotkey + SendInput path into this program's own window. Skipped if the window can't get the keyboard focus.
    static int MacroTest(string outFile)
    {
        var log = new StringBuilder(); bool ok = true;
        foreach (var m in Macros.Blender)
            foreach (var (mods, key) in m.Steps)
                if (Keys2Hid.Usage(key) == 0) { log.AppendLine($"NOT STORABLE: {m.Name} {KeyNames.Label(mods, key)}"); ok &= !m.InMouse; }
        log.AppendLine($"{Macros.Blender.Length} Blender macros, {Macros.Blender.Count(m => m.InMouse)} stored in the mouse, {Macros.Blender.Count(m => !m.InMouse)} typed by the app");

        var got = new System.Collections.Generic.List<string>();
        var f = new Form { TopMost = true, StartPosition = FormStartPosition.CenterScreen, Size = new Size(300, 120), Text = "Kone AIMO macro test", KeyPreview = true };
        f.KeyDown += (s, e) => { got.Add(KeyNames.Label(e.Shift ? 2 : 0, e.KeyCode)); e.Handled = e.SuppressKeyPress = true; };
        using (var player = new MacroPlayer())
        {
            var test = new Macro { Id = "test", Name = "test", Steps = new[] { (0, Keys.F15), (KeyNames.Shift, Keys.F16) } };
            var failed = player.Bind(new System.Collections.Generic.Dictionary<int, Macro> { [11] = test });   // F24
            f.Show(); f.Activate(); Application.DoEvents(); Thread.Sleep(300); Application.DoEvents();
            if (failed.Count > 0) { log.AppendLine("F24 is taken by another program; skipped the live test"); }
            else if (GetForegroundWindow() != f.Handle) log.AppendLine("SKIPPED live test: the test window could not take the keyboard focus");
            else
            {
                var press = new Macro { Steps = new[] { (0, Keys.F24) } };
                MacroPlayer.Play(press);                              // as if the mouse sent F24
                var end = DateTime.Now.AddSeconds(3);
                while (got.Count < 2 && DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(20); }
                log.AppendLine("received: " + string.Join(", ", got));
                bool live = got.Contains("F15") && got.Contains("Shift+F16") && !got.Contains("F24");
                log.AppendLine(live ? "LIVE OK: F24 was caught and the macro typed F15, Shift+F16" : "LIVE FAILED");
                ok &= live;
            }
        }
        f.Close();
        log.AppendLine(ok ? "RESULT OK" : "RESULT FAILED");
        File.WriteAllText(outFile, log.ToString());
        return ok ? 0 : 1;
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();

    /// Lights each LED white on its own (live, nothing stored), then gives the mouse its own lighting back.
    static void LightTest(KoneAimo m, StringBuilder log)
    {
        var before = MouseState.Read(m);
        m.DirectMode(true);
        for (int i = 0; i < 11; i++)
        {
            var leds = Enumerable.Repeat(Color.Black, 11).ToArray(); leds[i] = Color.White;
            m.DirectColors(leds); log.AppendLine($"{DateTime.Now:HH:mm:ss.f} lit {Profile.LedNames[i]}");
            Thread.Sleep(600);
        }
        m.DirectMode(false);
        log.AppendLine("stored settings untouched: " + MouseState.Read(m).SameAs(before));
    }

    static void Screenshots(string dir)
    {
        Directory.CreateDirectory(dir);
        var f = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000), LiveAllowed = false };
        f.Show(); Application.DoEvents();
        for (int i = 0; i < f.PageCount; i++)
        {
            f.ShowPage(i); Application.DoEvents();
            using (var bmp = new Bitmap(f.Width, f.Height))
            {
                f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                bmp.Save(Path.Combine(dir, $"page{i}.png"));
            }
        }
        ColorPicker.Snapshot(Path.Combine(dir, "picker.png"));
        f.Tray.Visible = false;
        Environment.Exit(0);
    }
}
