using twodog.Maui;

namespace Company.Product1.Maui;

public sealed class MainPage : ContentPage
{
    public MainPage()
    {
        var game = new GodotView { Project = "TPLRAWNAME" };
        var status = new Label { Text = "Starting Godot...", TextColor = Colors.White };
        var pause = new Button { Text = "Pause", IsEnabled = false };
        var paused = false;
        game.Started += (_, _) => { pause.IsEnabled = true; status.Text = "Godot is running"; };
        game.Failed += (_, exception) => { pause.IsEnabled = false; status.Text = exception.Message; };
        game.QuitRequested += (_, _) => { pause.IsEnabled = false; status.Text = "Godot stopped"; };
        pause.Clicked += (_, _) =>
        {
            paused = !paused;
            game.Dispatch(tree => tree.Paused = paused);
            pause.Text = paused ? "Resume" : "Pause";
        };

        // A translucent MAUI overlay. Input goes to MAUI controls or to the game beneath.
        var overlay = new HorizontalStackLayout
        {
            Padding = 12, Spacing = 12, VerticalOptions = LayoutOptions.Start,
            BackgroundColor = Color.FromArgb("#B0202B36"), Children = { pause, status },
        };
        var layout = new Grid();
        layout.Children.Add(game);
        layout.Children.Add(overlay);
        Content = layout;
    }
}
