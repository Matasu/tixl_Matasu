#nullable enable

namespace T3.Core.Resource;

public enum GraphicsBackend
{
    DX11,
    Vulkan
}

public interface IGraphicsDevice
{
    GraphicsBackend Backend { get; }
    object NativeDevice { get; }
}
