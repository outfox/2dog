namespace Godot;

/// <summary>
/// Exposes a property or field to the engine (inspector, Set/Get, scenes).
/// Processed by the twodog source generator into member registration.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class ExportAttribute(PropertyHint hint = PropertyHint.None, string hintString = "") : Attribute
{
    public PropertyHint Hint { get; } = hint;
    public string HintString { get; } = hintString;
}

/// <summary>
/// Declares a Godot signal from a delegate named *EventHandler. The source
/// generator emits the registration, a C# event, and an EmitSignalX helper.
/// </summary>
[AttributeUsage(AttributeTargets.Delegate)]
public sealed class SignalAttribute : Attribute;

/// <summary>Marks a user class as running in the editor (tool script semantics).</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ToolAttribute : Attribute;
