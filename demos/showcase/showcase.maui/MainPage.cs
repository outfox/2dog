using twodog.Maui;

namespace showcase.Maui;

public sealed class MainPage : ContentPage
{
    public MainPage()
    {
        var game = new GodotView { Project = "showcase" };
        var status = new Label { Text = "Starting Godot...", TextColor = Colors.White };
        var pause = new Button { Text = "Pause", IsEnabled = false };
        var speed = new Slider { Minimum = 0.1, Maximum = 2, Value = 1, WidthRequest = 160, IsEnabled = false };
        var mode = new Label { TextColor = Colors.White };
        var paused = false;
        game.Started += (_, _) =>
        {
            pause.IsEnabled = speed.IsEnabled = true;
            status.Text = "Godot is running";
            game.Dispatch(tree =>
            {
                GodotApiSmoke.RunAll(tree);
                var cubes = tree.CurrentScene.GetNode<global::Godot.Node3D>("Flair/WhiteCubes")
                    .GetChildren().OfType<global::Godot.Node3D>().ToArray();
                var axis = new global::Godot.Vector3(1, 1, 0).Normalized();
                tree.ProcessFrame += () =>
                {
                    foreach (var cube in cubes) cube.Rotate(axis, 1.8f * (float)tree.Root.GetProcessDeltaTime());
                };
                global::Godot.GD.Print("2DOG_MAUI_SHOWCASE_SMOKE_PASSED");
            });
        };
        game.PresentationModeChanged += (_, _) => mode.Text = $"Presentation: {game.ActivePresentationMode}";
        game.Failed += (_, exception) =>
        {
            pause.IsEnabled = speed.IsEnabled = false;
            status.Text = exception.Message;
            Console.Error.WriteLine($"2DOG_MAUI_SHOWCASE_SMOKE_FAILED: {exception}");
        };
        game.QuitRequested += (_, _) => { pause.IsEnabled = speed.IsEnabled = false; status.Text = "Godot stopped"; };
        pause.Clicked += (_, _) =>
        {
            paused = !paused;
            game.Dispatch(tree => tree.Paused = paused);
            pause.Text = paused ? "Resume" : "Pause";
        };
        speed.ValueChanged += (_, args) =>
        {
            if (speed.IsEnabled) game.Dispatch(_ => global::Godot.Engine.TimeScale = args.NewValue);
        };

        // A translucent MAUI overlay. Input goes to MAUI controls or to the game beneath.
        var overlay = new HorizontalStackLayout
        {
            Padding = 12, Spacing = 12, VerticalOptions = LayoutOptions.Start,
            BackgroundColor = Color.FromArgb("#B0202B36"), Children = { pause, speed, mode, status },
        };
        var layout = new Grid();
        layout.Children.Add(game);
        layout.Children.Add(overlay);
        Content = layout;
    }
}
