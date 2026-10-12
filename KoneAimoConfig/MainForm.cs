using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

class MainForm : Form
{
    MouseState onMouse, state;   // what the mouse holds / what is being edited
    int cur, layer;              // profile being edited, button layer (0 normal, 12 Easy-Shift)
    bool loading, allowShow, quitting, toldAboutTray, wantSeparate;
    readonly bool trayStart;
    readonly MacroPlayer player = new MacroPlayer();
    Dictionary<int, string> macroSlots = AppConfig.MacroSlots;
    public readonly NotifyIcon Tray = new NotifyIcon();
    public bool LiveAllowed = true;   // off for screenshots

    // frame
    readonly Label deviceStatus = Theme.Lbl("Looking for the mouse", Theme.Small, Theme.Secondary);
    readonly Label pageTitle = Theme.Lbl("", Theme.Title);
    readonly Label status = Theme.Lbl("", Theme.Small, Theme.Secondary);
    readonly Btn save = new Btn("Save to Mouse", Btn.Kind.Primary), revert = new Btn("Revert");
    readonly Segmented profiles = new Segmented("Profile 1", "Profile 2", "Profile 3", "Profile 4", "Profile 5");
    readonly Btn useProfile = new Btn("Use This Profile", Btn.Kind.Plain);
    readonly Label activeNote = Theme.Lbl("", Theme.Small, Theme.Secondary);
    readonly Panel host = Theme.DarkScrollbars(new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Window });
    readonly List<(NavItem nav, Control page, string title)> pages = new List<(NavItem, Control, string)>();

    // performance
    readonly Group dpiGroup = new Group("DPI", "The two buttons behind the wheel cycle through the stages that are on. 100 to 16,000 DPI, in steps of 50.");
    readonly Segmented stageInUse = new Segmented("1", "2", "3", "4", "5");
    readonly Switch[] stageOn = new Switch[5];
    readonly Slider[] stageSlider = new Slider[5];
    readonly NumberField[] dpiX = new NumberField[5], dpiY = new NumberField[5];
    readonly FlowLayoutPanel[] stageRow = new FlowLayoutPanel[5];
    readonly Switch separateXY = new Switch();
    readonly Segmented polling = new Segmented("125 Hz", "250 Hz", "500 Hz", "1000 Hz");
    readonly Slider sens = new Slider(-5, 5, 1, 200);
    readonly Label sensValue = Theme.Lbl("0", Theme.Body, Theme.Secondary);
    readonly Switch snap = new Switch();

    // lighting
    readonly MouseView mouseView = new MouseView();
    readonly PopupButton effect = new PopupButton(190);
    readonly Slider speed = new Slider(1, 11, 1, 170), bright = new Slider(0, 255, 1, 170);
    readonly Label speedValue = Theme.Lbl("", Theme.Body, Theme.Secondary), brightValue = Theme.Lbl("", Theme.Body, Theme.Secondary);
    readonly ColorWell[] wells = Enumerable.Range(0, 11).Select(_ => new ColorWell()).ToArray();
    readonly Group colourGroup = new Group("Colours", "", 390);
    readonly Switch livePreview = new Switch();
    readonly Timer liveTimer = new Timer { Interval = 40 };
    bool liveDirty, inDirect; int hoverLed = -1, page; int[] pickLeds; Color pickColor;

    // buttons
    readonly Segmented layerSel = new Segmented("Normal", "While Holding Easy-Shift");
    readonly PopupButton[] act = Enumerable.Range(0, 12).Select(_ => new PopupButton(206)).ToArray();
    readonly KeyCaps[] caps = Enumerable.Range(0, 12).Select(_ => new KeyCaps(196)).ToArray();

    // profiles & app
    readonly PopupButton copyTo = new PopupButton(120);
    int copyTarget = 1;
    readonly Switch showRaw = new Switch();
    readonly TextBox raw = Theme.DarkScrollbars(new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Size = new Size(640, 170), Font = Theme.Mono,
        BackColor = Theme.Group, ForeColor = Theme.Secondary, BorderStyle = BorderStyle.None, Visible = false });
    readonly Group rawGroup = new Group("Advanced");
    readonly Switch startup = new Switch(), restore = new Switch();
    readonly Label lastCheck = Theme.Lbl("", Theme.Body, Theme.Secondary, 300), macroCount = Theme.Lbl("", Theme.Body, Theme.Secondary);

    static readonly (int code, string name)[] Effects = { (0, "Off"), (1, "Fully lit"), (2, "Blinking"), (3, "Breathing"), (4, "Heartbeat"), (7, "Wave"), (9, "AIMO") };

    public MainForm(bool trayStart = false)
    {
        this.trayStart = trayStart; allowShow = !trayStart;
        Text = "Kone AIMO"; Font = Theme.Body; BackColor = Theme.Window; ForeColor = Theme.Text;
        ClientSize = new Size(1040, 780); MinimumSize = new Size(960, 640); StartPosition = FormStartPosition.CenterScreen;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        var right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Window };
        right.Controls.Add(host); right.Controls.Add(Header());
        Controls.Add(right); Controls.Add(Sidebar());

        AddPage("Performance", "", Theme.Cyan, PerformancePage());
        AddPage("Lighting", "", Theme.Yellow, LightingPage());
        AddPage("Buttons", "", Theme.Pink, ButtonsPage());
        AddPage("Blender Macros", "", Theme.Orange, MacrosPage());
        AddPage("Profiles & Backup", "", Theme.Mint, ProfilesPage());
        AddPage("App", "", Theme.Violet, AppPage());
        ShowPage(0);
        SetupTray();
        player.Played += m => BeginInvoke((Action)(() => Status($"Played: {m.Name}", Theme.Secondary)));
        BindMacros(null);
        liveTimer.Tick += (s, e) => { if (liveDirty) { liveDirty = false; UpdateLive(); } };
        liveTimer.Start();
        Resize += (s, e) => RequestLive(); VisibleChanged += (s, e) => RequestLive();
    }

    // ------------------------------------------------------------------ frame
    Control Sidebar()
    {
        var side = new SkyPanel { Dock = DockStyle.Left, Width = 244, BackColor = Theme.Sidebar };
        var icon = new DeviceTile { Location = new Point(20, 24) };
        var name = Theme.Lbl("Kone AIMO 16K", Theme.Strong); name.Location = new Point(70, 26);
        deviceStatus.Location = new Point(70, 47);
        side.Controls.AddRange(new Control[] { icon, name, deviceStatus });
        var nav = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Color.Transparent, Location = new Point(16, 96) };
        side.Controls.Add(nav);
        navHost = nav;
        side.Paint += (s, e) => { using (var p = new Pen(Theme.Line)) e.Graphics.DrawLine(p, side.Width - 1, 0, side.Width - 1, side.Height); };
        return side;
    }
    FlowLayoutPanel navHost;

    Control Header()
    {
        var h = new Panel { Dock = DockStyle.Top, Height = 124, BackColor = Theme.Window };
        pageTitle.Location = new Point(28, 14);
        h.Paint += (s, e) => { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; Theme.FillRound(e.Graphics, Theme.Accent, new RectangleF(34, pageTitle.Bottom + 2, 34, 4), 2); };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        save.Margin = new Padding(0); revert.Margin = new Padding(0, 0, 10, 0);
        buttons.Controls.Add(save); buttons.Controls.Add(revert);
        buttons.Size = buttons.PreferredSize; buttons.Location = new Point(h.Width - buttons.Width - 32, 20);
        status.Anchor = AnchorStyles.Top | AnchorStyles.Right; status.AutoSize = true; status.TextAlign = ContentAlignment.MiddleRight;
        h.Resize += (s, e) => { buttons.Left = h.Width - buttons.Width - 32; status.Left = buttons.Left - status.Width - 14; };
        status.SizeChanged += (s, e) => status.Left = buttons.Left - status.Width - 14;
        status.Top = 29;

        profiles.Location = new Point(32, 78);
        useProfile.Location = new Point(profiles.Right + 12, 82);
        activeNote.Location = new Point(profiles.Right + 16, 87);
        profiles.SizeChanged += (s, e) => { useProfile.Left = profiles.Right + 12; activeNote.Left = profiles.Right + 16; };
        h.Controls.AddRange(new Control[] { pageTitle, buttons, status, profiles, useProfile, activeNote });

        save.Click += (s, e) => Save();
        revert.Click += (s, e) => { state = onMouse.Clone(); ShowState(); Status("Changes undone.", Theme.Secondary); };
        profiles.SelectedIndexChanged += (s, e) => { if (!loading) { cur = profiles.SelectedIndex; wantSeparate = false; ShowState(); } };
        useProfile.Click += (s, e) => Edit(() => state.Active = cur);
        return h;
    }

    void AddPage(string title, string glyph, Color tile, Control page)
    {
        var nav = new NavItem(title, glyph, tile) { Margin = new Padding(0, 0, 0, 2) };
        int i = pages.Count;
        nav.Click += (s, e) => ShowPage(i);
        navHost.Controls.Add(nav);
        page.Visible = false; page.Location = new Point(32, 4);
        host.Controls.Add(page);
        pages.Add((nav, page, title));
    }

    public void ShowPage(int i)
    {
        page = i; RequestLive();
        for (int k = 0; k < pages.Count; k++) { pages[k].nav.Selected = k == i; pages[k].page.Visible = k == i; }
        pageTitle.Text = pages[i].title;
        host.AutoScrollPosition = Point.Empty;
    }
    public int PageCount => pages.Count;

    static FlowLayoutPanel Column(params Control[] cs)
    {
        var c = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Color.Transparent, Padding = new Padding(0, 0, 0, 28) };
        c.Controls.AddRange(cs); return c;
    }
    static FlowLayoutPanel Fit(FlowLayoutPanel r) { r.Size = r.PreferredSize; return r; }

    // ------------------------------------------------------------------ pages
    Control PerformancePage()
    {
        dpiGroup.Add("Stage in use", stageInUse);
        for (int i = 0; i < 5; i++)
        {
            int k = i;
            stageOn[i] = new Switch(); stageSlider[i] = new Slider(100, 16000, 50, 230);
            dpiX[i] = new NumberField(100, 16000, 50); dpiY[i] = new NumberField(100, 16000, 50) { Visible = false };
            stageOn[i].CheckedChanged += (s, e) => Edit(() => P.SetStageEnabled(k, stageOn[k].Checked));
            stageSlider[i].ValueChanged += (s, e) => Edit(() => SetX(k, stageSlider[k].Value));
            dpiX[i].ValueChanged += (s, e) => Edit(() => SetX(k, dpiX[k].Value));
            dpiY[i].ValueChanged += (s, e) => Edit(() => P.SetDpi(1, k, dpiY[k].Value));
            stageRow[i] = Fit(Theme.Row(stageSlider[i], dpiX[i], dpiY[i], stageOn[i]));
            dpiGroup.Add($"Stage {i + 1}", stageRow[i]);
        }
        dpiGroup.Add("Separate X and Y", separateXY, "Use a different horizontal and vertical DPI.");
        stageInUse.SelectedIndexChanged += (s, e) => Edit(() => P.ActiveStage = stageInUse.SelectedIndex);
        separateXY.CheckedChanged += (s, e) => Edit(() => { wantSeparate = separateXY.Checked; if (!wantSeparate) for (int i = 0; i < 5; i++) P.SetDpi(1, i, P.Dpi(0, i)); });

        var sensor = new Group("Sensor")
            .Add("Polling rate", polling, "How often the mouse reports its position.")
            .Add("Sensitivity", Fit(Theme.Row(sensValue, sens)), "Fine-tunes speed on top of DPI.")
            .Add("Angle snapping", snap, "Straightens lines you draw. Best left off for curves.");
        polling.SelectedIndexChanged += (s, e) => Edit(() => P.Polling = polling.SelectedIndex);
        sens.ValueChanged += (s, e) => Edit(() => P.Sensitivity = sens.Value);
        snap.CheckedChanged += (s, e) => Edit(() => P.AngleSnapping = snap.Checked);
        return Column(dpiGroup, sensor);
    }

    void SetX(int k, int v) { P.SetDpi(0, k, v); if (!separateXY.Checked) P.SetDpi(1, k, v); }

    Control LightingPage()
    {
        var view = new Group("Lights", "Click a light to change its colour. Hovering one lights only that one on the mouse.", 322).AddFull(mouseView);
        mouseView.LedClicked += i => Pick(Profile.LedNames[i], new[] { i });
        mouseView.LedHovered += i => { hoverLed = i; RequestLive(); };

        effect.Build = items => { foreach (var (code, name) in Effects) Menus.Item(items, name, code, P.Effect == code); };
        effect.Picked += tag => Edit(() =>
        {
            int code = (int)tag; P.Effect = code;
            if (code == 0) { P.Brightness = 0; for (int i = 0; i < 11; i++) P.SetLed(i, Color.Black); }   // the mouse ignores "off" alone
            else if (P.Brightness == 0) P.Brightness = 255;
        });
        livePreview.Checked = true;
        livePreview.CheckedChanged += (s, e) => RequestLive();
        var fx = new Group("Effect", "Animated effects start once you save. The preview shows them still.", 390)
            .Add("Effect", effect)
            .Add("Speed", Fit(Theme.Row(speedValue, speed)))
            .Add("Brightness", Fit(Theme.Row(brightValue, bright)))
            .Add("Preview on the mouse", livePreview);
        speed.ValueChanged += (s, e) => Edit(() => P.EffectSpeed = speed.Value);
        bright.ValueChanged += (s, e) => Edit(() => P.Brightness = bright.Value);

        for (int i = 0; i < 11; i++) { int k = i; wells[i].Click += (s, e) => Pick(Profile.LedNames[k], new[] { k }); }
        var all = new Btn("Choose");
        all.Click += (s, e) => Pick("All lights", Enumerable.Range(0, 11).ToArray());
        colourGroup
            .Add("All lights", all)
            .Add("Wheel", wells[0])
            .Add("Side panels", Fit(Theme.Row(wells[9], wells[10])), "The big lights, left and right")
            .Add("Left strip", Fit(Theme.Row(wells[1], wells[2], wells[3], wells[4])), "Front to back")
            .Add("Right strip", Fit(Theme.Row(wells[5], wells[6], wells[7], wells[8])), "Front to back");

        var rightCol = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(20, 0, 0, 0) };
        rightCol.Controls.Add(fx); rightCol.Controls.Add(colourGroup);
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent };
        row.Controls.Add(view); row.Controls.Add(rightCol);
        return Column(row);
    }

    Control ButtonsPage()
    {
        var g = new Group(null, "Shortcuts are stored in the mouse and work anywhere. Multi-step Blender macros need this app running in the tray.");
        for (int b = 0; b < 12; b++)
        {
            int k = b;
            act[b].Build = items => ActionMenu(items, k + layer);
            act[b].Picked += tag => Pick(k + layer, tag);
            caps[b].Recorded += (mods, key) => Record(k + layer, mods, key);
            g.Add(Actions.Slots[b], Fit(Theme.Row(caps[b], act[b])));
        }
        layerSel.SelectedIndex = 0;
        layerSel.SelectedIndexChanged += (s, e) => { layer = layerSel.SelectedIndex * 12; ShowState(); };
        var top = new Panel { Size = new Size(680, 44), BackColor = Color.Transparent };
        layerSel.Location = new Point((680 - layerSel.Width) / 2, 0); top.Controls.Add(layerSel);

        var blender = new Btn("Apply to This Profile");
        blender.Click += (s, e) => ApplyBlenderLayout();
        var preset = new Group("Preset").Add("Blender layout", blender,
            "Side buttons undo and redo, the wheel tilt steps frames. Holding Easy-Shift: the wheel scrubs, the tilt jumps keyframes, " +
            "the side buttons key and unkey, the top buttons play and reset the pose.");
        return Column(top, g, preset);
    }

    Control MacrosPage()
    {
        var col = Column();
        col.Controls.Add(new Group("Before you start")
            .Add("Assign", null, "Pick a macro from any button's menu on the Buttons page, then Save to Mouse.")
            .Add("Cursor", null, "Blender sends keys to the editor under the cursor. Keep it over the 3D Viewport for poses and views.")
            .Add("Keymap", null, "Uses Blender's default keymap. Play needs Spacebar Action set to Play, and the numpad views need Num Lock on."));
        foreach (var grp in Macros.Groups)
        {
            var g = new Group(grp);
            foreach (var m in Macros.Blender.Where(x => x.Group == grp))
            {
                var kc = new KeyCaps(240); kc.Set(m.Steps, false);
                var sub = string.Join(" ", new[] { m.Info, m.InMouse ? null : "Needs the app running." }.Where(x => x != null));
                g.Add(m.Name, kc, sub.Length > 0 ? sub : null);
            }
            col.Controls.Add(g);
        }
        return col;
    }

    Control ProfilesPage()
    {
        copyTo.Build = items => { for (int i = 0; i < KoneAimo.Profiles; i++) if (i != cur) Menus.Item(items, $"Profile {i + 1}", i, i == copyTarget); };
        copyTo.Picked += tag => { copyTarget = (int)tag; copyTo.Text = $"Profile {copyTarget + 1}"; };
        var copyBtn = new Btn("Copy");
        copyBtn.Click += (s, e) => Edit(() =>
        {
            if (copyTarget == cur) return;
            var c = P.Clone(); c.S[2] = (byte)copyTarget; c.B[2] = (byte)copyTarget; state.P[copyTarget] = c;
            Status($"Copied profile {cur + 1} to profile {copyTarget + 1}.", Theme.Secondary);
        });
        var copy = new Group("Profile").Add("Copy everything to", Fit(Theme.Row(copyTo, copyBtn)), "DPI, lighting and buttons of the profile you're editing.");

        var saveB = new Btn("Save"); var restoreB = new Btn("Restore");
        saveB.Click += (s, e) => SaveBackup(); restoreB.Click += (s, e) => RestoreBackup();
        var backup = new Group("Backup", "Your original settings are in backup_original_2026-10-12.json next to this app.")
            .Add("Save a backup", saveB, "All 5 profiles, to a file.")
            .Add("Restore a backup", restoreB, "Writes all 5 profiles from a file to the mouse.");

        raw.Height = 0;   // hidden rows take no space
        rawGroup.Add("Show raw data", showRaw, "The two blocks of bytes the mouse stores for this profile.").AddFull(raw);
        showRaw.CheckedChanged += (s, e) => { raw.Visible = showRaw.Checked; raw.Height = showRaw.Checked ? 170 : 0; rawGroup.Relayout(); };
        return Column(copy, backup, rawGroup);
    }

    Control AppPage()
    {
        startup.Checked = AppConfig.StartWithWindows; restore.Checked = AppConfig.RestoreAtStartup;
        startup.CheckedChanged += (s, e) => { AppConfig.StartWithWindows = startup.Checked; Tray.Visible = startup.Checked || trayStart; };
        restore.CheckedChanged += (s, e) => AppConfig.RestoreAtStartup = restore.Checked;
        var start = new Group("Startup")
            .Add("Open at login", startup, "Waits in the system tray. Needed for multi-step macros.")
            .Add("Restore lost settings", restore, "At login, checks the mouse against your last save and fixes anything it lost.")
            .Add("Last check", lastCheck);

        var test = new Btn("Run test");
        test.Click += (s, e) => { using (var f = new UnplugTestForm()) f.ShowDialog(this); Connect(); };
        var storage = new Group("Storage").Add("Unplug test", test, "Changes one value in a profile you aren't using, then checks it survives unplugging the mouse.");

        var about = new Group("About", "Built with help from Claude.")
            .Add("Macro keys in use", macroCount, "Multi-step macros use the spare keys F13 to F24.")
            .Add("Device", Theme.Lbl("ROCCAT Kone AIMO 16K (1E7D:2E2C)", Theme.Body, Theme.Secondary))
            .Add("Made by", Theme.Lbl("Miguel Valencia", Theme.Body, Theme.Secondary))
            .Add("Version", Theme.Lbl("3.1", Theme.Body, Theme.Secondary));
        return Column(start, storage, about);
    }

    // ------------------------------------------------------------------ buttons & macros
    void ActionMenu(ToolStripItemCollection items, int slot)
    {
        int a = P.Action(slot); var mac = MacroAt(slot);
        if (!(slot < 12 && Actions.WheelSlots.Contains(slot))) Menus.Item(items, "Disabled", 0, a == 0);
        Menus.Line(items);
        foreach (var c in new[] { 1, 2, 3, 4 }) Menus.Item(items, Actions.Name(c), c, a == c);
        void Sub(string name, params int[] codes) { var s = Menus.Sub(items, name, codes.Contains(a)); foreach (var c in codes) Menus.Item(s.DropDownItems, Actions.Name(c), c, a == c); }
        Sub("Scroll", 13, 14, 9, 10);
        Sub("DPI", 20, 21, 22);
        Sub("Profiles", 16, 17, 18);
        Sub("Media", 35, 33, 34, 36, 37, 38, 39);
        Sub("Browser", 8, 7);
        Menus.Item(items, Actions.Name(65), 65, a == 65);
        Menus.Line(items);
        Menus.Item(items, "Keyboard shortcut", "shortcut", a == Actions.Shortcut && mac == null);
        var bl = Menus.Sub(items, "Blender", mac != null);
        foreach (var grp in Macros.Groups)
        {
            Menus.Header(bl.DropDownItems, grp);
            foreach (var m in Macros.Blender.Where(x => x.Group == grp)) Menus.Item(bl.DropDownItems, m.Name, m, mac == m);
        }
    }

    /// The Blender macro a slot is set to (a matching stored shortcut, or an F13-F24 key bound to a macro).
    Macro MacroAt(int slot)
    {
        if (P.Action(slot) != Actions.Shortcut) return null;
        var (mods, key) = P.Shortcut(slot);
        if (Macros.IsMacroKey(key)) return macroSlots.TryGetValue(key - 104, out var id) ? Macros.ById(id) : null;
        return Macros.ForShortcut(mods, key);
    }

    void Pick(int slot, object tag)
    {
        if (tag is int code) Edit(() => P.SetAction(slot, code));
        else if (tag is string) { Edit(() => P.SetAction(slot, Actions.Shortcut)); BeginInvoke((Action)(() => caps[slot % 12].StartRecording())); }
        else if (tag is Macro m) Edit(() => Assign(P, slot, m));
    }

    void Record(int slot, int mods, Keys key)
    {
        int usage = Keys2Hid.Usage(key);
        if (usage == 0) { Status($"{KeyNames.Name(key)} can't be stored in the mouse.", Theme.Orange); return; }
        Edit(() => P.SetAction(slot, Actions.Shortcut, mods, usage));
    }

    void Assign(Profile p, int slot, Macro m)
    {
        if (m.InMouse) { p.SetAction(slot, Actions.Shortcut, m.Steps[0].mods, Keys2Hid.Usage(m.Steps[0].key)); return; }
        int n = macroSlots.Where(kv => kv.Value == m.Id).Select(kv => kv.Key).DefaultIfEmpty(-1).First();
        if (n < 0) n = Enumerable.Range(0, 12).Where(i => !macroSlots.ContainsKey(i)).DefaultIfEmpty(-1).First();
        if (n < 0) { Status("All 12 macro keys are in use. Clear one first.", Theme.Orange); return; }
        macroSlots[n] = m.Id; AppConfig.MacroSlots = macroSlots;
        p.SetAction(slot, Actions.Shortcut, 0, Macros.UsageOfSlot(n));
    }

    void ApplyBlenderLayout()
    {
        Edit(() =>
        {
            void Set(int slot, string id) => Assign(P, slot, Macros.ById(id));
            Set(7, "redo"); Set(8, "undo"); Set(3, "prev-frame"); Set(4, "next-frame");
            Set(13, "search"); Set(14, "frame"); Set(15, "prev-key"); Set(16, "next-key"); Set(17, "next-frame"); Set(18, "prev-frame");
            Set(19, "key"); Set(20, "unkey"); Set(22, "play"); Set(23, "reset-pose");
        });
        Status($"Blender layout set on profile {cur + 1}. Save to keep it.", Theme.Secondary);
    }

    /// Listens for the F13-F24 keys that the mouse has been told to send; forgets bindings nothing uses any more.
    void BindMacros(MouseState saved)
    {
        if (saved != null)
        {
            var used = new HashSet<int>(saved.P.SelectMany(p => Enumerable.Range(0, 24).Where(s => p.Action(s) == Actions.Shortcut && Macros.IsMacroKey(p.Shortcut(s).key)).Select(s => p.Shortcut(s).key - 104)));
            foreach (var n in macroSlots.Keys.Where(n => !used.Contains(n)).ToList()) macroSlots.Remove(n);
            AppConfig.MacroSlots = macroSlots;
        }
        var failed = player.Bind(macroSlots.Where(kv => Macros.ById(kv.Value) != null).ToDictionary(kv => kv.Key, kv => Macros.ById(kv.Value)));
        if (failed.Count > 0) Status($"Another program is using F{13 + failed[0]}; that macro won't play.", Theme.Orange);
        macroCount.Text = $"{macroSlots.Count} of 12";
    }

    // ------------------------------------------------------------------ state
    Profile P => state.P[cur];

    void Edit(Action change)
    {
        if (loading || state == null) return;
        change(); ShowState(); RequestLive();
    }

    void ShowState()
    {
        lastCheck.Text = AppConfig.LastCheck;
        bool has = state != null;
        foreach (var (_, page, _) in pages.Take(pages.Count - 1)) page.Enabled = has;
        profiles.Enabled = has;
        if (!has) { save.Enabled = revert.Enabled = false; useProfile.Visible = false; return; }
        loading = true;
        try
        {
            var p = P;
            if (copyTarget == cur) copyTarget = cur == 0 ? 1 : 0;
            copyTo.Text = $"Profile {copyTarget + 1}";
            profiles.Items = Enumerable.Range(0, 5).Select(i => $"Profile {i + 1}").ToArray();
            profiles.Marked = state.Active;
            profiles.Dots = state.P.Select(x => (Color?)x.Led(0)).ToArray();
            profiles.SelectedIndex = cur;
            useProfile.Visible = cur != state.Active;
            activeNote.Visible = cur == state.Active;
            activeNote.Text = "In use on the mouse";

            stageInUse.SelectedIndex = p.ActiveStage;
            bool separate = wantSeparate || Enumerable.Range(0, 5).Any(i => p.Dpi(0, i) != p.Dpi(1, i));
            separateXY.Checked = separate;
            for (int i = 0; i < 5; i++)
            {
                stageOn[i].Checked = p.StageEnabled(i);
                stageSlider[i].Value = p.Dpi(0, i);
                dpiX[i].Value = p.Dpi(0, i); dpiY[i].Value = p.Dpi(1, i);
                if (dpiY[i].Visible != separate) { dpiY[i].Visible = separate; stageSlider[i].Width = separate ? 150 : 230; Fit(stageRow[i]); }
            }
            dpiGroup.Relayout();
            polling.SelectedIndex = Math.Min(p.Polling, 3);
            sens.Value = Math.Max(-5, Math.Min(5, p.Sensitivity)); sensValue.Text = p.Sensitivity > 0 ? $"+{p.Sensitivity}" : $"{p.Sensitivity}";
            snap.Checked = p.AngleSnapping;

            effect.Text = Effects.Where(x => x.code == p.Effect).Select(x => x.name).DefaultIfEmpty($"Other ({p.Effect})").First();
            speed.Value = Math.Max(1, Math.Min(11, p.EffectSpeed)); speedValue.Text = $"{p.EffectSpeed}";
            bright.Value = p.Brightness; brightValue.Text = $"{p.Brightness * 100 / 255}%";
            for (int i = 0; i < 11; i++) { mouseView.Leds[i] = p.Led(i); wells[i].Color = p.Led(i); }
            mouseView.Invalidate();
            colourGroup.SetFooter(p.Effect == 0 ? "Lighting is off. Pick an effect to turn it back on."
                : p.Theme >= 0x80 ? $"ROCCAT theme {p.Theme - 0x80 + 1}. Change any colour to make it custom." : "Custom colours.");

            for (int b = 0; b < 12; b++)
            {
                int slot = b + layer, a = p.Action(slot); var mac = MacroAt(slot);
                var (mods, key) = p.Shortcut(slot);
                act[b].Text = mac != null ? mac.Name : a == Actions.Shortcut ? "Keyboard shortcut" : Actions.Name(a);
                if (mac != null) caps[b].Set(mac.Steps, mac.InMouse);
                else if (a == Actions.Shortcut) caps[b].Set(new[] { (mods, Keys2Hid.KeyOf(key)) }, true);
                else caps[b].Set(new (int, Keys)[0], false);
            }
            raw.Text = $"Settings (report 6):\r\n{Hex(p.S)}\r\n\r\nButtons (report 7):\r\n{Hex(p.B)}";
        }
        finally { loading = false; }

        bool dirty = onMouse != null && !Fixed(state).SameAs(onMouse);
        save.Enabled = revert.Enabled = dirty;
        if (dirty) Status("Unsaved changes", Theme.Orange);
        else if (status.ForeColor == Theme.Orange) Status("", Theme.Secondary);
    }

    public void Connect()
    {
        try
        {
            onMouse = KoneAimo.Use(MouseState.Read); state = onMouse.Clone();
            if (AppConfig.LastSaved == null) AppConfig.LastSaved = onMouse;
            cur = onMouse.Active;
            deviceStatus.Text = "Connected"; deviceStatus.ForeColor = Theme.Accent;
            ShowState();
            Status("", Theme.Secondary);
        }
        catch (Exception ex)
        {
            deviceStatus.Text = "Not connected"; deviceStatus.ForeColor = Theme.Red;
            onMouse = state = null; ShowState();
            Status(ex.Message, Theme.Red);
        }
    }

    void Save()
    {
        if (state == null) return;
        var target = Fixed(state);
        for (int i = 0; i < KoneAimo.Profiles; i++)
        {
            var p = target.P[i];
            if (!Enumerable.Range(0, 5).Any(p.StageEnabled)) { Alert($"Profile {i + 1}: switch on at least one DPI stage."); return; }
            if (!p.StageEnabled(p.ActiveStage)) { Alert($"Profile {i + 1}: the stage in use is switched off."); return; }
            if (!Enumerable.Range(0, 12).Any(s => p.Action(s) == 1) &&
                MessageBox.Show(this, $"Profile {i + 1} has no button set to Left click. Save anyway?", "Kone AIMO", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        }
        try
        {
            UseWaitCursor = true; Status("Saving", Theme.Secondary); Application.DoEvents();
            onMouse = KoneAimo.Use(m => MouseState.Apply(m, target, onMouse));
            inDirect = false; RequestLive();   // saving hands the lights back to the stored settings
            AppConfig.LastSaved = onMouse;
            BindMacros(onMouse);
            state = onMouse.Clone(); ShowState();
            Status($"Saved to the mouse at {DateTime.Now:HH:mm}", Theme.Accent);
        }
        catch (Exception ex) { Alert(ex.Message); }
        finally { UseWaitCursor = false; }
    }

    void SaveBackup()
    {
        using (var d = new SaveFileDialog { Filter = "Kone AIMO backup (*.json)|*.json", FileName = $"kone_aimo_backup_{DateTime.Now:yyyy-MM-dd_HHmm}.json" })
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            try { File.WriteAllText(d.FileName, KoneAimo.Use(MouseState.Read).ToJson()); Status("Backup saved.", Theme.Accent); }
            catch (Exception ex) { Alert(ex.Message); }
        }
    }

    void RestoreBackup()
    {
        using (var d = new OpenFileDialog { Filter = "Kone AIMO backup (*.json)|*.json", InitialDirectory = Path.GetDirectoryName(Application.ExecutablePath) })
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var loaded = MouseState.FromJson(File.ReadAllText(d.FileName));
                if (MessageBox.Show(this, "Write all 5 profiles from this backup to the mouse?", "Kone AIMO", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                state = loaded; Save();
            }
            catch (Exception ex) { Alert(ex.Message); }
        }
    }

    // ------------------------------------------------------------------ tray + startup
    void SetupTray()
    {
        Tray.Icon = Icon ?? SystemIcons.Application; Tray.Text = "Kone AIMO";
        Tray.Visible = trayStart || AppConfig.StartWithWindows;
        var menu = Menus.Create();
        menu.Opening += (s, e) =>
        {
            menu.Items.Clear();
            Menus.Item(menu.Items, "Open Kone AIMO", "open").Font = Theme.Strong;
            Menus.Line(menu.Items);
            int active = -1; try { active = KoneAimo.Use(m => m.ActiveProfile); } catch { }
            Menus.Header(menu.Items, "Profile in use");
            for (int i = 0; i < KoneAimo.Profiles; i++) Menus.Item(menu.Items, $"Profile {i + 1}", i, i == active);
            Menus.Line(menu.Items);
            Menus.Item(menu.Items, "Quit", "quit");
            Menus.Wire(menu.Items, tag =>
            {
                if (tag is int k) SwitchProfile(k);
                else if ((string)tag == "open") OpenWindow();
                else { quitting = true; Tray.Visible = false; Close(); Application.Exit(); }
            });
            e.Cancel = false;
        };
        menu.Items.Add("Open");   // the menu only opens if it has something; rebuilt on Opening
        Tray.ContextMenuStrip = menu;
        Tray.DoubleClick += (s, e) => OpenWindow();
    }

    void SwitchProfile(int k)
    {
        try
        {
            KoneAimo.Use(m => { m.ActiveProfile = k; return 0; });
            if (onMouse != null) { onMouse.Active = k; if (state != null) state.Active = k; ShowState(); }
            Tray.ShowBalloonTip(1500, "Kone AIMO", $"Now using profile {k + 1}.", ToolTipIcon.None);
        }
        catch (Exception ex) { Tray.ShowBalloonTip(3000, "Kone AIMO", ex.Message, ToolTipIcon.Warning); }
    }

    public void OpenWindow()
    {
        allowShow = true;
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void SetVisibleCore(bool value)
    {
        if (!allowShow) { value = false; if (!IsHandleCreated) CreateHandle(); }
        base.SetVisibleCore(value);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkWindow(Handle);
        if (trayStart) Task.Run(() => StartupCheck());
    }

    protected override void OnShown(EventArgs e) { base.OnShown(e); Connect(); }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!quitting && Tray.Visible && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true; Hide(); UpdateLive();
            if (!toldAboutTray) { Tray.ShowBalloonTip(2500, "Kone AIMO", "Still running in the tray, so your macros keep working.", ToolTipIcon.None); toldAboutTray = true; }
            return;
        }
        Tray.Visible = false; player.Dispose(); liveTimer.Stop();
        if (inDirect) try { KoneAimo.Use(m => { m.DirectMode(false); return 0; }); } catch { }
        base.OnFormClosing(e);
    }

    /// At Windows startup: wait for the mouse, compare it with what was last saved, put things back if they were lost.
    void StartupCheck()
    {
        string msg;
        try
        {
            var until = DateTime.Now.AddMinutes(3);
            while (!KoneAimo.IsPresent && DateTime.Now < until) System.Threading.Thread.Sleep(2000);
            System.Threading.Thread.Sleep(2000);
            var now = KoneAimo.Use(MouseState.Read);
            var saved = AppConfig.LastSaved;
            int lost = saved == null ? 0 : saved.ProfilesDifferingFrom(now);
            if (saved == null) { AppConfig.LastSaved = now; msg = "remembered the mouse's current settings."; }
            else if (lost == 0) msg = "nothing was lost.";
            else if (AppConfig.RestoreAtStartup)
            {
                KoneAimo.Use(m => MouseState.Apply(m, saved.WithLiveFrom(now), now));
                msg = $"put back {lost} profile(s) the mouse had lost.";
                BeginInvoke((Action)(() => Tray.ShowBalloonTip(5000, "Kone AIMO", "Your mouse had lost some settings. They have been put back.", ToolTipIcon.Info)));
            }
            else
            {
                msg = $"{lost} profile(s) differ from what you last saved (restoring is off).";
                BeginInvoke((Action)(() => Tray.ShowBalloonTip(5000, "Kone AIMO", "Your mouse's settings differ from what you last saved.", ToolTipIcon.Warning)));
            }
        }
        catch (Exception ex) { msg = "couldn't check: " + ex.Message; }
        AppConfig.LastCheck = $"{DateTime.Now:MMM d, HH:mm}: {msg}";
        BeginInvoke((Action)(() => { var t = "Kone AIMO: " + msg; Tray.Text = t.Length > 63 ? t.Substring(0, 63) : t; ShowState(); }));
    }

    // ------------------------------------------------------------------ helpers
    void Pick(string what, int[] leds)
    {
        pickLeds = leds;
        var c = ColorPicker.Pick(this, what, P.Led(leds[0]), col => { pickColor = col; RequestLive(); });
        pickLeds = null;
        if (c.HasValue) Edit(() => { foreach (var l in leds) P.SetLed(l, c.Value); });
        RequestLive();
    }

    // ------------------------------------------------------------------ live lighting on the mouse
    void RequestLive() => liveDirty = true;

    /// While the Lighting page is open the mouse shows the colours being edited (nothing is stored);
    /// anywhere else, or when the window hides, the mouse gets its saved lighting back.
    void UpdateLive()
    {
        bool want = LiveAllowed && livePreview.Checked && page == 1 && state != null && Visible && WindowState != FormWindowState.Minimized;
        try
        {
            if (!want) { if (inDirect) KoneAimo.Use(m => { m.DirectMode(false); return 0; }); inDirect = false; return; }
            var p = P; var leds = new Color[11];
            for (int i = 0; i < 11; i++)
            {
                var c = pickLeds != null && pickLeds.Contains(i) ? pickColor : p.Led(i);
                int k = pickLeds != null ? 255 : p.Effect == 0 ? 0 : p.Brightness;
                leds[i] = Color.FromArgb(c.R * k / 255, c.G * k / 255, c.B * k / 255);
            }
            if (hoverLed >= 0 && pickLeds == null)
                for (int i = 0; i < 11; i++) leds[i] = i == hoverLed ? (Theme.IsOff(leds[i]) ? Color.White : leds[i]) : Color.Black;
            KoneAimo.Use(m => { if (!inDirect) m.DirectMode(true); m.DirectColors(leds); return 0; });
            inDirect = true;
        }
        catch { inDirect = false; }
    }

    static MouseState Fixed(MouseState s) { var c = s.Clone(); foreach (var p in c.P) p.FixChecksum(); return c; }
    void Status(string msg, Color c) { status.Text = msg; status.ForeColor = c; }
    void Alert(string msg) { Status(msg, Theme.Red); MessageBox.Show(this, msg, "Kone AIMO", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    static string Hex(byte[] b) => string.Join(" ", b.Select(x => x.ToString("X2")));

    /// Mouse icon at the top of the sidebar, drawn like the logo tile on the portfolio.
    class DeviceTile : Control
    {
        public DeviceTile() { Size = new Size(38, 38); SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true); BackColor = Color.Transparent; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, 37, 37);
            Theme.FillRound(e.Graphics, Theme.Fill, r, 11);
            Theme.StrokeRound(e.Graphics, Theme.LineStrong, r, 11);
            Theme.Glyph(e.Graphics, "\uE962", 14, Theme.Accent, r);
        }
    }
}

/// Walks through the unplug test: marks a value, waits for unplug + replug, checks it survived, puts it back.
class UnplugTestForm : Form
{
    readonly Label title = Theme.Lbl("Is the mouse storing your settings?", Theme.Big);
    readonly Label say = Theme.Lbl("", Theme.Body, Theme.Text, 400);
    readonly Btn close = new Btn("Cancel");
    PersistTest.Marker marker;
    volatile bool closed, done;

    public UnplugTestForm()
    {
        Text = "Unplug Test"; ClientSize = new Size(460, 240); BackColor = Theme.Group; ForeColor = Theme.Text; Font = Theme.Body;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent;
        title.Location = new Point(28, 24); say.Location = new Point(28, 64);
        close.Location = new Point(ClientSize.Width - close.Width - 24, ClientSize.Height - close.Height - 20); close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        Controls.AddRange(new Control[] { title, say, close });
        close.Click += (s, e) => Close();
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkWindow(Handle); }

    void Say(string t, Color? c = null)
    {
        if (closed || IsDisposed) return;
        BeginInvoke((Action)(() => { if (!IsDisposed) { say.Text = t; say.ForeColor = c ?? Theme.Text; } }));
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            Say("Changing one value in a profile you aren't using.");
            marker = await Task.Run(() => PersistTest.Mark());
            string head = $"Test value: DPI stage 1 of profile {marker.Profile + 1} is now {marker.Dpi}.\n\n";
            bool replugged = await Task.Run(() => PersistTest.WaitForReplug(TimeSpan.FromMinutes(5), t => Say(head + t), () => closed));
            if (closed) return;
            string result; Color colour = Theme.Text;
            if (!replugged) result = "Timed out waiting for the unplug, so nothing was tested.";
            else if (await Task.Run(() => PersistTest.StillThere(marker))) { result = "Passed. The change survived the power loss, so the mouse keeps your settings."; colour = Theme.Accent; }
            else { result = "Failed. The mouse forgot the change. Turn on Restore lost settings on the App page."; colour = Theme.Red; }
            Say(result + "\n\nPutting the test value back.", colour);
            await Task.Run(() => PersistTest.Restore(marker));
            done = true;
            Say(result + "\n\nTest value removed. Your settings are as they were.", colour);
            close.Text = "Done"; close.Invalidate();
        }
        catch (Exception ex) { Say("Something went wrong: " + ex.Message, Theme.Red); if (!IsDisposed) { close.Text = "Close"; close.Invalidate(); } }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        closed = true;
        if (marker != null && !done && KoneAimo.IsPresent) try { PersistTest.Restore(marker); } catch { }
        base.OnFormClosing(e);
    }
}
