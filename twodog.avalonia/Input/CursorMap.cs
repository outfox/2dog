using System.Collections.Generic;
using Avalonia.Input;
using Godot;

namespace twodog;

/// <summary>Maps the engine's requested cursor shape to an Avalonia cursor, cached per shape.</summary>
internal static class CursorMap
{
    private static readonly Dictionary<DisplayServer.CursorShape, Cursor> Cache = [];

    public static Cursor ToCursor(DisplayServer.CursorShape shape)
    {
        if (Cache.TryGetValue(shape, out var cached)) return cached;
        var cursor = new Cursor(ToStandardType(shape));
        Cache[shape] = cursor;
        return cursor;
    }

    internal static StandardCursorType ToStandardType(DisplayServer.CursorShape shape) => shape switch
    {
        DisplayServer.CursorShape.Arrow => StandardCursorType.Arrow,
        DisplayServer.CursorShape.Ibeam => StandardCursorType.Ibeam,
        DisplayServer.CursorShape.PointingHand => StandardCursorType.Hand,
        DisplayServer.CursorShape.Cross => StandardCursorType.Cross,
        DisplayServer.CursorShape.Wait => StandardCursorType.Wait,
        DisplayServer.CursorShape.Busy => StandardCursorType.AppStarting,
        DisplayServer.CursorShape.Drag => StandardCursorType.DragMove,
        DisplayServer.CursorShape.CanDrop => StandardCursorType.DragCopy,
        DisplayServer.CursorShape.Forbidden => StandardCursorType.No,
        DisplayServer.CursorShape.Vsize => StandardCursorType.SizeNorthSouth,
        DisplayServer.CursorShape.Hsize => StandardCursorType.SizeWestEast,
        DisplayServer.CursorShape.Bdiagsize => StandardCursorType.BottomLeftCorner,
        DisplayServer.CursorShape.Fdiagsize => StandardCursorType.BottomRightCorner,
        DisplayServer.CursorShape.Move => StandardCursorType.SizeAll,
        DisplayServer.CursorShape.Vsplit => StandardCursorType.SizeNorthSouth,
        DisplayServer.CursorShape.Hsplit => StandardCursorType.SizeWestEast,
        DisplayServer.CursorShape.Help => StandardCursorType.Help,
        _ => StandardCursorType.Arrow,
    };
}
