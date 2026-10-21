using Godot;

internal static class ShaderBakingSmoke
{
    public static void Run()
    {
        using var rd = RenderingServer.CreateLocalRenderingDevice();
        using var source = new RDShaderSource
        {
            SourceCompute = """
                #version 450
                layout(local_size_x=1) in;
                layout(constant_id=0) const uint VALUE = 7u;
                layout(push_constant, std430) uniform Params { uint add; } params;
                layout(set=0,binding=0,std430) writeonly buffer Output { uint value; } result;
                void main() { result.value = VALUE + params.add; }
                """,
        };
        using var spirv = rd.ShaderCompileSpirVFromSource(source);
        if (spirv.CompileErrorCompute.Length != 0) throw new Exception(spirv.CompileErrorCompute);
        var binary = rd.ShaderCompileBinaryFromSpirV(spirv, "BakedSpecializationSmoke");
        if (binary.Length == 0) throw new Exception("WebGPU did not bake a shader container.");
        foreach (uint value in new uint[] { 7, 23 })
        {
            var shader = rd.ShaderCreateFromBytecode(binary.ToArray());
            if (!shader.IsValid) throw new Exception("WebGPU could not reload baked WGSL.");
            using var specialization = new RDPipelineSpecializationConstant { ConstantId = 0, Value = value };
            var pipeline = rd.ComputePipelineCreate(shader, [specialization]);
            var output = rd.StorageBufferCreate(4);
            using var uniform = new RDUniform { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 0 };
            uniform.AddId(output);
            var set = rd.UniformSetCreate([uniform], shader, 0);
            var list = rd.ComputeListBegin();
            rd.ComputeListBindComputePipeline(list, pipeline);
            rd.ComputeListBindUniformSet(list, set, 0);
            rd.ComputeListSetPushConstant(list, BitConverter.GetBytes(5u), 4);
            rd.ComputeListDispatch(list, 1, 1, 1);
            rd.ComputeListEnd();
            rd.Submit();
            rd.Sync();
            var pixels = rd.BufferGetData(output);
            if (pixels.Length != 4 || BitConverter.ToUInt32(pixels) != value + 5)
                throw new Exception($"Baked shader specialization or push constants changed: expected {value + 5}.");
            rd.FreeRid(set); rd.FreeRid(output); rd.FreeRid(pipeline); rd.FreeRid(shader);
        }
        rd.Free();
        Console.WriteLine("NATIVE_BAKED_WGSL_AND_SPECIALIZATION_PASSED");
    }
}
