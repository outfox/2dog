using Godot;
using Engine = twodog.Engine;

internal static class ContractRejections
{
    public static void Run(Engine engine)
    {
        using var rd = RenderingServer.CreateLocalRenderingDevice();
        foreach (string format in new[] { "rg32i", "rgba16i" })
        {
            using var source = new RDShaderSource { SourceCompute = """
                #version 450
                layout(local_size_x=1) in;
                layout(FORMAT,set=0,binding=0) uniform coherent iimage2D values;
                void main() {
                    imageStore(values, ivec2(0), ivec4(10, 20, 30, 40));
                    memoryBarrierImage();
                    imageStore(values, ivec2(0), imageLoad(values, ivec2(0)) + ivec4(1));
                }
                """.Replace("FORMAT", format) };
            using var spirv = rd.ShaderCompileSpirVFromSource(source);
            if (spirv.CompileErrorCompute.Length != 0) throw new Exception(spirv.CompileErrorCompute);
            var shader = rd.ShaderCreateFromSpirV(spirv);
            if (!shader.IsValid)
            {
                ExpectRejection(engine, "WebGPU: read/write storage texture format is unsupported on this adapter.", "!shader_id");
                continue;
            }
            if (format == "rg32i") throw new Exception("RG32 read/write must be rejected rather than emulated with stale snapshots.");
            using var textureFormat = new RDTextureFormat {
                Width = 1, Height = 1, Format = RenderingDevice.DataFormat.R16G16B16A16Sint,
                UsageBits = RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.CanCopyFromBit,
            };
            using var view = new RDTextureView();
            var texture = rd.TextureCreate(textureFormat, view);
            using var image = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = 0 };
            image.AddId(texture);
            var set = rd.UniformSetCreate([image], shader, 0);
            Dispatch(rd, shader, set);
            var data = rd.TextureGetData(texture, 0);
            short[] expected = [11, 21, 31, 41];
            if (data.Length != 8 || expected.Where((value, i) => BitConverter.ToInt16(data, i * 2) != value).Any())
                throw new Exception("Native tier2 storage texture lost same-dispatch writes.");
            rd.FreeRid(set); rd.FreeRid(texture); rd.FreeRid(shader);
        }
        foreach (bool readOnly in new[] { false, true })
        {
            using var source = new RDShaderSource { SourceCompute = """
                #version 450
                layout(local_size_x=1) in;
                layout(set=0,binding=0,std430) ACCESS buffer A { uint x; } a;
                layout(set=0,binding=1,std430) ACCESS buffer B { uint x; } b;
                layout(set=0,binding=2,std430) buffer Result { uint x; } result;
                void main() { BODY }
                """.Replace("ACCESS", readOnly ? "readonly" : "")
                    .Replace("BODY", readOnly ? "result.x = a.x + b.x;" : "a.x = 7u; memoryBarrierBuffer(); b.x = 13u; result.x = 1u;") };
            using var spirv = rd.ShaderCompileSpirVFromSource(source);
            if (spirv.CompileErrorCompute.Length != 0) throw new Exception(spirv.CompileErrorCompute);
            var shader = rd.ShaderCreateFromSpirV(spirv);
            var buffer = rd.StorageBufferCreate(4, BitConverter.GetBytes(3u));
            var other = rd.StorageBufferCreate(4, BitConverter.GetBytes(5u));
            var output = rd.StorageBufferCreate(4, BitConverter.GetBytes(0u));
            using var a = new RDUniform { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 0 };
            using var b = new RDUniform { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 1 };
            using var result = new RDUniform { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 2 };
            a.AddId(buffer); b.AddId(buffer); result.AddId(output);
            var set = rd.UniformSetCreate([a, b, result], shader, 0);
            if (readOnly)
            {
                Dispatch(rd, shader, set);
                if (BitConverter.ToUInt32(rd.BufferGetData(output)) != 6u) throw new Exception("Read-only buffer sharing failed.");
                rd.FreeRid(set);
            }
            else
            {
                if (set.IsValid) throw new Exception("Writable storage aliases must be rejected.");
                ExpectRejection(engine, "WebGPU: overlapping writable storage-buffer bindings are unsupported.", "!driver_uniform_set");
                b.ClearIds(); b.AddId(other);
                set = rd.UniformSetCreate([a, b, result], shader, 0);
                Dispatch(rd, shader, set);
                if (BitConverter.ToUInt32(rd.BufferGetData(buffer)) != 7u || BitConverter.ToUInt32(rd.BufferGetData(other)) != 13u)
                    throw new Exception("Distinct writable storage buffers lost writes.");
                rd.FreeRid(set);
            }
            rd.FreeRid(buffer); rd.FreeRid(other); rd.FreeRid(output); rd.FreeRid(shader);
        }
        rd.Free();
    }

    private static void ExpectRejection(Engine engine, string reason, string wrapperCondition)
    {
        var errors = engine.Errors.Drain();
        if (!errors.Any(error => error.Text.StartsWith(reason, StringComparison.Ordinal)) ||
            errors.Any(error => !error.Text.StartsWith(reason, StringComparison.Ordinal) &&
                !(error.File.EndsWith("rendering_device.cpp", StringComparison.Ordinal) && error.Code.Contains(wrapperCondition, StringComparison.Ordinal))))
            throw new Exception("Missing or unexpected capability error: " + string.Join("\n", errors.Select(error => error.ToString())));
    }

    private static void Dispatch(RenderingDevice rd, Rid shader, Rid set)
    {
        var pipeline = rd.ComputePipelineCreate(shader);
        var list = rd.ComputeListBegin();
        rd.ComputeListBindComputePipeline(list, pipeline);
        rd.ComputeListBindUniformSet(list, set, 0);
        rd.ComputeListDispatch(list, 1, 1, 1);
        rd.ComputeListEnd(); rd.Submit(); rd.Sync();
        rd.FreeRid(pipeline);
    }
}
