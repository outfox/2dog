using System.Collections.Generic;
using System.Reflection;

namespace twodog;

/// <summary>
/// Reads <c>[AssemblyMetadata]</c> values without referencing the attribute type: mobile trimmers remove instances
/// of it by key, which turns a typed <c>GetCustomAttributes</c> lookup into trim warning IL2045.
/// </summary>
internal static class AssemblyMetadata
{
    private const string AttributeName = "System.Reflection.AssemblyMetadataAttribute";

    /// <summary>Non-empty values of the metadata entries with the given key, in declaration order.</summary>
    public static IEnumerable<string> Values(Assembly assembly, string key)
    {
        foreach (var attribute in Data(assembly))
        {
            if (attribute.AttributeType.FullName != AttributeName || attribute.ConstructorArguments.Count != 2) continue;
            if (attribute.ConstructorArguments[0].Value as string != key) continue;
            if (attribute.ConstructorArguments[1].Value is string { Length: > 0 } value) yield return value;
        }
    }

    private static IList<CustomAttributeData> Data(Assembly assembly)
    {
        try
        {
            return assembly.GetCustomAttributesData();
        }
        catch
        {
            // Some dynamic/reflection-emit assemblies may throw
            return [];
        }
    }
}
