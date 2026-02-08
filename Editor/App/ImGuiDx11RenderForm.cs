using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ImGuiNET;
using Silk.NET.Input;
using SilkWindows;
using T3.Editor.Gui.UiHelpers;
using T3.SystemUi;

// ReSharper disable InconsistentNaming
// ReSharper disable IdentifierTypo
namespace T3.Editor.App;

/// <summary>
/// Wraps a SilkRenderWindow and maps Silk.NET input events to ImGui IO.
/// Replaces the previous RenderForm-based implementation.
/// </summary>
public class ImGuiDx11RenderForm : IDisposable
{
    internal static IWindowsFormsMessageHandler[] InputMethods = Array.Empty<IWindowsFormsMessageHandler>();

    public SilkRenderWindow RenderWindow { get; }
    public IntPtr Handle => RenderWindow.Handle;
    public int ClientWidth => RenderWindow.ClientWidth;
    public int ClientHeight => RenderWindow.ClientHeight;

    public static event Action<string[], Vector2> FilesDropped;

    public ImGuiDx11RenderForm(string title)
        : this(title, 640, 480)
    {
    }

    public ImGuiDx11RenderForm(string title, int width, int height, bool disableClose = false)
    {
        RenderWindow = new SilkRenderWindow(title, width, height, resizable: true, visible: false);

        SetupImGuiInput();
        SetupFileDrop();

        if (disableClose)
        {
            DisableCloseButton(RenderWindow.Handle);
        }

        // Install WndProc hook for SpaceMouse WM_INPUT forwarding
        InstallWndProcHook();
    }

    private void SetupImGuiInput()
    {
        foreach (var keyboard in RenderWindow.Keyboards)
        {
            keyboard.KeyDown += OnKeyDown;
            keyboard.KeyUp += OnKeyUp;
            keyboard.KeyChar += OnKeyChar;
        }

        foreach (var mouse in RenderWindow.Mice)
        {
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
            mouse.MouseMove += OnMouseMove;
            mouse.Scroll += OnMouseScroll;
        }

        RenderWindow.FocusChanged += OnFocusChanged;
    }

    private void SetupFileDrop()
    {
        RenderWindow.FileDrop += paths =>
                                 {
                                     if (paths == null || paths.Length == 0)
                                         return;

                                     var io = ImGui.GetIO();
                                     var mousePos = io.MousePos;
                                     FilesDropped?.Invoke(paths, mousePos);
                                 };
    }

    #region ImGui Input Mapping

    private void OnMouseDown(IMouse mouse, MouseButton button)
    {
        if (_isViewer)
            return;

        var io = ImGui.GetIO();
        var btnIndex = button switch
        {
            MouseButton.Left => 0,
            MouseButton.Right => 1,
            MouseButton.Middle => 2,
            _ => -1
        };

        if (btnIndex >= 0)
            io.MouseDown[btnIndex] = true;
    }

    private void OnMouseUp(IMouse mouse, MouseButton button)
    {
        var io = ImGui.GetIO();
        var btnIndex = button switch
        {
            MouseButton.Left => 0,
            MouseButton.Right => 1,
            MouseButton.Middle => 2,
            _ => -1
        };

        if (btnIndex >= 0)
            io.MouseDown[btnIndex] = false;
    }

    private void OnMouseMove(IMouse mouse, Vector2 position)
    {
        if (!_isViewer)
        {
            ImGui.GetIO().MousePos = position;
        }
    }

    private void OnMouseScroll(IMouse mouse, ScrollWheel scroll)
    {
        var io = ImGui.GetIO();

        // Check Ctrl state for zoom detection
        var isCtrl = false;
        foreach (var kb in RenderWindow.Keyboards)
        {
            if (kb.IsKeyPressed(Key.ControlLeft) || kb.IsKeyPressed(Key.ControlRight))
            {
                isCtrl = true;
                break;
            }
        }

        var now = Environment.TickCount64;
        var inZoomGesture = (now - _lastZoomTick) < 80;

        if (isCtrl || inZoomGesture)
        {
            // Silk.NET/GLFW provides scroll values already in notches (1.0 per detent)
            MouseWheelPanning.AddZoom(scroll.Y);
            _lastZoomTick = now;
            return;
        }

        // Vertical scroll - scroll.Y is already in notches (unlike raw Win32 delta/120)
        if (Math.Abs(scroll.Y) > 0.001f)
        {
            io.MouseWheel += scroll.Y / 2;
            MouseWheelPanning.AddVerticalScroll(scroll.Y);
        }

        // Horizontal scroll
        if (Math.Abs(scroll.X) > 0.001f)
        {
            io.MouseWheelH += scroll.X / 2;
            MouseWheelPanning.AddHorizontalScroll(scroll.X);
        }
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int scancode)
    {
        var io = ImGui.GetIO();
        var vk = SilkKeyMap.ToVirtualKey(key);

        if (SilkKeyMap.IsShift(key))
        {
            io.KeyShift = true;
            if (vk < io.KeysDown.Count)
                io.KeysDown[vk] = true;
            io.KeysDown[(int)T3.SystemUi.Key.ShiftKey] = true;
        }
        else if (SilkKeyMap.IsControl(key))
        {
            io.KeyCtrl = true;
            io.KeysDown[(int)T3.SystemUi.Key.CtrlKey] = true;
        }
        else if (SilkKeyMap.IsAlt(key))
        {
            io.KeyAlt = true;
            io.KeysDown[(int)T3.SystemUi.Key.Alt] = true;
            KeyHandler.SetKeyDown(T3.SystemUi.Key.Alt);
        }
        else if (vk > 0 && vk < 256)
        {
            io.KeysDown[vk] = true;
        }
    }

    private void OnKeyUp(IKeyboard keyboard, Key key, int scancode)
    {
        var io = ImGui.GetIO();
        var vk = SilkKeyMap.ToVirtualKey(key);

        if (SilkKeyMap.IsShift(key))
        {
            io.KeyShift = false;
            io.KeysDown[(int)T3.SystemUi.Key.ShiftKey] = false;
        }
        else if (SilkKeyMap.IsControl(key))
        {
            io.KeyCtrl = false;
            io.KeysDown[(int)T3.SystemUi.Key.CtrlKey] = false;
        }
        else if (SilkKeyMap.IsAlt(key))
        {
            io.KeyAlt = false;
            io.KeysDown[(int)T3.SystemUi.Key.Alt] = false;
            KeyHandler.SetKeyUp(T3.SystemUi.Key.Alt);
        }
        else if (vk > 0 && vk < 256)
        {
            io.KeysDown[vk] = false;
        }
    }

    private void OnKeyChar(IKeyboard keyboard, char character)
    {
        if (character > 0 && character < 0x10000)
            ImGui.GetIO().AddInputCharacter(character);
    }

    private void OnFocusChanged(bool focused)
    {
        if (focused)
        {
            // On focus gain, reset all key states to prevent stuck keys
            var io = ImGui.GetIO();
            for (int i = 0; i < io.KeysDown.Count; i++)
                io.KeysDown[i] = false;
            io.KeyShift = false;
            io.KeyCtrl = false;
            io.KeyAlt = false;
        }
        else
        {
            // On focus loss, clear alt key
            var io = ImGui.GetIO();
            io.KeysDown[(int)T3.SystemUi.Key.Alt] = false;
            KeyHandler.SetKeyUp(T3.SystemUi.Key.Alt);
        }
    }

    #endregion

    #region Mouse Cursor

    internal void UpdateMouseCursor()
    {
        ImGuiIOPtr io = ImGui.GetIO();
        if (((uint)io.ConfigFlags & (uint)ImGuiConfigFlags.NoMouseCursorChange) > 0)
            return;

        ImGuiMouseCursor imguiCursor = ImGui.GetMouseCursor();
        if (imguiCursor == ImGuiMouseCursor.None || io.MouseDrawCursor)
        {
            Cursor.Current = null;
            return;
        }

        Cursor newCursor = imguiCursor switch
        {
            ImGuiMouseCursor.TextInput => Cursors.IBeam,
            ImGuiMouseCursor.ResizeAll => Cursors.SizeAll,
            ImGuiMouseCursor.ResizeEW => Cursors.SizeWE,
            ImGuiMouseCursor.ResizeNS => Cursors.SizeNS,
            ImGuiMouseCursor.ResizeNESW => Cursors.SizeNESW,
            ImGuiMouseCursor.ResizeNWSE => Cursors.SizeNWSE,
            ImGuiMouseCursor.Hand => Cursors.Hand,
            _ => Cursors.Arrow
        };

        if (Cursor.Current != newCursor)
        {
            Cursor.Current = newCursor;
        }
    }

    #endregion

    #region WndProc Hook for SpaceMouse (WM_INPUT)

    private void InstallWndProcHook()
    {
        // Always install the hook - InputMethods may be populated after construction
        // (e.g. when SpaceMouse is initialized via SetInteractionDevices)
        _newWndProcDelegate = WndProcHook;
        var newWndProcPtr = Marshal.GetFunctionPointerForDelegate(_newWndProcDelegate);
        _originalWndProc = SetWindowLongPtr(RenderWindow.Handle, GWL_WNDPROC, newWndProcPtr);
    }

    private IntPtr WndProcHook(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_INPUT)
        {
            foreach (var handler in InputMethods)
            {
                var message = Message.Create(hwnd, (int)msg, wParam, lParam);
                handler.ProcessMessage(message);
            }
        }

        return CallWindowProc(_originalWndProc, hwnd, msg, wParam, lParam);
    }

    private const int GWL_WNDPROC = -4;
    private const uint WM_INPUT = 0x00FF;

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    private WndProcDelegate _newWndProcDelegate;
    private IntPtr _originalWndProc;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    #endregion

    #region Window Close Button

    private static void DisableCloseButton(IntPtr hwnd)
    {
        var sysMenu = GetSystemMenu(hwnd, false);
        if (sysMenu != IntPtr.Zero)
        {
            EnableMenuItem(sysMenu, SC_CLOSE, MF_BYCOMMAND | MF_GRAYED);
        }
    }

    private const uint SC_CLOSE = 0xF060;
    private const uint MF_BYCOMMAND = 0x00000000;
    private const uint MF_GRAYED = 0x00000001;

    [DllImport("user32.dll")]
    private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

    [DllImport("user32.dll")]
    private static extern bool EnableMenuItem(IntPtr hMenu, uint uIDEnableItem, uint uEnable);

    #endregion

    /// <summary>
    /// Set to true for the viewer window, which ignores mouse input.
    /// </summary>
    internal bool IsViewer
    {
        get => _isViewer;
        set => _isViewer = value;
    }

    private bool _isViewer;
    private long _lastZoomTick;

    public void Dispose()
    {
        RenderWindow?.Dispose();
    }
}
