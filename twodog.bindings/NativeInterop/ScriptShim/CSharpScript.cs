namespace Godot.NativeInterop;

/// <summary>
/// The Script resource behind a res://*.cs path: resolves the file-stem class
/// name against ClassRegistry (auto-registration puts user classes there) and
/// creates script instances that layer the managed type onto plain engine
/// objects - GodotSharp's attachment model on the gdext stack.
/// Registered with ClaimAllVirtuals: ScriptExtension's virtuals are all
/// engine-REQUIRED with no C++ defaults, so unknown names safely no-op to the
/// caller-initialized default return.
/// </summary>
internal sealed unsafe class CSharpScript : ScriptExtension
{
    internal string ResPath = "";
    internal ClassRegistry.ClassInfo? Target;
    private ScriptMemberMap? _members;

    private ScriptMemberMap? Members => _members ??= Target is null ? null : ScriptMemberMap.For(Target.Type);

    internal static CSharpScript ForPath(string path)
    {
        var script = new CSharpScript { ResPath = path };
        var stem = System.IO.Path.GetFileNameWithoutExtension(path);
        if (ClassRegistry.TryGetByClassName(stem, out var info))
        {
            script.Target = info;
        }
        else
        {
            Console.Error.WriteLine(
                $"twodog: no registered class '{stem}' for script '{path}'. Script classes must be " +
                "compiled into the host and named after their file (auto-registration finds them).");
        }
        return script;
    }

    public override bool _CanInstantiate() => Target is not null;

    public override StringName _GetInstanceBaseType() => Target?.BaseEngineClass ?? "";

    public override StringName _GetGlobalName() => "";

    public override ScriptLanguage? _GetLanguage() => CSharpLanguage.Singleton;

    public override bool _IsValid() => true;

    public override bool _IsTool() => false;

    public override bool _IsAbstract() => false;

    public override bool _HasSourceCode() => false;

    public override bool _HasMethod(StringName method) =>
        Members?.FindMethod(method.ToString()) is not null;

    public override bool _HasStaticMethod(StringName method) => false;

    public override bool _HasScriptSignal(StringName signal) =>
        Members?.Signals.Contains(signal.ToString()) == true;

    private static ulong __vsnInstanceCreate;

    internal override bool __CallVirtual(ulong nameSn, nint* args, nint ret)
    {
        if (__vsnInstanceCreate == 0) __vsnInstanceCreate = StringNames.Get("_instance_create").Opaque;
        if (nameSn == __vsnInstanceCreate)
        {
            *(nint*)ret = ScriptInstanceBridge.Create(this, *(nint*)args[0]);
            return true;
        }
        return base.__CallVirtual(nameSn, args, ret);
    }
}
