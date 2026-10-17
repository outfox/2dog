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
                throw new Exception($"{promotedFormat} storage texture transfer did not preserve every depth slice and mipmap.");
        }
        rd.Free();
        Console.WriteLine("NATIVE_DESCRIPTOR_ARRAY_AND_FLOAT_CLASSIFICATION_PASSED");
    }
}
