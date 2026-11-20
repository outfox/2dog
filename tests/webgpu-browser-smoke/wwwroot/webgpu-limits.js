function restrictWebGPUAdapter(options) {
    if (options.has('limited-compute') || options.has('base-formats') || options.has('limited-bindings')) {
        const requestDevice = GPUAdapter.prototype.requestDevice;
        GPUAdapter.prototype.requestDevice = function (descriptor = {}) {
            return requestDevice.call(this, {
                ...descriptor,
                requiredLimits: {
                    ...descriptor.requiredLimits,
                    ...(options.has('limited-compute') ? { maxComputeInvocationsPerWorkgroup: 256 } : {}),
                    ...(options.has('limited-bindings') ? { maxSampledTexturesPerShaderStage: 16, maxInterStageShaderVariables: 16 } : {}),
                },
                requiredFeatures: [...(descriptor.requiredFeatures || [])].filter(feature =>
                    !options.has('base-formats') || !['texture-formats-tier1', 'texture-formats-tier2', 'depth32float-stencil8'].includes(feature)),
            });
        };
    }
}

if (typeof DedicatedWorkerGlobalScope !== 'undefined' && self instanceof DedicatedWorkerGlobalScope) {
    self.addEventListener('message', event => {
        if (!Object.hasOwn(event.data ?? {}, 'smokeQuery')) return;
        event.stopImmediatePropagation();
        restrictWebGPUAdapter(new URLSearchParams(event.data.smokeQuery));
        const encoder = GPURenderPassEncoder.prototype;
        const binding = encoder.setVertexBuffer;
        const draw = encoder.drawIndexed;
        const bound = new WeakSet();
        encoder.setVertexBuffer = function (...args) {
            if (!bound.has(this)) {
                bound.add(this);
                queueMicrotask(() => bound.delete(this));
            }
            return binding.apply(this, args);
        };
        encoder.drawIndexed = function (...args) {
            if (bound.has(this)) {
                event.data.port.postMessage(true);
                encoder.setVertexBuffer = binding;
                encoder.drawIndexed = draw;
            }
            return draw.apply(this, args);
        };
    }, true);
}
