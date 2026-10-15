namespace showcase.Maui;

public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new MainPage()) { Title = "showcase - MAUI host" };
}
