// ROCCAT Kone AIMO 16K (Remastered, USB 1E7D:2E2C): talks to the mouse through HID feature reports.
// Protocol: libratbag PR #1883, phiph-s/roccat-kone-aimo-linux and OpenRGB, checked against a real mouse.
//   0x04 (3 B)   select profile + data type (0x80 settings, 0x90 buttons) / ready status (3 = busy)
//   0x05 (3 B)   active profile
//   0x06 (126 B) settings: DPI, polling, lighting, 16-bit byte-sum checksum at the end
//   0x07 (75 B)  24 button slots {action, modifiers, key}
//   0x0E (6 B)   "use what I just wrote": 0e <report> 01 00 00 ff
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32.SafeHandles;

static class Hid
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceInterfaceData { public int Size; public Guid ClassGuid; public int Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)]
    public struct Attributes { public int Size; public ushort VendorId, ProductId, Version; }
    [StructLayout(LayoutKind.Sequential)]
    public struct Caps
    {
        public ushort Usage, UsagePage, InputLength, OutputLength, FeatureLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 27)] public ushort[] Rest;
    }

    [DllImport("hid.dll")] public static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] public static extern bool HidD_GetAttributes(SafeFileHandle h, ref Attributes a);
    [DllImport("hid.dll")] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
    [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr data, out Caps caps);
    [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buf, int len);
    [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_SetFeature(SafeFileHandle h, byte[] buf, int len);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr parent, int flags);
    [DllImport("setupapi.dll")]
    public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, int index, ref DeviceInterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref DeviceInterfaceData data, IntPtr detail, int size, out int needed, IntPtr info);
    [DllImport("setupapi.dll")] public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr sec, uint creation, uint flags, IntPtr template);

    public static IEnumerable<string> DevicePaths()
    {
        HidD_GetHidGuid(out var guid);
        var set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12); // present | interface
        try
        {
            for (int i = 0; ; i++)
            {
                var d = new DeviceInterfaceData { Size = Marshal.SizeOf<DeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref d)) yield break;
                SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out int needed, IntPtr.Zero);
                var buf = Marshal.AllocHGlobal(needed);
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(set, ref d, buf, needed, out _, IntPtr.Zero))
                        yield return Marshal.PtrToStringUni(buf + 4);
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }
}

class KoneAimo : IDisposable
{
    public const int Profiles = 5, SettingsLen = 126, ButtonsLen = 75;
    const byte Status = 0x04, ProfileSel = 0x05, SettingsId = 0x06, ButtonsId = 0x07, Activate = 0x0E;
    readonly SafeFileHandle h;
    readonly int featureLen;

    KoneAimo(SafeFileHandle handle, int len) { h = handle; featureLen = len; }

    /// The configuration interface: VID 1E7D, PID 2E2C, usage page 0x0B. Null when the mouse is not plugged in.
    static (string path, int featureLen)? Find()
    {
        foreach (var path in Hid.DevicePaths().Where(p => p.ToLowerInvariant().Contains("vid_1e7d&pid_2e2c")))
        {
            using (var q = Hid.CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
            {
                if (q.IsInvalid || !Hid.HidD_GetPreparsedData(q, out var pp)) continue;
                Hid.HidP_GetCaps(pp, out var caps); Hid.HidD_FreePreparsedData(pp);
                if (caps.UsagePage == 0x0B && caps.FeatureLength >= SettingsLen) return (path, caps.FeatureLength);
            }
        }
        return null;
    }

    public static bool IsPresent => Find() != null;

    public static KoneAimo Open()
    {
        var found = Find() ?? throw new IOException("ROCCAT Kone AIMO 16K not found. Is it plugged in?");
        var h = Hid.CreateFile(found.path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (h.IsInvalid) throw new IOException("The mouse was found but could not be opened (is the old ROCCAT software running?).");
        return new KoneAimo(h, found.featureLen);
    }

    /// Opens the mouse for one job and lets go of it again, so unplugging/replugging never leaves a dead handle.
    public static T Use<T>(Func<KoneAimo, T> job) { using (var m = Open()) return job(m); }

    public void Dispose() => h.Dispose();

    byte[] Get(byte id, int n)
    {
        var b = new byte[featureLen]; b[0] = id;
        if (!Hid.HidD_GetFeature(h, b, b.Length)) throw new IOException($"Reading report 0x{id:X2} failed", new Win32Exception());
        return b.Take(n).ToArray();
    }

    void Set(params byte[] data)
    {
        var b = new byte[featureLen]; data.CopyTo(b, 0);
        if (!Hid.HidD_SetFeature(h, b, b.Length)) throw new IOException($"Writing report 0x{data[0]:X2} failed", new Win32Exception());
        WaitReady();
    }

    void WaitReady()
    {
        for (int i = 0; i < 40; i++)
        {
            Thread.Sleep(i == 0 ? 10 : 50);
            if (Get(Status, 3)[1] != 3) return;
        }
        throw new IOException("The mouse stayed busy for too long.");
    }

    public byte[] ReadSettings(int profile) { Set(Status, (byte)profile, 0x80); return Get(SettingsId, SettingsLen); }
    public byte[] ReadButtons(int profile) { Set(Status, (byte)profile, 0x90); return Get(ButtonsId, ButtonsLen); }

    public void WriteSettings(int profile, byte[] settings)
    {
        Set(Status, (byte)profile, 0x80);
        Set(settings);
        Set(Activate, SettingsId, 0x01, 0x00, 0x00, 0xFF);
    }

    public void WriteButtons(int profile, byte[] buttons)
    {
        Set(Status, (byte)profile, 0x90);
        Set(buttons);
        Set(Activate, ButtonsId, 0x01, 0x00, 0x00, 0xFF);
    }

    // Live lighting (not stored): 0x0E 06 01 01 00 FF hands the 11 LEDs to the host, 0x0D sets them,
    // and 0x0E 06 01 00 00 FF gives the stored lighting back. Same commands as OpenRGB.
    public void DirectMode(bool on)
    {
        Get(Status, 3);
        Set(Activate, SettingsId, 0x01, (byte)(on ? 1 : 0), 0x00, 0xFF);
    }

    public void DirectColors(System.Drawing.Color[] leds)
    {
        var b = new byte[46]; b[0] = 0x0D; b[1] = 0x2E;
        for (int i = 0; i < 11; i++) { b[2 + 4 * i] = leds[i].R; b[3 + 4 * i] = leds[i].G; b[4 + 4 * i] = leds[i].B; }
        Set(b);
    }

    public int ActiveProfile
    {
        get => Get(ProfileSel, 3)[2];
        set => Set(ProfileSel, 0x03, (byte)value);
    }
}

/// One on-mouse profile as raw reports; the properties are views on the bytes, so unknown bytes survive untouched.
class Profile
{
    public byte[] S, B;   // settings (report 6), buttons (report 7)
    public Profile(byte[] s, byte[] b) { S = s; B = b; }
    public Profile Clone() => new Profile((byte[])S.Clone(), (byte[])B.Clone());

    public static ushort Checksum(byte[] s) { int sum = 0; for (int i = 0; i < s.Length - 2; i++) sum += s[i]; return (ushort)sum; }
    public bool ChecksumOk => Checksum(S) == (S[S.Length - 2] | S[S.Length - 1] << 8);
    public void FixChecksum() { var c = Checksum(S); S[S.Length - 2] = (byte)c; S[S.Length - 1] = (byte)(c >> 8); }

    public int Sensitivity { get => S[3] - 6; set => S[3] = (byte)(value + 6); }              // -5..+5
    public bool StageEnabled(int i) => (S[4] >> i & 1) != 0;
    public void SetStageEnabled(int i, bool on) => S[4] = (byte)(on ? S[4] | 1 << i : S[4] & ~(1 << i));
    public int ActiveStage { get => S[5]; set => S[5] = (byte)value; }
    public int Dpi(int axis, int i) => (S[6 + 10 * axis + 2 * i] | S[7 + 10 * axis + 2 * i] << 8) * 50;
    public void SetDpi(int axis, int i, int dpi) { int v = dpi / 50; S[6 + 10 * axis + 2 * i] = (byte)v; S[7 + 10 * axis + 2 * i] = (byte)(v >> 8); }
    public static readonly int[] PollingHz = { 125, 250, 500, 1000 };
    public int Polling { get => S[26]; set => S[26] = (byte)value; }
    public bool AngleSnapping { get => S[27] != 0; set => S[27] = (byte)(value ? 1 : 0); }
    public int Effect { get => S[33]; set => S[33] = (byte)value; }
    public int EffectSpeed { get => S[34]; set => S[34] = (byte)value; }
    public int Brightness { get => S[35]; set => S[35] = (byte)value; }
    public int Theme => S[116];   // 0x80-0x89 a ROCCAT colour theme, 0x00-0x09 custom colours

    public static readonly string[] LedNames = { "Wheel", "Left 1", "Left 2", "Left 3", "Left 4", "Right 1", "Right 2", "Right 3", "Right 4", "Left side panel", "Right side panel" };
    static int LedOffset(int led) => led == 0 ? 61 : led <= 4 ? 66 + 5 * (led - 1) : led <= 8 ? 86 + 5 * (led - 5) : 106 + 5 * (led - 9);
    public System.Drawing.Color Led(int led) { int o = LedOffset(led); return System.Drawing.Color.FromArgb(S[o + 1], S[o + 2], S[o + 3]); }
    public void SetLed(int led, System.Drawing.Color c)
    {
        int o = LedOffset(led);
        S[o] = 0xFF; S[o + 1] = c.R; S[o + 2] = c.G; S[o + 3] = c.B; S[o + 4] = 0;
        S[116] &= 0x7F;   // own colours instead of the theme's
    }

    // buttons: slot 0-11 normal, 12-23 the same buttons while Easy-Shift is held
    public int Action(int slot) => B[3 + 3 * slot];
    public (int mods, int key) Shortcut(int slot) => (B[4 + 3 * slot], B[5 + 3 * slot]);
    public void SetAction(int slot, int action, int mods = 0, int key = 0)
    {
        B[3 + 3 * slot] = (byte)action;
        B[4 + 3 * slot] = (byte)(action == Actions.Shortcut ? mods : 0);
        B[5 + 3 * slot] = (byte)(action == Actions.Shortcut ? key : 0);
    }
}

static class Actions
{
    public const int Shortcut = 5;
    public static readonly (int code, string name)[] List =
    {
        (0, "Disabled"), (1, "Left click"), (2, "Right click"), (3, "Middle click"), (4, "Double click"),
        (Shortcut, "Keyboard shortcut"), (7, "Browser forward"), (8, "Browser back"),
        (13, "Scroll up"), (14, "Scroll down"), (9, "Scroll left (tilt)"), (10, "Scroll right (tilt)"),
        (20, "DPI cycle"), (21, "DPI up"), (22, "DPI down"),
        (16, "Profile cycle"), (17, "Profile up"), (18, "Profile down"),
        (35, "Play / pause"), (33, "Previous track"), (34, "Next track"), (36, "Stop"),
        (37, "Mute"), (38, "Volume up"), (39, "Volume down"), (65, "Easy-Shift (hold)"),
    };
    public static readonly string[] Slots =
    {
        "Left button", "Right button", "Wheel click", "Wheel tilt left", "Wheel tilt right", "Wheel up", "Wheel down",
        "Side button front", "Side button back", "Easy-Shift button", "Top button front", "Top button back",
    };
    public static readonly int[] WheelSlots = { 3, 4, 5, 6 };   // the firmware needs these for scrolling
    public static string Name(int code) => List.Where(a => a.code == code).Select(a => a.name).FirstOrDefault() ?? $"Other (code {code})";
}

/// .NET key <-> HID keyboard usage, for keyboard shortcuts on buttons.
static class Keys2Hid
{
    static readonly Dictionary<System.Windows.Forms.Keys, int> map = Build();
    static Dictionary<System.Windows.Forms.Keys, int> Build()
    {
        var m = new Dictionary<System.Windows.Forms.Keys, int>();
        for (int i = 0; i < 26; i++) m[System.Windows.Forms.Keys.A + i] = 4 + i;
        for (int i = 1; i <= 9; i++) m[System.Windows.Forms.Keys.D1 + i - 1] = 29 + i;
        m[System.Windows.Forms.Keys.D0] = 39;
        for (int i = 0; i < 12; i++) m[System.Windows.Forms.Keys.F1 + i] = 58 + i;
        for (int i = 0; i < 12; i++) m[System.Windows.Forms.Keys.F13 + i] = 104 + i;
        for (int i = 1; i <= 9; i++) m[System.Windows.Forms.Keys.NumPad1 + i - 1] = 88 + i;
        m[System.Windows.Forms.Keys.NumPad0] = 98;
        var extra = new (System.Windows.Forms.Keys k, int u)[]
        {
            (System.Windows.Forms.Keys.Enter, 40), (System.Windows.Forms.Keys.Escape, 41), (System.Windows.Forms.Keys.Back, 42),
            (System.Windows.Forms.Keys.Tab, 43), (System.Windows.Forms.Keys.Space, 44), (System.Windows.Forms.Keys.OemMinus, 45),
            (System.Windows.Forms.Keys.Oemplus, 46), (System.Windows.Forms.Keys.OemOpenBrackets, 47), (System.Windows.Forms.Keys.Oem6, 48),
            (System.Windows.Forms.Keys.Oem5, 49), (System.Windows.Forms.Keys.Oem1, 51), (System.Windows.Forms.Keys.Oem7, 52),
            (System.Windows.Forms.Keys.Oemtilde, 53), (System.Windows.Forms.Keys.Oemcomma, 54), (System.Windows.Forms.Keys.OemPeriod, 55),
            (System.Windows.Forms.Keys.OemQuestion, 56), (System.Windows.Forms.Keys.CapsLock, 57), (System.Windows.Forms.Keys.PrintScreen, 70),
            (System.Windows.Forms.Keys.Scroll, 71), (System.Windows.Forms.Keys.Pause, 72), (System.Windows.Forms.Keys.Insert, 73),
            (System.Windows.Forms.Keys.Home, 74), (System.Windows.Forms.Keys.PageUp, 75), (System.Windows.Forms.Keys.Delete, 76),
            (System.Windows.Forms.Keys.End, 77), (System.Windows.Forms.Keys.PageDown, 78), (System.Windows.Forms.Keys.Right, 79),
            (System.Windows.Forms.Keys.Left, 80), (System.Windows.Forms.Keys.Down, 81), (System.Windows.Forms.Keys.Up, 82),
            (System.Windows.Forms.Keys.NumLock, 83), (System.Windows.Forms.Keys.Divide, 84), (System.Windows.Forms.Keys.Multiply, 85),
            (System.Windows.Forms.Keys.Subtract, 86), (System.Windows.Forms.Keys.Add, 87), (System.Windows.Forms.Keys.Decimal, 99),
            (System.Windows.Forms.Keys.OemBackslash, 100), (System.Windows.Forms.Keys.Apps, 101),
        };
        foreach (var (k, u) in extra) m[k] = u;
        return m;
    }
    public static int Usage(System.Windows.Forms.Keys k) => map.TryGetValue(k, out var u) ? u : 0;
    public static System.Windows.Forms.Keys KeyOf(int usage) => usage == 0 ? System.Windows.Forms.Keys.None : map.FirstOrDefault(p => p.Value == usage).Key;
    public static string Label(int mods, int usage) => KeyNames.Label(mods, KeyOf(usage));
}

/// All five profiles + which one is active; saved as a small JSON file.
class MouseState
{
    public Profile[] P = new Profile[KoneAimo.Profiles];
    public int Active;

    public static MouseState Read(KoneAimo m)
    {
        var st = new MouseState { Active = m.ActiveProfile };
        for (int i = 0; i < KoneAimo.Profiles; i++) st.P[i] = new Profile(m.ReadSettings(i), m.ReadButtons(i));
        return st;
    }

    public MouseState Clone() => new MouseState { P = P.Select(p => p.Clone()).ToArray(), Active = Active };

    public bool SameAs(MouseState o) => Active == o.Active && P.Zip(o.P, (a, b) => a.S.SequenceEqual(b.S) && a.B.SequenceEqual(b.B)).All(x => x);

    // The DPI stage in use (settings byte 5, plus the checksum it changes) and the active profile move when the
    // mouse's own buttons are pressed; they are not "lost settings".
    static bool Live(int i) => i == 5 || i >= KoneAimo.SettingsLen - 2;
    public int ProfilesDifferingFrom(MouseState o) =>
        P.Zip(o.P, (a, b) => !a.B.SequenceEqual(b.B) || Enumerable.Range(0, a.S.Length).Any(i => !Live(i) && a.S[i] != b.S[i])).Count(x => x);
    public MouseState WithLiveFrom(MouseState o)
    {
        var c = Clone(); c.Active = o.Active;
        for (int i = 0; i < c.P.Length; i++) { c.P[i].S[5] = o.P[i].S[5]; c.P[i].FixChecksum(); }
        return c;
    }

    /// Writes only what differs from what the mouse holds, then reads everything back to check it stuck.
    public static MouseState Apply(KoneAimo m, MouseState target, MouseState onMouse)
    {
        for (int i = 0; i < KoneAimo.Profiles; i++)
        {
            target.P[i].FixChecksum();
            if (!target.P[i].S.SequenceEqual(onMouse.P[i].S)) m.WriteSettings(i, target.P[i].S);
            if (!target.P[i].B.SequenceEqual(onMouse.P[i].B)) m.WriteButtons(i, target.P[i].B);
        }
        if (target.Active != onMouse.Active) m.ActiveProfile = target.Active;
        var back = Read(m);
        if (!back.SameAs(target)) throw new IOException("The mouse did not keep everything that was written. Nothing is lost: reload to see what it holds now.");
        return back;
    }

    public string ToJson()
    {
        var sb = new StringBuilder();
        sb.Append("{\n \"device\": \"ROCCAT Kone Aimo 16K 1e7d:2e2c\",\n \"active_profile\": ").Append(Active).Append(",\n \"profiles\": [\n");
        for (int i = 0; i < P.Length; i++)
            sb.Append($"  {{\n   \"settings\": \"{Hex(P[i].S)}\",\n   \"buttons\": \"{Hex(P[i].B)}\"\n  }}").Append(i < P.Length - 1 ? ",\n" : "\n");
        return sb.Append(" ]\n}\n").ToString();
    }

    public static MouseState FromJson(string json)
    {
        var st = new MouseState { Active = int.Parse(Regex.Match(json, "\"active_profile\"\\s*:\\s*(\\d+)").Groups[1].Value) };
        var s = Regex.Matches(json, "\"settings\"\\s*:\\s*\"([0-9a-fA-F]+)\"").Cast<Match>().Select(x => Unhex(x.Groups[1].Value)).ToArray();
        var b = Regex.Matches(json, "\"buttons\"\\s*:\\s*\"([0-9a-fA-F]+)\"").Cast<Match>().Select(x => Unhex(x.Groups[1].Value)).ToArray();
        if (s.Length != KoneAimo.Profiles || b.Length != KoneAimo.Profiles) throw new InvalidDataException("The file does not hold 5 profiles.");
        for (int i = 0; i < KoneAimo.Profiles; i++)
        {
            if (s[i].Length != KoneAimo.SettingsLen || s[i][0] != 0x06 || s[i][2] != i || b[i].Length != KoneAimo.ButtonsLen || b[i][0] != 0x07 || b[i][2] != i)
                throw new InvalidDataException($"Profile {i + 1} in the file is not valid Kone AIMO data.");
            st.P[i] = new Profile(s[i], b[i]);
            if (!st.P[i].ChecksumOk) throw new InvalidDataException($"Profile {i + 1} in the file is damaged (checksum).");
        }
        if (st.Active < 0 || st.Active >= KoneAimo.Profiles) throw new InvalidDataException("Bad active profile.");
        return st;
    }

    static string Hex(byte[] b) => string.Concat(b.Select(x => x.ToString("x2")));
    static byte[] Unhex(string s) => Enumerable.Range(0, s.Length / 2).Select(i => Convert.ToByte(s.Substring(2 * i, 2), 16)).ToArray();
}
