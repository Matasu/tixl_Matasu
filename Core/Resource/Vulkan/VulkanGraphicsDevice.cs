#nullable enable
using System;
using System.Linq;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan.Extensions.KHR;
using T3.Core.Logging;

namespace T3.Core.Resource.Vulkan;

public sealed unsafe class VulkanGraphicsDevice : IGraphicsDevice, IDisposable
{
    public GraphicsBackend Backend => GraphicsBackend.Vulkan;
    public object NativeDevice => Device;

    public Vk Api { get; }
    public Instance Instance { get; private set; }
    public PhysicalDevice PhysicalDevice { get; private set; }
    public Device Device { get; private set; }
    public VulkanQueueFamilyIndices QueueFamilyIndices { get; private set; }
    public Queue GraphicsQueue { get; private set; }
    public Queue PresentQueue { get; private set; }

    private ExtDebugUtils? _debugUtils;
    private DebugUtilsMessengerEXT _debugMessenger;

    private readonly bool _hasSurface;

    public VulkanGraphicsDevice(IVkSurface? surface = null)
    {
        Api = Vk.GetApi();
        _hasSurface = surface != null;

        CreateInstance(surface);
        if (VulkanValidation.IsEnabled)
        {
            _debugMessenger = VulkanValidation.SetupDebugMessenger(Api, Instance, out _debugUtils);
        }
        PickPhysicalDevice();

        KhrSurface? khrSurface = null;
        SurfaceKHR? vkSurface = null;
        if (surface != null)
        {
            vkSurface = surface.Create<AllocationCallbacks>(Instance.ToHandle(), null).ToSurface();
            if (!Api.TryGetInstanceExtension(Instance, out KhrSurface khrSurfaceExt))
                throw new InvalidOperationException("Failed to get KHR_surface extension.");
            khrSurface = khrSurfaceExt;
        }

        QueueFamilyIndices = VulkanQueueFamilyIndices.FindQueueFamilies(
            Api, PhysicalDevice, vkSurface, khrSurface);

        if (!QueueFamilyIndices.IsComplete)
            throw new InvalidOperationException("Failed to find suitable queue families.");

        CreateLogicalDevice();
        GetQueues();

        Log.Debug("Vulkan device initialized successfully.");
    }

    private void CreateInstance(IVkSurface? surface)
    {
        var appInfo = new ApplicationInfo
        {
            SType = StructureType.ApplicationInfo,
            ApiVersion = Vk.Version12
        };

        var extensions = GetRequiredExtensions(surface);
        var extensionPtrs = extensions.Select(e => (nint)Marshal.StringToHGlobalAnsi(e)).ToArray();

        try
        {
            fixed (nint* extensionsPinned = extensionPtrs)
            {
                var createInfo = new InstanceCreateInfo
                {
                    SType = StructureType.InstanceCreateInfo,
                    PApplicationInfo = &appInfo,
                    EnabledExtensionCount = (uint)extensionPtrs.Length,
                    PpEnabledExtensionNames = (byte**)extensionsPinned
                };

                if (VulkanValidation.IsEnabled && VulkanValidation.CheckValidationLayerSupport(Api))
                {
                    var layerPtrs = VulkanValidation.ValidationLayers
                        .Select(l => (nint)Marshal.StringToHGlobalAnsi(l)).ToArray();
                    try
                    {
                        fixed (nint* layersPinned = layerPtrs)
                        {
                            createInfo.EnabledLayerCount = (uint)layerPtrs.Length;
                            createInfo.PpEnabledLayerNames = (byte**)layersPinned;

                            if (Api.CreateInstance(&createInfo, null, out var instance) != Result.Success)
                                throw new InvalidOperationException("Failed to create Vulkan instance.");
                            Instance = instance;
                        }
                    }
                    finally
                    {
                        foreach (var ptr in layerPtrs)
                            Marshal.FreeHGlobal(ptr);
                    }
                }
                else
                {
                    if (Api.CreateInstance(&createInfo, null, out var instance) != Result.Success)
                        throw new InvalidOperationException("Failed to create Vulkan instance.");
                    Instance = instance;
                }
            }
        }
        finally
        {
            foreach (var ptr in extensionPtrs)
                Marshal.FreeHGlobal(ptr);
        }
    }

    private void PickPhysicalDevice()
    {
        uint deviceCount = 0;
        Api.EnumeratePhysicalDevices(Instance, &deviceCount, null);

        if (deviceCount == 0)
            throw new InvalidOperationException("No Vulkan-capable GPU found.");

        var devices = new PhysicalDevice[deviceCount];
        fixed (PhysicalDevice* ptr = devices)
        {
            Api.EnumeratePhysicalDevices(Instance, &deviceCount, ptr);
        }

        // Prefer discrete GPUs
        PhysicalDevice = devices[0];
        foreach (var device in devices)
        {
            Api.GetPhysicalDeviceProperties(device, out var properties);
            if (properties.DeviceType == PhysicalDeviceType.DiscreteGpu)
            {
                PhysicalDevice = device;
                var name = Marshal.PtrToStringAnsi((nint)properties.DeviceName);
                Log.Debug($"Vulkan: Selected GPU: {name}");
                break;
            }
        }
    }

    private void CreateLogicalDevice()
    {
        var uniqueQueueFamilies = new[] { QueueFamilyIndices.GraphicsFamily!.Value, QueueFamilyIndices.PresentFamily!.Value }
            .Distinct().ToArray();

        var queueCreateInfos = new DeviceQueueCreateInfo[uniqueQueueFamilies.Length];
        var queuePriority = 1.0f;

        for (var i = 0; i < uniqueQueueFamilies.Length; i++)
        {
            queueCreateInfos[i] = new DeviceQueueCreateInfo
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = uniqueQueueFamilies[i],
                QueueCount = 1,
                PQueuePriorities = &queuePriority
            };
        }

        var deviceFeatures = new PhysicalDeviceFeatures();

        // Enable swapchain extension when a surface is provided
        var deviceExtensions = _hasSurface
            ? new[] { KhrSwapchain.ExtensionName }
            : Array.Empty<string>();
        var extensionPtrs = deviceExtensions.Select(e => (nint)Marshal.StringToHGlobalAnsi(e)).ToArray();

        try
        {
            fixed (DeviceQueueCreateInfo* queueCreateInfosPtr = queueCreateInfos)
            fixed (nint* extensionsPinned = extensionPtrs)
            {
                var createInfo = new DeviceCreateInfo
                {
                    SType = StructureType.DeviceCreateInfo,
                    QueueCreateInfoCount = (uint)queueCreateInfos.Length,
                    PQueueCreateInfos = queueCreateInfosPtr,
                    PEnabledFeatures = &deviceFeatures,
                    EnabledExtensionCount = (uint)extensionPtrs.Length,
                    PpEnabledExtensionNames = (byte**)extensionsPinned
                };

                if (Api.CreateDevice(PhysicalDevice, &createInfo, null, out var device) != Result.Success)
                    throw new InvalidOperationException("Failed to create Vulkan logical device.");

                Device = device;
            }
        }
        finally
        {
            foreach (var ptr in extensionPtrs)
                Marshal.FreeHGlobal(ptr);
        }
    }

    private void GetQueues()
    {
        Api.GetDeviceQueue(Device, QueueFamilyIndices.GraphicsFamily!.Value, 0, out var graphicsQueue);
        GraphicsQueue = graphicsQueue;

        Api.GetDeviceQueue(Device, QueueFamilyIndices.PresentFamily!.Value, 0, out var presentQueue);
        PresentQueue = presentQueue;
    }

    private static string[] GetRequiredExtensions(IVkSurface? surface)
    {
        var extensions = new System.Collections.Generic.List<string>();

        if (VulkanValidation.IsEnabled)
        {
            extensions.Add(ExtDebugUtils.ExtensionName);
        }

        if (surface != null)
        {
            var surfaceExtensions = SilkMarshal.PtrToStringArray(
                (nint)surface.GetRequiredExtensions(out var count), (int)count);
            extensions.AddRange(surfaceExtensions);
        }

        return extensions.ToArray();
    }

    public void Dispose()
    {
        Api.DeviceWaitIdle(Device);

        VulkanValidation.DestroyDebugMessenger(_debugUtils, _debugMessenger);
        Api.DestroyDevice(Device, null);
        Api.DestroyInstance(Instance, null);
        Api.Dispose();
    }
}
