using twodog.cli;

namespace twodog.tests.ToolTests;

public class AndroidSdkTests
{
    private static string Sdk(TempProjectDir tmp, string folder = "sdk", bool windows = true)
    {
        tmp.Write($"{folder}/platform-tools/{(windows ? "adb.exe" : "adb")}", "");
        Directory.CreateDirectory(Path.Combine(tmp.Dir, folder, "platforms"));
        Directory.CreateDirectory(Path.Combine(tmp.Dir, folder, "build-tools"));
        return Path.Combine(tmp.Dir, folder);
    }

    [Theory]
    [InlineData("ANDROID_HOME")]
    [InlineData("ANDROID_SDK_ROOT")]
    [InlineData("AndroidSdkDirectory")]
    public void ConfiguredSdk_IsRecognizedWithoutChangingItsSetting(string name)
    {
        using var tmp = new TempProjectDir();
        var sdk = Sdk(tmp);
        var env = new FakeEnvironment();
        env.Vars[name] = sdk;

        var result = AndroidSdk.Inspect(env);

        Assert.Equal(sdk, result.Directory);
        Assert.Empty(result.Warnings);
        Assert.Equal(new Dictionary<string, string> { [name] = sdk }, env.Vars);
    }

    [Theory]
    [InlineData("ANDROID_HOME", "missing")]
    [InlineData("ANDROID_SDK_ROOT", "missing")]
    [InlineData("AndroidSdkDirectory", "missing")]
    [InlineData("ANDROID_HOME", "relative")]
    [InlineData("ANDROID_HOME", "quoted")]
    [InlineData("ANDROID_HOME", "incomplete")]
    public void InvalidOrIncompleteSetting_IsAnActionableWarning(string name, string problem)
    {
        using var tmp = new TempProjectDir();
        var sdk = Sdk(tmp);
        var env = new FakeEnvironment();
        env.Vars[name] = problem switch
        {
            "relative" => "sdk",
            "quoted" => $"\"{sdk}\"",
            "incomplete" => tmp.Dir,
            _ => Path.Combine(tmp.Dir, "missing"),
        };

        var result = AndroidSdk.Inspect(env);

        Assert.Null(result.Directory);
        var warning = Assert.Single(result.Warnings);
        Assert.Contains(name, warning);
        Assert.Contains("Please", warning);
        if (problem == "incomplete")
        {
            Assert.Contains("platform-tools", warning);
            Assert.Contains("platforms", warning);
            Assert.Contains("build-tools", warning);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SdkVariables_MustAgreeButAllowATrailingSeparator(bool different)
    {
        using var tmp = new TempProjectDir();
        var env = new FakeEnvironment();
        env.Vars["ANDROID_HOME"] = Sdk(tmp);
        env.Vars["ANDROID_SDK_ROOT"] = different ? Sdk(tmp, "other") : env.Vars["ANDROID_HOME"] + Path.DirectorySeparatorChar;

        var result = AndroidSdk.Inspect(env);

        if (different)
            Assert.Contains("point at different SDK directories", Assert.Single(result.Warnings));
        else
            Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData("windows")]
    [InlineData("macos")]
    [InlineData("linux")]
    public void StandardInstall_DoesNotRequireEnvironmentOverrides(string os)
    {
        using var tmp = new TempProjectDir();
        var env = new FakeEnvironment(isWindows: os == "windows", isMacOS: os == "macos");
        env.Vars[os == "windows" ? "LOCALAPPDATA" : "HOME"] = tmp.Dir;
        var sdk = Sdk(tmp, os == "macos" ? "Library/Android/sdk" : "Android/Sdk", windows: os == "windows");

        var result = AndroidSdk.Inspect(env);

        Assert.Equal(sdk.Replace('/', Path.DirectorySeparatorChar), result.Directory);
        Assert.Empty(result.Warnings);
        Assert.Single(env.Vars);
    }

    [Fact]
    public void PreferencesDirectory_IsNotMistakenForAnSdk()
    {
        using var tmp = new TempProjectDir();
        var env = new FakeEnvironment();
        env.Vars["ANDROID_SDK_HOME"] = Sdk(tmp);

        var result = AndroidSdk.Inspect(env);

        Assert.Null(result.Directory);
        Assert.Contains("Could not find an Android SDK", Assert.Single(result.Warnings));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void Scaffold_OnlyWarnsForNewAndroidHosts_WithoutChangingPathsOrRunningCommands(bool existing, bool android)
    {
        using var tmp = new TempProjectDir();
        var env = new FakeEnvironment();
        var kind = android ? HostKind.Android : HostKind.Desktop;
        var project = new ProjectContext
        {
            Dir = tmp.Dir, BaseName = "Game",
            ExistingHosts = existing ? [new ExistingHost(kind, "Game.host")] : [],
        };
        var options = new ScaffoldOptions
        {
            DryRun = true, Restore = false,
            Hosts = existing ? [] : [new HostSpec(kind, "Game.host")],
        };
        var runner = new FakeProcessRunner(_ => throw new Exception("SDK detection must not run commands"));
        ScaffoldResult? result = null;

        var run = CliConsole.Capture(() =>
        {
            result = ScaffoldCommand.Run(project, options, workloadRunner: runner, environment: env);
            return result.ExitCode;
        });

        Assert.Equal(ExitCodes.Ok, run.ExitCode);
        Assert.Equal(android && !existing, result!.Warnings.Any(w => w.Contains("Android SDK")));
        Assert.Equal(android && !existing, run.Stderr.Contains("Android SDK"));
        Assert.Empty(env.Vars);
        Assert.Empty(runner.Requests);
        Assert.Empty(Directory.EnumerateFileSystemEntries(tmp.Dir));
    }
}
