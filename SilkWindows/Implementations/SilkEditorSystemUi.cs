using System.Diagnostics;
using System.Drawing;
using Silk.NET.Windowing;
using T3.SystemUi;
using TextCopy;

namespace SilkWindows.Implementations;

/// <summary>
/// Cross-platform implementation of <see cref="IEditorSystemUiService"/>.
/// Delegates <see cref="ICoreSystemUiService"/> to an injected instance (for cursor/input
/// which still comes from the platform window), and provides cross-platform alternatives for:
/// - DPI scaling (no-op, handled natively by Silk.NET/GLFW)
/// - Clipboard (TextCopy)
/// - File dialogs (SilkWindows FileManager)
/// - Screen/monitor info (Silk.NET monitor enumeration)
/// </summary>
public sealed class SilkEditorSystemUi : IEditorSystemUiService
{
    private readonly ICoreSystemUiService _coreService;

    public SilkEditorSystemUi(ICoreSystemUiService coreService)
    {
        _coreService = coreService;
    }

    #region IEditorSystemUiService

    public void EnableDpiAwareScaling()
    {
        // Silk.NET/GLFW handles DPI awareness natively.
    }

    public void SetClipboardText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        try
        {
            ClipboardService.SetText(text);
        }
        catch (Exception)
        {
            // Clipboard may be unavailable (e.g. headless environment)
        }
    }

    public string GetClipboardText()
    {
        try
        {
            return ClipboardService.GetText() ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    public IFilePicker CreateFilePicker()
    {
        return new SilkFilePicker();
    }

    public IReadOnlyList<IScreen> AllScreens
    {
        get
        {
            try
            {
                var platform = Window.GetWindowPlatform(false);
                if (platform == null)
                    return Array.Empty<IScreen>();

                var monitors = platform.GetMonitors();
                return monitors.Select(m => (IScreen)new SilkScreen(m)).ToArray();
            }
            catch (Exception)
            {
                return Array.Empty<IScreen>();
            }
        }
    }

    #endregion

    #region ICoreSystemUiService (delegated)

    public void OpenWithDefaultApplication(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            throw new Exception("Uri is empty");

        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }

    public void ExitApplication()
    {
        Environment.Exit(0);
    }

    public void ExitThread()
    {
        Environment.Exit(0);
    }

    public ICursor Cursor => _coreService.Cursor;

    public void SetUnhandledExceptionMode(bool throwException)
    {
        // No cross-platform equivalent; AppDomain unhandled exception handling
        // is configured separately if needed.
    }

    #endregion

    private sealed class SilkScreen : IScreen
    {
        private readonly IMonitor _monitor;

        public SilkScreen(IMonitor monitor)
        {
            _monitor = monitor;
        }

        public int BitsPerPixel => 32; // Default for modern displays
        public Rectangle Bounds => new(_monitor.Bounds.Origin.X, _monitor.Bounds.Origin.Y,
                                       _monitor.Bounds.Size.X, _monitor.Bounds.Size.Y);
        public Rectangle WorkingArea => Bounds; // Silk.NET doesn't expose taskbar-adjusted area
        public string DeviceName => _monitor.Name;
        public bool Primary => _monitor.Index == 0;
    }
}
