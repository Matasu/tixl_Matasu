using System.Runtime.InteropServices;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace SilkWindows;

/// <summary>
/// Wraps a Silk.NET window for use as a DX11 render target.
/// Provides native window handle (HWND) for swap chain creation,
/// input handling via Silk.NET.Input, and a render loop replacement
/// for SharpDX's RenderLoop.
/// </summary>
public sealed class SilkRenderWindow : IDisposable
{
    private readonly IWindow _window;
    private IInputContext _inputContext;

    /// <summary>
    /// Native window handle (HWND on Windows). Available immediately after construction.
    /// Use this for DX11 swap chain OutputHandle.
    /// </summary>
    public IntPtr Handle { get; private set; }

    /// <summary>Client area width in pixels.</summary>
    public int ClientWidth => _window.Size.X;

    /// <summary>Client area height in pixels.</summary>
    public int ClientHeight => _window.Size.Y;

    public string Title
    {
        get => _window.Title;
        set => _window.Title = value;
    }

    public bool IsVisible
    {
        get => _window.IsVisible;
        set => _window.IsVisible = value;
    }

    public Vector2D<int> Position
    {
        get => _window.Position;
        set => _window.Position = value;
    }

    public Vector2D<int> Size
    {
        get => _window.Size;
        set => _window.Size = value;
    }

    public WindowBorder WindowBorder
    {
        get => _window.WindowBorder;
        set => _window.WindowBorder = value;
    }

    public WindowState WindowState
    {
        get => _window.WindowState;
        set => _window.WindowState = value;
    }

    /// <summary>Silk.NET input context for keyboard/mouse access.</summary>
    public IInputContext InputContext => _inputContext;

    public IReadOnlyList<IKeyboard> Keyboards => _inputContext?.Keyboards;
    public IReadOnlyList<IMouse> Mice => _inputContext?.Mice;

    public event Action<Vector2D<int>> Resized;
    public event Action Closing;
    public event Action<bool> FocusChanged;
    public event Action<string[]> FileDrop;

    public SilkRenderWindow(string title, int width, int height, bool resizable = true, bool visible = false)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException("SilkRenderWindow requires Windows for DX11 rendering");

        var options = WindowOptions.Default;
        options.Title = title;
        options.Size = new Vector2D<int>(width, height);
        options.API = GraphicsAPI.None; // No OpenGL/Vulkan - we use DX11
        options.IsVisible = visible;
        options.ShouldSwapAutomatically = false;
        options.VSync = false; // DX11 swap chain handles vsync
        options.IsEventDriven = false;
        options.FramesPerSecond = 10000; // Don't throttle - DX11 Present handles timing
        options.UpdatesPerSecond = 10000;
        options.WindowBorder = resizable ? WindowBorder.Resizable : WindowBorder.Fixed;

        _window = Window.Create(options);

        // Wire up events before Initialize
        _window.Resize += size => Resized?.Invoke(size);
        _window.Closing += () => Closing?.Invoke();
        _window.FocusChanged += focused => FocusChanged?.Invoke(focused);
        _window.FileDrop += paths => FileDrop?.Invoke(paths);

        // Initialize immediately so HWND is available before Run()
        _window.Initialize();

        // Get native HWND
        var native = _window.Native;
        if (native?.Win32 is { } win32)
        {
            Handle = win32.Value.Hwnd;
        }
        else
        {
            throw new PlatformNotSupportedException("Failed to get Win32 window handle from Silk.NET");
        }

        // Create input context
        _inputContext = _window.CreateInput();
    }

    /// <summary>
    /// Runs the window's render loop, calling the provided callback each frame.
    /// This method blocks until the window is closed.
    /// Replaces SharpDX.Windows.RenderLoop.Run.
    /// </summary>
    public void Run(Action renderCallback)
    {
        _window.Render += _ => renderCallback();
        _window.IsVisible = true;
        _window.Run();
    }

    /// <summary>Sets the client area size.</summary>
    public void SetSize(int width, int height)
    {
        _window.Size = new Vector2D<int>(width, height);
    }

    /// <summary>Sets the window position.</summary>
    public void SetPosition(int x, int y)
    {
        _window.Position = new Vector2D<int>(x, y);
    }

    /// <summary>Closes the window and ends the render loop.</summary>
    public void Close()
    {
        _window.Close();
    }

    public void Dispose()
    {
        _inputContext?.Dispose();
        _window?.Dispose();
    }
}
