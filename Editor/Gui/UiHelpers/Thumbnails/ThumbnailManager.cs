#nullable enable

using System.IO;
using System.Threading.Tasks;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.Mathematics.Interop;
using SharpDX.WIC;
using T3.Core.Model;
using T3.Core.Resource;
using T3.Editor.Gui.Windows.RenderExport;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.UiHelpers.Thumbnails;

internal static class ThumbnailManager
{
    /// <summary>
    /// Updates the atlas subresource on the main thread.
    /// Call this once per frame from the Editor update loop.
    /// </summary>
    internal static void Update()
    {
        if (!_initialized)
            Initialize();

        lock (_uploadQueue)
        {
            var deviceContext = ResourceManager.Device.ImmediateContext;
            while (_uploadQueue.Count > 0)
            {
                var upload = _uploadQueue.Dequeue();

                var destRegion = new ResourceRegion
                                     {
                                         Left = upload.Slot.X * SlotWidth + Padding,
                                         Top = upload.Slot.Y * SlotHeight + Padding,
                                         Right = upload.Slot.X * SlotWidth + SlotWidth - Padding,
                                         Bottom = upload.Slot.Y * SlotHeight + SlotHeight - Padding,
                                         Front = 0, Back = 1
                                     };

                // Fast GPU copy to atlas
                deviceContext.CopySubresourceRegion(upload.Texture, 0, null, _atlas, 0, destRegion.Left, destRegion.Top);

                if (_slots.TryGetValue(upload.Guid, out var slot))
                    slot.IsLoading = false;

                upload.Texture.Dispose();
            }
        }
    }

    internal sealed record ThumbnailRect(Vector2 Min, Vector2 Max, bool IsReady);

    internal static ShaderResourceView? AtlasSrv { get; private set; }


    /// <summary>
    /// Scales a texture to 4:3, crops the center, and saves it to the package's thumbnail cache.
    /// </summary>
    internal static void SaveThumbnail(Guid guid, SymbolPackage package, T3.Core.DataTypes.Texture2D sourceTexture)
    {
        var device = ResourceManager.Device;
        var context = device.ImmediateContext;

        // 1. Prepare directory: SymbolPackage.Folder/.temp/thumbnails/
        var thumbDir = Path.Combine(package.Folder, ".temp", "thumbnails");
        try
        {
            Directory.CreateDirectory(thumbDir);
        }
        catch
        {
            return;
        }

        var filePath = Path.Combine(thumbDir, $"{guid}.png");

        // 2. Setup 4:3 target (256x192 is a solid standard for thumbnails)
        const int targetWidth = SlotWidth;
        const int targetHeight = SlotHeight;

        var desc = new Texture2DDescription()
                       {
                           Width = targetWidth,
                           Height = targetHeight,
                           ArraySize = 1,
                           BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                           Format = SharpDX.DXGI.Format.R8G8B8A8_UNorm,
                           Usage = ResourceUsage.Default,
                           SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                           MipLevels = 1
                       };

        using var tempTarget = new SharpDX.Direct3D11.Texture2D(device, desc);
        using var rtv = new RenderTargetView(device, tempTarget);
        var sourceSrv = SrvManager.GetSrvForTexture(sourceTexture); //

        // 3. Aspect Ratio Logic: Calculate Viewport for Center-Crop
        var sourceAspect = (float)sourceTexture.Description.Width / sourceTexture.Description.Height;
        var targetAspect = (float)targetWidth / targetHeight;

        float viewWidth = targetWidth;
        float viewHeight = targetHeight;
        float offsetX = 0;
        float offsetY = 0;

        if (sourceAspect > targetAspect) // Source is wider: crop sides
        {
            viewWidth = targetHeight * sourceAspect;
            offsetX = (targetWidth - viewWidth) / 2f;
        }
        else // Source is taller: crop top/bottom
        {
            viewHeight = targetWidth / sourceAspect;
            offsetY = (targetHeight - viewHeight) / 2f;
        }

        // 4. GPU Render (Scale and Crop)
        context.OutputMerger.SetTargets(rtv);
        context.Rasterizer.SetViewport(new ViewportF(offsetX, offsetY, viewWidth, viewHeight));
        context.ClearRenderTargetView(rtv, new RawColor4(0, 0, 0, 1));

        context.VertexShader.Set(SharedResources.FullScreenVertexShaderResource.Value); //
        context.PixelShader.Set(SharedResources.FullScreenPixelShaderResource.Value); //
        context.PixelShader.SetShaderResource(0, sourceSrv);

        context.Draw(3, 0); // Full-screen triangle trick

        // Cleanup GPU state
        context.PixelShader.SetShaderResource(0, null);
        context.OutputMerger.SetTargets((RenderTargetView?)null);

        // 5. Save to Disk using ScreenshotWriter's async pipeline
        var newTexture = new T3.Core.DataTypes.Texture2D(tempTarget);
        ScreenshotWriter.StartSavingToFile(newTexture, filePath, ScreenshotWriter.FileFormats.Png); //
    }

    /// <summary>
    /// Returns UV coordinates for a thumbnail. Triggers async load if not cached.
    /// </summary>
    internal static ThumbnailRect GetThumbnail(Guid guid, SymbolPackage? package)
    {
        if (package == null)
            return _waiting;


        
        if (!_slots.TryGetValue(guid, out var slot))
        {
            // Check for file existence BEFORE creating a slot
            var path = Path.Combine(package.Folder, ".temp", "thumbnails", $"{guid}.png");
            if (!File.Exists(path)) 
                return _waiting;

            RequestAsyncLoad(guid, package);
            return _waiting;
        }        
        
        slot.LastUsed = DateTime.Now;

        // Calculate UVs with the 2px padding offset
        var x = (float)(slot.X * SlotWidth + Padding) / AtlasSize;
        var y = (float)(slot.Y * SlotHeight + Padding) / AtlasSize;
        var w = (float)(SlotWidth - Padding * 2) / AtlasSize;
        var h = (float)(SlotHeight - Padding * 2) / AtlasSize;

        return new ThumbnailRect(new Vector2(x, y), new Vector2(x + w, y + h), !slot.IsLoading);
    }

    private static readonly ThumbnailRect _waiting = new(Vector2.Zero, Vector2.Zero, false);

    private static async void RequestAsyncLoad(Guid guid, SymbolPackage package)
    {
        try
        {
            // Path: SymbolPackage.Folder/.temp/thumbnails/
            var path = Path.Combine(package.Folder, ".temp", "thumbnails", $"{guid}.png");
            if (!File.Exists(path)) return;

            var targetSlot = GetLruSlot(guid);

            // Manual WIC decode to resolve 'FromFile' error
            var tex = await LoadTextureViaWic(path);

            lock (_uploadQueue)
            {
                _uploadQueue.Enqueue(new PendingUpload(guid, tex, targetSlot));
            }
        }
        catch (Exception e)
        {
            T3.Core.Logging.Log.Error($"Thumbnail load failed for {guid}: {e.Message}");
            _slots.Remove(guid);
        }
    }

    private static async Task<SharpDX.Direct3D11.Texture2D> LoadTextureViaWic(string path)
    {
        return await Task.Run(() =>
                              {
                                  // Use WIC for decoding (Same logic used in ScreenshotWriter.cs)
                                  using var factory = new ImagingFactory();
                                  using var decoder = new BitmapDecoder(factory, path, DecodeOptions.CacheOnDemand);
                                  using var frame = decoder.GetFrame(0);
                                  using var converter = new FormatConverter(factory);

                                  converter.Initialize(frame, PixelFormat.Format32bppRGBA);

                                  var stride = converter.Size.Width * 4;
                                  using var buffer = new SharpDX.DataStream(converter.Size.Height * stride, true, true);
                                  converter.CopyPixels(stride, buffer);

                                  return new SharpDX.Direct3D11.Texture2D(ResourceManager.Device, new Texture2DDescription()
                                                                                                      {
                                                                                                          Width = converter.Size.Width,
                                                                                                          Height = converter.Size.Height,
                                                                                                          ArraySize = 1,
                                                                                                          BindFlags = BindFlags.ShaderResource,
                                                                                                          Usage = ResourceUsage.Immutable,
                                                                                                          Format = SharpDX.DXGI.Format.R8G8B8A8_UNorm,
                                                                                                          MipLevels = 1,
                                                                                                          SampleDescription =
                                                                                                              new SharpDX.DXGI.SampleDescription(1, 0),
                                                                                                      }, new SharpDX.DataRectangle(buffer.DataPointer, stride));
                              });
    }

    private static ThumbnailSlot GetLruSlot(Guid guid)
    {
        // LRU Eviction
        if (_slots.Count >= MaxSlots)
        {
            var oldest = _slots.Values.OrderBy(s => s.LastUsed).First();
            _slots.Remove(oldest.Guid);
            oldest.Guid = guid;
            oldest.IsLoading = true;
            oldest.LastUsed = DateTime.Now;
            _slots[guid] = oldest;
            return oldest;
        }

        var newSlot = new ThumbnailSlot
                          {
                              Guid = guid,
                              X = _slots.Count % 23,
                              Y = _slots.Count / 23,
                              IsLoading = true,
                              LastUsed = DateTime.Now
                          };
        _slots[guid] = newSlot;
        return newSlot;
    }

    private static void Initialize()
    {
        var device = ResourceManager.Device;
        var desc = new Texture2DDescription
                       {
                           Width = AtlasSize,
                           Height = AtlasSize,
                           MipLevels = 1,
                           ArraySize = 1,
                           Format = SharpDX.DXGI.Format.R8G8B8A8_UNorm,
                           SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                           Usage = ResourceUsage.Default,
                           BindFlags = BindFlags.ShaderResource,
                           CpuAccessFlags = CpuAccessFlags.None
                       };

        _atlas = new SharpDX.Direct3D11.Texture2D(device, desc);
        AtlasSrv = new ShaderResourceView(device, _atlas);
        _initialized = true;
    }

    
    // --- Configuration ---
    private const int AtlasSize = 4096;
    private const int SlotWidth = 178; // Results in ~23 columns
    private const int SlotHeight = 133; // 4:3 ratio (178 * 0.75)
    private const int Padding = 2; // 2px gap to avoid bleeding
    private const int MaxSlots = 500; //

    // --- Internal Structures ---
    private record struct PendingUpload(Guid Guid, SharpDX.Direct3D11.Texture2D Texture, ThumbnailSlot Slot);

    private sealed class ThumbnailSlot
    {
        public Guid Guid = Guid.Empty;
        public int X;
        public int Y;
        public DateTime LastUsed;
        public bool IsLoading;
    }

    // --- State ---
    private static SharpDX.Direct3D11.Texture2D? _atlas;

    private static readonly Dictionary<Guid, ThumbnailSlot> _slots = new();
    private static readonly Queue<PendingUpload> _uploadQueue = new();

    private static bool _initialized;
}