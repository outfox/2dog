using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Egl;
using Avalonia.OpenGL.Features;
using Avalonia.Rendering.Composition;

namespace twodog.Presentation;

/// <summary>
/// Avalonia registers the OpenGL external-objects feature (the compositor-side import half
/// of zero-copy texture sharing) for GLX contexts but not for EGL contexts, so the Wayland
/// and X11-EGL backends report no importable handle types and sessions fall back to CPU
/// readback. Until upstream registers the feature for EGL like <c>GlxContext</c> does,
/// this repairs the live compositor: on the render thread, it creates
/// <see cref="ExternalObjectsOpenGlExtensionFeature"/> for the Skia GL context and hands it
/// to the already-constructed Skia external-objects wrapper, which consults it per call.
/// Reflection-based by necessity; every step is fail-soft (worst case: CPU presentation).
/// </summary>
internal static class EglExternalObjectsShim
{
    private static readonly bool Diag = Environment.GetEnvironmentVariable("TWODOG_AVALONIA_DIAG") == "1";

    private static void Log(string message)
    {
        if (Diag) Console.WriteLine($"2DOG_EGL: {message}");
    }

    internal static async Task TryRepairAsync(Compositor compositor)
    {
        if (!OperatingSystem.IsLinux()) return;
        try
        {
            // Compositor.Server -> ServerCompositor.RenderInterface: the manager owning the
            // backend render context (the GlSkiaGpu on GL platforms).
            var server = typeof(Compositor)
                .GetProperty("Server", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(compositor);
            var manager = server?.GetType()
                .GetProperty("RenderInterface", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(server);
            var invokeServerJob = typeof(Compositor).GetMethod("InvokeServerJobAsync",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, [typeof(Action), typeof(bool)]);
            if (manager is null || invokeServerJob is null)
            {
                Log($"no render interface manager ({manager?.GetType().Name ?? "null"}) or job dispatch");
                return;
            }

            // The context is created, used, and feature-queried on the render thread; the
            // repair runs there too so the GL work needs no cross-thread current-context.
            var job = invokeServerJob.Invoke(compositor, [(Action)(() => Repair(manager)), false]) as Task;
            if (job is not null) await job;
        }
        catch (Exception ex)
        {
            Log($"repair dispatch failed: {ex}");
        }
    }

    private static void Repair(object manager)
    {
        try
        {
            var managerType = manager.GetType();
            managerType.GetMethod("EnsureValidBackendContext")?.Invoke(manager, null);
            var context = managerType
                .GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(manager);
            var feature = context?.GetType().GetMethod("TryGetFeature", [typeof(Type)])
                ?.Invoke(context, [typeof(Avalonia.Platform.IExternalObjectsRenderInterfaceContextFeature)]);
            if (feature is null)
            {
                Log($"render context {context?.GetType().Name ?? "null"} has no external objects wrapper");
                return;
            }

            var featureField = feature.GetType()
                .GetField("_feature", BindingFlags.Instance | BindingFlags.NonPublic);
            if (featureField is null)
            {
                Log($"no _feature field on {feature.GetType().Name}");
                return;
            }
            if (featureField.GetValue(feature) is not null) return; // already wired (GLX)

            if (managerType.GetProperty("GpuContext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(manager) is not EglContext glContext)
            {
                Log("backend context is not EGL");
                return;
            }

            using (glContext.MakeCurrent())
            {
                if (ExternalObjectsOpenGlExtensionFeature.TryCreate(glContext) is not { } externalObjects)
                {
                    Log("EGL context lacks the external-objects GL extensions");
                    return;
                }
                featureField.SetValue(feature, externalObjects);
                Log("external objects feature installed on EGL context");
            }
        }
        catch (Exception ex)
        {
            Log($"repair failed: {ex}");
        }
    }
}
