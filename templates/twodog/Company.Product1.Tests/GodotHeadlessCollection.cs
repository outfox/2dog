using twodog.fixture;

namespace Company.Product1.Tests;

[CollectionDefinition("GodotHeadless", DisableParallelization = true)]
public class GodotHeadlessCollection : ICollectionFixture<GodotHeadlessFixture>;
