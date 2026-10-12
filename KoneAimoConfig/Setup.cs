// Installs the app for the current user (no admin needed): copies the exe to %LOCALAPPDATA%\Programs,
// adds a Start menu shortcut and an entry in Settings > Apps so it can be uninstalled like anything else.
// The same exe is the installer, the app and the uninstaller.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

static class Setup
{
    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Program.Title);
    static string Exe => Path.Combine(Dir, "KoneAimoConfig.exe");
    static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Program.Title + ".lnk");
    static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Program.Title + ".lnk");
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\UnofficialKoneAimoConfig";

    static Version Installed
    {
        get { using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey)) return Version.TryParse(k?.GetValue("DisplayVersion") as string, out var v) ? v : null; }
    }

    /// Offer to install when this copy isn't the installed one, unless an equal or newer version is installed
    /// or this very copy is already the one that opens at login (a portable setup).
    public static bool ShouldOffer()
    {
        if (string.Equals(Path.GetDirectoryName(Application.ExecutablePath), Dir, StringComparison.OrdinalIgnoreCase)) return false;
        if (AppConfig.StartWithWindows) return false;
        var v = Installed;
        return v == null || v < Program.Version;
    }

    /// OK: installed and started. Ignore: run this copy without installing. Cancel: do nothing.
    public static DialogResult Offer() { using (var f = new SetupForm(Installed)) return f.ShowDialog(); }

    public static void Install(bool openAtLogin, bool desktopShortcut)
    {
        QuitRunning();
        Directory.CreateDirectory(Dir);
        File.Copy(Application.ExecutablePath, Exe, true);
        Shortcut(StartMenuLink);
        if (desktopShortcut) Shortcut(DesktopLink);
        using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            k.SetValue("DisplayName", Program.Title);
            k.SetValue("DisplayVersion", Program.Version.ToString(3));
            k.SetValue("Publisher", "Miguel Valencia");
            k.SetValue("DisplayIcon", Exe);
            k.SetValue("InstallLocation", Dir);
            k.SetValue("UninstallString", $"\"{Exe}\" --uninstall");
            k.SetValue("QuietUninstallString", $"\"{Exe}\" --uninstall --quiet");
            k.SetValue("EstimatedSize", (int)(new FileInfo(Exe).Length / 1024), RegistryValueKind.DWord);
            k.SetValue("NoModify", 1, RegistryValueKind.DWord); k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        if (openAtLogin) Process.Start(Exe, "--startup on").WaitForExit();
        else RemoveStartup();
    }

    /// Takes the "open at login" entry away only if it starts the installed copy (not some other copy of the exe).
    static void RemoveStartup()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(AppConfig.RunKey, true))
            if (k?.GetValue(AppConfig.RunName) is string v && v.IndexOf(Dir, StringComparison.OrdinalIgnoreCase) >= 0) k.DeleteValue(AppConfig.RunName, false);
    }

    public static int Uninstall(bool quiet)
    {
        if (!quiet && MessageBox.Show($"Remove {Program.Title} from this PC?\n\nYour mouse keeps its settings, and your backups in {AppConfig.Dir} stay where they are.",
                Program.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 1;
        QuitRunning();
        RemoveStartup();
        File.Delete(StartMenuLink); File.Delete(DesktopLink);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
        // this exe can't delete itself while it runs, so a hidden cmd removes the folder a moment after it exits
        if (Path.GetFileName(Dir) == Program.Title && Directory.Exists(Dir))
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"{Dir}\"") { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });
        if (!quiet) MessageBox.Show($"{Program.Title} was removed.", Program.Title);
        return 0;
    }

    /// Asks a running copy to quit (so its exe can be replaced) and waits up to 5 s for it.
    static void QuitRunning()
    {
        if (EventWaitHandle.TryOpenExisting(Program.QuitEvent, out var quit)) using (quit) quit.Set();
        for (int i = 0; i < 50 && Mutex.TryOpenExisting(Program.MutexName, out var m); i++) { m.Dispose(); Thread.Sleep(100); }
    }

    static void Shortcut(string link)
    {
        var shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        var sc = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { link });
        void Set(string name, object value) => sc.GetType().InvokeMember(name, BindingFlags.SetProperty, null, sc, new[] { value });
        Set("TargetPath", Exe); Set("WorkingDirectory", Dir); Set("Description", "Settings for the ROCCAT Kone AIMO Remastered mouse");
        sc.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
    }

    public static void Snapshot(string path)
    {
        using (var f = new SetupForm(null) { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000) })
        {
            f.Show(); Application.DoEvents();
            using (var bmp = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height)); bmp.Save(path); }
        }
    }

    class SetupForm : Form
    {
        readonly Switch atLogin = new Switch { Checked = true }, desktop = new Switch();
        readonly Btn install, run = new Btn("Just run it");

        public SetupForm(Version installed)
        {
            bool update = installed != null;
            Text = Program.Title; ClientSize = new Size(520, 400); BackColor = Theme.Window; ForeColor = Theme.Text; Font = Theme.Body;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var title = Theme.Lbl(update ? "Update " + Program.Title : "Install " + Program.Title, Theme.Big); title.Location = new Point(28, 24);
            Paint += (s, e) => { e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; Theme.FillRound(e.Graphics, Theme.Accent, new RectangleF(30, title.Bottom + 4, 30, 4), 2); };
            var about = Theme.Lbl((update ? $"Version {installed.ToString(3)} is installed. This replaces it with {Program.Version.ToString(3)}.\n\n" : "") +
                "Settings for the ROCCAT Kone AIMO Remastered (16K) mouse. Unofficial: not made, endorsed or supported by Turtle Beach or ROCCAT. " +
                "It comes with no warranty, so use it at your own risk.", Theme.Body, Theme.Secondary, 464);
            about.Location = new Point(28, 76); about.Size = about.PreferredSize;   // wrap now, so Bottom is right
            var options = new Group(null, "Installs for your Windows user only, in " + Dir.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%") + ".", 464)
                .Add("Open at login", atLogin, "Needed for multi-step macros and the settings check.")
                .Add("Desktop shortcut", desktop);
            options.Location = new Point(28, about.Bottom + 20);
            install = new Btn(update ? "Update" : "Install", Btn.Kind.Primary);
            install.Location = new Point(ClientSize.Width - install.Width - 28, ClientSize.Height - install.Height - 22);
            run.Location = new Point(install.Left - run.Width - 10, install.Top + 2);
            Controls.AddRange(new Control[] { title, about, options, install, run });

            install.Click += (s, e) =>
            {
                install.Enabled = run.Enabled = false; UseWaitCursor = true;
                try { Install(atLogin.Checked, desktop.Checked); Process.Start(Exe); DialogResult = DialogResult.OK; }
                catch (Exception ex) { UseWaitCursor = false; install.Enabled = run.Enabled = true; MessageBox.Show(this, "Couldn't install: " + ex.Message, Program.Title, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            run.Click += (s, e) => DialogResult = DialogResult.Ignore;
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkWindow(Handle); }
    }
}
