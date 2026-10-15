using System.Reflection;
using Microsoft.Testing.Platform.Builder;
using Company.Product1.GdUnit;
using twodog.GdUnit;

var project = Assembly.GetExecutingAssembly()
    .GetCustomAttributes<AssemblyMetadataAttribute>()
    .Single(attribute => attribute.Key == "GodotProject").Value!;
var builder = await TestApplication.CreateBuilderAsync(args);
builder.AddSelfRegisteredExtensions(args);
builder.AddGdUnit(new GdUnitOptions
{
    ProjectPath = project,
    TestRoots = ["TPLRAWNAME.gdunit/tests"]
});
using var application = await builder.BuildAsync();
return await application.RunAsync();
