#nullable enable
using System;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace T3.Core.Resource.Vulkan;

/// <summary>
/// Wraps a VkBuffer and its associated VkDeviceMemory.
/// Provides factory methods for common buffer types used in compute and rendering.
/// </summary>
public sealed unsafe class VulkanBuffer : IDisposable
{
    public Buffer Buffer { get; }
    public DeviceMemory Memory { get; }
    public ulong Size { get; }

    private readonly Vk _vk;
    private readonly Device _device;
    private bool _disposed;

    private VulkanBuffer(Vk vk, Device device, Buffer buffer, DeviceMemory memory, ulong size)
    {
        _vk = vk;
        _device = device;
        Buffer = buffer;
        Memory = memory;
        Size = size;
    }

    public static VulkanBuffer CreateStructuredBuffer(
        Vk vk, Device device, VulkanMemoryAllocator allocator,
        ulong size, BufferUsageFlags additionalUsage = 0)
    {
        var usageFlags = BufferUsageFlags.StorageBufferBit |
                         BufferUsageFlags.TransferDstBit |
                         BufferUsageFlags.TransferSrcBit |
                         additionalUsage;

        return Create(vk, device, allocator, size, usageFlags,
            MemoryPropertyFlags.DeviceLocalBit);
    }

    public static VulkanBuffer CreateConstantBuffer(
        Vk vk, Device device, VulkanMemoryAllocator allocator, ulong size)
    {
        return Create(vk, device, allocator, size,
            BufferUsageFlags.UniformBufferBit | BufferUsageFlags.TransferDstBit,
            MemoryPropertyFlags.DeviceLocalBit);
    }

    public static VulkanBuffer CreateStagingBuffer(
        Vk vk, Device device, VulkanMemoryAllocator allocator, ulong size)
    {
        return Create(vk, device, allocator, size,
            BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
    }

    public static VulkanBuffer Create(
        Vk vk, Device device, VulkanMemoryAllocator allocator,
        ulong size, BufferUsageFlags usage, MemoryPropertyFlags memoryFlags)
    {
        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive
        };

        if (vk.CreateBuffer(device, &bufferInfo, null, out var buffer) != Result.Success)
            throw new InvalidOperationException("Failed to create Vulkan buffer.");

        vk.GetBufferMemoryRequirements(device, buffer, out var memRequirements);
        var memory = allocator.Allocate(memRequirements, memoryFlags);

        if (vk.BindBufferMemory(device, buffer, memory, 0) != Result.Success)
        {
            vk.DestroyBuffer(device, buffer, null);
            allocator.Free(memory);
            throw new InvalidOperationException("Failed to bind buffer memory.");
        }

        return new VulkanBuffer(vk, device, buffer, memory, size);
    }

    public void Upload<T>(VulkanContext context, VulkanMemoryAllocator allocator,
        ReadOnlySpan<T> data) where T : unmanaged
    {
        var dataSize = (ulong)(data.Length * sizeof(T));
        if (dataSize > Size)
            throw new ArgumentException($"Data size ({dataSize}) exceeds buffer size ({Size}).");

        var staging = CreateStagingBuffer(_vk, _device, allocator, dataSize);
        try
        {
            void* mapped;
            if (_vk.MapMemory(_device, staging.Memory, 0, dataSize, 0, &mapped) != Result.Success)
                throw new InvalidOperationException("Failed to map staging buffer memory.");

            fixed (T* srcPtr = data)
            {
                System.Buffer.MemoryCopy(srcPtr, mapped, (long)dataSize, (long)dataSize);
            }

            _vk.UnmapMemory(_device, staging.Memory);

            allocator.CopyBufferToBuffer(context, staging.Buffer, Buffer, dataSize);
        }
        finally
        {
            staging.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _vk.DestroyBuffer(_device, Buffer, null);
        _vk.FreeMemory(_device, Memory, null);
    }
}
