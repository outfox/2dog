using twodog.Testing;
using Xunit;

namespace twodog.Testing.Xunit;

/// <summary>Non-parallel xUnit collection backed by a rendering-enabled fixture.</summary>
[CollectionDefinition(nameof(RenderingCollection), DisableParallelization = true)]
public class RenderingCollection : ICollectionFixture<Fixture>;
