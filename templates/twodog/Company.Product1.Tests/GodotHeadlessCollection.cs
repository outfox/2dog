using twodog.xunit;
using Xunit;

namespace Company.Product1.Tests;

[CollectionDefinition("GodotHeadless", DisableParallelization = true)]
public class GodotHeadlessCollection : ICollectionFixture<GodotHeadlessFixture>;
