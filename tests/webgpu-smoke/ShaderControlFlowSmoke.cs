using Godot;

internal static class ShaderControlFlowSmoke
{
    public static void Run()
    {
        using var rd = RenderingServer.CreateLocalRenderingDevice();
        using var source = new RDShaderSource { SourceCompute = """
            #version 450
            layout(local_size_x=1) in;
            layout(set=0,binding=0,std430) writeonly buffer Output { uint value[2]; } result;
            uint choose(uint input_value) {
                for (uint i = 0u; i < 4u; i++) {
                    switch (i) {
                        case 1u: if (input_value != 0u) return 37u; break;
                        default: break;
                    }
                }
                return 11u;
            }
            void main() { result.value[0] = choose(0u); result.value[1] = choose(1u); }
            """ };
        using var spirv = rd.ShaderCompileSpirVFromSource(source);
        if (spirv.CompileErrorCompute.Length != 0) throw new Exception(spirv.CompileErrorCompute);
        var shader = rd.ShaderCreateFromSpirV(spirv);
        var pipeline = rd.ComputePipelineCreate(shader);
        var output = rd.StorageBufferCreate(8);
        using var uniform = new RDUniform { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 0 };
        uniform.AddId(output);
        var set = rd.UniformSetCreate([uniform], shader, 0);
        var list = rd.ComputeListBegin();
        rd.ComputeListBindComputePipeline(list, pipeline);
        rd.ComputeListBindUniformSet(list, set, 0);
        rd.ComputeListDispatch(list, 1, 1, 1);
        rd.ComputeListEnd();
        rd.Submit(); rd.Sync();
        var data = rd.BufferGetData(output);
        if (data.Length != 8 || BitConverter.ToUInt32(data) != 11 || BitConverter.ToUInt32(data.AsSpan(4)) != 37)
            throw new Exception("Return from switch inside loop changed the result.");
        rd.FreeRid(set); rd.FreeRid(output); rd.FreeRid(pipeline); rd.FreeRid(shader);
        rd.Free();
        Console.WriteLine("NATIVE_SWITCH_LOOP_RETURN_PASSED");
        CheckEmptyFramebuffer();
    }

    private static void CheckEmptyFramebuffer()
    {
        using var rd = RenderingServer.CreateLocalRenderingDevice();
        using var source = new RDShaderSource
        {
            SourceVertex = """
                #version 450
                void main() {
                    vec2 p = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
                    gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
                }
                """,
            SourceFragment = """
                #version 450
                layout(set=0,binding=0,std430) buffer Output { uint value; } result;
                layout(location=0) out vec4 color;
                void main() { atomicAdd(result.value, 1u); color = vec4(0.0); }
                """,
        };
        using var spirv = rd.ShaderCompileSpirVFromSource(source);
        if (spirv.CompileErrorVertex.Length != 0 || spirv.CompileErrorFragment.Length != 0)
            throw new Exception(spirv.CompileErrorVertex + spirv.CompileErrorFragment);
        var shader = rd.ShaderCreateFromSpirV(spirv);
        var format = rd.FramebufferFormatCreateEmpty();
        var framebuffer = rd.FramebufferCreateEmpty(new Vector2I(4, 4), validateWithFormat: format);
        using var raster = new RDPipelineRasterizationState();
        using var msaa = new RDPipelineMultisampleState();
        using var depth = new RDPipelineDepthStencilState();
        using var blendAttachment = new RDPipelineColorBlendStateAttachment();
        using var blend = new RDPipelineColorBlendState { Attachments = [blendAttachment] };
        var pipeline = rd.RenderPipelineCreate(shader, format, -1, RenderingDevice.RenderPrimitive.Triangles,
            raster, msaa, depth, blend);
        var output = rd.StorageBufferCreate(4, new byte[4]);
        using var uniform = new RDUniform { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 0 };
        uniform.AddId(output);
        var set = rd.UniformSetCreate([uniform], shader, 0);
        var list = rd.DrawListBegin(framebuffer);
        rd.DrawListBindRenderPipeline(list, pipeline);
        rd.DrawListBindUniformSet(list, set, 0);
        rd.DrawListDraw(list, false, 1, 3);
        rd.DrawListEnd();
        rd.Submit(); rd.Sync();
        if (BitConverter.ToUInt32(rd.BufferGetData(output)) != 16)
            throw new Exception("Empty framebuffer did not execute its fragment shader.");
        rd.FreeRid(set); rd.FreeRid(output); rd.FreeRid(pipeline); rd.FreeRid(shader); rd.FreeRid(framebuffer);
        rd.Free();
        Console.WriteLine("NATIVE_EMPTY_FRAMEBUFFER_PASSED");
    }
}
