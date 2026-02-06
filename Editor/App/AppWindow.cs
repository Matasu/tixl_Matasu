using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using SilkWindows;
using T3.Core.DataTypes.Vector;
using T3.Core.Resource;
using T3.Core.SystemUi;
using T3.Editor.Gui.Styling;
using T3.SystemUi;
using Device = SharpDX.Direct3D11.Device;
using Rectangle = System.Drawing.Rectangle;
using Resource = SharpDX.Direct3D11.Resource;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.App;

/// <summary>
/// Functions and properties related to rendering DX11 content into Silk.NET windows
/// </summary>
internal sealed class AppWindow
{
    public IntPtr HwndHandle => Form.Handle;
    public Int2 Size => new(Width, Height);
    public int Width => Form.ClientWidth;
    public int Height => Form.ClientHeight;
    public bool IsFullScreen => _isFullScreen;

    internal SwapChain SwapChain { get => _swapChain; private set => _swapChain = value; }
    internal RenderTargetView RenderTargetView { get => _renderTargetView; private set => _renderTargetView = value; }
    internal ImGuiDx11RenderForm Form { get; private set; }

    internal SwapChainDescription SwapChainDescription => new()
                                                              {
                                                                  ModeDescription = new ModeDescription(Width,
                                                                                                        Height,
                                                                                                        new Rational(60, 1),
                                                                                                        Format.R8G8B8A8_UNorm),
                                                                  IsWindowed = true,
                                                                  OutputHandle = Form.Handle,
                                                                  SampleDescription = new SampleDescription(1, 0),

                                                                  // Working consistently
                                                                  BufferCount = 2,
                                                                  SwapEffect = SwapEffect.Discard,
                                                                  Usage = Usage.RenderTargetOutput
                                                              };

    internal bool IsMinimized => Form.RenderWindow.WindowState == Silk.NET.Windowing.WindowState.Minimized;
    internal bool IsCursorOverWindow => IsCursorInWindowBounds();
    public Texture2D Texture { get; set; }

    internal AppWindow(string windowTitle, bool disableClose)
    {
        CreateWindow(windowTitle, disableClose);
    }

    public void SetVisible(bool isVisible)
    {
        Form.RenderWindow.IsVisible = isVisible;
    }

    public void SetSizeable()
    {
        Form.RenderWindow.WindowBorder = WindowBorder.Resizable;
        _isFullScreen = false;
        if (_boundsBeforeFullscreen.Width > 0 && _boundsBeforeFullscreen.Height > 0)
        {
            Form.RenderWindow.Position = new Vector2D<int>(_boundsBeforeFullscreen.X, _boundsBeforeFullscreen.Y);
            Form.RenderWindow.Size = new Vector2D<int>(_boundsBeforeFullscreen.Width, _boundsBeforeFullscreen.Height);
        }
    }

    public void Show() => Form.RenderWindow.IsVisible = true;

    public Vector2 GetDpi()
    {
        var dpi = GetDpiForWindow(Form.Handle);
        if (dpi == 0) dpi = 96; // fallback to standard DPI
        return new Vector2(dpi, dpi);
    }

    internal void SetFullScreen(int screenIndex)
    {
        var pos = Form.RenderWindow.Position;
        var size = Form.RenderWindow.Size;
        _boundsBeforeFullscreen = new Rectangle(pos.X, pos.Y, size.X, size.Y);

        // Get screen bounds
        var screenBounds = Screen.AllScreens[screenIndex].Bounds;
        Form.RenderWindow.WindowBorder = WindowBorder.Hidden;
        Form.RenderWindow.Position = new Vector2D<int>(screenBounds.X, screenBounds.Y);
        Form.RenderWindow.Size = new Vector2D<int>(screenBounds.Width, screenBounds.Height);
        _isFullScreen = true;
    }

    internal void UpdateSpanningBounds(int x, int y, int width, int height)
    {
        if (_isFullScreen)
        {
            Form.RenderWindow.Position = new Vector2D<int>(x, y);
            Form.RenderWindow.Size = new Vector2D<int>(width, height);
        }
        else
        {
            var pos = Form.RenderWindow.Position;
            var size = Form.RenderWindow.Size;
            _boundsBeforeFullscreen = new Rectangle(pos.X, pos.Y, size.X, size.Y);
            Form.RenderWindow.WindowBorder = WindowBorder.Hidden;
            Form.RenderWindow.Position = new Vector2D<int>(x, y);
            Form.RenderWindow.Size = new Vector2D<int>(width, height);
            _isFullScreen = true;
        }
    }

    internal void InitViewSwapChain(Factory factory)
    {
        SwapChain = new SwapChain(factory, _device, SwapChainDescription);
        SwapChain.ResizeBuffers(bufferCount: 3, Width, Height,
                                SwapChain.Description.ModeDescription.Format, SwapChain.Description.Flags);
    }

    internal void PrepareRenderingFrame()
    {
        _deviceContext.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
        _deviceContext.Rasterizer.SetViewport(new Viewport(0, 0, Width, Height, 0.0f, 1.0f));
        _deviceContext.OutputMerger.SetTargets(RenderTargetView);

        var color = UiColors.WindowBackground.ToByte4();
        var sharpDxColor = new SharpDX.Color(color.X, color.Y, color.Z, color.W);
        _deviceContext.ClearRenderTargetView(RenderTargetView, sharpDxColor);
    }

    internal void RunRenderLoop(Action callback) => Form.RenderWindow.Run(() => callback());

    internal void SetSize(int width, int height) => Form.RenderWindow.SetSize(width, height);

    internal void SetBorderStyleSizable() => Form.RenderWindow.WindowBorder = WindowBorder.Resizable;

    internal void InitializeWindow(FormWindowState windowState, CancelEventHandler handleClose, bool handleKeys)
    {
        InitRenderTargetsAndEventHandlers();

        if (handleKeys)
        {
            // Track keys via Silk.NET input for the system KeyHandler
            foreach (var keyboard in Form.RenderWindow.Keyboards)
            {
                keyboard.KeyDown += (kb, key, scancode) =>
                                    {
                                        var vk = SilkKeyMap.ToVirtualKey(key);
                                        if (vk != 0)
                                            KeyHandler.SetKeyDown(vk);
                                    };
                keyboard.KeyUp += (kb, key, scancode) =>
                                  {
                                      var vk = SilkKeyMap.ToVirtualKey(key);
                                      if (vk != 0)
                                          KeyHandler.SetKeyUp(vk);
                                  };
            }
        }

        // Track mouse via Silk.NET input for cursor position
        foreach (var mouse in Form.RenderWindow.Mice)
        {
            mouse.MouseMove += (m, pos) =>
                               {
                                   if (CoreUi.Instance?.Cursor != null)
                                   {
                                       // Update cursor position relative to screen
                                       // Note: ICursor.Position expects screen coordinates
                                   }
                               };
        }

        if (handleClose != null)
        {
            Form.RenderWindow.Closing += () =>
                                         {
                                             var args = new CancelEventArgs();
                                             handleClose(Form, args);
                                             // Note: Silk.NET doesn't support cancelling close from the event.
                                             // The close handler is responsible for preventing close if needed.
                                         };
        }

        // Apply initial window state
        switch (windowState)
        {
            case FormWindowState.Maximized:
                Form.RenderWindow.WindowState = Silk.NET.Windowing.WindowState.Maximized;
                break;
            case FormWindowState.Minimized:
                Form.RenderWindow.WindowState = Silk.NET.Windowing.WindowState.Minimized;
                break;
            default:
                Form.RenderWindow.WindowState = Silk.NET.Windowing.WindowState.Normal;
                break;
        }
    }

    internal void SetDevice(Device device, DeviceContext deviceContext, SwapChain swapChain = null)
    {
        if (_hasSetDevice)
            throw new InvalidOperationException("Device has already been set");

        _hasSetDevice = true;
        _device = device;
        _deviceContext = deviceContext;
        _swapChain = swapChain;
    }

    internal void Release()
    {
        _renderTargetView.Dispose();
        _backBufferTexture.Dispose();
        _swapChain.Dispose();
        Form?.Dispose();
    }

    private void CreateWindow(string windowTitle, bool disableClose)
    {
        if (disableClose)
        {
            Form = new ImGuiDx11RenderForm(windowTitle, 640, 360 + 20, disableClose: true);
            Form.RenderWindow.WindowBorder = WindowBorder.Hidden;
        }
        else
        {
            Form = new ImGuiDx11RenderForm(windowTitle, 640, 480);
        }
    }

    private void InitRenderTargetsAndEventHandlers()
    {
        var device = _device;
        _backBufferTexture = Resource.FromSwapChain<Texture2D>(SwapChain, 0);
        RenderTargetView = new RenderTargetView(device, _backBufferTexture);

        Form.RenderWindow.Resized += size =>
                                     {
                                         if (!_isResizingRightNow)
                                         {
                                             RebuildBackBuffer(device, ref _renderTargetView, ref _backBufferTexture, ref _swapChain, size.X, size.Y);
                                         }
                                     };
    }

    private static void RebuildBackBuffer(Device device, ref RenderTargetView rtv, ref Texture2D buffer, ref SwapChain swapChain, int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        rtv.Dispose();
        buffer.Dispose();
        swapChain.ResizeBuffers(3, width, height, Format.Unknown, 0);
        buffer = Resource.FromSwapChain<Texture2D>(swapChain, 0);
        rtv = new RenderTargetView(device, buffer);
    }

    private bool IsCursorInWindowBounds()
    {
        if (!GetCursorPos(out var cursorPos))
            return false;

        var winPos = Form.RenderWindow.Position;
        var winSize = Form.RenderWindow.Size;
        return cursorPos.X >= winPos.X && cursorPos.X < winPos.X + winSize.X
            && cursorPos.Y >= winPos.Y && cursorPos.Y < winPos.Y + winSize.Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private bool _hasSetDevice;
    private Device _device;
    private DeviceContext _deviceContext;
    private SwapChain _swapChain;
    private RenderTargetView _renderTargetView;
    private Texture2D _backBufferTexture;
    public Texture2D BackBufferTexture => _backBufferTexture;
    private bool _isResizingRightNow;
    private bool _isFullScreen;
    private Rectangle _boundsBeforeFullscreen;

    public void SetTexture(Texture2D texture)
    {
        Texture = texture;
    }
}
