#nullable enable
using System;
using Silk.NET.Vulkan;

namespace T3.Core.Resource.Vulkan;

/// <summary>
/// Wraps Vulkan pipeline creation for both compute and graphics pipelines.
/// Manages pipeline layout, pipeline cache, and the pipeline itself.
/// </summary>
public sealed unsafe class VulkanPipeline : IDisposable
{
    public Pipeline Pipeline { get; }
    public PipelineLayout Layout { get; }
    public PipelineBindPoint BindPoint { get; }

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly PipelineCache _cache;
    private bool _disposed;

    private VulkanPipeline(Vk vk, Device device, Pipeline pipeline,
        PipelineLayout layout, PipelineCache cache, PipelineBindPoint bindPoint)
    {
        _vk = vk;
        _device = device;
        Pipeline = pipeline;
        Layout = layout;
        _cache = cache;
        BindPoint = bindPoint;
    }

    public static VulkanPipeline CreateCompute(
        Vk vk, Device device,
        byte[] spirvBytecode, string entryPoint,
        ReadOnlySpan<DescriptorSetLayout> setLayouts,
        PipelineCache cache = default)
    {
        var layout = CreatePipelineLayout(vk, device, setLayouts);

        var shaderModule = CreateShaderModule(vk, device, spirvBytecode);
        try
        {
            var entryNamePtr = System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi(entryPoint);
            try
            {
                var stageInfo = new PipelineShaderStageCreateInfo
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ShaderStageFlags.ComputeBit,
                    Module = shaderModule,
                    PName = (byte*)entryNamePtr
                };

                var pipelineInfo = new ComputePipelineCreateInfo
                {
                    SType = StructureType.ComputePipelineCreateInfo,
                    Stage = stageInfo,
                    Layout = layout
                };

                if (vk.CreateComputePipelines(device, cache, 1, &pipelineInfo, null, out var pipeline) != Result.Success)
                {
                    vk.DestroyPipelineLayout(device, layout, null);
                    throw new InvalidOperationException("Failed to create compute pipeline.");
                }

                return new VulkanPipeline(vk, device, pipeline, layout, cache, PipelineBindPoint.Compute);
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(entryNamePtr);
            }
        }
        finally
        {
            vk.DestroyShaderModule(device, shaderModule, null);
        }
    }

    public static VulkanPipeline CreateGraphics(
        Vk vk, Device device,
        byte[] vertexSpirv, byte[] fragmentSpirv,
        string vertexEntry, string fragmentEntry,
        ReadOnlySpan<DescriptorSetLayout> setLayouts,
        RenderPass renderPass,
        ReadOnlySpan<VertexInputBindingDescription> vertexBindings,
        ReadOnlySpan<VertexInputAttributeDescription> vertexAttributes,
        GraphicsPipelineConfig? config = null,
        PipelineCache cache = default)
    {
        var cfg = config ?? GraphicsPipelineConfig.Default;
        var layout = CreatePipelineLayout(vk, device, setLayouts);

        var vertModule = CreateShaderModule(vk, device, vertexSpirv);
        var fragModule = CreateShaderModule(vk, device, fragmentSpirv);
        try
        {
            var vertEntryPtr = System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi(vertexEntry);
            var fragEntryPtr = System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi(fragmentEntry);
            try
            {
                var shaderStages = stackalloc PipelineShaderStageCreateInfo[2];
                shaderStages[0] = new PipelineShaderStageCreateInfo
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ShaderStageFlags.VertexBit,
                    Module = vertModule,
                    PName = (byte*)vertEntryPtr
                };
                shaderStages[1] = new PipelineShaderStageCreateInfo
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ShaderStageFlags.FragmentBit,
                    Module = fragModule,
                    PName = (byte*)fragEntryPtr
                };

                fixed (VertexInputBindingDescription* bindingsPtr = vertexBindings)
                fixed (VertexInputAttributeDescription* attribsPtr = vertexAttributes)
                {
                    var vertexInputInfo = new PipelineVertexInputStateCreateInfo
                    {
                        SType = StructureType.PipelineVertexInputStateCreateInfo,
                        VertexBindingDescriptionCount = (uint)vertexBindings.Length,
                        PVertexBindingDescriptions = bindingsPtr,
                        VertexAttributeDescriptionCount = (uint)vertexAttributes.Length,
                        PVertexAttributeDescriptions = attribsPtr
                    };

                    var inputAssembly = new PipelineInputAssemblyStateCreateInfo
                    {
                        SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                        Topology = cfg.Topology,
                        PrimitiveRestartEnable = false
                    };

                    var viewportState = new PipelineViewportStateCreateInfo
                    {
                        SType = StructureType.PipelineViewportStateCreateInfo,
                        ViewportCount = 1,
                        ScissorCount = 1
                    };

                    var rasterizer = new PipelineRasterizationStateCreateInfo
                    {
                        SType = StructureType.PipelineRasterizationStateCreateInfo,
                        DepthClampEnable = false,
                        RasterizerDiscardEnable = false,
                        PolygonMode = cfg.PolygonMode,
                        LineWidth = 1.0f,
                        CullMode = cfg.CullMode,
                        FrontFace = cfg.FrontFace,
                        DepthBiasEnable = false
                    };

                    var multisampling = new PipelineMultisampleStateCreateInfo
                    {
                        SType = StructureType.PipelineMultisampleStateCreateInfo,
                        SampleShadingEnable = false,
                        RasterizationSamples = SampleCountFlags.Count1Bit
                    };

                    var colorBlendAttachment = new PipelineColorBlendAttachmentState
                    {
                        ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit |
                                         ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                        BlendEnable = cfg.BlendEnable
                    };

                    if (cfg.BlendEnable)
                    {
                        colorBlendAttachment.SrcColorBlendFactor = BlendFactor.SrcAlpha;
                        colorBlendAttachment.DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha;
                        colorBlendAttachment.ColorBlendOp = BlendOp.Add;
                        colorBlendAttachment.SrcAlphaBlendFactor = BlendFactor.One;
                        colorBlendAttachment.DstAlphaBlendFactor = BlendFactor.Zero;
                        colorBlendAttachment.AlphaBlendOp = BlendOp.Add;
                    }

                    var colorBlending = new PipelineColorBlendStateCreateInfo
                    {
                        SType = StructureType.PipelineColorBlendStateCreateInfo,
                        LogicOpEnable = false,
                        AttachmentCount = 1,
                        PAttachments = &colorBlendAttachment
                    };

                    var depthStencil = new PipelineDepthStencilStateCreateInfo
                    {
                        SType = StructureType.PipelineDepthStencilStateCreateInfo,
                        DepthTestEnable = cfg.DepthTestEnable,
                        DepthWriteEnable = cfg.DepthWriteEnable,
                        DepthCompareOp = CompareOp.Less,
                        DepthBoundsTestEnable = false,
                        StencilTestEnable = false
                    };

                    var dynamicStates = stackalloc DynamicState[]
                    {
                        DynamicState.Viewport,
                        DynamicState.Scissor
                    };
                    var dynamicState = new PipelineDynamicStateCreateInfo
                    {
                        SType = StructureType.PipelineDynamicStateCreateInfo,
                        DynamicStateCount = 2,
                        PDynamicStates = dynamicStates
                    };

                    var pipelineInfo = new GraphicsPipelineCreateInfo
                    {
                        SType = StructureType.GraphicsPipelineCreateInfo,
                        StageCount = 2,
                        PStages = shaderStages,
                        PVertexInputState = &vertexInputInfo,
                        PInputAssemblyState = &inputAssembly,
                        PViewportState = &viewportState,
                        PRasterizationState = &rasterizer,
                        PMultisampleState = &multisampling,
                        PColorBlendState = &colorBlending,
                        PDepthStencilState = &depthStencil,
                        PDynamicState = &dynamicState,
                        Layout = layout,
                        RenderPass = renderPass,
                        Subpass = 0
                    };

                    if (vk.CreateGraphicsPipelines(device, cache, 1, &pipelineInfo, null, out var pipeline) != Result.Success)
                    {
                        vk.DestroyPipelineLayout(device, layout, null);
                        throw new InvalidOperationException("Failed to create graphics pipeline.");
                    }

                    return new VulkanPipeline(vk, device, pipeline, layout, cache, PipelineBindPoint.Graphics);
                }
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(vertEntryPtr);
                System.Runtime.InteropServices.Marshal.FreeHGlobal(fragEntryPtr);
            }
        }
        finally
        {
            vk.DestroyShaderModule(device, vertModule, null);
            vk.DestroyShaderModule(device, fragModule, null);
        }
    }

    public static PipelineCache CreatePipelineCache(Vk vk, Device device)
    {
        var cacheInfo = new PipelineCacheCreateInfo
        {
            SType = StructureType.PipelineCacheCreateInfo
        };

        if (vk.CreatePipelineCache(device, &cacheInfo, null, out var cache) != Result.Success)
            throw new InvalidOperationException("Failed to create pipeline cache.");

        return cache;
    }

    private static PipelineLayout CreatePipelineLayout(
        Vk vk, Device device, ReadOnlySpan<DescriptorSetLayout> setLayouts)
    {
        fixed (DescriptorSetLayout* layoutsPtr = setLayouts)
        {
            var layoutInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = (uint)setLayouts.Length,
                PSetLayouts = layoutsPtr
            };

            if (vk.CreatePipelineLayout(device, &layoutInfo, null, out var layout) != Result.Success)
                throw new InvalidOperationException("Failed to create pipeline layout.");

            return layout;
        }
    }

    private static ShaderModule CreateShaderModule(Vk vk, Device device, byte[] spirvBytecode)
    {
        fixed (byte* codePtr = spirvBytecode)
        {
            var createInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)spirvBytecode.Length,
                PCode = (uint*)codePtr
            };

            if (vk.CreateShaderModule(device, &createInfo, null, out var module) != Result.Success)
                throw new InvalidOperationException("Failed to create shader module.");

            return module;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _vk.DestroyPipeline(_device, Pipeline, null);
        _vk.DestroyPipelineLayout(_device, Layout, null);
    }
}

/// <summary>
/// Configuration for graphics pipeline creation.
/// </summary>
public sealed class GraphicsPipelineConfig
{
    public PrimitiveTopology Topology { get; init; } = PrimitiveTopology.TriangleList;
    public PolygonMode PolygonMode { get; init; } = PolygonMode.Fill;
    public CullModeFlags CullMode { get; init; } = CullModeFlags.BackBit;
    public FrontFace FrontFace { get; init; } = FrontFace.CounterClockwise;
    public bool BlendEnable { get; init; }
    public bool DepthTestEnable { get; init; } = true;
    public bool DepthWriteEnable { get; init; } = true;

    public static GraphicsPipelineConfig Default { get; } = new();
}
