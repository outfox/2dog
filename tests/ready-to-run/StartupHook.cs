using System.Diagnostics.Tracing;

/// <summary>
/// Counts GodotSharp and twodog methods whose first call used precompiled ReadyToRun code, and prints the counts at
/// process exit. Files can carry R2R code the runtime then ignores (stream or collectible loads), so this checks use.
/// </summary>
internal static class StartupHook
{
    private static R2RListener? _listener;

    public static void Initialize()
    {
        _listener = new R2RListener();
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            Console.WriteLine($"2DOG_R2R_PROBE godotsharp={_listener.GodotSharp} twodog={_listener.TwoDog}");
    }

    private sealed class R2RListener : EventListener
    {
        private const EventKeywords CompilationDiagnosticKeyword = (EventKeywords)0x2000000000;

        private int _godotSharp;
        private int _twoDog;

        public int GodotSharp => Volatile.Read(ref _godotSharp);
        public int TwoDog => Volatile.Read(ref _twoDog);

        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "Microsoft-Windows-DotNETRuntime")
                EnableEvents(source, EventLevel.Verbose, CompilationDiagnosticKeyword);
        }

        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            if (e.EventName != "R2RGetEntryPoint" || e.PayloadNames is null || e.Payload is null) return;

            var index = e.PayloadNames.IndexOf("MethodNamespace");
            if (index < 0 || e.Payload[index] is not string type) return;

            // MethodNamespace holds the declaring type's full name, e.g. Godot.Node or twodog.Engine.
            if (type.StartsWith("Godot.", StringComparison.Ordinal)) Interlocked.Increment(ref _godotSharp);
            else if (type.StartsWith("twodog.", StringComparison.Ordinal)) Interlocked.Increment(ref _twoDog);
        }
    }
}
