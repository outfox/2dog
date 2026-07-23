using System.Reflection;
using System.Runtime.Loader;
using Engine = twodog.Engine;

namespace twodog.Hosting.Runtime;

/// <summary>
/// The resident engine program behind fixture-style hosting: boots the engine,
/// signals boot, then pumps the main loop while executing scenarios submitted
/// through the <see cref="EngineWorkQueue"/> passed as InstanceOptions.State.
/// Scenarios run on the engine thread between iterations - the same threading
/// model as single-instance twodog.
/// </summary>
public sealed class ResidentProgram : IEngineProgram
{
    public int Run(IInstanceContext ctx)
    {
        var queue = ctx.State as EngineWorkQueue
            ?? throw new InvalidOperationException(
                $"{nameof(ResidentProgram)} requires an {nameof(EngineWorkQueue)} as InstanceOptions.State.");

        try
        {
            var engine = new Engine(ctx.Tag, ctx.ProjectDir, ctx.Args) { NativePath = ctx.NativePath };
            using var godot = engine.Start();
            var session = new EngineSession(engine, godot);
            ctx.SignalBooted();
            try
            {
                while (!ctx.QuitRequested)
                {
                    if (godot.Iteration()) break;
                    while (queue.TryTake(out var item)) Execute(session, item);
                }
            }
            finally
            {
                engine.Dispose();
            }
        }
        finally
        {
            // Runs on boot failure too: fails everything still queued AND
            // rejects submissions racing with this shutdown - no work item can
            // be left forever-pending.
            queue.Close(new OperationCanceledException($"Engine instance '{ctx.Tag}' shut down."));
        }
        return 0;
    }

    private static void Execute(EngineSession session, EngineWorkItem item)
    {
        try
        {
            var scenario = (IEngineScenario)Activator.CreateInstance(ResolveScenarioType(item.TypeName))!;
            item.Complete(scenario.Run(session, item.Argument));
        }
        catch (Exception e)
        {
            item.Fail(e);
        }
    }

    /// <summary>Resolves "Full.Name, AssemblySimpleName" inside this instance's ALC,
    /// so the scenario type is the ALC-local copy (Godot type identity matches).</summary>
    private static Type ResolveScenarioType(string typeName)
    {
        var parts = typeName.Split(',', 2);
        if (parts.Length != 2)
            throw new ArgumentException($"Scenario type name must be 'Full.Name, AssemblySimpleName', got '{typeName}'.");
        var alc = AssemblyLoadContext.GetLoadContext(typeof(ResidentProgram).Assembly)!;
        var assembly = alc.LoadFromAssemblyName(new AssemblyName(parts[1].Trim()));
        return assembly.GetType(parts[0].Trim(), throwOnError: true)!;
    }
}
