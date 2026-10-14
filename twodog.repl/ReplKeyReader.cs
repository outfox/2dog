using System.Text;

namespace twodog.Repl;

/// <summary>Turns console keys into editor inputs. Unix terminals report pastes and modified keys (Shift+Enter) as escape
/// sequences. Windows console input has no paste markers: a burst of keys that arrives while the editor waits is a paste
/// whose newlines and tabs are literal text. Keys typed ahead while the REPL was busy keep their editing meaning.</summary>
internal sealed class ReplKeyReader(Func<bool> available, Func<ConsoleKeyInfo> read, bool windows)
{
    private readonly List<ConsoleKeyInfo> replay = [];

    private ConsoleKeyInfo Next()
    {
        if (replay.Count == 0) return read();
        var key = replay[0];
        replay.RemoveAt(0);
        return key;
    }

    public bool Available => replay.Count > 0 || available();

    // A paste can arrive in chunks; allow a moment before deciding that a burst has ended.
    private bool AvailableSoon()
    {
        if (Available) return true;
        Thread.Sleep(2);
        return Available;
    }

    /// <summary>Reads one input. <paramref name="idle"/> means the caller waited for this key.</summary>
    public TerminalInput Read(bool idle)
    {
        while (true)
        {
            var key = Next();
            if (!windows && key.KeyChar == '\x1b' && Escape() is { } decoded) return decoded;
            if (!windows && key.KeyChar == '\0' && key.Modifiers == 0) return new KeyInput(new('\0', ConsoleKey.Spacebar, false, false, true));
            if (!windows && key.KeyChar == '\x1b') continue;
            var paste = idle && windows;
            if (!Batchable(key, paste)) return new KeyInput(key);
            if (paste)
            {
                if (!AvailableSoon()) return new KeyInput(key);
                var text = Text(Burst(key, paste: true, int.MaxValue));
                // A single line ending in a line break runs, as if typed; longer pastes wait for the user.
                var line = text.TrimEnd('\n');
                if (line.Length == text.Length || line.Contains('\n')) return new TextInput(text);
                replay.Insert(0, new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
                return new TextInput(line);
            }
            // Typed ahead while busy: individual keys, unless a long run of text shows a paste.
            var run = Burst(key, paste: false, LongRun);
            if (run.Count < LongRun)
            {
                replay.InsertRange(0, run.Skip(1));
                return new KeyInput(key);
            }
            run.AddRange(Burst(null, paste: false, int.MaxValue));
            return new TextInput(Text(run));
        }
    }

    internal const int LongRun = 16;

    private static string Text(List<ConsoleKeyInfo> keys) => string.Concat(keys.Select(Literal));

    private List<ConsoleKeyInfo> Burst(ConsoleKeyInfo? first, bool paste, int limit)
    {
        var keys = first is { } start ? new List<ConsoleKeyInfo> { start } : [];
        while (keys.Count < limit && (paste ? AvailableSoon() : Available))
        {
            var next = Next();
            if (!Batchable(next, paste))
            {
                replay.Insert(0, next);
                break;
            }
            keys.Add(next);
        }
        return keys;
    }

    private static bool Plain(ConsoleKeyInfo key) => !char.IsControl(key.KeyChar) && key.KeyChar != '\0' &&
        (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) is 0 or (ConsoleModifiers.Control | ConsoleModifiers.Alt);

    private static bool Batchable(ConsoleKeyInfo key, bool paste) => Plain(key) ||
        paste && key.Modifiers is 0 or ConsoleModifiers.Shift && key.Key is ConsoleKey.Enter or ConsoleKey.Tab;

    private static char Literal(ConsoleKeyInfo key) => key.Key switch
    {
        ConsoleKey.Enter => '\n',
        ConsoleKey.Tab => '\t',
        _ => key.KeyChar,
    };

    // After ESC: bracketed paste, xterm modifyOtherKeys or kitty keyboard sequences, Alt+Enter/Backspace, or a lone
    // Escape. Returns null for a sequence this editor does not use, which is dropped rather than typed.
    private TerminalInput? Escape()
    {
        var escape = new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false);
        if (!Available) return new KeyInput(escape);
        var first = Next();
        if (first.Key is ConsoleKey.Enter or ConsoleKey.Backspace)
            return new KeyInput(new(first.KeyChar, first.Key, false, true, false));
        // Some console layers consume the '[' of a CSI sequence: its parameters then follow ESC directly.
        var bracketless = char.IsAsciiDigit(first.KeyChar);
        if (first.KeyChar is not ('[' or 'O') && !bracketless)
        {
            // Escape followed by typing, never a dropped character.
            replay.Insert(0, first);
            return new KeyInput(escape);
        }
        var keys = new List<ConsoleKeyInfo> { first };
        var sequence = new StringBuilder(bracketless ? first.KeyChar.ToString() : "");
        while (Available)
        {
            var next = Next();
            // Only parameter, intermediate and final bytes belong to the sequence; a key such as Enter does not.
            if (next.KeyChar is < ' ' or > '~')
            {
                replay.Insert(0, next);
                break;
            }
            keys.Add(next);
            sequence.Append(next.KeyChar);
            if (next.KeyChar is >= '@' and <= '~') break;
        }
        var body = sequence.ToString();
        if (body == "200~") return Paste();
        if (bracketless && Decode(body) is null)
        {
            // Not a sequence after all: Escape, then the digits and text that followed it.
            replay.InsertRange(0, keys);
            return new KeyInput(escape);
        }
        return Decode(body) is { } key ? new KeyInput(key) : null;
    }

    private TextInput Paste()
    {
        var text = new StringBuilder();
        while (true)
        {
            text.Append(Next().KeyChar);
            if (text[^1] != '~') continue;
            var value = text.ToString();
            foreach (var end in (ReadOnlySpan<string>)["\x1b[201~", "\u001b201~"])
                if (value.EndsWith(end, StringComparison.Ordinal)) return new TextInput(value[..^end.Length]);
        }
    }

    /// <summary>Decodes "27;modifiers;code~" (modifyOtherKeys) and "code;modifiers u" (kitty) bodies.</summary>
    internal static ConsoleKeyInfo? Decode(string body)
    {
        int code, modifiers = 1;
        string[] fields;
        if (body.EndsWith('~') && body.StartsWith("27;", StringComparison.Ordinal))
        {
            fields = body[3..^1].Split(';');
            if (fields.Length != 2 || !int.TryParse(fields[0], out modifiers) || !int.TryParse(fields[1], out code)) return null;
        }
        else if (body.EndsWith('u'))
        {
            fields = body[..^1].Split(';');
            if (!int.TryParse(fields[0].Split(':')[0], out code) ||
                fields.Length > 1 && !int.TryParse(fields[1].Split(':')[0], out modifiers)) return null;
        }
        else return null;
        var bits = modifiers - 1;
        bool shift = (bits & 1) != 0, alt = (bits & 2) != 0, control = (bits & 4) != 0;
        var (key, character) = code switch
        {
            13 => (ConsoleKey.Enter, '\r'),
            9 => (ConsoleKey.Tab, '\t'),
            27 => (ConsoleKey.Escape, '\x1b'),
            127 or 8 => (ConsoleKey.Backspace, '\b'),
            32 => (ConsoleKey.Spacebar, ' '),
            >= 'a' and <= 'z' => ((ConsoleKey)(code - 32), control ? (char)(code - 96) : (char)(shift ? code - 32 : code)),
            >= '0' and <= '9' => ((ConsoleKey)code, (char)code),
            > 32 and < 0xD800 => (ConsoleKey.NoName, (char)code),
            _ => (ConsoleKey.NoName, '\0'),
        };
        return character == '\0' ? null : new ConsoleKeyInfo(character, key, shift, alt, control);
    }
}
