using twodog.Testing;
using Xunit;

namespace twodog.Testing.Xunit;

/// <summary>Non-parallel xUnit collection backed by a headless fixture.</summary>
[CollectionDefinition(DisableParallelization = true)]
public class HeadlessCollection : ICollectionFixture<HeadlessFixture>;

/// <summary>Compatibility definition for tests using the string collection name.</summary>
[CollectionDefinition(nameof(HeadlessCollection), DisableParallelization = true)]
public class NamedHeadlessCollection : ICollectionFixture<HeadlessFixture>;
