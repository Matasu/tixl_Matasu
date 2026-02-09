#nullable enable
using System;
using System.Collections.Generic;
using Silk.NET.Vulkan;

namespace T3.Core.Resource.Vulkan;

/// <summary>
/// Simple Vulkan memory allocator that handles memory type selection,
/// allocation tracking, and staged upload patterns.
/// </summary>
public sealed unsafe class VulkanMemoryAllocator : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly PhysicalDeviceMemoryProperties _memoryProperties;
    private readonly List<DeviceMemory> _allocations = new();

    public VulkanMemoryAllocator(Vk vk, Device device, PhysicalDevice physicalDevice)
    {
        _vk = vk;
        _device = device;

        _vk.GetPhysicalDeviceMemoryProperties(physicalDevice, out _memoryProperties);
    }

    public uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        for (uint i = 0; i < _memoryProperties.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1u << (int)i)) != 0 &&
                (_memoryProperties.MemoryTypes[(int)i].PropertyFlags & properties) == properties)
            {
                return i;
            }
        }

        throw new InvalidOperationException(
            $"Failed to find suitable memory type (filter: {typeFilter}, flags: {properties}).");
    }

    public DeviceMemory Allocate(MemoryRequirements requirements, MemoryPropertyFlags properties)
    {
        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = FindMemoryType(requirements.MemoryTypeBits, properties)
        };

        if (_vk.AllocateMemory(_device, &allocInfo, null, out var memory) != Result.Success)
            throw new InvalidOperationException("Failed to allocate Vulkan device memory.");

        _allocations.Add(memory);
        return memory;
    }

    public void CopyBufferToBuffer(VulkanContext context, Buffer src, Buffer dst, ulong size)
    {
        var commandBuffer = context.BeginSingleTimeCommands();

        var copyRegion = new BufferCopy { Size = size };
        _vk.CmdCopyBuffer(commandBuffer, src, dst, 1, &copyRegion);

        context.EndSingleTimeCommands(commandBuffer);
    }

    public void CopyBufferToImage(VulkanContext context, Buffer src, Image dst,
        uint width, uint height)
    {
        var commandBuffer = context.BeginSingleTimeCommands();

        var region = new BufferImageCopy
        {
            BufferOffset = 0,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = 0,
                BaseArrayLayer = 0,
                LayerCount = 1
            },
            ImageOffset = default,
            ImageExtent = new Extent3D(width, height, 1)
        };

        _vk.CmdCopyBufferToImage(commandBuffer, src, dst,
            ImageLayout.TransferDstOptimal, 1, &region);

        context.EndSingleTimeCommands(commandBuffer);
    }

    public void Free(DeviceMemory memory)
    {
        _allocations.Remove(memory);
        _vk.FreeMemory(_device, memory, null);
    }

    public void Dispose()
    {
        foreach (var memory in _allocations)
        {
            _vk.FreeMemory(_device, memory, null);
        }
        _allocations.Clear();
    }
}
