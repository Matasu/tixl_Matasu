#nullable enable
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using T3.Core.Logging;

namespace T3.Core.Resource.Vulkan;

public static class VulkanValidation
{
    public static readonly string[] ValidationLayers = { "VK_LAYER_KHRONOS_validation" };

    public static bool IsEnabled
    {
        get
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }

    public static unsafe bool CheckValidationLayerSupport(Vk vk)
    {
        uint layerCount = 0;
        vk.EnumerateInstanceLayerProperties(&layerCount, null);

        var availableLayers = new LayerProperties[layerCount];
        fixed (LayerProperties* ptr = availableLayers)
        {
            vk.EnumerateInstanceLayerProperties(&layerCount, ptr);
        }

        foreach (var layerName in ValidationLayers)
        {
            var found = false;
            foreach (var layer in availableLayers)
            {
                var name = Marshal.PtrToStringAnsi((nint)layer.LayerName);
                if (name == layerName)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
                return false;
        }

        return true;
    }

    public static unsafe DebugUtilsMessengerEXT SetupDebugMessenger(
        Vk vk,
        Instance instance,
        out ExtDebugUtils? debugUtils)
    {
        debugUtils = null;

        if (!IsEnabled)
            return default;

        if (!vk.TryGetInstanceExtension(instance, out ExtDebugUtils utils))
        {
            Log.Warning("Vulkan: Failed to get debug utils extension.");
            return default;
        }

        debugUtils = utils;

        var createInfo = new DebugUtilsMessengerCreateInfoEXT
        {
            SType = StructureType.DebugUtilsMessengerCreateInfoExt,
            MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.WarningBitExt
                            | DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt,
            MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt
                        | DebugUtilsMessageTypeFlagsEXT.ValidationBitExt
                        | DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt,
            PfnUserCallback = (DebugUtilsMessengerCallbackFunctionEXT)DebugCallback
        };

        utils.CreateDebugUtilsMessenger(instance, &createInfo, null, out var messenger);
        return messenger;
    }

    public static unsafe void DestroyDebugMessenger(
        ExtDebugUtils? debugUtils,
        DebugUtilsMessengerEXT messenger)
    {
        if (debugUtils == null || messenger.Handle == 0)
            return;

        debugUtils.DestroyDebugUtilsMessenger(messenger, null);
    }

    private static unsafe uint DebugCallback(
        DebugUtilsMessageSeverityFlagsEXT messageSeverity,
        DebugUtilsMessageTypeFlagsEXT messageTypes,
        DebugUtilsMessengerCallbackDataEXT* pCallbackData,
        void* pUserData)
    {
        var message = Marshal.PtrToStringAnsi((nint)pCallbackData->PMessage) ?? "(null)";

        if ((messageSeverity & DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt) != 0)
        {
            Log.Error($"Vulkan Validation: {message}");
        }
        else if ((messageSeverity & DebugUtilsMessageSeverityFlagsEXT.WarningBitExt) != 0)
        {
            Log.Warning($"Vulkan Validation: {message}");
        }
        else
        {
            Log.Debug($"Vulkan: {message}");
        }

        return Vk.False;
    }
}
