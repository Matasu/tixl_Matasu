#nullable enable
using System;
using System.Collections.Generic;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace T3.Core.Resource.Vulkan;

/// <summary>
/// Manages Vulkan descriptor pools, descriptor set layouts,
/// and descriptor set allocation/updates.
/// </summary>
public sealed unsafe class VulkanDescriptorPool : IDisposable
{
    public DescriptorPool Pool { get; }

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly List<DescriptorSetLayout> _layouts = new();
    private bool _disposed;

    private VulkanDescriptorPool(Vk vk, Device device, DescriptorPool pool)
    {
        _vk = vk;
        _device = device;
        Pool = pool;
    }

    public static VulkanDescriptorPool Create(
        Vk vk, Device device,
        ReadOnlySpan<DescriptorPoolSize> poolSizes,
        uint maxSets = 64)
    {
        fixed (DescriptorPoolSize* sizesPtr = poolSizes)
        {
            var poolInfo = new DescriptorPoolCreateInfo
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                PoolSizeCount = (uint)poolSizes.Length,
                PPoolSizes = sizesPtr,
                MaxSets = maxSets,
                Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit
            };

            if (vk.CreateDescriptorPool(device, &poolInfo, null, out var pool) != Result.Success)
                throw new InvalidOperationException("Failed to create Vulkan descriptor pool.");

            return new VulkanDescriptorPool(vk, device, pool);
        }
    }

    public DescriptorSetLayout CreateLayout(
        ReadOnlySpan<DescriptorSetLayoutBinding> bindings)
    {
        fixed (DescriptorSetLayoutBinding* bindingsPtr = bindings)
        {
            var layoutInfo = new DescriptorSetLayoutCreateInfo
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = (uint)bindings.Length,
                PBindings = bindingsPtr
            };

            if (_vk.CreateDescriptorSetLayout(_device, &layoutInfo, null, out var layout) != Result.Success)
                throw new InvalidOperationException("Failed to create descriptor set layout.");

            _layouts.Add(layout);
            return layout;
        }
    }

    public DescriptorSet AllocateSet(DescriptorSetLayout layout)
    {
        var allocInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = Pool,
            DescriptorSetCount = 1,
            PSetLayouts = &layout
        };

        if (_vk.AllocateDescriptorSets(_device, &allocInfo, out var descriptorSet) != Result.Success)
            throw new InvalidOperationException("Failed to allocate descriptor set.");

        return descriptorSet;
    }

    public void FreeSet(DescriptorSet set)
    {
        _vk.FreeDescriptorSets(_device, Pool, 1, &set);
    }

    public void UpdateBufferBinding(DescriptorSet set, uint binding,
        Buffer buffer, ulong size, ulong offset = 0,
        DescriptorType type = DescriptorType.StorageBuffer)
    {
        var bufferInfo = new DescriptorBufferInfo
        {
            Buffer = buffer,
            Offset = offset,
            Range = size
        };

        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = binding,
            DstArrayElement = 0,
            DescriptorType = type,
            DescriptorCount = 1,
            PBufferInfo = &bufferInfo
        };

        _vk.UpdateDescriptorSets(_device, 1, &write, 0, null);
    }

    public void UpdateImageBinding(DescriptorSet set, uint binding,
        ImageView imageView, Sampler sampler,
        ImageLayout layout = ImageLayout.ShaderReadOnlyOptimal,
        DescriptorType type = DescriptorType.CombinedImageSampler)
    {
        var imageInfo = new DescriptorImageInfo
        {
            ImageLayout = layout,
            ImageView = imageView,
            Sampler = sampler
        };

        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = binding,
            DstArrayElement = 0,
            DescriptorType = type,
            DescriptorCount = 1,
            PImageInfo = &imageInfo
        };

        _vk.UpdateDescriptorSets(_device, 1, &write, 0, null);
    }

    public void UpdateStorageImageBinding(DescriptorSet set, uint binding,
        ImageView imageView, ImageLayout layout = ImageLayout.General)
    {
        var imageInfo = new DescriptorImageInfo
        {
            ImageLayout = layout,
            ImageView = imageView
        };

        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = binding,
            DstArrayElement = 0,
            DescriptorType = DescriptorType.StorageImage,
            DescriptorCount = 1,
            PImageInfo = &imageInfo
        };

        _vk.UpdateDescriptorSets(_device, 1, &write, 0, null);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (var layout in _layouts)
        {
            _vk.DestroyDescriptorSetLayout(_device, layout, null);
        }
        _layouts.Clear();
        _vk.DestroyDescriptorPool(_device, Pool, null);
    }
}
