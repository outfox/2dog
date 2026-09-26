using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using twodog;
using twodog.fixture;

namespace twodog.tests.EngineTests;

public class AssemblyPreloaderTests
{
    [Fact]
    public void HostShippedGameLoadsItsOwnCopyAndSkipsLeftovers()
    {
        var project = Directory.CreateTempSubdirectory("2dog-preloader-");
        var output = Directory.CreateDirectory(Path.Combine(project.FullName, ".godot", "mono", "temp", "bin", "Debug"));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "showcase.dll"), Path.Combine(output.FullName, "showcase.dll"));
        // A renamed project's output next to the game: this host does not ship it, so it must stay unloaded.
        var leftover = $"leftover{Guid.NewGuid():N}";
        var builder = new PersistedAssemblyBuilder(new AssemblyName(leftover), typeof(object).Assembly);
        builder.DefineDynamicModule(leftover).DefineType("Leftover", TypeAttributes.Public).CreateType();
        builder.Save(Path.Combine(output.FullName, leftover + ".dll"));

        AssemblyPreloader.PreloadGameAssemblies(project.FullName);

        Assert.DoesNotContain(AssemblyLoadContext.Default.Assemblies, a => a.GetName().Name == leftover);
        var game = Assert.Single(AssemblyLoadContext.Default.Assemblies, a => a.GetName().Name == "showcase");
        Assert.Equal(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), Path.GetDirectoryName(game.Location),
            ignoreCase: true);

        // Only on success: a wrongly pre-loaded leftover stays locked on Windows.
        project.Delete(recursive: true);
    }
}
