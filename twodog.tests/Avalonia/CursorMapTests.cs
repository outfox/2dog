using Avalonia.Input;
using Godot;
using twodog;

namespace twodog.tests.AvaloniaTests;

// Pure mapping table - no engine, no Avalonia platform; runs in parallel.
public class CursorMapTests
{
    [Theory]
    [InlineData(DisplayServer.CursorShape.Arrow, StandardCursorType.Arrow)]
    [InlineData(DisplayServer.CursorShape.Ibeam, StandardCursorType.Ibeam)]
    [InlineData(DisplayServer.CursorShape.PointingHand, StandardCursorType.Hand)]
    [InlineData(DisplayServer.CursorShape.Forbidden, StandardCursorType.No)]
    [InlineData(DisplayServer.CursorShape.Vsize, StandardCursorType.SizeNorthSouth)]
    [InlineData(DisplayServer.CursorShape.Hsize, StandardCursorType.SizeWestEast)]
    [InlineData(DisplayServer.CursorShape.Move, StandardCursorType.SizeAll)]
    [InlineData(DisplayServer.CursorShape.Help, StandardCursorType.Help)]
    public void Shape_SpotChecks(DisplayServer.CursorShape shape, StandardCursorType expected) =>
        Assert.Equal(expected, CursorMap.ToStandardType(shape));

    [Fact]
    public void EveryShape_HasAnExplicitMapping()
    {
        // Arrow doubles as the fallback, so among known shapes only Arrow itself may map to it.
        for (var shape = DisplayServer.CursorShape.Arrow; shape < DisplayServer.CursorShape.Max; shape++)
        {
            var mapped = CursorMap.ToStandardType(shape);
            Assert.Equal(shape == DisplayServer.CursorShape.Arrow, mapped == StandardCursorType.Arrow);
        }
    }
}
