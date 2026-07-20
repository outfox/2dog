using System.Runtime.CompilerServices;
using Godot.NativeInterop;

namespace Godot;

/// <summary>GodotSharp-compatible awaitable/awaiter interfaces.</summary>
public interface IAwaitable<out TResult>
{
    IAwaiter<TResult> GetAwaiter();
}

public interface IAwaiter<out TResult> : INotifyCompletion
{
    bool IsCompleted { get; }
    TResult GetResult();
}

/// <summary>
/// Awaits a signal emission: <c>await node.ToSignal(source, "signal")</c>.
///
/// Connects ONE_SHOT with a custom callable whose closure roots this awaiter -
/// the engine-side connection owns the delegate lifetime, so the awaiter stays
/// alive until the signal fires (the connection auto-disconnects and the
/// engine's free_func releases everything) or the source dies (the await then
/// never resumes, matching GodotSharp).
///
/// The signal callback runs on the engine thread during emission, and the
/// continuation is invoked inline there - awaited code resumes on the main
/// thread with no marshalling.
/// </summary>
public sealed class SignalAwaiter : IAwaiter<Variant[]>, IAwaitable<Variant[]>
{
    private readonly Lock _gate = new();
    private bool _completed;
    private Variant[] _result = [];
    private Action? _continuation;

    public SignalAwaiter(GodotObject source, string signal)
    {
        // The handler delegate closes over `this`: the engine connection keeps
        // the awaiter reachable until fired.
        var raw = (Action<nint, long>)OnSignal;
        using var callable = Callable.FromSignalHandler(raw, static (d, a, n) => ((Action<nint, long>)d)(a, n));
        var err = source.Connect(signal, callable, (uint)GodotObject.ConnectFlags.OneShot);
        if (err != Error.Ok)
        {
            throw new InvalidOperationException($"ToSignal: cannot connect to '{signal}' on {source.GetClass()} ({err}).");
        }
    }

    private void OnSignal(nint args, long argCount)
    {
        var result = new Variant[argCount];
        for (var i = 0; i < argCount; i++)
        {
            result[i] = SignalArg.VariantAt(args, i); // owned copies
        }

        Action? continuation;
        lock (_gate)
        {
            _result = result;
            _completed = true;
            continuation = _continuation;
            _continuation = null;
        }
        continuation?.Invoke();
    }

    public bool IsCompleted
    {
        get
        {
            lock (_gate)
            {
                return _completed;
            }
        }
    }

    public void OnCompleted(Action continuation)
    {
        lock (_gate)
        {
            if (!_completed)
            {
                _continuation = continuation;
                return;
            }
        }
        continuation();
    }

    /// <summary>The emitted signal arguments (owned; dispose non-POD contents if needed).</summary>
    public Variant[] GetResult()
    {
        lock (_gate)
        {
            return _result;
        }
    }

    public IAwaiter<Variant[]> GetAwaiter() => this;
}
