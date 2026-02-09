#nullable enable
using Device = SharpDX.Direct3D11.Device;

namespace T3.Core.Resource;

public sealed class DX11GraphicsDevice : IGraphicsDevice
{
    public DX11GraphicsDevice(Device device)
    {
        NativeDX11Device = device;
    }

    public GraphicsBackend Backend => GraphicsBackend.DX11;

    public object NativeDevice => NativeDX11Device;

    public Device NativeDX11Device { get; }
}
