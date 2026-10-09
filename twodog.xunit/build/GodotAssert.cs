using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using twodog.Testing;
using Xunit;
using Xunit.Sdk;

namespace twodog.Testing.Xunit;

/// <summary>Bounded waits which advance Godot on the fixture's owner thread.</summary>
public static class GodotTestExtensions
{
    private sealed class PumpState { public int Active; }
    private static readonly ConditionalWeakTable<FixtureBase, PumpState> Pumps = new();

    /// <summary>Pumps frames until a condition holds. Defaults to five seconds and observes xUnit cancellation.</summary>
    public static async Task WaitUntilAsync(this FixtureBase godot, Func<bool> condition, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default, [CallerArgumentExpression(nameof(condition))] string? description = null)
    {
        ArgumentNullException.ThrowIfNull(godot);
        ArgumentNullException.ThrowIfNull(condition);
        var limit = timeout ?? TimeSpan.FromSeconds(5);
        if (limit <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken, cancellationToken);
        var token = cancellation.Token;
        token.ThrowIfCancellationRequested();
        var state = Pumps.GetValue(godot, _ => new PumpState());
        if (Interlocked.Exchange(ref state.Active, 1) != 0)
            throw new InvalidOperationException("A wait is already pumping this fixture. Await one operation at a time.");
        try
        {
            var clock = Stopwatch.StartNew();
            while (!condition())
            {
                token.ThrowIfCancellationRequested();
                if (clock.Elapsed >= limit)
                    throw new XunitException($"Timed out after {limit.TotalSeconds:g} s waiting for {description ?? "the condition"}.");
                if (godot.Engine.Iteration())
                    throw new XunitException($"Godot requested quit while waiting for {description ?? "the condition"}.");
                // Let managed asynchronous work run, preserving the engine's synchronization context.
                await Task.Delay(1, token);
            }
        }
        finally { Volatile.Write(ref state.Active, 0); }
    }

    /// <summary>Pumps frames while an operation runs, then propagates its result, exception or cancellation.</summary>
    public static async Task AwaitAsync(this FixtureBase godot, Task operation, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await godot.WaitUntilAsync(() => operation.IsCompleted, timeout, cancellationToken, "the asynchronous operation");
        await operation;
    }

    /// <summary>Pumps frames while an operation runs and returns its result.</summary>
    public static async Task<T> AwaitAsync<T>(this FixtureBase godot, Task<T> operation, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        await godot.AwaitAsync((Task)operation, timeout, cancellationToken);
        return await operation;
    }
}

/// <summary>Assertions for native signals and deferred object deletion.</summary>
public static class GodotAssert
{
    /// <summary>Connects immediately. AssertEmitted checks the count; WaitAsync pumps until an emission arrives.</summary>
    public static SignalExpectation ExpectSignal(GodotObject source, StringName signal)
        => new(source, signal, onEmission => Callable.From(onEmission));

    /// <summary>Records a signal's single argument as well as its emission count.</summary>
    public static SignalExpectation<T> ExpectSignal<[MustBeVariant] T>(GodotObject source, StringName signal)
        => new(source, signal);

    /// <summary>Asserts that an object becomes invalid, advancing frames to flush QueueFree when necessary.</summary>
    public static Task FreedAsync(FixtureBase godot, GodotObject instance, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return godot.WaitUntilAsync(() => !GodotObject.IsInstanceValid(instance), timeout, cancellationToken,
            $"{instance.GetType().Name} to be freed");
    }
}

/// <summary>A signal subscription owned by a test. Dispose always disconnects; it does not assert.</summary>
public class SignalExpectation : IDisposable
{
    private readonly GodotObject _source;
    private readonly StringName _signal;
    private readonly string _name;
    private readonly Callable _callable;
    private bool _disposed;

    internal SignalExpectation(GodotObject source, StringName signal, Func<Action, Callable> makeCallable)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(signal);
        if (!GodotObject.IsInstanceValid(source)) throw new ArgumentException("Signal source has been freed.", nameof(source));
        if (!source.HasSignal(signal)) throw new ArgumentException($"Signal '{signal}' does not exist.", nameof(signal));
        _source = source;
        _name = signal.ToString();
        _signal = new StringName(_name);
        _callable = makeCallable(() => Count++);
        var error = source.Connect(_signal, _callable);
        if (error != Error.Ok)
        {
            _signal.Dispose();
            throw new XunitException($"Could not connect signal '{_name}': {error}.");
        }
    }

    /// <summary>The number of emissions since the expectation was created.</summary>
    public int Count { get; private set; }

    /// <summary>Asserts an exact emission count (one by default), including signals emitted synchronously.</summary>
    public void AssertEmitted(int count = 1)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (Count != count) throw new XunitException($"Expected signal '{_name}' {count} time(s), but observed {Count}.");
    }

    /// <summary>Pumps until at least count emissions arrive. A timeout or cancellation disconnects the listener.</summary>
    public async Task WaitAsync(FixtureBase godot, int count = 1, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        try
        {
            await godot.WaitUntilAsync(() =>
            {
                if (Count >= count) return true;
                if (!GodotObject.IsInstanceValid(_source))
                    throw new XunitException($"Signal '{_name}' source was freed before {count} emission(s) arrived.");
                return false;
            }, timeout, cancellationToken, $"signal '{_name}' ({count} emission(s))");
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Disconnects safely even if the source was freed while waiting.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (GodotObject.IsInstanceValid(_source) && _source.IsConnected(_signal, _callable))
            _source.Disconnect(_signal, _callable);
        _signal.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>A disposable expectation for a signal with one argument.</summary>
public sealed class SignalExpectation<[MustBeVariant] T> : SignalExpectation
{
    private readonly List<T> _values;

    internal SignalExpectation(GodotObject source, StringName signal) : this(source, signal, []) { }

    private SignalExpectation(GodotObject source, StringName signal, List<T> values)
        : base(source, signal, onEmission => Callable.From<T>(value => { values.Add(value); onEmission(); }))
        => _values = values;

    /// <summary>The signal arguments in emission order.</summary>
    public IReadOnlyList<T> Values => _values;
}
