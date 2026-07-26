using twodog.fixture;
using Xunit;

namespace twodog.xunit;

#pragma warning disable CS0618 // Compatibility collection for the obsolete fixture API.
[System.Obsolete("Use twodog.Testing.Xunit.RenderingCollection instead.")]
[CollectionDefinition(nameof(GodotCollection), DisableParallelization = true)]
public class GodotCollection : ICollectionFixture<GodotFixture>;
#pragma warning restore CS0618
