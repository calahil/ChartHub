using System.Runtime.InteropServices;

namespace ChartHub.Server.Services;

/// <summary>
/// P/Invoke bindings and Linux ABI types for the uinput kernel module.
/// /dev/uinput requires the server process user to be in the 'input' group or
/// hold CAP_SYS_ADMIN. See ChartHub.Server/README.md for setup instructions.
/// </summary>
internal static class UinputNative
{
    // ── libc syscalls ────────────────────────────────────────────────────────

    // open() accepts a null-terminated byte string. Encode to UTF-8 before calling.
    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int open_native(byte[] pathname, int flags);

    /// <summary>Opens a file path on Linux via libc open(2).</summary>
    internal static int open(string pathname, int flags)
    {
        // Append null terminator and encode as UTF-8 bytes to satisfy CA2101 without
        // an incorrect Unicode ABI on POSIX — open(2) expects a raw byte string.
        byte[] pathBytes = System.Text.Encoding.UTF8.GetBytes(pathname + '\0');
        return open_native(pathBytes, flags);
    }

    [DllImport("libc", SetLastError = true)]
    internal static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    internal static extern int write(int fd, byte[] buf, int count);

    [DllImport("libc", SetLastError = true)]
    internal static extern int ioctl(int fd, ulong request, int arg);

    [DllImport("libc", SetLastError = true)]
    internal static extern int ioctl(int fd, ulong request, ref UinputSetup arg);

    [DllImport("libc", SetLastError = true)]
    internal static extern int ioctl(int fd, ulong request, ref UinputAbsSetup arg);

    // ── open flags ───────────────────────────────────────────────────────────

    internal const int O_WRONLY = 1;
    internal const int O_NONBLOCK = 2048;

    // ── ioctl request codes ──────────────────────────────────────────────────

    internal const ulong UI_SET_EVBIT = 0x40045564;
    internal const ulong UI_SET_KEYBIT = 0x40045565;
    internal const ulong UI_SET_RELBIT = 0x40045566;
    internal const ulong UI_SET_ABSBIT = 0x40045567;
    internal const ulong UI_SET_MSCBIT = 0x40045568;
    internal const ulong UI_DEV_SETUP = 0x405c5503;
    // UI_ABS_SETUP = _IOWR('U', 4, struct uinput_abs_setup); size=28=0x1C
    internal const ulong UI_ABS_SETUP = 0xC01C5504;
    internal const ulong UI_DEV_CREATE = 0x5501;
    internal const ulong UI_DEV_DESTROY = 0x5502;

    // ── event types ──────────────────────────────────────────────────────────

    internal const int EV_SYN = 0x00;
    internal const int EV_KEY = 0x01;
    internal const int EV_REL = 0x02;
    internal const int EV_ABS = 0x03;
    internal const int EV_MSC = 0x04;

    // ── synchronisation event ────────────────────────────────────────────────

    internal const int SYN_REPORT = 0x00;

    // ── relative axes (mouse) ─────────────────────────────────────────────────

    internal const int REL_X = 0x00;
    internal const int REL_Y = 0x01;

    // ── absolute axes (analog sticks, triggers, D-pad) ────────────────────────

    internal const int ABS_X = 0x00;
    internal const int ABS_Y = 0x01;
    internal const int ABS_Z = 0x02;
    internal const int ABS_RX = 0x03;
    internal const int ABS_RY = 0x04;
    internal const int ABS_RZ = 0x05;
    internal const int ABS_HAT0X = 0x10;
    internal const int ABS_HAT0Y = 0x11;

    // ── mouse button codes ───────────────────────────────────────────────────

    internal const int BTN_LEFT = 0x110;
    internal const int BTN_RIGHT = 0x111;

    // ── gamepad button codes ─────────────────────────────────────────────────

    internal const int BTN_A = 0x130;
    internal const int BTN_B = 0x131;
    internal const int BTN_X = 0x133;
    internal const int BTN_Y = 0x134;
    // Xbox 360 shoulder and thumb buttons — registered for correct SDL2 button indices.
    internal const int BTN_TL = 0x136;
    internal const int BTN_TR = 0x137;
    internal const int BTN_SELECT = 0x13a;
    internal const int BTN_START = 0x13b;
    internal const int BTN_MODE = 0x13c;
    internal const int BTN_THUMBL = 0x13d;
    internal const int BTN_THUMBR = 0x13e;

    // ── mappings: button-id strings to Linux key codes ───────────────────────

    internal static readonly IReadOnlyDictionary<string, int> GamepadButtonCodes =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = BTN_A,
            ["b"] = BTN_B,
            ["x"] = BTN_X,
            ["y"] = BTN_Y,
            ["select"] = BTN_SELECT,
            ["start"] = BTN_START,
        };

    // ── keyboard key codes (common subset) ───────────────────────────────────

    internal const int KEY_RESERVED = 0;
    internal const int KEY_ESC = 1;
    internal const int KEY_1 = 2;
    internal const int KEY_2 = 3;
    internal const int KEY_3 = 4;
    internal const int KEY_4 = 5;
    internal const int KEY_5 = 6;
    internal const int KEY_6 = 7;
    internal const int KEY_7 = 8;
    internal const int KEY_8 = 9;
    internal const int KEY_9 = 10;
    internal const int KEY_0 = 11;
    internal const int KEY_MINUS = 12;
    internal const int KEY_EQUAL = 13;
    internal const int KEY_BACKSPACE = 14;
    internal const int KEY_TAB = 15;
    internal const int KEY_Q = 16;
    internal const int KEY_W = 17;
    internal const int KEY_E = 18;
    internal const int KEY_R = 19;
    internal const int KEY_T = 20;
    internal const int KEY_Y = 21;
    internal const int KEY_U = 22;
    internal const int KEY_I = 23;
    internal const int KEY_O = 24;
    internal const int KEY_P = 25;
    internal const int KEY_LEFTBRACE = 26;
    internal const int KEY_RIGHTBRACE = 27;
    internal const int KEY_ENTER = 28;
    internal const int KEY_LEFTCTRL = 29;
    internal const int KEY_A = 30;
    internal const int KEY_S = 31;
    internal const int KEY_D = 32;
    internal const int KEY_F = 33;
    internal const int KEY_G = 34;
    internal const int KEY_H = 35;
    internal const int KEY_J = 36;
    internal const int KEY_K = 37;
    internal const int KEY_L = 38;
    internal const int KEY_SEMICOLON = 39;
    internal const int KEY_APOSTROPHE = 40;
    internal const int KEY_GRAVE = 41;
    internal const int KEY_LEFTSHIFT = 42;
    internal const int KEY_BACKSLASH = 43;
    internal const int KEY_Z = 44;
    internal const int KEY_X_KEY = 45;
    internal const int KEY_C = 46;
    internal const int KEY_V = 47;
    internal const int KEY_B = 48;
    internal const int KEY_N = 49;
    internal const int KEY_M = 50;
    internal const int KEY_COMMA = 51;
    internal const int KEY_DOT = 52;
    internal const int KEY_SLASH = 53;
    internal const int KEY_RIGHTSHIFT = 54;
    internal const int KEY_LEFTALT = 56;
    internal const int KEY_SPACE = 57;
    internal const int KEY_CAPSLOCK = 58;

    // ── uinput ABI structs ────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    internal struct TimeVal
    {
        public long TvSec;
        public long TvUsec;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct InputEvent
    {
        public TimeVal Time;
        public ushort Type;
        public ushort Code;
        public int Value;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct UinputId
    {
        public ushort BusType;
        public ushort Vendor;
        public ushort Product;
        public ushort Version;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct UinputSetup
    {
        public UinputId Id;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string Name;

        public uint FfEffectsMax;
    }

    /// <summary>Maps to Linux struct input_absinfo.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct InputAbsinfo
    {
        public int Value;
        public int Minimum;
        public int Maximum;
        public int Fuzz;
        public int Flat;
        public int Resolution;
    }

    /// <summary>Maps to Linux struct uinput_abs_setup (used with UI_ABS_SETUP).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct UinputAbsSetup
    {
        public uint Code;
        public InputAbsinfo Absinfo;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    internal static byte[] SerialiseInputEvent(ushort type, ushort code, int value)
    {
        InputEvent ev = new()
        {
            Time = default,
            Type = type,
            Code = code,
            Value = value,
        };

        int size = Marshal.SizeOf<InputEvent>();
        byte[] buffer = new byte[size];
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(ev, ptr, false);
            Marshal.Copy(ptr, buffer, 0, size);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        return buffer;
    }

    internal static void WriteSync(int fd)
    {
        byte[] ev = SerialiseInputEvent((ushort)EV_SYN, (ushort)SYN_REPORT, 0);
        _ = write(fd, ev, ev.Length);
    }

    /// <summary>Maps a printable character to a Linux key code and a shift flag.</summary>
    internal static bool TryMapChar(char c, out int keyCode, out bool shift)
    {
        shift = false;
        keyCode = KEY_RESERVED;

        if (char.IsLetter(c))
        {
            char lower = char.ToLowerInvariant(c);
            shift = char.IsUpper(c);
            keyCode = lower switch
            {
                'a' => KEY_A,
                'b' => KEY_B,
                'c' => KEY_C,
                'd' => KEY_D,
                'e' => KEY_E,
                'f' => KEY_F,
                'g' => KEY_G,
                'h' => KEY_H,
                'i' => KEY_I,
                'j' => KEY_J,
                'k' => KEY_K,
                'l' => KEY_L,
                'm' => KEY_M,
                'n' => KEY_N,
                'o' => KEY_O,
                'p' => KEY_P,
                'q' => KEY_Q,
                'r' => KEY_R,
                's' => KEY_S,
                't' => KEY_T,
                'u' => KEY_U,
                'v' => KEY_V,
                'w' => KEY_W,
                'x' => KEY_X_KEY,
                'y' => KEY_Y,
                'z' => KEY_Z,
                _ => KEY_RESERVED,
            };
            return keyCode != KEY_RESERVED;
        }

        switch (c)
        {
            case '0': keyCode = KEY_0; return true;
            case '1': keyCode = KEY_1; return true;
            case '2': keyCode = KEY_2; return true;
            case '3': keyCode = KEY_3; return true;
            case '4': keyCode = KEY_4; return true;
            case '5': keyCode = KEY_5; return true;
            case '6': keyCode = KEY_6; return true;
            case '7': keyCode = KEY_7; return true;
            case '8': keyCode = KEY_8; return true;
            case '9': keyCode = KEY_9; return true;
            case ' ': keyCode = KEY_SPACE; return true;
            case '\n': keyCode = KEY_ENTER; return true;
            case '\t': keyCode = KEY_TAB; return true;
            case '-': keyCode = KEY_MINUS; return true;
            case '=': keyCode = KEY_EQUAL; return true;
            case ',': keyCode = KEY_COMMA; return true;
            case '.': keyCode = KEY_DOT; return true;
            case '/': keyCode = KEY_SLASH; return true;
            case ';': keyCode = KEY_SEMICOLON; return true;
            case '\'': keyCode = KEY_APOSTROPHE; return true;
            case '[': keyCode = KEY_LEFTBRACE; return true;
            case ']': keyCode = KEY_RIGHTBRACE; return true;
            case '\\': keyCode = KEY_BACKSLASH; return true;
            case '`': keyCode = KEY_GRAVE; return true;
            // Shifted variants
            case '!': keyCode = KEY_1; shift = true; return true;
            case '@': keyCode = KEY_2; shift = true; return true;
            case '#': keyCode = KEY_3; shift = true; return true;
            case '$': keyCode = KEY_4; shift = true; return true;
            case '%': keyCode = KEY_5; shift = true; return true;
            case '^': keyCode = KEY_6; shift = true; return true;
            case '&': keyCode = KEY_7; shift = true; return true;
            case '*': keyCode = KEY_8; shift = true; return true;
            case '(': keyCode = KEY_9; shift = true; return true;
            case ')': keyCode = KEY_0; shift = true; return true;
            case '_': keyCode = KEY_MINUS; shift = true; return true;
            case '+': keyCode = KEY_EQUAL; shift = true; return true;
            case '<': keyCode = KEY_COMMA; shift = true; return true;
            case '>': keyCode = KEY_DOT; shift = true; return true;
            case '?': keyCode = KEY_SLASH; shift = true; return true;
            case ':': keyCode = KEY_SEMICOLON; shift = true; return true;
            case '"': keyCode = KEY_APOSTROPHE; shift = true; return true;
            case '{': keyCode = KEY_LEFTBRACE; shift = true; return true;
            case '}': keyCode = KEY_RIGHTBRACE; shift = true; return true;
            case '|': keyCode = KEY_BACKSLASH; shift = true; return true;
            case '~': keyCode = KEY_GRAVE; shift = true; return true;
            default: return false;
        }
    }
}
