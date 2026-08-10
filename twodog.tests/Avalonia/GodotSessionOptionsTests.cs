using twodog;

namespace twodog.tests.AvaloniaTests;

// Pure option-validation tests; no engine and no Avalonia platform involved.
public class GodotSessionOptionsTests
{
    [Fact]
    public void MaxFramesPerSecond_DefaultsToAuto() =>
        Assert.Equal(0, new GodotSessionOptions { Project = "test" }.MaxFramesPerSecond);

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-1)]
    [InlineData(double.NegativeInfinity)]
    public void MaxFramesPerSecond_RejectsInvalid(double value) =>
        Assert.Throws<ArgumentException>(() =>
            new GodotSessionOptions { Project = "test", MaxFramesPerSecond = value });

    [Theory]
    [InlineData(0)]
    [InlineData(59.94)]
    [InlineData(double.PositiveInfinity)]
    public void MaxFramesPerSecond_AcceptsAutoPositiveAndUncapped(double value) =>
        Assert.Equal(value,
            new GodotSessionOptions { Project = "test", MaxFramesPerSecond = value }.MaxFramesPerSecond);

    [Fact]
    public void DetachedFramesPerSecond_RejectsNaN() =>
        Assert.Throws<ArgumentException>(() =>
            new GodotSessionOptions { Project = "test", DetachedFramesPerSecond = double.NaN });
}
