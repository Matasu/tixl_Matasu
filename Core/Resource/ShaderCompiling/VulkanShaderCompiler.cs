#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using T3.Core.DataTypes;
using T3.Core.Logging;
using T3.Core.Resource.Assets;

namespace T3.Core.Resource.ShaderCompiling;

/// <summary>
/// An implementation of <see cref="ShaderCompiler"/> that compiles HLSL to SPIR-V
/// using the DirectXShaderCompiler (DXC) for Vulkan backend support.
/// </summary>
public sealed class VulkanShaderCompiler : ShaderCompiler
{
    private readonly string _dxcPath;

    public VulkanShaderCompiler(string? dxcPath = null)
    {
        _dxcPath = dxcPath ?? FindDxc();
    }

    protected override bool CompileShaderFromSource<TShader>(
        ShaderCompilationArgs args,
        out byte[] blob,
        out string errorMessage)
    {
        blob = null!;
        errorMessage = string.Empty;

        if (!_shaderProfiles.TryGetValue(typeof(TShader), out var profile))
        {
            errorMessage = $"Unsupported shader type: {typeof(TShader).Name}";
            return false;
        }

        string? tempDir = null;
        try
        {
            tempDir = Path.Combine(Path.GetTempPath(), "t3-dxc-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);

            var sourceFile = Path.Combine(tempDir, "shader.hlsl");
            var outputFile = Path.Combine(tempDir, "shader.spv");
            File.WriteAllText(sourceFile, args.SourceCode);

            var includeDirs = ResolveIncludeDirectories(args);
            var dxcArgs = BuildDxcArguments(sourceFile, outputFile, args.EntryPoint, profile, includeDirs);

            var processInfo = new ProcessStartInfo
            {
                FileName = _dxcPath,
                Arguments = dxcArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                errorMessage = "Failed to start DXC process.";
                return false;
            }

            var stderr = process.StandardError.ReadToEnd();
            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit(30_000);

            if (process.ExitCode != 0)
            {
                errorMessage = ExtractDxcErrorMessage(stderr, stdout);
                return false;
            }

            if (!File.Exists(outputFile))
            {
                errorMessage = "DXC produced no output file.";
                return false;
            }

            blob = File.ReadAllBytes(outputFile);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"DXC compilation failed: {ex.Message}";
            return false;
        }
        finally
        {
            if (tempDir != null && Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); }
                catch { /* best effort cleanup */ }
            }
        }
    }

    protected override void CreateShaderInstance<TShader>(
        string name,
        in byte[] blob,
        out TShader shader)
    {
        var shaderType = typeof(TShader);

        if (!_shaderConstructors.TryGetValue(shaderType, out var constructor))
            throw new ArgumentException($"No Vulkan shader constructor registered for type: {shaderType.Name}");

        shader = (TShader)constructor(blob);
        shader.Name = name;
    }

    private static string BuildDxcArguments(
        string sourceFile,
        string outputFile,
        string entryPoint,
        string profile,
        IReadOnlyList<string> includeDirs)
    {
        var args = $"-spirv -fspv-target-env=vulkan1.2 -E {entryPoint} -T {profile} \"{sourceFile}\" -Fo \"{outputFile}\"";

        foreach (var dir in includeDirs)
        {
            args += $" -I \"{dir}\"";
        }

        return args;
    }

    private static List<string> ResolveIncludeDirectories(ShaderCompilationArgs args)
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (args.Owner?.AvailableResourcePackages != null)
        {
            foreach (var package in args.Owner.AvailableResourcePackages)
            {
                var folder = package.AssetsFolder;
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                {
                    dirs.Add(folder);
                }
            }
        }

        // Resolve individual includes to discover their directories
        var includes = GetIncludesFrom(args.SourceCode);
        foreach (var include in includes)
        {
            var includeInLib = "Lib:shaders/" + include;
            if (AssetRegistry.TryResolveAddress(includeInLib, args.Owner, out var path, out _))
            {
                var dir = Path.GetDirectoryName(path);
                if (dir != null)
                    dirs.Add(dir);
            }
        }

        return dirs.ToList();
    }

    private static string ExtractDxcErrorMessage(string stderr, string stdout)
    {
        var message = !string.IsNullOrWhiteSpace(stderr) ? stderr : stdout;
        if (string.IsNullOrWhiteSpace(message))
            return "DXC compilation failed with no error message.";

        return message.Trim();
    }

    private static string FindDxc()
    {
        foreach (var candidate in new[] { "dxc", "dxc.exe" })
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = candidate,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p != null)
                {
                    p.WaitForExit(5000);
                    if (p.ExitCode == 0)
                        return candidate;
                }
            }
            catch
            {
                // Not found, try next
            }
        }

        return "dxc";
    }

    private static readonly IReadOnlyDictionary<Type, Func<byte[], AbstractShader>> _shaderConstructors =
        new Dictionary<Type, Func<byte[], AbstractShader>>
        {
            { typeof(VulkanVertexShader), data => new VulkanVertexShader(new SpirvShaderModule(data), data) },
            { typeof(VulkanPixelShader), data => new VulkanPixelShader(new SpirvShaderModule(data), data) },
            { typeof(VulkanComputeShader), data => new VulkanComputeShader(new SpirvShaderModule(data), data) },
            { typeof(VulkanGeometryShader), data => new VulkanGeometryShader(new SpirvShaderModule(data), data) },
        };

    /// <summary>
    /// DXC shader profiles. SM 6.0+ is required for SPIR-V output.
    /// Includes both Vulkan-specific types and existing T3 DX11 types
    /// so CompileShaderFromSource works during migration.
    /// </summary>
    private static readonly IReadOnlyDictionary<Type, string> _shaderProfiles =
        new Dictionary<Type, string>
        {
            // Vulkan shader types
            { typeof(VulkanVertexShader), "vs_6_0" },
            { typeof(VulkanPixelShader), "ps_6_0" },
            { typeof(VulkanComputeShader), "cs_6_0" },
            { typeof(VulkanGeometryShader), "gs_6_0" },
            // DX11 types (for compatibility during migration)
            { typeof(T3.Core.DataTypes.VertexShader), "vs_6_0" },
            { typeof(T3.Core.DataTypes.PixelShader), "ps_6_0" },
            { typeof(T3.Core.DataTypes.ComputeShader), "cs_6_0" },
            { typeof(T3.Core.DataTypes.GeometryShader), "gs_6_0" },
        };
}
