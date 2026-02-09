#nullable enable
using System;
using Silk.NET.Vulkan;

namespace T3.Core.Resource.Vulkan;

/// <summary>
/// Wraps a VkImage, VkDeviceMemory, and VkImageView.
/// Provides factory methods for texture and depth/stencil images,
/// and utilities for image layout transitions.
/// </summary>
public sealed unsafe class VulkanImage : IDisposable
{
    public Image Image { get; }
    public DeviceMemory Memory { get; }
    public ImageView View { get; }
    public Format Format { get; }
    public uint Width { get; }
    public uint Height { get; }
    public uint Depth { get; }

    private readonly Vk _vk;
    private readonly Device _device;
    private bool _disposed;

    private VulkanImage(Vk vk, Device device, Image image, DeviceMemory memory,
        ImageView view, Format format, uint width, uint height, uint depth)
    {
        _vk = vk;
        _device = device;
        Image = image;
        Memory = memory;
        View = view;
        Format = format;
        Width = width;
        Height = height;
        Depth = depth;
    }

    public static VulkanImage CreateTexture2D(
        Vk vk, Device device, VulkanMemoryAllocator allocator,
        uint width, uint height, Format format,
        ImageUsageFlags usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit)
    {
        return Create(vk, device, allocator, width, height, 1, format, ImageType.Type2D,
            usage, ImageAspectFlags.ColorBit, MemoryPropertyFlags.DeviceLocalBit,
            ImageViewType.Type2D);
    }

    public static VulkanImage CreateTexture3D(
        Vk vk, Device device, VulkanMemoryAllocator allocator,
        uint width, uint height, uint depth, Format format,
        ImageUsageFlags usage = ImageUsageFlags.StorageBit | ImageUsageFlags.SampledBit)
    {
        return Create(vk, device, allocator, width, height, depth, format, ImageType.Type3D,
            usage, ImageAspectFlags.ColorBit, MemoryPropertyFlags.DeviceLocalBit,
            ImageViewType.Type3D);
    }

    public static VulkanImage CreateDepthStencil(
        Vk vk, Device device, VulkanMemoryAllocator allocator,
        uint width, uint height, Format format = Format.D32Sfloat)
    {
        return Create(vk, device, allocator, width, height, 1, format, ImageType.Type2D,
            ImageUsageFlags.DepthStencilAttachmentBit,
            ImageAspectFlags.DepthBit, MemoryPropertyFlags.DeviceLocalBit,
            ImageViewType.Type2D);
    }

    public void TransitionLayout(VulkanContext context,
        ImageLayout oldLayout, ImageLayout newLayout)
    {
        var commandBuffer = context.BeginSingleTimeCommands();

        var aspectMask = newLayout == ImageLayout.DepthStencilAttachmentOptimal
            ? ImageAspectFlags.DepthBit
            : ImageAspectFlags.ColorBit;

        DeriveAccessAndStage(oldLayout, newLayout,
            out var srcAccess, out var dstAccess,
            out var srcStage, out var dstStage);

        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = Image,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = aspectMask,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            },
            SrcAccessMask = srcAccess,
            DstAccessMask = dstAccess
        };

        _vk.CmdPipelineBarrier(commandBuffer,
            srcStage, dstStage,
            0,
            0, null,
            0, null,
            1, &barrier);

        context.EndSingleTimeCommands(commandBuffer);
    }

    private static void DeriveAccessAndStage(
        ImageLayout oldLayout, ImageLayout newLayout,
        out AccessFlags srcAccess, out AccessFlags dstAccess,
        out PipelineStageFlags srcStage, out PipelineStageFlags dstStage)
    {
        srcAccess = 0;
        dstAccess = 0;
        srcStage = PipelineStageFlags.TopOfPipeBit;
        dstStage = PipelineStageFlags.BottomOfPipeBit;

        if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.TransferDstOptimal)
        {
            dstAccess = AccessFlags.TransferWriteBit;
            srcStage = PipelineStageFlags.TopOfPipeBit;
            dstStage = PipelineStageFlags.TransferBit;
        }
        else if (oldLayout == ImageLayout.TransferDstOptimal && newLayout == ImageLayout.ShaderReadOnlyOptimal)
        {
            srcAccess = AccessFlags.TransferWriteBit;
            dstAccess = AccessFlags.ShaderReadBit;
            srcStage = PipelineStageFlags.TransferBit;
            dstStage = PipelineStageFlags.FragmentShaderBit;
        }
        else if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.DepthStencilAttachmentOptimal)
        {
            dstAccess = AccessFlags.DepthStencilAttachmentReadBit | AccessFlags.DepthStencilAttachmentWriteBit;
            srcStage = PipelineStageFlags.TopOfPipeBit;
            dstStage = PipelineStageFlags.EarlyFragmentTestsBit;
        }
        else if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.General)
        {
            srcStage = PipelineStageFlags.TopOfPipeBit;
            dstStage = PipelineStageFlags.ComputeShaderBit;
        }
    }

    private static VulkanImage Create(
        Vk vk, Device device, VulkanMemoryAllocator allocator,
        uint width, uint height, uint depth,
        Format format, ImageType imageType, ImageUsageFlags usage,
        ImageAspectFlags aspectFlags, MemoryPropertyFlags memoryFlags,
        ImageViewType viewType)
    {
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = imageType,
            Extent = new Extent3D(width, height, depth),
            MipLevels = 1,
            ArrayLayers = 1,
            Format = format,
            Tiling = ImageTiling.Optimal,
            InitialLayout = ImageLayout.Undefined,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
            Samples = SampleCountFlags.Count1Bit
        };

        if (vk.CreateImage(device, &imageInfo, null, out var image) != Result.Success)
            throw new InvalidOperationException("Failed to create Vulkan image.");

        vk.GetImageMemoryRequirements(device, image, out var memRequirements);
        var memory = allocator.Allocate(memRequirements, memoryFlags);

        if (vk.BindImageMemory(device, image, memory, 0) != Result.Success)
        {
            vk.DestroyImage(device, image, null);
            allocator.Free(memory);
            throw new InvalidOperationException("Failed to bind image memory.");
        }

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = viewType,
            Format = format,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = aspectFlags,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };

        if (vk.CreateImageView(device, &viewInfo, null, out var view) != Result.Success)
        {
            vk.DestroyImage(device, image, null);
            allocator.Free(memory);
            throw new InvalidOperationException("Failed to create image view.");
        }

        return new VulkanImage(vk, device, image, memory, view, format, width, height, depth);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _vk.DestroyImageView(_device, View, null);
        _vk.DestroyImage(_device, Image, null);
        _vk.FreeMemory(_device, Memory, null);
    }
}
