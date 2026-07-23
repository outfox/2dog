using System.Reflection;
using System.Runtime.Loader;

namespace DualHost;

/// <summary>
/// One ALC per engine instance: dual.driver + twodog.bindings/gdextension load
/// here, giving the instance its own copy of every managed static (proc table,
/// method-bind caches, StringNames, InstanceBindings, Engine instance ptr).
/// Non-collectible: UnmanagedCallersOnly pointers stay registered native-side
/// for module lifetime. System.* falls through to the default ALC (shared
/// CoreLib types are what let strings/delegates cross the boundary).
/// </summary>
internal sealed class InstanceAlc(string name, string driverPath) : AssemblyLoadContext(name, isCollectible: false)
{
    private readonly AssemblyDependencyResolver _resolver = new(driverPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }
}
