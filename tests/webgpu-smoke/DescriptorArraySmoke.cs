using Godot;

internal static class DescriptorArraySmoke
{
    public static void Run()
    {
        using var rd = RenderingServer.CreateLocalRenderingDevice();
        using var source = new RDShaderSource { SourceCompute = """
            #version 450
            layout(local_size_x=1,local_size_y=1,local_size_z=1) in;
            layout(r32ui,set=0,binding=0) uniform readonly uimage2D source_images[2];
            layout(r32ui,set=0,binding=1) uniform writeonly uimage2D dest_images[2];
            void main() {
                uint i = gl_GlobalInvocationID.x;
                uvec4 payload = imageLoad(source_images[i], ivec2(0));
                imageStore(dest_images[i], ivec2(0), payload);
                float value = uintBitsToFloat(payload.r);
                vec4 vector_value = vec4(value);
                uint scalar_flags = isnan(value) ? 1u : (isinf(value) ? 2u : 0u);
                uint vector_flags = all(isnan(vector_value)) ? 1u : (all(isinf(vector_value)) ? 2u : 0u);
                imageStore(dest_images[i], ivec2(1, 0), uvec4(scalar_flags | (vector_flags << 8u)));
            }
            """ };
        using var spirv = rd.ShaderCompileSpirVFromSource(source);
        if (spirv.CompileErrorCompute.Length != 0) throw new Exception(spirv.CompileErrorCompute);
        var shader = rd.ShaderCreateFromSpirV(spirv);
        var pipeline = rd.ComputePipelineCreate(shader);
        using var format = new RDTextureFormat
        {
            Width = 2, Height = 1, Format = RenderingDevice.DataFormat.R32Uint,
            UsageBits = RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.CanUpdateBit | RenderingDevice.TextureUsageBits.CanCopyFromBit,
        };
        using var view = new RDTextureView();
        var textures = new Rid[4];
        uint[] expected = [0x7fc00000u, 0xff800000u];
        for (int i = 0; i < 4; i++) textures[i] = rd.TextureCreate(format, view, [[.. BitConverter.GetBytes(i < 2 ? expected[i] : 0u), 0, 0, 0, 0]]);
        using var src = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = 0 };
        using var dst = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = 1 };
        src.AddId(textures[0]); src.AddId(textures[1]); dst.AddId(textures[2]); dst.AddId(textures[3]);
        var uniforms = rd.UniformSetCreate([src, dst], shader, 0);
        var list = rd.ComputeListBegin();
        rd.ComputeListBindComputePipeline(list, pipeline);
        rd.ComputeListBindUniformSet(list, uniforms, 0);
        rd.ComputeListDispatch(list, 2, 1, 1);
        rd.ComputeListEnd();
        rd.Submit(); rd.Sync();
        for (int i = 0; i < 2; i++)
        {
            var data = rd.TextureGetData(textures[2 + i], 0);
            if (data.Length != 8 || BitConverter.ToUInt32(data) != expected[i] || BitConverter.ToUInt32(data.AsSpan(4)) != (uint)((i + 1) * 257))
                throw new Exception($"Descriptor array element {i} did not preserve its own texture data: {Convert.ToHexString(data)}.");
        }
        rd.FreeRid(uniforms); rd.FreeRid(pipeline); rd.FreeRid(shader);
        foreach (var texture in textures) rd.FreeRid(texture);
        using var volumeFormat = new RDTextureFormat
        {
            TextureType = RenderingDevice.TextureType.Type3D,
            Width = 4, Height = 4, Depth = 4, Mipmaps = 3,
            Format = RenderingDevice.DataFormat.R32Uint,
            UsageBits = RenderingDevice.TextureUsageBits.CanCopyFromBit | RenderingDevice.TextureUsageBits.CanUpdateBit,
        };
        byte[] volumePixels = Enumerable.Range(1, 64 + 8 + 1).SelectMany(i => BitConverter.GetBytes((uint)i)).ToArray();
        var volume = rd.TextureCreate(volumeFormat, view, [volumePixels]);
        var volumeReadback = rd.TextureGetData(volume, 0);
        rd.FreeRid(volume);
        if (!volumeReadback.SequenceEqual(volumePixels))
            throw new Exception("3D texture readback did not preserve every depth slice and mipmap.");
        foreach (var promotedFormat in new[] { RenderingDevice.DataFormat.R16Uint, RenderingDevice.DataFormat.R16Sfloat })
        {
            volumeFormat.Format = promotedFormat;
            volumeFormat.UsageBits |= RenderingDevice.TextureUsageBits.StorageBit;
            byte[] promotedPixels = Enumerable.Range(1, 64 + 8 + 1)
                .SelectMany(i => BitConverter.GetBytes(promotedFormat == RenderingDevice.DataFormat.R16Uint ? (ushort)(40000 + i) : BitConverter.HalfToUInt16Bits((Half)(i / 8.0f))))
                .ToArray();
            var promotedVolume = rd.TextureCreate(volumeFormat, view, [promotedPixels]);
            var promotedReadback = rd.TextureGetData(promotedVolume, 0);
            rd.FreeRid(promotedVolume);
            if (!promotedReadback.SequenceEqual(promotedPixels))
                throw new Exception($"{promotedFormat} storage texture transfer changed: expected {Convert.ToHexString(promotedPixels)}, actual {Convert.ToHexString(promotedReadback)}.");
        }
        StorageTextureFeedback(rd, view);
        foreach (bool renderAttachment in new[] { false, true })
        {
            using var msaaFormat = new RDTextureFormat
            {
                Width = 8, Height = 8, Format = RenderingDevice.DataFormat.R8G8B8A8Unorm,
                Samples = RenderingDevice.TextureSamples.Samples4,
                UsageBits = RenderingDevice.TextureUsageBits.ColorAttachmentBit | RenderingDevice.TextureUsageBits.CanCopyFromBit | RenderingDevice.TextureUsageBits.CanCopyToBit,
            };
            var multisampled = rd.TextureCreate(msaaFormat, view);
            msaaFormat.Samples = RenderingDevice.TextureSamples.Samples1;
            if (!renderAttachment) msaaFormat.UsageBits &= ~RenderingDevice.TextureUsageBits.ColorAttachmentBit;
            var resolved = rd.TextureCreate(msaaFormat, view);
            rd.TextureClear(multisampled, new Color(0.2f, 0.4f, 0.8f, 1), 0, 1, 0, 1);
            if (rd.TextureResolveMultisample(multisampled, resolved) != Error.Ok)
                throw new Exception("Could not resolve a multisampled color texture.");
            rd.Submit(); rd.Sync();
            var resolvedPixels = rd.TextureGetData(resolved, 0);
            rd.FreeRid(multisampled); rd.FreeRid(resolved);
            byte[] expectedColor = [51, 102, 204, 255];
            if (resolvedPixels.Length != 8 * 8 * 4 || resolvedPixels.Where((value, index) => value != expectedColor[index % 4]).Any())
                throw new Exception($"MSAA resolve lost color data (render attachment: {renderAttachment}).");
        }
        Console.WriteLine("NATIVE_MSAA_RESOLVE_PASSED");
        rd.Free();
        Console.WriteLine("NATIVE_DESCRIPTOR_ARRAY_AND_FLOAT_CLASSIFICATION_PASSED");
    }

    private static void StorageTextureFeedback(RenderingDevice rd, RDTextureView view)
    {
        // RG32 always needs the shadow fallback; RGBA16 also tests native access on tier2 devices.
        foreach (bool wide in new[] { false, true })
        foreach (bool volume in new[] { false, true })
        {
            using var source = new RDShaderSource { SourceCompute = """
                #version 450
                layout(local_size_x=1,local_size_y=1,local_size_z=1) in;
                layout(IMAGE_FORMAT,set=0,binding=0) uniform IMAGE_TYPE values;
                layout(push_constant,std430) uniform Params { int factor; } params;
                void main() {
                    ivec3 coordinate = ivec3(gl_GlobalInvocationID);
                    imageStore(values, coordinate, imageLoad(values, coordinate) + ivec4(1, 2, 3, 4) * params.factor);
                }
                """.Replace("IMAGE_TYPE", volume ? "iimage3D" : "iimage2DArray")
                    .Replace("IMAGE_FORMAT", wide ? "rg32i" : "rgba16i") };
            using var spirv = rd.ShaderCompileSpirVFromSource(source);
            if (spirv.CompileErrorCompute.Length != 0) throw new Exception(spirv.CompileErrorCompute);
            var shader = rd.ShaderCreateFromSpirV(spirv);
            var pipeline = rd.ComputePipelineCreate(shader);
            using var format = new RDTextureFormat
            {
                TextureType = volume ? RenderingDevice.TextureType.Type3D : RenderingDevice.TextureType.Type2DArray,
                Width = 2, Height = 1, Depth = volume ? 2u : 1u, ArrayLayers = volume ? 1u : 2u,
                Format = wide ? RenderingDevice.DataFormat.R32G32Sint : RenderingDevice.DataFormat.R16G16B16A16Sint,
                UsageBits = RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.CanUpdateBit | RenderingDevice.TextureUsageBits.CanCopyFromBit,
            };
            byte[] initialLayer = wide
                ? Enumerable.Repeat(7, volume ? 8 : 4).SelectMany(BitConverter.GetBytes).ToArray()
                : Enumerable.Repeat((short)7, volume ? 16 : 8).SelectMany(BitConverter.GetBytes).ToArray();
            var texture = rd.TextureCreate(format, view, volume ? [initialLayer] : [initialLayer, initialLayer]);
            using var uniform = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = 0 };
            uniform.AddId(texture);
            var set = rd.UniformSetCreate([uniform], shader, 0);
            var indirect = rd.StorageBufferCreate(12, [.. BitConverter.GetBytes(2u), .. BitConverter.GetBytes(1u), .. BitConverter.GetBytes(2u)], RenderingDevice.StorageBufferUsage.Indirect);
            var list = rd.ComputeListBegin();
            rd.ComputeListBindComputePipeline(list, pipeline);
            rd.ComputeListBindUniformSet(list, set, 0);
            for (int dispatch = 0; dispatch < 4; dispatch++)
            {
                if (dispatch < 2) rd.ComputeListSetPushConstant(list, BitConverter.GetBytes(dispatch + 1), 4);
                if (dispatch % 2 == 0) rd.ComputeListDispatch(list, 2, 1, 2);
                else rd.ComputeListDispatchIndirect(list, indirect, 0);
            }
            rd.ComputeListEnd(); rd.Submit(); rd.Sync();
            for (uint layer = 0; layer < (volume ? 1 : 2); layer++)
            {
                var data = rd.TextureGetData(texture, layer);
                if (data.Length != (volume ? 32 : 16)) throw new Exception("Storage texture feedback returned an incomplete volume or array.");
                int componentBytes = wide ? 4 : 2;
                for (int component = 0; component < data.Length / componentBytes; component++)
                {
                    int actual = wide ? BitConverter.ToInt32(data, component * 4) : BitConverter.ToInt16(data, component * 2);
                    if (actual != 7 + (component % (wide ? 2 : 4) + 1) * 7)
                        throw new Exception($"Storage texture feedback lost a dispatch (format: {format.Format}, volume: {volume}, component: {component}, actual: {actual}).");
                }
            }
            rd.FreeRid(indirect); rd.FreeRid(set); rd.FreeRid(texture); rd.FreeRid(pipeline); rd.FreeRid(shader);
        }
        Console.WriteLine("NATIVE_STORAGE_TEXTURE_FEEDBACK_PASSED");
    }
}
