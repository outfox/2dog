namespace Company.Product1.Maui;

public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new MainPage()) { Title = "TPLRAWNAME - MAUI host" };
}
