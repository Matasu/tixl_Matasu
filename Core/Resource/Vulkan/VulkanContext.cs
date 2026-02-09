#nullable enable
using System;
using Silk.NET.Vulkan;
using T3.Core.Logging;

namespace T3.Core.Resource.Vulkan;

public sealed unsafe class VulkanContext : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly Queue _graphicsQueue;

    public CommandPool CommandPool { get; }

    public VulkanContext(Vk vk, Device device, Queue graphicsQueue, uint graphicsQueueFamily)
    {
        _vk = vk;
        _device = device;
        _graphicsQueue = graphicsQueue;

        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = graphicsQueueFamily,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit
        };

        if (_vk.CreateCommandPool(_device, &poolInfo, null, out var pool) != Result.Success)
            throw new InvalidOperationException("Failed to create Vulkan command pool.");

        CommandPool = pool;
    }

    public CommandBuffer AllocateCommandBuffer(CommandBufferLevel level = CommandBufferLevel.Primary)
    {
        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = CommandPool,
            Level = level,
            CommandBufferCount = 1
        };

        if (_vk.AllocateCommandBuffers(_device, &allocInfo, out var commandBuffer) != Result.Success)
            throw new InvalidOperationException("Failed to allocate Vulkan command buffer.");

        return commandBuffer;
    }

    public CommandBuffer BeginSingleTimeCommands()
    {
        var commandBuffer = AllocateCommandBuffer();

        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        _vk.BeginCommandBuffer(commandBuffer, &beginInfo);
        return commandBuffer;
    }

    public void EndSingleTimeCommands(CommandBuffer commandBuffer)
    {
        _vk.EndCommandBuffer(commandBuffer);

        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer
        };

        _vk.QueueSubmit(_graphicsQueue, 1, &submitInfo, default);
        _vk.QueueWaitIdle(_graphicsQueue);

        _vk.FreeCommandBuffers(_device, CommandPool, 1, &commandBuffer);
    }

    public Fence CreateFence(bool signaled = false)
    {
        var fenceInfo = new FenceCreateInfo
        {
            SType = StructureType.FenceCreateInfo,
            Flags = signaled ? FenceCreateFlags.SignaledBit : 0
        };

        if (_vk.CreateFence(_device, &fenceInfo, null, out var fence) != Result.Success)
            throw new InvalidOperationException("Failed to create Vulkan fence.");

        return fence;
    }

    public Semaphore CreateSemaphore()
    {
        var semaphoreInfo = new SemaphoreCreateInfo
        {
            SType = StructureType.SemaphoreCreateInfo
        };

        if (_vk.CreateSemaphore(_device, &semaphoreInfo, null, out var semaphore) != Result.Success)
            throw new InvalidOperationException("Failed to create Vulkan semaphore.");

        return semaphore;
    }

    public void Dispose()
    {
        _vk.DestroyCommandPool(_device, CommandPool, null);
    }
}
