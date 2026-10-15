using System.Collections.Concurrent;
using Android.Runtime;
using Android.Widget;
using Godot;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Handlers;

namespace twodog.Maui;

internal sealed class GodotViewHandler : ViewHandler<GodotView, FrameLayout>
{
    private Java.Lang.Object? _fragment;
    private Java.Lang.Runnable? _started, _quit;
    private readonly ConcurrentDictionary<int, Java.Lang.Runnable> _commands = new();
    private int _sequence;
    private volatile bool _ready;

    public GodotViewHandler() : base(ViewMapper) { }
    protected override FrameLayout CreatePlatformView() => new(Context);

    protected override void ConnectHandler(FrameLayout platformView)
    {
        base.ConnectHandler(platformView);
        platformView.ViewAttachedToWindow += Attached;
        platformView.ViewDetachedFromWindow += Detached;
    }

    private void Attached(object? sender, Android.Views.View.ViewAttachedToWindowEventArgs args)
    {
        if (_fragment is not null) return;
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(VirtualView.Project);
            var activity = Platform.CurrentActivity
                ?? throw new InvalidOperationException("The MAUI Activity is not available.");
            var view = VirtualView;
            _started = new Java.Lang.Runnable(() =>
            {
                _ready = true;
                view.ActivePresentationMode = GodotPresentationMode.Gpu;
                view.NotifyStarted();
            });
            _quit = new Java.Lang.Runnable(() =>
            {
                _ready = false;
                view.ActivePresentationMode = null;
                view.NotifyQuit();
            });
            using var type = Java.Lang.Class.ForName("dev.twodog.maui.TwoDogMauiFragment");
            var method = JNIEnv.GetStaticMethodID(type.Handle, "attach",
                "(Landroidx/fragment/app/FragmentActivity;Landroid/widget/FrameLayout;[Ljava/lang/String;Ljava/lang/Runnable;Ljava/lang/Runnable;)Ldev/twodog/maui/TwoDogMauiFragment;");
            var arguments = JNIEnv.NewArray(VirtualView.Arguments);
            try
            {
                var handle = JNIEnv.CallStaticObjectMethod(type.Handle, method,
                    [new JValue(activity), new JValue(PlatformView), new JValue(arguments), new JValue(_started), new JValue(_quit)]);
                _fragment = Java.Lang.Object.GetObject<Java.Lang.Object>(handle, JniHandleOwnership.TransferLocalRef);
            }
            finally { JNIEnv.DeleteLocalRef(arguments); }
        }
        catch (Exception exception)
        {
            Detach();
            VirtualView.NotifyFailure(exception);
        }
    }

    public void Dispatch(Action<SceneTree> action)
    {
        if (!_ready || _fragment is null) throw new InvalidOperationException("Godot has not started.");
        var view = VirtualView;
        var id = Interlocked.Increment(ref _sequence);
        var command = new Java.Lang.Runnable(() =>
        {
            try
            {
                if (global::Godot.Engine.GetMainLoop() is SceneTree tree) action(tree);
            }
            catch (Exception exception) { view.NotifyFailure(exception); }
            finally
            {
                if (_commands.TryRemove(id, out var completed)) completed.Dispose();
            }
        });
        _commands[id] = command;
        using var type = Java.Lang.Class.ForName("dev.twodog.maui.TwoDogMauiFragment");
        var method = JNIEnv.GetMethodID(type.Handle, "dispatch", "(Ljava/lang/Runnable;)V");
        try { JNIEnv.CallVoidMethod(_fragment.Handle, method, [new JValue(command)]); }
        catch
        {
            _commands.TryRemove(id, out _);
            command.Dispose();
            throw;
        }
    }

    private void Detached(object? sender, Android.Views.View.ViewDetachedFromWindowEventArgs args) => Detach();
    private void Detach()
    {
        _ready = false;
        VirtualView.ActivePresentationMode = null;
        if (_fragment is not null)
        {
            using var type = Java.Lang.Class.ForName("dev.twodog.maui.TwoDogMauiFragment");
            var method = JNIEnv.GetMethodID(type.Handle, "detach", "()V");
            JNIEnv.CallVoidMethod(_fragment.Handle, method);
            _fragment.Dispose();
            _fragment = null;
        }
        _started?.Dispose();
        _started = null;
        _quit?.Dispose();
        _quit = null;
    }

    protected override void DisconnectHandler(FrameLayout platformView)
    {
        platformView.ViewAttachedToWindow -= Attached;
        platformView.ViewDetachedFromWindow -= Detached;
        Detach();
        base.DisconnectHandler(platformView);
    }
}
