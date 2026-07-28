using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace twodog.tests.EngineTests;

// On wasm every managed-to-native transition needs a trampoline compiled at publish time, keyed by a
// signature cookie (return char + one char per arg: i=ptr/int32, l=int64, f=float, d=double, v=void).
// 'delegate* unmanaged' calli gets no trampolines generated automatically, so twodog.WebTrampolines
// must declare every shape NativeFuncs can invoke; a missing shape aborts web builds at runtime
// (mono aot-runtime-wasm.c, "<disabled>" g_error). The fork carries a source-only sibling list
// (GodotSharp.csproj ships SourceFiles/** uncompiled), checked here by parsing it from the repo.
public class WebTrampolineCoverageTests
{
    private static Type TwodogTrampolines => typeof(Engine).Assembly
        .GetType("twodog.WebTrampolines", throwOnError: true)!;

    [Fact]
    public void TwodogTrampolines_CoverEveryNativeFuncsShape()
    {
        var declared = TrampolineDelegates(TwodogTrampolines)
            .Select(t => t.GetMethod("Invoke")!)
            .Select(invoke => Cookie(
                TypeChar(invoke.ReturnType),
                invoke.GetParameters().Select(p => TypeChar(p.ParameterType))))
            .ToHashSet();
        var missing = NeededCookies().Where(kv => !declared.Contains(kv.Key)).ToList();
        Assert.True(missing.Count == 0,
            "twodog.engine/WebTrampolines.cs is missing trampoline shapes: " +
            string.Join(", ", missing.Select(kv => $"{kv.Key} (e.g. {kv.Value})")));
    }

    [Fact]
    public void TwodogTrampolineDelegates_CarryUnmanagedFunctionPointer()
    {
        // Without the attribute the wasm publish scan ignores the delegate and generates nothing.
        var unattributed = TrampolineDelegates(TwodogTrampolines)
            .Where(t => t.GetCustomAttribute<UnmanagedFunctionPointerAttribute>() is null)
            .Select(t => t.Name)
            .ToList();
        Assert.True(unattributed.Count == 0,
            $"Trampoline delegates missing [UnmanagedFunctionPointer]: {string.Join(", ", unattributed)}");
    }

    [Fact]
    public void ForkTrampolineSource_CoversEveryNativeFuncsShape()
    {
        // CI test jobs check out no submodules; the twodog-assembly tests above guard what ships.
        // Skip only when the whole submodule is absent so a moved/deleted file still fails.
        Assert.SkipWhen(!File.Exists(Path.Combine(RepoRoot(), "godot", "version.py")),
            "godot submodule is not checked out");
        var (declared, delegateCount, attributeCount) = ParseForkTrampolines();
        var missing = NeededCookies().Where(kv => !declared.Contains(kv.Key)).ToList();
        Assert.True(missing.Count == 0,
            "fork SourceFiles/WebTrampolines.cs is missing trampoline shapes: " +
            string.Join(", ", missing.Select(kv => $"{kv.Key} (e.g. {kv.Value})")));
        Assert.Equal(delegateCount, attributeCount);
    }

    private static Dictionary<string, string> NeededCookies()
    {
        var cookies = new Dictionary<string, string>();
        var methods = typeof(Godot.NativeInterop.NativeFuncs)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(m => m.Name.StartsWith("godotsharp_", StringComparison.Ordinal));
        foreach (var method in methods)
            cookies.TryAdd(
                Cookie(TypeChar(method.ReturnType), method.GetParameters().Select(p => TypeChar(p.ParameterType))),
                method.Name);
        return cookies;
    }

    private static IEnumerable<Type> TrampolineDelegates(Type container) => container
        .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
        .Where(t => t.IsSubclassOf(typeof(MulticastDelegate)));

    private static string Cookie(char ret, IEnumerable<char> args)
    {
        var sb = new StringBuilder();
        // Non-enum struct returns lower to void plus a hidden ret-buffer pointer argument.
        sb.Append(ret == 'S' ? "vi" : ret.ToString());
        foreach (var c in args)
        {
            if (c is 'S' or 'v')
                throw new NotSupportedException(
                    "By-value struct arguments have no verified wasm signature mapping; " +
                    "check mono's type_to_c before teaching this test about them.");
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static char TypeChar(Type type)
    {
        if (type == typeof(void)) return 'v';
        if (type.IsByRef || type.IsPointer || type.IsFunctionPointer
            || type == typeof(IntPtr) || type == typeof(UIntPtr)) return 'i';
        // The callbacks generator marshals string parameters to godot_string passed byref.
        if (type == typeof(string)) return 'i';
        if (type.IsEnum) type = Enum.GetUnderlyingType(type);
        if (type == typeof(long) || type == typeof(ulong)) return 'l';
        if (type == typeof(float)) return 'f';
        if (type == typeof(double)) return 'd';
        if (type == typeof(bool) || type == typeof(char) || type == typeof(sbyte) || type == typeof(byte)
            || type == typeof(short) || type == typeof(ushort) || type == typeof(int) || type == typeof(uint)) return 'i';
        if (type.IsValueType) return 'S';
        throw new NotSupportedException($"Unmapped type '{type}' in native signature.");
    }

    private static char TypeChar(string sourceTypeName) => sourceTypeName switch
    {
        "void" => 'v',
        "IntPtr" or "UIntPtr" or "nint" or "nuint" => 'i',
        "long" or "ulong" => 'l',
        "float" => 'f',
        "double" => 'd',
        "bool" or "char" or "sbyte" or "byte" or "short" or "ushort" or "int" or "uint" => 'i',
        "Godot.Error" => 'l', // enum Error : long
        "Godot.Color" => 'S',
        _ => throw new NotSupportedException(
            $"Unmapped source type '{sourceTypeName}' in fork WebTrampolines.cs; extend this test's mapping."),
    };

    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static (HashSet<string> Cookies, int Delegates, int Attributes) ParseForkTrampolines()
    {
        var path = Path.Combine(RepoRoot(), "godot", "modules", "mono", "glue", "GodotSharp", "GodotSharp",
            "SourceFiles", "WebTrampolines.cs");
        var source = File.ReadAllText(path);
        var cookies = new HashSet<string>();
        var declarations = Regex.Matches(source, @"delegate\s+([\w.]+)\s+\w+\s*\(([^)]*)\);");
        foreach (Match m in declarations)
        {
            var args = m.Groups[2].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            cookies.Add(Cookie(TypeChar(m.Groups[1].Value), args.Select(a => TypeChar(a.Split(' ')[0]))));
        }
        return (cookies, declarations.Count, Regex.Matches(source, @"\[UnmanagedFunctionPointer").Count);
    }
}
