using System;
using System.Runtime.ExceptionServices;
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
    private bool _syncingSession;
    private GodotSession? _attachedSession;
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
        if (change.Property == SessionProperty && _onTree)
        {
            SyncSession();
        }
        else if (change.Property == BoundsProperty && _onTree)
        {
            Session?.NotifyControlResized();
        }
    }

    // Reconciles the actually-attached session with the Session property. Attach/Detach raise
    // ActiveModeChanged, whose handlers may reassign Session reentrantly; the guard suppresses
    // nested syncs and the loop applies whatever value the property settled on, so no
    // assignment is silently discarded.
    private void SyncSession()
    {
        if (_syncingSession) return;
        _syncingSession = true;
        ExceptionDispatchInfo? failure = null;
        try
        {
            while (!ReferenceEquals(_attachedSession, Session))
            {
                var previous = _attachedSession;
                var desired = Session;
                previous?.Detach(this);
                _attachedSession = null;
                try
                {
                    desired?.Attach(this);
                    _attachedSession = desired;
                }
                catch (Exception ex)
                {
                    // A failed Attach must not leave Session pointing at a session this
                    // control never attached: restore the previous one, or clear when it
                    // cannot come back (already disposed). The first failure is rethrown
                    // once the state settles.
                    failure ??= ExceptionDispatchInfo.Capture(ex);
                    try { desired?.Detach(this); }
                    catch { }
                    try
                    {
                        previous?.Attach(this);
                        _attachedSession = previous;
                    }
                    catch
                    {
                        try { previous?.Detach(this); }
                        catch { }
                    }
                    // A handler may have reassigned Session during the rollback Attach; that
                    // value wins (the loop attaches it next). Only write the restored value
                    // while the property still holds the session that just failed.
                    if (ReferenceEquals(Session, desired))
                        SetCurrentValue(SessionProperty, _attachedSession);
                }
            }
        }
        finally
        {
            _syncingSession = false;
        }
        failure?.Throw();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null) _topLevel.ScalingChanged += OnScalingChanged;
        _onTree = true;
        SyncSession();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _onTree = false;
        if (_topLevel is not null) _topLevel.ScalingChanged -= OnScalingChanged;
        _topLevel = null;
        _attachedSession?.Detach(this);
        _attachedSession = null;
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
