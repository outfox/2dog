using System.Reflection;
using System.Runtime.Loader;

namespace twodog.Hosting;

/// <summary>
/// One ALC per engine instance. The program assembly and its dependencies
/// (twodog.bindings, twodog.gdextension, ...) load here, giving the instance
/// its own copy of every managed static - which is the entire isolation model.
/// twodog.hosting itself plus opt-in shared assemblies fall through to the
/// default ALC so contract types keep one identity. Non-collectible:
/// UnmanagedCallersOnly pointers stay registered native-side for module lifetime.
/// </summary>
internal sealed class InstanceAlc : AssemblyLoadContext
{
    private static readonly string HostingAssemblyName = typeof(InstanceAlc).Assembly.GetName().Name!;

    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _rootAssemblyPath;
    private readonly HashSet<string> _shared;

    public InstanceAlc(string name, string rootAssemblyPath, IEnumerable<string> sharedAssemblies,
                       AssemblyDependencyResolver resolver)
        : base($"2dog-{name}", isCollectible: false)
    {
        _rootAssemblyPath = rootAssemblyPath;
        _resolver = resolver; // EngineHost validated the shared list against it
        _shared = new HashSet<string>(sharedAssemblies, StringComparer.OrdinalIgnoreCase) { HostingAssemblyName };
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is { } name && _shared.Contains(name))
            return null; // default-ALC fallthrough: shared identity

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        if (path is not null) return LoadFromAssemblyPath(path);

        // The root component itself is not always listed in its own deps resolution.
        if (string.Equals(assemblyName.Name, Path.GetFileNameWithoutExtension(_rootAssemblyPath), StringComparison.OrdinalIgnoreCase))
            return LoadFromAssemblyPath(_rootAssemblyPath);

        return null; // System.* etc. fall through to the default ALC
    }
}
