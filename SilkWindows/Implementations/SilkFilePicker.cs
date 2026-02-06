using SilkWindows.Implementations.FileManager;
using T3.SystemUi;

namespace SilkWindows.Implementations;

/// <summary>
/// Cross-platform file picker using the SilkWindows <see cref="FileManager.FileManager"/>.
/// Opens an ImGui-based file browser window to replace the WinForms OpenFileDialog.
/// </summary>
internal sealed class SilkFilePicker : IFilePicker
{
    public string FileName { get; set; } = string.Empty;
    public string Filter { get; set; } = string.Empty;
    public string InitialDirectory { get; set; } = string.Empty;
    public bool Multiselect { get; set; }
    public bool RestoreDirectory { get; set; }
    public bool ShowHelp { get; set; }
    public bool ShowReadOnly { get; set; }
    public string Title { get; set; } = "Open File";
    public bool ValidateNames { get; set; } = true;
    public bool CheckFileExists { get; set; } = true;
    public bool CheckPathExists { get; set; } = true;
    public int FilterIndex { get; set; } = 1;

    public bool ChooseFile()
    {
        var directory = GetEffectiveDirectory();
        if (!Directory.Exists(directory))
            directory = Environment.CurrentDirectory;

        var managedDir = new ManagedDirectory(directory, IsReadOnly: true);
        var fileFilter = BuildFileFilter();
        var mode = CheckFileExists ? FileManagerMode.PickFile : FileManagerMode.PickDirectory;

        var fileManager = new FileManager.FileManager(mode, managedDir, fileFilter);
        var windowTitle = string.IsNullOrEmpty(Title) ? "Open File" : Title;

        var options = new SimpleWindowOptions(
            Size: new System.Numerics.Vector2(800, 600),
            Fps: 60,
            Vsync: true,
            IsResizable: true,
            AlwaysOnTop: false);

        var result = ImGuiWindowService.Instance.Show<PathInformation>(windowTitle, fileManager, options);

        if (result == null)
            return false;

        if (mode == FileManagerMode.PickDirectory)
        {
            // Callers expect Path.GetDirectoryName(FileName) to return the picked directory,
            // so place a sentinel filename inside the directory path (matches WinForms behavior).
            var sentinel = string.IsNullOrEmpty(FileName) ? "." : FileName;
            FileName = Path.Combine(result.AbsolutePath, sentinel);
        }
        else
        {
            FileName = result.AbsolutePath;
        }

        return true;
    }

    public void Dispose()
    {
    }

    private string GetEffectiveDirectory()
    {
        if (!string.IsNullOrEmpty(InitialDirectory))
            return InitialDirectory;

        if (!string.IsNullOrEmpty(FileName))
        {
            var dir = Path.GetDirectoryName(FileName);
            if (!string.IsNullOrEmpty(dir))
                return dir;
        }

        return Environment.CurrentDirectory;
    }

    private Func<string, bool>? BuildFileFilter()
    {
        if (string.IsNullOrEmpty(Filter))
            return null;

        // Parse WinForms-style filter: "Description|*.ext1;*.ext2|Description2|*.ext3"
        var parts = Filter.Split('|');
        if (parts.Length < 2)
            return null;

        // Use the selected FilterIndex (1-based, picks the pattern part)
        var patternIndex = Math.Clamp((FilterIndex - 1) * 2 + 1, 1, parts.Length - 1);
        var patterns = parts[patternIndex];

        if (patterns.Contains("*.*"))
            return null; // All files

        var extensions = patterns.Split(';')
                                 .Select(p => p.Trim())
                                 .Where(p => p.StartsWith("*."))
                                 .Select(p => p[1..].ToLowerInvariant()) // ".ext"
                                 .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (extensions.Count == 0)
            return null;

        return path => extensions.Contains(Path.GetExtension(path).ToLowerInvariant());
    }
}
