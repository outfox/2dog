using Microsoft.Maui.Hosting;

namespace twodog.Maui;

/// <summary>MAUI registration for the Godot view.</summary>
public static class MauiAppBuilderExtensions
{
    /// <summary>Register Windows and Android native handlers for <see cref="GodotView"/>.</summary>
    public static MauiAppBuilder UseTwoDog(this MauiAppBuilder builder) =>
        builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<GodotView, GodotViewHandler>());
}
