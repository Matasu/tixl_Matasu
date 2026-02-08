using Silk.NET.Input;

namespace SilkWindows;

/// <summary>
/// Maps Silk.NET Key enum values to Windows Virtual Key codes.
/// The system's T3.SystemUi.Key enum uses Windows VK values,
/// so this mapping allows Silk.NET input to work with the existing key handling.
/// </summary>
public static class SilkKeyMap
{
    /// <summary>
    /// Converts a Silk.NET Key to its Windows Virtual Key code equivalent.
    /// Returns 0 for unmapped keys.
    /// </summary>
    public static int ToVirtualKey(Key key) => key switch
    {
        // Letters (Silk.NET Key.A = 65 = VK_A)
        Key.A => 0x41,
        Key.B => 0x42,
        Key.C => 0x43,
        Key.D => 0x44,
        Key.E => 0x45,
        Key.F => 0x46,
        Key.G => 0x47,
        Key.H => 0x48,
        Key.I => 0x49,
        Key.J => 0x4A,
        Key.K => 0x4B,
        Key.L => 0x4C,
        Key.M => 0x4D,
        Key.N => 0x4E,
        Key.O => 0x4F,
        Key.P => 0x50,
        Key.Q => 0x51,
        Key.R => 0x52,
        Key.S => 0x53,
        Key.T => 0x54,
        Key.U => 0x55,
        Key.V => 0x56,
        Key.W => 0x57,
        Key.X => 0x58,
        Key.Y => 0x59,
        Key.Z => 0x5A,

        // Numbers
        Key.Number0 => 0x30,
        Key.Number1 => 0x31,
        Key.Number2 => 0x32,
        Key.Number3 => 0x33,
        Key.Number4 => 0x34,
        Key.Number5 => 0x35,
        Key.Number6 => 0x36,
        Key.Number7 => 0x37,
        Key.Number8 => 0x38,
        Key.Number9 => 0x39,

        // Function keys
        Key.F1 => 0x70,
        Key.F2 => 0x71,
        Key.F3 => 0x72,
        Key.F4 => 0x73,
        Key.F5 => 0x74,
        Key.F6 => 0x75,
        Key.F7 => 0x76,
        Key.F8 => 0x77,
        Key.F9 => 0x78,
        Key.F10 => 0x79,
        Key.F11 => 0x7A,
        Key.F12 => 0x7B,
        Key.F13 => 0x7C,
        Key.F14 => 0x7D,
        Key.F15 => 0x7E,
        Key.F16 => 0x7F,
        Key.F17 => 0x80,
        Key.F18 => 0x81,
        Key.F19 => 0x82,
        Key.F20 => 0x83,
        Key.F21 => 0x84,
        Key.F22 => 0x85,
        Key.F23 => 0x86,
        Key.F24 => 0x87,

        // Navigation
        Key.Escape => 0x1B,
        Key.Enter => 0x0D,
        Key.Tab => 0x09,
        Key.Backspace => 0x08,
        Key.Insert => 0x2D,
        Key.Delete => 0x2E,
        Key.Right => 0x27,
        Key.Left => 0x25,
        Key.Down => 0x28,
        Key.Up => 0x26,
        Key.PageUp => 0x21,
        Key.PageDown => 0x22,
        Key.Home => 0x24,
        Key.End => 0x23,
        Key.Space => 0x20,

        // Modifiers
        Key.ShiftLeft => 0x10,  // VK_SHIFT
        Key.ShiftRight => 0x10,
        Key.ControlLeft => 0x11, // VK_CONTROL
        Key.ControlRight => 0x11,
        Key.AltLeft => 0x12,    // VK_MENU
        Key.AltRight => 0x12,
        Key.SuperLeft => 0x5B,  // VK_LWIN
        Key.SuperRight => 0x5C, // VK_RWIN
        Key.Menu => 0x5D,       // VK_APPS

        // Lock keys
        Key.CapsLock => 0x14,
        Key.ScrollLock => 0x91,
        Key.NumLock => 0x90,
        Key.PrintScreen => 0x2C,
        Key.Pause => 0x13,

        // Numpad
        Key.Keypad0 => 0x60,
        Key.Keypad1 => 0x61,
        Key.Keypad2 => 0x62,
        Key.Keypad3 => 0x63,
        Key.Keypad4 => 0x64,
        Key.Keypad5 => 0x65,
        Key.Keypad6 => 0x66,
        Key.Keypad7 => 0x67,
        Key.Keypad8 => 0x68,
        Key.Keypad9 => 0x69,
        Key.KeypadDecimal => 0x6E,
        Key.KeypadDivide => 0x6F,
        Key.KeypadMultiply => 0x6A,
        Key.KeypadSubtract => 0x6D,
        Key.KeypadAdd => 0x6B,
        Key.KeypadEnter => 0x0D,

        // OEM keys
        Key.Semicolon => 0xBA,     // VK_OEM_1
        Key.Equal => 0xBB,         // VK_OEM_PLUS
        Key.Comma => 0xBC,         // VK_OEM_COMMA
        Key.Minus => 0xBD,         // VK_OEM_MINUS
        Key.Period => 0xBE,        // VK_OEM_PERIOD
        Key.Slash => 0xBF,         // VK_OEM_2
        Key.GraveAccent => 0xC0,   // VK_OEM_3
        Key.LeftBracket => 0xDB,   // VK_OEM_4
        Key.BackSlash => 0xDC,     // VK_OEM_5
        Key.RightBracket => 0xDD,  // VK_OEM_6
        Key.Apostrophe => 0xDE,    // VK_OEM_7

        _ => 0
    };

    /// <summary>
    /// Returns true if the given Silk.NET key is a shift key.
    /// </summary>
    public static bool IsShift(Key key) => key is Key.ShiftLeft or Key.ShiftRight;

    /// <summary>
    /// Returns true if the given Silk.NET key is a control key.
    /// </summary>
    public static bool IsControl(Key key) => key is Key.ControlLeft or Key.ControlRight;

    /// <summary>
    /// Returns true if the given Silk.NET key is an alt key.
    /// </summary>
    public static bool IsAlt(Key key) => key is Key.AltLeft or Key.AltRight;
}
