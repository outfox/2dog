using twodog.Testing;
using twodog.Testing.Xunit;
using Xunit.v3;

namespace twodog.tests.EngineTests;

public class CollectionRegistrationTests
{
    [Theory]
    [InlineData(typeof(TypedHeadless), typeof(HeadlessFixture))]
    [InlineData(typeof(NamedHeadless), typeof(HeadlessFixture))]
    [InlineData(typeof(TypedRendering), typeof(Fixture))]
    [InlineData(typeof(NamedRendering), typeof(Fixture))]
    public void TypedAndNamedCollectionsBothDisableParallelization(Type testClass, Type fixture)
    {
        var assembly = typeof(CollectionRegistrationTests).Assembly;
        var factory = new CollectionPerClassTestCollectionFactory(new XunitTestAssembly(assembly, null, assembly.GetName().Version));
        var collection = factory.Get(testClass);
        Assert.True(collection.DisableParallelization);
        Assert.Contains(fixture, collection.CollectionFixtureTypes);
    }

    [Collection<HeadlessCollection>]
    private class TypedHeadless;
    [Collection(nameof(HeadlessCollection))]
    private class NamedHeadless;
    [Collection<RenderingCollection>]
    private class TypedRendering;
    [Collection(nameof(RenderingCollection))]
    private class NamedRendering;
}
