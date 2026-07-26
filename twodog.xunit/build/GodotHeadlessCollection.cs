using twodog.fixture;
using Xunit;

namespace twodog.xunit;

#pragma warning disable CS0618 // Compatibility collection for the obsolete fixture API.
[System.Obsolete("Use twodog.Testing.Xunit.HeadlessCollection instead.")]
[CollectionDefinition(nameof(GodotHeadlessCollection), DisableParallelization = true)]
public class GodotHeadlessCollection : ICollectionFixture<GodotHeadlessFixture>;
#pragma warning restore CS0618
