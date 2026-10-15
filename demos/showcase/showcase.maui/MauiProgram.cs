using twodog.Maui;

namespace showcase.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp() => MauiApp.CreateBuilder()
        .UseMauiApp<App>()
        .UseTwoDog()
        .Build();
}
