namespace Godot.NativeInterop;

/// <summary>
/// The C# script language on the gdext stack. Registered with ClaimAllVirtuals:
/// every ScriptLanguageExtension virtual is engine-REQUIRED with no C++ default
/// (EXBIND), so claiming them silences registration-time validation while
/// unknown names no-op to caller-initialized defaults (empty/false/zero).
/// </summary>
internal sealed class CSharpLanguage : ScriptLanguageExtension
{
    internal static CSharpLanguage? Singleton;

    public override string _GetName() => "C#";

    public override string _GetType() => "CSharpScript";

    public override string _GetExtension() => "cs";

    public override string[] _GetRecognizedExtensions() => ["cs"];

    public override bool _HandlesGlobalClassType(string type) => false;
}
