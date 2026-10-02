using Android.App;
using Android.Content;
using Android.OS;

[Activity(Label = "2dog Android smoke", MainLauncher = true, Exported = true)]
public class MainActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var intent = new Intent();
        intent.SetClassName(PackageName!, "dev.twodog.host.TwoDogActivity");
        StartActivity(intent);
        Finish();
    }
}
