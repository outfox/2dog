using UIKit;

namespace showcase.ios;

public static class Program
{
    // UIKit owns the process; AppDelegate starts Godot once the app finished launching.
    public static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}
