#nullable enable
using Silk.NET.Vulkan;

namespace T3.Core.Resource.Vulkan;

public readonly struct VulkanQueueFamilyIndices
{
    public uint? GraphicsFamily { get; init; }
    public uint? PresentFamily { get; init; }

    public bool IsComplete => GraphicsFamily.HasValue && PresentFamily.HasValue;

    public static unsafe VulkanQueueFamilyIndices FindQueueFamilies(
        Vk vk,
        PhysicalDevice physicalDevice,
        SurfaceKHR? surface = null,
        KhrSurface? khrSurface = null)
    {
        uint queueFamilyCount = 0;
        vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &queueFamilyCount, null);

        var queueFamilies = new QueueFamilyProperties[queueFamilyCount];
        fixed (QueueFamilyProperties* ptr = queueFamilies)
        {
            vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &queueFamilyCount, ptr);
        }

        uint? graphicsFamily = null;
        uint? presentFamily = null;

        for (uint i = 0; i < queueFamilyCount; i++)
        {
            if ((queueFamilies[i].QueueFlags & QueueFlags.GraphicsBit) != 0)
            {
                graphicsFamily = i;
            }

            if (surface.HasValue && khrSurface != null)
            {
                khrSurface.GetPhysicalDeviceSurfaceSupport(physicalDevice, i, surface.Value, out var presentSupport);
                if (presentSupport)
                {
                    presentFamily = i;
                }
            }
            else
            {
                // Headless mode: use graphics queue for present
                presentFamily ??= graphicsFamily;
            }

            if (graphicsFamily.HasValue && presentFamily.HasValue)
                break;
        }

        return new VulkanQueueFamilyIndices
        {
            GraphicsFamily = graphicsFamily,
            PresentFamily = presentFamily
        };
    }
}
