#nullable enable
using System;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using T3.Core.DataTypes;
using T3.Core.Resource;
using T3.Core.Resource.ShaderCompiling;
using T3.Core.Resource.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace VulkanSmokeTest;

/// <summary>
/// E2E Vulkan smoke test (ti-80y): proves the full compute pipeline works.
/// Init device → compile shader → create resources → dispatch → readback → verify.
/// </summary>
internal static unsafe class Program
{
    private const uint ImageWidth = 64;
    private const uint ImageHeight = 64;

    private const string ComputeShaderHlsl = """
        RWTexture2D<float4> output : register(u0);

        [numthreads(8, 8, 1)]
        void main(uint3 DTid : SV_DispatchThreadID)
        {
            output[DTid.xy] = float4(1.0, 0.0, 0.0, 1.0);
        }
        """;

    static int Main()
    {
        Console.WriteLine("=== Vulkan E2E Smoke Test (ti-80y) ===");
        Console.WriteLine();

        try
        {
            // Step 1: Init VulkanGraphicsDevice (headless, no surface)
            Console.Write("[1/7] Initializing VulkanGraphicsDevice... ");
            using var device = new VulkanGraphicsDevice();
            Console.WriteLine("OK");

            var vk = device.Api;
            var dev = device.Device;

            // Step 2: Create VulkanContext and VulkanMemoryAllocator
            Console.Write("[2/7] Creating context and memory allocator... ");
            using var context = new VulkanContext(
                vk, dev, device.GraphicsQueue,
                device.QueueFamilyIndices.GraphicsFamily!.Value);
            using var allocator = new VulkanMemoryAllocator(vk, dev, device.PhysicalDevice);
            Console.WriteLine("OK");

            // Step 3: Compile trivial compute shader via VulkanShaderCompiler
            Console.Write("[3/7] Compiling compute shader via VulkanShaderCompiler... ");
            ShaderCompiler.Instance = new VulkanShaderCompiler();

            var consumer = new TempResourceConsumer(Array.Empty<IResourcePackage>());
            var args = new ShaderCompiler.ShaderCompilationArgs(
                ComputeShaderHlsl, "main", consumer, "SmokeTestCompute", null);

            if (!ShaderCompiler.TryCompileShaderFromSource<VulkanComputeShader>(
                    args, useCache: false, forceRecompile: true,
                    out var computeShader, out var compileReason))
            {
                Console.WriteLine("FAILED");
                Console.Error.WriteLine($"Shader compilation failed: {compileReason}");
                return 1;
            }

            var spirvBytes = computeShader.SpirvBytecode!;
            Console.WriteLine($"OK ({spirvBytes.Length} bytes SPIR-V)");

            // Step 4: Create storage image (RWTexture2D target)
            Console.Write("[4/7] Creating storage image and readback buffer... ");
            using var storageImage = VulkanImage.CreateTexture2D(
                vk, dev, allocator,
                ImageWidth, ImageHeight, Format.R8G8B8A8Unorm,
                ImageUsageFlags.StorageBit | ImageUsageFlags.TransferSrcBit);

            // Transition image to General layout for compute shader writes
            storageImage.TransitionLayout(context, ImageLayout.Undefined, ImageLayout.General);

            // Create host-visible readback buffer (4 bytes per pixel: RGBA8)
            var readbackSize = (ulong)(ImageWidth * ImageHeight * 4);
            using var readbackBuffer = VulkanBuffer.Create(
                vk, dev, allocator, readbackSize,
                BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
            Console.WriteLine("OK");

            // Step 5: Create descriptor set and compute pipeline
            Console.Write("[5/7] Creating descriptor set and compute pipeline... ");
            var poolSizes = new DescriptorPoolSize[]
            {
                new() { Type = DescriptorType.StorageImage, DescriptorCount = 1 }
            };
            using var descriptorPool = VulkanDescriptorPool.Create(vk, dev, poolSizes, maxSets: 1);

            var binding = new DescriptorSetLayoutBinding
            {
                Binding = 0,
                DescriptorType = DescriptorType.StorageImage,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.ComputeBit
            };
            var setLayout = descriptorPool.CreateLayout(new ReadOnlySpan<DescriptorSetLayoutBinding>(&binding, 1));
            var descriptorSet = descriptorPool.AllocateSet(setLayout);

            // Bind storage image to descriptor set
            descriptorPool.UpdateStorageImageBinding(descriptorSet, 0, storageImage.View);

            // Create compute pipeline
            using var pipeline = VulkanPipeline.CreateCompute(
                vk, dev, spirvBytes, "main",
                new ReadOnlySpan<DescriptorSetLayout>(&setLayout, 1));
            Console.WriteLine("OK");

            // Step 6: Record and submit compute dispatch
            Console.Write("[6/7] Recording and submitting compute dispatch... ");
            {
                var cmdBuf = context.BeginSingleTimeCommands();

                vk.CmdBindPipeline(cmdBuf, PipelineBindPoint.Compute, pipeline.Pipeline);

                var set = descriptorSet;
                vk.CmdBindDescriptorSets(cmdBuf, PipelineBindPoint.Compute,
                    pipeline.Layout, 0, 1, &set, 0, null);

                // Dispatch: 64/8 = 8 groups per dimension
                vk.CmdDispatch(cmdBuf, ImageWidth / 8, ImageHeight / 8, 1);

                // Barrier: compute write → transfer read
                var imageBarrier = new ImageMemoryBarrier
                {
                    SType = StructureType.ImageMemoryBarrier,
                    OldLayout = ImageLayout.General,
                    NewLayout = ImageLayout.TransferSrcOptimal,
                    SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                    DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                    Image = storageImage.Image,
                    SubresourceRange = new ImageSubresourceRange
                    {
                        AspectMask = ImageAspectFlags.ColorBit,
                        BaseMipLevel = 0,
                        LevelCount = 1,
                        BaseArrayLayer = 0,
                        LayerCount = 1
                    },
                    SrcAccessMask = AccessFlags.ShaderWriteBit,
                    DstAccessMask = AccessFlags.TransferReadBit
                };

                vk.CmdPipelineBarrier(cmdBuf,
                    PipelineStageFlags.ComputeShaderBit,
                    PipelineStageFlags.TransferBit,
                    0,
                    0, null,
                    0, null,
                    1, &imageBarrier);

                // Copy image to readback buffer
                var copyRegion = new BufferImageCopy
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
                    ImageExtent = new Extent3D(ImageWidth, ImageHeight, 1)
                };

                vk.CmdCopyImageToBuffer(cmdBuf, storageImage.Image,
                    ImageLayout.TransferSrcOptimal, readbackBuffer.Buffer, 1, &copyRegion);

                context.EndSingleTimeCommands(cmdBuf);
            }
            Console.WriteLine("OK");

            // Step 7: Read back and verify pixels
            Console.Write("[7/7] Verifying readback pixels... ");
            {
                void* mapped;
                if (vk.MapMemory(dev, readbackBuffer.Memory, 0, readbackSize, 0, &mapped) != Result.Success)
                {
                    Console.WriteLine("FAILED");
                    Console.Error.WriteLine("Failed to map readback buffer memory.");
                    return 1;
                }

                var pixels = new Span<byte>(mapped, (int)readbackSize);
                var totalPixels = (int)(ImageWidth * ImageHeight);
                var failedPixels = 0;

                for (var i = 0; i < totalPixels; i++)
                {
                    var offset = i * 4;
                    var r = pixels[offset];
                    var g = pixels[offset + 1];
                    var b = pixels[offset + 2];
                    var a = pixels[offset + 3];

                    // Expect red: R=255, G=0, B=0, A=255
                    if (r != 255 || g != 0 || b != 0 || a != 255)
                    {
                        if (failedPixels == 0)
                        {
                            Console.WriteLine("FAILED");
                            Console.Error.WriteLine(
                                $"Pixel [{i % ImageWidth},{i / ImageWidth}]: " +
                                $"expected (255,0,0,255), got ({r},{g},{b},{a})");
                        }
                        failedPixels++;
                    }
                }

                vk.UnmapMemory(dev, readbackBuffer.Memory);

                if (failedPixels > 0)
                {
                    Console.Error.WriteLine($"Total failed pixels: {failedPixels}/{totalPixels}");
                    return 1;
                }

                Console.WriteLine($"OK ({totalPixels} pixels verified)");
            }

            Console.WriteLine();
            Console.WriteLine("PASS: Vulkan E2E smoke test completed successfully.");
            Console.WriteLine("  - VulkanGraphicsDevice initialized");
            Console.WriteLine("  - Compute shader compiled via VulkanShaderCompiler");
            Console.WriteLine("  - Storage image and buffer created");
            Console.WriteLine("  - Compute dispatch recorded and submitted");
            Console.WriteLine($"  - All {ImageWidth}x{ImageHeight} pixels verified as red (255,0,0,255)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAILED");
            Console.Error.WriteLine($"Unhandled exception: {ex}");
            return 1;
        }
    }
}
