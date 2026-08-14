using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.XamlTypeInfo;

namespace showcase;

// Code-only WinUI 3 application: without XAML in the project there is no generated metadata
// provider, so the app forwards WinUI's own controls provider itself - without it the first
// control style lookup dies in native code with a stowed exception (0xC000027B).
internal sealed class App : Application, IXamlMetadataProvider
{
    private readonly string[] _args;
    private readonly XamlControlsXamlMetaDataProvider _provider = new();
    private Window? _window;

    public App(string[] args) => _args = args;

    public IXamlType GetXamlType(Type type) => _provider.GetXamlType(type);
    public IXamlType GetXamlType(string fullName) => _provider.GetXamlType(fullName);
    public XmlnsDefinition[] GetXmlnsDefinitions() => _provider.GetXmlnsDefinitions();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Default control templates for the code-created controls. Application.Resources
        // is not accessible before OnLaunched in a code-only app.
        Resources.MergedDictionaries.Add(new XamlControlsResources());
        _window = new MainWindow(_args);
        _window.Activate();
    }
}
