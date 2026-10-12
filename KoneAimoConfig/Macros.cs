// Blender macros. A one-step macro is a plain shortcut stored in the mouse (works without this app).
// A multi-step macro: the mouse sends an unused key (F13-F24), this app catches it and types the steps.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

static class KeyNames
{
    public const int Ctrl = 1, Shift = 2, Alt = 4, Win = 8;

    public static string Name(Keys k)
    {
        switch (k)
        {
            case Keys.Decimal: return "Num .";
            case Keys.Divide: return "Num /";
            case Keys.Multiply: return "Num *";
            case Keys.Subtract: return "Num -";
            case Keys.Add: return "Num +";
            case Keys.Left: return "←";
            case Keys.Right: return "→";
            case Keys.Up: return "↑";
            case Keys.Down: return "↓";
            case Keys.Next: return "Page Down";
            case Keys.PageUp: return "Page Up";
            case Keys.Back: return "Backspace";
            case Keys.Escape: return "Esc";
            case Keys.Return: return "Enter";
            case Keys.Oemtilde: return "`";
            case Keys.OemMinus: return "-";
            case Keys.Oemplus: return "=";
            case Keys.OemOpenBrackets: return "[";
            case Keys.Oem6: return "]";
            case Keys.Oem5: return "\\";
            case Keys.Oem1: return ";";
            case Keys.Oem7: return "'";
            case Keys.Oemcomma: return ",";
            case Keys.OemPeriod: return ".";
            case Keys.OemQuestion: return "/";
        }
        if (k >= Keys.D0 && k <= Keys.D9) return ((int)(k - Keys.D0)).ToString();
        if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return "Num " + (int)(k - Keys.NumPad0);
        return k.ToString();
    }

    public static IEnumerable<string> Caps(int mods, Keys k)
    {
        mods = (mods | mods >> 4) & 0xF;   // right-hand modifiers count as the left ones
        if ((mods & Ctrl) != 0) yield return "Ctrl";
        if ((mods & Shift) != 0) yield return "Shift";
        if ((mods & Alt) != 0) yield return "Alt";
        if ((mods & Win) != 0) yield return "Win";
        if (k != Keys.None) yield return Name(k);
    }

    public static string Label(int mods, Keys k) => string.Join("+", Caps(mods, k));

    [DllImport("user32.dll")] static extern short GetKeyState(int key);
    public static bool WinDown() => (GetKeyState(0x5B) & 0x8000) != 0 || (GetKeyState(0x5C) & 0x8000) != 0;
}

class Macro
{
    public string Id, Name, Group, Info;
    public (int mods, Keys key)[] Steps;
    public bool InMouse => Steps.Length == 1;
}

static class Macros
{
    static Macro M(string group, string id, string name, string info, params (int, Keys)[] steps) =>
        new Macro { Group = group, Id = id, Name = name, Info = info, Steps = steps };
    const int C = KeyNames.Ctrl, S = KeyNames.Shift, A = KeyNames.Alt;

    /// Blender's default keymap (4.x / 5.x).
    public static readonly Macro[] Blender =
    {
        M("Animation", "key", "Insert keyframe", "Keys the selected objects or bones.", (0, Keys.I)),
        M("Animation", "unkey", "Delete keyframe", "Removes the keyframe on the current frame.", (A, Keys.I)),
        M("Animation", "play", "Play / pause", "Plays the animation.", (0, Keys.Space)),
        M("Animation", "next-frame", "Next frame", null, (0, Keys.Right)),
        M("Animation", "prev-frame", "Previous frame", null, (0, Keys.Left)),
        M("Animation", "next-key", "Next keyframe", null, (0, Keys.Up)),
        M("Animation", "prev-key", "Previous keyframe", null, (0, Keys.Down)),
        M("Animation", "start", "Jump to start", null, (S, Keys.Left)),
        M("Animation", "end", "Jump to end", null, (S, Keys.Right)),
        M("Animation", "reset-pose", "Reset pose", "Clears location, rotation and scale of the selected bones.", (A, Keys.G), (A, Keys.R), (A, Keys.S)),
        M("Animation", "reset-key", "Reset pose and key it", "Same, then keys it.", (A, Keys.G), (A, Keys.R), (A, Keys.S), (0, Keys.I)),
        M("Animation", "mirror-pose", "Mirror pose to the other side", "Copies the pose and pastes it flipped. Needs .L / .R bone names.", (C, Keys.C), (C | S, Keys.V)),
        M("Animation", "key-all", "Select all and key", "Keys every bone on the current frame.", (0, Keys.A), (0, Keys.I)),

        M("Editing", "undo", "Undo", null, (C, Keys.Z)),
        M("Editing", "redo", "Redo", null, (C | S, Keys.Z)),
        M("Editing", "edit-mode", "Toggle edit mode", null, (0, Keys.Tab)),
        M("Editing", "mode-pie", "Mode pie menu", null, (C, Keys.Tab)),
        M("Editing", "duplicate", "Duplicate", null, (S, Keys.D)),
        M("Editing", "add", "Add menu", null, (S, Keys.A)),
        M("Editing", "search", "Search menu", null, (0, Keys.F3)),
        M("Editing", "delete", "Delete", null, (0, Keys.X)),

        M("View", "frame", "Frame selected", "Num Lock on.", (0, Keys.Decimal)),
        M("View", "camera", "Camera view", "Num Lock on.", (0, Keys.NumPad0)),
        M("View", "front", "Front view", "Num Lock on.", (0, Keys.NumPad1)),
        M("View", "right", "Right view", "Num Lock on.", (0, Keys.NumPad3)),
        M("View", "top", "Top view", "Num Lock on.", (0, Keys.NumPad7)),
        M("View", "local", "Isolate selection", "Local view on and off.", (0, Keys.Divide)),
        M("View", "xray", "Toggle X-ray", null, (A, Keys.Z)),
        M("View", "maximize", "Maximize editor", null, (C, Keys.Space)),

        M("File", "save", "Save", null, (C, Keys.S)),
        M("File", "render", "Render image", null, (0, Keys.F12)),
        M("File", "render-anim", "Render animation", null, (C, Keys.F12)),
    };
    public static readonly string[] Groups = { "Animation", "Editing", "View", "File" };

    public static Macro ById(string id) => Blender.FirstOrDefault(m => m.Id == id);

    /// A one-step macro that matches a shortcut stored on the mouse.
    public static Macro ForShortcut(int mods, int usage)
    {
        mods = (mods | mods >> 4) & 0xF;
        return Blender.FirstOrDefault(m => m.InMouse && m.Steps[0].mods == mods && Keys2Hid.Usage(m.Steps[0].key) == usage);
    }

    // F13..F24 = HID usages 104..115
    public static bool IsMacroKey(int usage) => usage >= 104 && usage <= 115;
    public static int UsageOfSlot(int n) => 104 + n;            // n = 0..11
}

/// Catches F13-F24 anywhere in Windows and types the macro bound to that key.
class MacroPlayer : NativeWindow, IDisposable
{
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);

    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit, Size = 40)] struct INPUT { [FieldOffset(0)] public int type; [FieldOffset(8)] public KEYBDINPUT ki; }

    readonly Dictionary<int, Macro> bound = new Dictionary<int, Macro>();   // slot 0..11 (F13..F24) -> macro
    public event Action<Macro> Played;

    public MacroPlayer() { CreateHandle(new CreateParams()); }

    /// Binds slot n (F13+n) to a macro. Returns the slots that could not be bound (another program owns the key).
    public List<int> Bind(Dictionary<int, Macro> map)
    {
        foreach (var n in bound.Keys) UnregisterHotKey(Handle, 100 + n);
        bound.Clear();
        var failed = new List<int>();
        foreach (var kv in map)
        {
            if (RegisterHotKey(Handle, 100 + kv.Key, 0x4000 /*no repeat*/, (uint)(Keys.F13 + kv.Key))) bound[kv.Key] = kv.Value;
            else failed.Add(kv.Key);
        }
        return failed;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 && bound.TryGetValue(m.WParam.ToInt32() - 100, out var macro))   // WM_HOTKEY
            ThreadPool.QueueUserWorkItem(_ => { Play(macro); Played?.Invoke(macro); });
        base.WndProc(ref m);
    }

    public static void Play(Macro macro)
    {
        foreach (var (mods, key) in macro.Steps) { Press(mods, key); Thread.Sleep(45); }
    }

    static readonly Keys[] Extended = { Keys.Left, Keys.Right, Keys.Up, Keys.Down, Keys.Insert, Keys.Delete, Keys.Home, Keys.End, Keys.PageUp, Keys.Next, Keys.Divide, Keys.RControlKey, Keys.RMenu };
    static INPUT Key(Keys k, bool up)
    {
        uint flags = (up ? 2u : 0u) | (Extended.Contains(k) ? 1u : 0u);
        return new INPUT { type = 1, ki = new KEYBDINPUT { wVk = (ushort)k, wScan = (ushort)MapVirtualKey((uint)k, 0), dwFlags = flags } };
    }
    static void Press(int mods, Keys key)
    {
        var mk = new List<Keys>();
        if ((mods & KeyNames.Ctrl) != 0) mk.Add(Keys.ControlKey);
        if ((mods & KeyNames.Shift) != 0) mk.Add(Keys.ShiftKey);
        if ((mods & KeyNames.Alt) != 0) mk.Add(Keys.Menu);
        if ((mods & KeyNames.Win) != 0) mk.Add(Keys.LWin);
        var seq = mk.Select(k => Key(k, false)).Concat(new[] { Key(key, false), Key(key, true) }).Concat(mk.AsEnumerable().Reverse().Select(k => Key(k, true))).ToArray();
        SendInput((uint)seq.Length, seq, Marshal.SizeOf<INPUT>());
    }

    public void Dispose() { Bind(new Dictionary<int, Macro>()); DestroyHandle(); }
}
