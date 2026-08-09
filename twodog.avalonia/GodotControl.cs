using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace twodog;

/// <summary>
/// Hosts a Godot viewport inside the Avalonia visual tree. Attach a <see cref="GodotSession"/>
/// to show the engine's output; Avalonia controls placed over this control render on top of it.
/// </summary>
public class GodotControl : Control
{
    public static readonly StyledProperty<GodotSession?> SessionProperty =
        AvaloniaProperty.Register<GodotControl, GodotSession?>(nameof(Session));

    private bool _onTree;

    static GodotControl() => FocusableProperty.OverrideDefaultValue<GodotControl>(true);

    /// <summary>The session shown by this control. One control per session at a time.</summary>
    public GodotSession? Session
    {
        get => GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    /// <summary>Latest CPU-presented frame; null in GPU mode (a composition visual renders).</summary>
    internal Bitmap? PresentedFrame { get; set; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SessionProperty && _onTree)
        {
            (change.OldValue as GodotSession)?.Detach(this);
            (change.NewValue as GodotSession)?.Attach(this);
        }
        else if (change.Property == BoundsProperty && _onTree)
        {
            Session?.NotifyControlResized();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _onTree = true;
        Session?.Attach(this);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _onTree = false;
        Session?.Detach(this);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        base.OnPointerPressed(e);
    }

    public override void Render(DrawingContext context)
    {
        if (PresentedFrame is { } frame)
            context.DrawImage(frame, new Rect(frame.Size), new Rect(Bounds.Size));
        else
            context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
    }
}
