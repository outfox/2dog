namespace twodog.tests.HostingTests;

/// <summary>Direct tests of the slot-explicit pool core (internal). Each test
/// uses its own random source file, so its pool key never collides with the
/// engine copies other tests create.</summary>
public sealed class NativePoolTests : IDisposable
{
    private readonly string _source;
    private readonly List<string> _copies = [];

    public NativePoolTests()
    {
        _source = Path.Combine(Path.GetTempPath(), $"2dog-pool-test-{Guid.NewGuid():N}.bin");
        var payload = new byte[4096];
        Random.Shared.NextBytes(payload);
        File.WriteAllBytes(_source, payload);
    }

    public void Dispose()
    {
        File.Delete(_source);
        // Slot dirs and the per-source identity dir; quiet best-effort cleanup.
        foreach (var keyDir in _copies.Select(c => Path.GetDirectoryName(Path.GetDirectoryName(c))!).Distinct())
        {
            try
            {
                Directory.Delete(keyDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private string Acquire(int slot)
    {
        var copy = EngineHost.AcquireNativeCopy(_source, slot);
        lock (_copies)
        {
            _copies.Add(copy);
        }
        return copy;
    }

    [Fact]
    public void CopiesCarrySlotDistinctNamesAndFullContent()
    {
        var copy = Acquire(91_000);
        Assert.NotEqual(_source, copy);
        Assert.Contains("slot-91000", copy);
        Assert.Equal(File.ReadAllBytes(_source), File.ReadAllBytes(copy));
    }

    [Fact]
    public void PartialCopiesAreEvicted()
    {
        var copy = Acquire(91_001);
        File.WriteAllBytes(copy, new byte[16]); // truncated leftover of a crashed process
        var again = Acquire(91_001);
        Assert.Equal(copy, again);
        Assert.Equal(new FileInfo(_source).Length, new FileInfo(again).Length);
    }

    [Fact]
    public void ConcurrentSlotsYieldDistinctVerifiedCopies()
    {
        var copies = Enumerable.Range(91_100, 8)
            .AsParallel().WithDegreeOfParallelism(8)
            .Select(Acquire)
            .ToArray();
        Assert.Equal(copies.Length, copies.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(copies, c => Assert.Equal(new FileInfo(_source).Length, new FileInfo(c).Length));
    }
}
