using System;
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
    private bool _restoringSession;
    private TopLevel? _topLevel;

    static GodotControl() => FocusableProperty.OverrideDefaultValue<GodotControl>(true);

    /// <summary>The session shown by this control. One control per session at a time.</summary>
    public GodotSession? Session
    {
        get => GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    /// <summary>Latest CPU-presented frame; null in GPU mode (a composition visual renders).</summary>
    internal Bitmap? PresentedFrame { get; set; }

    // Window sizing and input DIP-to-pixel conversion must agree on the scale; both read it here.
    internal double RenderScaling => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SessionProperty && _onTree && !_restoringSession)
        {
            var oldSession = change.OldValue as GodotSession;
            oldSession?.Detach(this);
            try
            {
                (change.NewValue as GodotSession)?.Attach(this);
            }
            catch
            {
                try { (change.NewValue as GodotSession)?.Detach(this); }
                catch { }
                RestoreSession(oldSession);
                throw;
            }
        }
        else if (change.Property == BoundsProperty && _onTree)
        {
            Session?.NotifyControlResized();
        }
    }

    // A failed Attach must not leave Session pointing at a session this control never attached:
    // restore the previous one, or clear when it cannot come back (already disposed).
    private void RestoreSession(GodotSession? previous)
    {
        _restoringSession = true;
        try
        {
            try { previous?.Attach(this); }
            catch
            {
                try { previous?.Detach(this); }
                catch { }
                previous = null;
            }
            SetCurrentValue(SessionProperty, previous);
        }
        finally
        {
            _restoringSession = false;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null) _topLevel.ScalingChanged += OnScalingChanged;
        _onTree = true;
        Session?.Attach(this);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _onTree = false;
        if (_topLevel is not null) _topLevel.ScalingChanged -= OnScalingChanged;
        _topLevel = null;
        Session?.Detach(this);
        base.OnDetachedFromVisualTree(e);
    }

    private void OnScalingChanged(object? sender, EventArgs e) => Session?.NotifyControlResized();

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
