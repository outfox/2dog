using twodog.fixture;
using Xunit;

namespace twodog.xunit;

[CollectionDefinition(nameof(GodotHeadlessCollection), DisableParallelization = true)]
public class GodotHeadlessCollection : ICollectionFixture<GodotHeadlessFixture>;
