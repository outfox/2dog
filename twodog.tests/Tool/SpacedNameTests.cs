using twodog.cli;

namespace twodog.tests.ToolTests;

// Coverage for the spaced-name guard and the --rename fix: whitespace in a
// project's .NET restore identity makes `dotnet publish` of a referencing
// host silently drop the game's transitive NuGet packages (dotnet/sdk parses
// the assets file's dependency strings up to the first whitespace), so the
// tool refuses such names and offers to fix them.

public class SetAssemblyNameTests
{
    [Fact]
    public void ReplacesExistingValue_LeavingOtherBytesUntouched()
    {
        using var tmp = new TempProjectDir();
        const string original =
            "config_version=5\n\n[application]\n\nconfig/name=\"Fast Dragon\"\n\n" +
            "[dotnet]\n\nproject/assembly_name=\"Fast Dragon\"\n\n[rendering]\n\nquality=2\n";
        var path = tmp.Write("project.godot", original);

        new GodotProjectFile(path).SetAssemblyName("FastDragon");

        var text = File.ReadAllText(path);
        Assert.Equal(original.Replace("assembly_name=\"Fast Dragon\"", "assembly_name=\"FastDragon\""), text);
        Assert.Equal("FastDragon", new GodotProjectFile(path).Get("dotnet", "project/assembly_name"));
        // The display name is deliberately not touched.
        Assert.Equal("Fast Dragon", new GodotProjectFile(path).Get("application", "config/name"));
    }

    [Fact]
    public void InsertsIntoExistingSectionWithoutTheKey()
    {
        using var tmp = new TempProjectDir();
        var path = tmp.Write("project.godot", "config_version=5\n\n[dotnet]\n\n[rendering]\n\nquality=2\n");

        new GodotProjectFile(path).SetAssemblyName("FastDragon");

        var file = new GodotProjectFile(path);
        Assert.Equal("FastDragon", file.Get("dotnet", "project/assembly_name"));
        Assert.Equal("2", file.Get("rendering", "quality"));
    }

    [Fact]
    public void AppendsSectionWhenMissing()
    {
        using var tmp = new TempProjectDir();
        const string original = "config_version=5\n\n[application]\n\nconfig/name=\"Game\"\n";
        var path = tmp.Write("project.godot", original);

        new GodotProjectFile(path).SetAssemblyName("Game");

        Assert.StartsWith(original, File.ReadAllText(path));
        Assert.Equal("Game", new GodotProjectFile(path).Get("dotnet", "project/assembly_name"));
    }
}

public class SolutionRenameProjectTests
{
    [Fact]
    public void Slnx_RepointsTheProjectPath()
    {
        using var tmp = new TempProjectDir();
        var path = tmp.Write("Game.slnx",
            "<Solution>\n  <Project Path=\"Fast Dragon.csproj\" />\n  <Project Path=\"Other/Other.csproj\" />\n</Solution>\n");

        Assert.True(SolutionOps.RenameProject(path, "Fast Dragon", "FastDragon"));

        var text = File.ReadAllText(path);
        Assert.Contains("Path=\"FastDragon.csproj\"", text);
        Assert.DoesNotContain("Fast Dragon.csproj", text);
        Assert.Contains("Other/Other.csproj", text);
    }

    [Fact]
    public void ClassicSln_RepointsPathAndDisplayName()
    {
        using var tmp = new TempProjectDir();
        var path = tmp.Write("Game.sln",
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Fast Dragon\", \"Fast Dragon.csproj\", \"{11111111-2222-3333-4444-555555555555}\"\nEndProject\n");

        Assert.True(SolutionOps.RenameProject(path, "Fast Dragon", "FastDragon"));

        var text = File.ReadAllText(path);
        Assert.Contains("= \"FastDragon\", \"FastDragon.csproj\"", text);
        Assert.DoesNotContain("Fast Dragon", text);
    }

    [Fact]
    public void NoReference_ReturnsFalse()
    {
        using var tmp = new TempProjectDir();
        var path = tmp.Write("Game.slnx", "<Solution>\n  <Project Path=\"Other.csproj\" />\n</Solution>\n");
        Assert.False(SolutionOps.RenameProject(path, "Fast Dragon", "FastDragon"));
    }
}

public class SpacedIdentityTests
{
    private static string? Identity(TempProjectDir tmp, string projectGodot)
    {
        var path = tmp.Write("project.godot", projectGodot);
        return ScaffoldCommand.SpacedIdentity(tmp.Dir, new GodotProjectFile(path));
    }

    [Fact]
    public void SpacedAssemblyName_IsDetected()
    {
        using var tmp = new TempProjectDir();
        Assert.Equal("Fast Dragon", Identity(tmp, "[dotnet]\nproject/assembly_name=\"Fast Dragon\"\n"));
    }

    [Fact]
    public void SpacedSoleCsproj_IsDetected()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("Fast Dragon.csproj", "<Project/>");
        Assert.Equal("Fast Dragon", Identity(tmp, "config_version=5\n"));
    }

    [Fact]
    public void CleanProject_IsNull()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("FastDragon.csproj", "<Project/>");
        Assert.Null(Identity(tmp, "[dotnet]\nproject/assembly_name=\"FastDragon\"\n"));
    }

    [Fact]
    public void SpacedDisplayNameAlone_IsNull()
    {
        // config/name is only ever a sanitized fallback; spaces there are fine.
        using var tmp = new TempProjectDir();
        Assert.Null(Identity(tmp, "[application]\nconfig/name=\"Fast Dragon\"\n"));
    }
}

public class SpacedNameGuardTests
{
    private const string SpacedProject =
        "[application]\nconfig/name=\"Fast Dragon\"\n\n[dotnet]\nproject/assembly_name=\"Fast Dragon\"\n";

    private static ProjectContext Open(TempProjectDir tmp, string? renameTo = null, string? nameOverride = null) =>
        ScaffoldCommand.Open(new ScaffoldOptions
        {
            ProjectPath = tmp.Dir, RenameTo = renameTo, NameOverride = nameOverride,
        });

    private static TempProjectDir SpacedTemp(bool withCsproj = true)
    {
        var tmp = new TempProjectDir();
        tmp.Write("project.godot", SpacedProject);
        if (withCsproj) tmp.Write("Fast Dragon.csproj", "<Project/>");
        return tmp;
    }

    [Fact]
    public void NoHosts_ThrowsOfferingRename()
    {
        using var tmp = SpacedTemp();
        var ex = Assert.Throws<SpacedNameException>(() => Open(tmp));
        Assert.True(ex.CanOfferRename);
        Assert.Equal("Fast Dragon", ex.OldName);
        Assert.Equal("FastDragon", ex.Suggested);
        Assert.Contains("--rename FastDragon", ex.Message);
        Assert.Contains("rename 'Fast Dragon.csproj' to 'FastDragon.csproj'", ex.Message);
    }

    [Fact]
    public void WithHosts_ThrowsChecklistWithoutTheOffer()
    {
        using var tmp = SpacedTemp();
        tmp.Write("Fast Dragon.2dog/Fast Dragon.2dog.csproj",
            "<Project><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>" +
            "<ItemGroup><PackageReference Include=\"2dog.engine\"/></ItemGroup></Project>");

        var ex = Assert.Throws<ToolException>(() => Open(tmp));
        Assert.IsNotType<SpacedNameException>(ex);
        Assert.Contains("in each host csproj", ex.Message);
        Assert.DoesNotContain("Or let 2dog do it", ex.Message);
    }

    [Fact]
    public void RenameWithHosts_ExplainsTheLimit()
    {
        using var tmp = SpacedTemp();
        tmp.Write("Fast Dragon.2dog/Fast Dragon.2dog.csproj",
            "<Project><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>" +
            "<ItemGroup><PackageReference Include=\"2dog.engine\"/></ItemGroup></Project>");

        var ex = Assert.Throws<ToolException>(() => Open(tmp, renameTo: "FastDragon"));
        Assert.Contains("--rename only works before any 2dog hosts exist", ex.Message);
    }

    [Fact]
    public void ValidRename_PopulatesTheOperation()
    {
        using var tmp = SpacedTemp();
        var project = Open(tmp, renameTo: "FastDragon");
        Assert.Equal("FastDragon", project.BaseName);
        Assert.Equal(new RenameOperation("Fast Dragon", "FastDragon", CsprojExists: true), project.Rename);
    }

    [Fact]
    public void RenameToAnotherSpacedName_Throws()
    {
        using var tmp = SpacedTemp();
        var ex = Assert.Throws<ToolException>(() => Open(tmp, renameTo: "Still Bad"));
        Assert.Contains("not a usable name", ex.Message);
    }

    [Fact]
    public void RenameCollidingWithExistingCsproj_Throws()
    {
        using var tmp = SpacedTemp();
        tmp.Write("FastDragon.csproj", "<Project/>");
        var ex = Assert.Throws<ToolException>(() => Open(tmp, renameTo: "FastDragon"));
        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public void RenameConflictingWithName_Throws()
    {
        using var tmp = SpacedTemp();
        var ex = Assert.Throws<ToolException>(() => Open(tmp, renameTo: "FastDragon", nameOverride: "Other"));
        Assert.Contains("--name", ex.Message);
        Assert.Contains("--rename", ex.Message);
    }

    [Fact]
    public void RenameOnACleanProject_Throws()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", "[dotnet]\nproject/assembly_name=\"FastDragon\"\n");
        tmp.Write("FastDragon.csproj", "<Project/>");
        var ex = Assert.Throws<ToolException>(() => Open(tmp, renameTo: "Whatever"));
        Assert.Contains("only for projects whose .NET name contains whitespace", ex.Message);
    }
}

public class RenameEndToEndTests
{
    private static readonly string SpacedCsproj =
        $"""
        <Project Sdk="Godot.NET.Sdk/{ToolVersions.GodotSdkVersion}">
            <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <!-- fixture marker: the user's own csproj content -->
            </PropertyGroup>
        </Project>
        """;

    private const string SpacedProject =
        "config_version=5\n\n[application]\n\nconfig/name=\"Fast Dragon\"\n\n" +
        "[dotnet]\n\nproject/assembly_name=\"Fast Dragon\"\n";

    private static TempProjectDir SpacedTemp()
    {
        var tmp = new TempProjectDir();
        tmp.Write("project.godot", SpacedProject);
        tmp.Write("Fast Dragon.csproj", SpacedCsproj);
        tmp.Write("Fast Dragon.slnx", "<Solution>\n  <Project Path=\"Fast Dragon.csproj\" />\n</Solution>\n");
        return tmp;
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(TempProjectDir tmp, bool dryRun = false)
    {
        var options = new ScaffoldOptions
        {
            ProjectPath = tmp.Dir, RenameTo = "FastDragon", Restore = false, DryRun = dryRun,
        };
        var project = ScaffoldCommand.Open(options);
        var excluded = Hosts.All.Where(k => k != HostKind.Desktop).ToList();
        options.Hosts = HostSelection.Defaults(excluded, project);
        return CliConsole.Capture(() => ScaffoldCommand.Run(project, options));
    }

    [Fact]
    public void Rename_MovesCsprojUpdatesGodotAndSolution_AndScaffoldsUnderTheNewName()
    {
        using var tmp = SpacedTemp();
        Assert.Equal(0, Run(tmp).ExitCode);

        // The user's csproj moved (its content patched, not replaced by a template).
        Assert.False(File.Exists(Path.Combine(tmp.Dir, "Fast Dragon.csproj")));
        var csproj = File.ReadAllText(Path.Combine(tmp.Dir, "FastDragon.csproj"));
        Assert.Contains("fixture marker", csproj);
        Assert.Contains("<EnableDynamicLoading>true</EnableDynamicLoading>", csproj);

        // project.godot: .NET identity fixed, display name untouched.
        var godot = new GodotProjectFile(Path.Combine(tmp.Dir, "project.godot"));
        Assert.Equal("FastDragon", godot.Get("dotnet", "project/assembly_name"));
        Assert.Equal("Fast Dragon", godot.Get("application", "config/name"));

        // Solution repointed (dotnet sln add may rewrite formatting; only the
        // reference matters).
        var slnx = File.ReadAllText(Path.Combine(tmp.Dir, "Fast Dragon.slnx"));
        Assert.Contains("FastDragon.csproj", slnx);
        Assert.DoesNotContain("\"Fast Dragon.csproj\"", slnx);

        // The host was scaffolded against the new name.
        var host = File.ReadAllText(Path.Combine(tmp.Dir, "FastDragon.2dog", "FastDragon.2dog.csproj"));
        Assert.Contains("../FastDragon.csproj", host);
        Assert.DoesNotContain("TPLRAWNAME", host);
        Assert.DoesNotContain("Company.Product1", host);
    }

    [Fact]
    public void DryRun_PlansTheRenameButChangesNothing()
    {
        using var tmp = SpacedTemp();
        var before = Directory.EnumerateFiles(tmp.Dir, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(f => f + "\n" + File.ReadAllText(f))
            .ToList();

        var (exitCode, stdout, _) = Run(tmp, dryRun: true);

        Assert.Equal(0, exitCode);
        Assert.Contains("would: rename Fast Dragon.csproj to FastDragon.csproj", stdout);
        Assert.Contains("would: set [dotnet] assembly_name=\"FastDragon\" in project.godot", stdout);
        Assert.Equal(before, Directory.EnumerateFiles(tmp.Dir, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(f => f + "\n" + File.ReadAllText(f))
            .ToList());
    }

    [Fact]
    public void SpacedAssemblyNameWithoutCsproj_ScaffoldsTheNewNameAndFixesGodot()
    {
        using var tmp = new TempProjectDir();
        tmp.Write("project.godot", SpacedProject);

        var options = new ScaffoldOptions { ProjectPath = tmp.Dir, RenameTo = "FastDragon", Restore = false };
        var project = ScaffoldCommand.Open(options);
        options.Hosts = [];
        Assert.Equal(0, CliConsole.Capture(() => ScaffoldCommand.Run(project, options)).ExitCode);

        Assert.True(File.Exists(Path.Combine(tmp.Dir, "FastDragon.csproj")));
        var text = File.ReadAllText(Path.Combine(tmp.Dir, "project.godot"));
        Assert.Equal("FastDragon", new GodotProjectFile(Path.Combine(tmp.Dir, "project.godot"))
            .Get("dotnet", "project/assembly_name"));
        // No duplicate [dotnet] section from the create-branch append.
        Assert.Single(text.Split("[dotnet]")[1..]);
    }
}

public class RenameCommandLineTests
{
    [Fact]
    public void Rename_Parses()
    {
        var cmd = CommandLine.Parse(["add", "--rename", "FastDragon"]);
        Assert.Equal("FastDragon", cmd.Options.RenameTo);
    }

    [Fact]
    public void Rename_WithoutValue_IsAUsageError()
    {
        Assert.Throws<UsageException>(() => CommandLine.Parse(["add", "--rename"]));
    }

    [Fact]
    public void Rename_OnNew_IsAUsageError()
    {
        var ex = Assert.Throws<UsageException>(() => CommandLine.Parse(["new", "X", "--rename", "Y"]));
        Assert.Contains("add/convert only", ex.Message);
    }
}

public class NewNameAnnouncementTests
{
    [Fact]
    public void SanitizedNewName_IsAnnounced()
    {
        using var tmp = new TempProjectDir();
        var options = new ScaffoldOptions
        {
            CreateProject = true, NameOverride = "My Game!",
            ProjectPath = Path.Combine(tmp.Dir, "MyGame"),
        };

        ProjectContext? project = null;
        var (_, stdout, _) = CliConsole.Capture(() =>
        {
            project = ScaffoldCommand.Open(options);
            return 0;
        });

        Assert.Equal("MyGame", project!.BaseName);
        Assert.Contains("project name adjusted: 'My Game!' -> 'MyGame'", stdout);
    }

    [Fact]
    public void PrepareNewProject_DefaultsTheDirectoryToTheSanitizedName()
    {
        var cmd = CommandLine.Parse(["new", "My Game!", "--desktop", "--no-restore"]);
        Program.PrepareNewProject(cmd, interactive: false);
        Assert.Equal("MyGame", cmd.Options.ProjectPath);
        Assert.Equal("My Game!", cmd.Options.NameOverride);
    }
}
