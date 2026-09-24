using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace twodog;

/// <summary>Kind of a reported <see cref="GodotError"/>, matching Godot's error handler types.</summary>
public enum GodotErrorType
{
    /// <summary>An engine or C# error, e.g. push_error or a failed engine check.</summary>
    Error,
    /// <summary>A warning, e.g. push_warning.</summary>
    Warning,
    /// <summary>A script error.</summary>
    Script,
    /// <summary>A shader compilation error.</summary>
    Shader,
}

/// <summary>One error or warning reported by Godot, e.g. from push_error, a failed engine check, or a caught C# exception.</summary>
public sealed record GodotError(GodotErrorType Type, string Code, string Message, string Function, string File, int Line)
{
    /// <summary>The explanation when Godot provides one, otherwise the failed condition or error text.</summary>
    public string Text => Message.Length > 0 ? Message : Code;

    /// <summary>Type, text and origin, laid out like Godot's console output.</summary>
    public override string ToString() => $"{Type}: {Text}\n   at {Function} ({File}:{Line})";
}

/// <summary>
/// Errors and warnings Godot reported while an <see cref="Engine"/> with <see cref="Engine.CaptureErrors"/> ran.
/// Godot reports from any thread; all members are thread-safe.
/// </summary>
public sealed class GodotErrorLog
{
    private readonly Lock _gate = new();
    private readonly List<GodotError> _errors = [];

    /// <summary>Returns the errors reported so far and clears the log.</summary>
    public GodotError[] Drain()
    {
        lock (_gate)
        {
            var drained = _errors.ToArray();
            _errors.Clear();
            return drained;
        }
    }

    /// <summary>
    /// Consumes the reported errors, asserting that there is at least one and each contains <paramref name="fragment"/>.
    /// Throws <see cref="GodotErrorException"/> otherwise, so the expectation fails a test in any framework.
    /// </summary>
    public GodotError[] Expect(string fragment)
    {
        var errors = Drain();
        if (errors.Length == 0)
            throw new GodotErrorException($"Expected a Godot error containing \"{fragment}\", but none was reported.", errors);
        if (errors.Any(e => !e.ToString().Contains(fragment, StringComparison.Ordinal)))
            throw new GodotErrorException($"Expected only Godot errors containing \"{fragment}\".", errors);
        return errors;
    }

    internal void Add(GodotError error)
    {
        lock (_gate) _errors.Add(error);
    }

    /// <summary>Feeds this log from libgodot until the returned registration is disposed.</summary>
    internal IDisposable Register()
    {
        var handle = GCHandle.Alloc(this);
        try
        {
            unsafe
            {
                LibGodot.libgodot_set_error_callback((nint)(delegate* unmanaged<nint, byte*, byte*, int, byte*, byte*, byte, int, void>)&OnError,
                    GCHandle.ToIntPtr(handle));
            }
        }
        catch (EntryPointNotFoundException e)
        {
            handle.Free();
            throw new InvalidOperationException(
                $"{nameof(Engine)}: The loaded libgodot has no libgodot_set_error_callback; " +
                $"{nameof(Engine.CaptureErrors)} needs natives from the same 2dog release as 2dog.engine.", e);
        }
        return new Registration(handle);
    }

    // Godot holds its error lock here: only copy the report, never call back into Godot, never throw.
    [UnmanagedCallersOnly]
    private static unsafe void OnError(nint userdata, byte* function, byte* file, int line, byte* code, byte* message,
        byte editorNotify, int type)
    {
        try
        {
            var log = (GodotErrorLog)GCHandle.FromIntPtr(userdata).Target!;
            log.Add(new GodotError((GodotErrorType)type, Utf8(code), Utf8(message), Utf8(function), Utf8(file), line));
        }
        catch
        {
            // An exception must not unwind into native code.
        }
    }

    private static unsafe string Utf8(byte* text) => Marshal.PtrToStringUTF8((nint)text) ?? "";

    private sealed class Registration(GCHandle handle) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            LibGodot.libgodot_set_error_callback(0, 0);
            handle.Free();
        }
    }
}

/// <summary>Thrown when Godot reported errors a test did not expect, or did not report the ones it expected.</summary>
public sealed class GodotErrorException(string message, IReadOnlyList<GodotError> errors)
    : Exception(errors.Count == 0 ? message : message + "\n" + string.Join("\n", errors))
{
    /// <summary>The errors that were reported.</summary>
    public IReadOnlyList<GodotError> Errors { get; } = errors;
}
