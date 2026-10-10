using twodog.Testing;
using Xunit;

namespace twodog.Testing.Xunit;

/// <summary>Non-parallel xUnit collection backed by a rendering-enabled fixture.</summary>
[CollectionDefinition(DisableParallelization = true)]
public class RenderingCollection : ICollectionFixture<Fixture>;

/// <summary>Compatibility definition for tests using the string collection name.</summary>
[CollectionDefinition(nameof(RenderingCollection), DisableParallelization = true)]
public class NamedRenderingCollection : ICollectionFixture<Fixture>;
