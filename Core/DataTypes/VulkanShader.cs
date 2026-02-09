#nullable enable
using System;
using T3.Core.DataTypes.Vector;

namespace T3.Core.DataTypes;

/// <summary>
/// Lightweight SPIR-V bytecode holder for Vulkan shaders.
/// VkShaderModule creation is deferred to pipeline build time.
/// </summary>
public sealed class SpirvShaderModule : IDisposable
{
    public byte[] SpirvBytecode { get; }

    public SpirvShaderModule(byte[] spirvBytecode)
    {
        SpirvBytecode = spirvBytecode;
    }

    public void Dispose() { }
}

public sealed class VulkanVertexShader : Shader<SpirvShaderModule>
{
    public VulkanVertexShader(SpirvShaderModule module, byte[] spirvBytecode)
        : base(module, spirvBytecode)
    {
        SpirvBytecodeBase = spirvBytecode;
    }
}

public sealed class VulkanPixelShader : Shader<SpirvShaderModule>
{
    public VulkanPixelShader(SpirvShaderModule module, byte[] spirvBytecode)
        : base(module, spirvBytecode)
    {
        SpirvBytecodeBase = spirvBytecode;
    }
}

public sealed class VulkanComputeShader : Shader<SpirvShaderModule>
{
    public byte[]? SpirvBytecode { get; set; }

    public VulkanComputeShader(SpirvShaderModule module, byte[] spirvBytecode)
        : base(module, spirvBytecode)
    {
        SpirvBytecode = spirvBytecode;
        SpirvBytecodeBase = spirvBytecode;
    }

    public bool TryGetThreadGroups(out Int3 threadGroups)
    {
        threadGroups = default;
        if (SpirvBytecode == null || SpirvBytecode.Length < 20)
            return false;

        return TryExtractThreadGroupsFromSpirv(SpirvBytecode, out threadGroups);
    }

    /// <summary>
    /// Parses SPIR-V binary to extract OpExecutionMode LocalSize values
    /// for compute shader thread group dimensions.
    /// </summary>
    internal static bool TryExtractThreadGroupsFromSpirv(byte[] spirv, out Int3 threadGroups)
    {
        threadGroups = default;

        if (spirv.Length < 20)
            return false;

        var words = new uint[spirv.Length / 4];
        Buffer.BlockCopy(spirv, 0, words, 0, words.Length * 4);

        // Validate SPIR-V magic number
        if (words[0] != 0x07230203)
            return false;

        // Walk instructions starting after the 5-word header
        int i = 5;
        while (i < words.Length)
        {
            var wordCount = (int)(words[i] >> 16);
            var opcode = (ushort)(words[i] & 0xFFFF);

            if (wordCount == 0)
                break;

            // OpExecutionMode = 16, LocalSize = 17
            // Format: OpExecutionMode %entryPoint LocalSize %x %y %z
            if (opcode == 16 && wordCount >= 6 && i + 5 < words.Length)
            {
                var mode = words[i + 2];
                if (mode == 17) // LocalSize
                {
                    threadGroups = new Int3((int)words[i + 3], (int)words[i + 4], (int)words[i + 5]);
                    return true;
                }
            }

            i += wordCount;
        }

        return false;
    }
}

public sealed class VulkanGeometryShader : Shader<SpirvShaderModule>
{
    public VulkanGeometryShader(SpirvShaderModule module, byte[] spirvBytecode)
        : base(module, spirvBytecode)
    {
        SpirvBytecodeBase = spirvBytecode;
    }
}
