namespace showcase.Maui;

public sealed class App : Application
{
    // Games are dark; this keeps system bars, the title bar and stock controls legible over them.
    public App() => UserAppTheme = AppTheme.Dark;

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Desktop size and placement; Android ignores both.
        var window = new Window(new MainPage())
        {
            Title = "2dog showcase — MAUI host",
            Width = 1152, Height = 720, MinimumWidth = 480, MinimumHeight = 360,
        };
        var display = DeviceDisplay.MainDisplayInfo;
        if (DeviceInfo.Idiom == DeviceIdiom.Desktop && display.Density > 0)
        {
            window.X = Math.Max(0, (display.Width / display.Density - window.Width) / 2);
            window.Y = Math.Max(0, (display.Height / display.Density - window.Height) / 2);
        }
        return window;
    }
}
