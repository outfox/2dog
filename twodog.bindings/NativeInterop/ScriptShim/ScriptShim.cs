namespace Godot.NativeInterop;

/// <summary>
/// Wires the C# script language into the engine so scenes referencing
/// res://*.cs scripts attach managed classes - the GodotSharp compat layer for
/// the gdext stack. Initialized by GdExtensionHost at SCENE init (after queued
/// class registrations flush, before ScriptServer::init_languages and the main
/// scene load); torn down at SCENE deinit.
/// </summary>
internal static class ScriptShim
{
    private static CSharpLanguage? _language;
    private static CSharpScriptLoader? _loader;

    internal static void Initialize()
    {
        if (_language is not null) return;

        ClassRegistry.RegisterInternal(typeof(CSharpLanguage), claimAllVirtuals: true);
        ClassRegistry.RegisterInternal(typeof(CSharpScript), claimAllVirtuals: true);
        ClassRegistry.RegisterInternal(typeof(CSharpScriptLoader),
            customVirtuals: CSharpScriptLoader.LoadCustomVirtuals);

        _language = new CSharpLanguage();
        CSharpLanguage.Singleton = _language;
        var err = Engine.RegisterScriptLanguage(_language);
        if (err != Error.Ok)
        {
            Console.Error.WriteLine($"twodog: registering the C# script language failed: {err}");
            _language.Free();
            _language = null;
            CSharpLanguage.Singleton = null;
            return;
        }

        _loader = new CSharpScriptLoader();
        ResourceLoader.AddResourceFormatLoader(_loader);
    }

    internal static void Shutdown()
    {
        if (_loader is not null)
        {
            ResourceLoader.RemoveResourceFormatLoader(_loader);
            _loader.Dispose();
            _loader = null;
        }
        if (_language is not null)
        {
            Engine.UnregisterScriptLanguage(_language);
            _language.Free();
            _language = null;
            CSharpLanguage.Singleton = null;
        }
    }
}
