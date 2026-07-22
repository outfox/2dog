using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Godot.NativeInterop;

/// <summary>
/// Reflected member table for a script-attached managed type: [Export] members,
/// [Signal] names, and callable methods, all under their verbatim C# names
/// (GodotSharp parity - scenes and GDScript call sites use C# names). Engine
/// virtuals arrive snake_cased and map through GeneratedVirtualNames.
/// </summary>
internal sealed class ScriptMemberMap
{
    internal sealed class ExportEntry
    {
        public required Func<object, object?> Get;
        public required Action<object, object?> Set;
        public required Type ClrType;
    }

    private static readonly ConcurrentDictionary<Type, ScriptMemberMap> Cache = [];

    internal readonly Dictionary<string, ExportEntry> Exports = [];
    internal readonly HashSet<string> Signals = [];
    private readonly Dictionary<string, MethodInfo?> _methods = [];
    private readonly Type _type;
    private readonly object _gate = new();

    internal static ScriptMemberMap For(Type type) => Cache.GetOrAdd(type, static t => new ScriptMemberMap(t));

    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Script types are ClassRegistry-registered; Register<T>'s " +
                        "DynamicallyAccessedMembers annotation keeps their members.")]
    private ScriptMemberMap(Type type)
    {
        _type = type;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // Walk declared members per level so private members of base classes are
        // seen too (Type.GetMembers with NonPublic skips inherited privates).
        for (var t = type; t is not null && t != typeof(GodotObject); t = t.BaseType)
        {
            foreach (var p in t.GetProperties(flags | BindingFlags.DeclaredOnly))
            {
                if (!Exports.ContainsKey(p.Name) && HasExport(p) && p.CanRead && p.CanWrite)
                    Exports[p.Name] = new ExportEntry { Get = p.GetValue, Set = p.SetValue, ClrType = p.PropertyType };
            }
            foreach (var f in t.GetFields(flags | BindingFlags.DeclaredOnly))
            {
                if (!Exports.ContainsKey(f.Name) && HasExport(f) && !f.IsInitOnly)
                    Exports[f.Name] = new ExportEntry { Get = f.GetValue, Set = f.SetValue, ClrType = f.FieldType };
            }
            foreach (var n in t.GetNestedTypes(flags))
            {
                if (typeof(Delegate).IsAssignableFrom(n) && HasSignal(n)
                    && n.Name.EndsWith("EventHandler", StringComparison.Ordinal))
                {
                    Signals.Add(n.Name.Substring(0, n.Name.Length - "EventHandler".Length));
                }
            }
        }
    }

    private static bool HasExport(MemberInfo m) => m.IsDefined(typeof(ExportAttribute), inherit: false);
    private static bool HasSignal(MemberInfo m) => m.IsDefined(typeof(SignalAttribute), inherit: false);

    /// <summary>
    /// Resolves an engine-facing method name: engine virtuals ("_process") map
    /// to their C# names ("_Process"); everything else is a verbatim C# name.
    /// Cached, including misses.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Script types are ClassRegistry-registered; Register<T>'s " +
                        "DynamicallyAccessedMembers annotation keeps their members.")]
    internal MethodInfo? FindMethod(string gdName)
    {
        lock (_gate)
        {
            if (_methods.TryGetValue(gdName, out var cached)) return cached;

            var csName = GeneratedVirtualNames.Map.TryGetValue(gdName, out var mapped) ? mapped : gdName;
            // First name match (GetMethod throws on overloads; scripts rarely
            // overload engine-callable methods).
            MethodInfo? mi = null;
            foreach (var m in _type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.Name == csName) { mi = m; break; }
            }

            // Engine virtuals: the generated no-op stub in the bindings assembly
            // is not an override - only count user code.
            if (mi is not null && mi.DeclaringType!.Assembly == typeof(GodotObject).Assembly) mi = null;

            _methods[gdName] = mi;
            return mi;
        }
    }
}
