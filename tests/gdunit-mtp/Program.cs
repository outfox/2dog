using System.Reflection;
using GdUnitMtp;
using Microsoft.Testing.Platform.Builder;
using twodog.GdUnit;

var metadata = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
    .ToDictionary(item => item.Key, item => item.Value!);
var builder = await TestApplication.CreateBuilderAsync(args);
builder.AddSelfRegisteredExtensions(args);
builder.AddGdUnit(new GdUnitOptions
{
    ProjectPath = Environment.GetEnvironmentVariable("GDUNIT_TEST_PROJECT") ?? metadata["GameProject"],
    TestRoots = (Environment.GetEnvironmentVariable("GDUNIT_TEST_ROOTS") ?? metadata["TestRoot"]).Split(';'),
    ArtifactsDirectory = Environment.GetEnvironmentVariable("GDUNIT_TEST_ARTIFACTS")
        ?? Path.Combine(AppContext.BaseDirectory, "TestResults", "gdunit"),
    SessionTimeout = TimeSpan.FromMilliseconds(int.Parse(Environment.GetEnvironmentVariable("GDUNIT_TEST_TIMEOUT_MS") ?? "300000"))
});
using var application = await builder.BuildAsync();
return await application.RunAsync();
