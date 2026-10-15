using twodog.Maui;

namespace Company.Product1.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp() => MauiApp.CreateBuilder()
        .UseMauiApp<App>()
        .UseTwoDog()
        .Build();
}
