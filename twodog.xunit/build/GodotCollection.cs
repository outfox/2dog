using twodog.fixture;
using Xunit;

namespace twodog.xunit;

[CollectionDefinition(nameof(GodotCollection), DisableParallelization = true)]
public class GodotCollection : ICollectionFixture<GodotFixture>;
