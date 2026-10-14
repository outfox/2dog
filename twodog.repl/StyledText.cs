using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace twodog.Repl;

/// <summary>A terminal foreground/background color: one of the 16 theme colors, or 24-bit RGB.</summary>
internal readonly record struct TermColor
{
    private readonly int value;
    private TermColor(int value) => this.value = value;
    public static TermColor Palette(int index) => new(index & 0xF);
    public static TermColor Rgb(byte r, byte g, byte b) => new(0x1000000 | r << 16 | g << 8 | b);
    public bool IsRgb => (value & 0x1000000) != 0;
    public static readonly TermColor Red = Palette(1), Green = Palette(2), Yellow = Palette(3), Blue = Palette(4),
        Magenta = Palette(5), Cyan = Palette(6), White = Palette(7), BrightBlack = Palette(8), BrightRed = Palette(9),
        BrightGreen = Palette(10), BrightYellow = Palette(11), BrightBlue = Palette(12), BrightMagenta = Palette(13),
        BrightCyan = Palette(14), BrightWhite = Palette(15);

    internal string Sgr(bool background)
    {
        if (IsRgb) return $"{(background ? 48 : 38)};2;{value >> 16 & 0xFF};{value >> 8 & 0xFF};{value & 0xFF}";
        var index = value & 0xF;
        return ((index < 8 ? 30 : 82) + index + (background ? 10 : 0)).ToString(CultureInfo.InvariantCulture);
    }
}

internal readonly record struct TextStyle(TermColor? Foreground = null, TermColor? Background = null, bool Bold = false,
    bool Dim = false, bool Italic = false, bool Underline = false, bool Inverse = false)
{
    public static implicit operator TextStyle(TermColor color) => new(color);
    public bool IsPlain => this == default;

    /// <summary>The SGR sequence that applies this style after a reset; empty for plain text.</summary>
    internal string Sgr()
    {
        var codes = new StringBuilder();
        if (Bold) codes.Append(";1");
        if (Dim) codes.Append(";2");
        if (Italic) codes.Append(";3");
        if (Underline) codes.Append(";4");
        if (Inverse) codes.Append(";7");
        if (Foreground is { } foreground) codes.Append(';').Append(foreground.Sgr(background: false));
        if (Background is { } background) codes.Append(';').Append(background.Sgr(background: true));
        return codes.Length == 0 ? "" : "\x1b[" + codes.ToString(1, codes.Length - 1) + "m";
    }
}

internal readonly record struct StyledSpan(int Start, int Length, TextStyle Style)
{
    public int End => Start + Length;
    public bool Contains(int position) => position >= Start && position < End;
    public StyledSpan Offset(int offset) => this with { Start = Start + offset };
}

/// <summary>Immutable text with styled ranges. Later spans take precedence where spans overlap.</summary>
internal sealed class StyledText
{
    public static readonly StyledText Empty = new("");
    public string Text { get; }
    public ImmutableArray<StyledSpan> Spans { get; }
    public int Length => Text.Length;

    public StyledText(string text, IEnumerable<StyledSpan>? spans = null)
    {
        Text = text;
        Spans = spans?.Where(s => s.Length > 0).Select(s => Clamp(s, text.Length)).Where(s => s.Length > 0).ToImmutableArray()
            ?? ImmutableArray<StyledSpan>.Empty;
    }

    public StyledText(string text, TextStyle style) : this(text, [new StyledSpan(0, text.Length, style)]) { }

    private static StyledSpan Clamp(StyledSpan span, int length)
    {
        var start = Math.Clamp(span.Start, 0, length);
        return span with { Start = start, Length = Math.Clamp(span.End, start, length) - start };
    }

    public static implicit operator StyledText(string text) => new(text);

    public static StyledText operator +(StyledText left, StyledText right)
        => new(left.Text + right.Text, left.Spans.Concat(right.Spans.Select(s => s.Offset(left.Length))));

    public StyledText Substring(int start, int length)
        => new(Text.Substring(start, length), Spans.Select(s => s.Offset(-start)));

    /// <summary>The effective style of each character, honoring span precedence.</summary>
    internal TextStyle[] StyleMap()
    {
        var styles = new TextStyle[Text.Length];
        foreach (var span in Spans)
            for (var i = span.Start; i < span.End; i++) styles[i] = span.Style;
        return styles;
    }

    public string ToAnsi(bool color)
    {
        if (!color || Spans.IsEmpty) return Text;
        var styles = StyleMap();
        var result = new StringBuilder();
        var current = default(TextStyle);
        for (var i = 0; i < Text.Length; i++)
        {
            if (styles[i] != current)
            {
                if (!current.IsPlain) result.Append("\x1b[0m");
                result.Append(styles[i].Sgr());
                current = styles[i];
            }
            result.Append(Text[i]);
        }
        if (!current.IsPlain) result.Append("\x1b[0m");
        return result.ToString();
    }

    public override string ToString() => Text;
}

/// <summary>Terminal cell widths: wide East Asian and emoji runes take two cells, combining marks none.</summary>
internal static class CellWidth
{
    public static int Of(Rune rune)
    {
        var value = rune.Value;
        if (value < 0x20 || value is >= 0x7F and < 0xA0) return 0;
        if (value < 0x300) return 1;
        var category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format) return 0;
        if (value is 0x200B or (>= 0xFE00 and <= 0xFE0F)) return 0;
        foreach (var (start, end) in Wide)
        {
            if (value < start) break;
            if (value <= end) return 2;
        }
        return 1;
    }

    // East Asian wide and fullwidth blocks and emoji, in ascending order.
    private static readonly (int Start, int End)[] Wide =
    [
        (0x1100, 0x115F), (0x2E80, 0x303E), (0x3041, 0x33FF), (0x3400, 0x4DBF), (0x4E00, 0x9FFF), (0xA000, 0xA4CF),
        (0xAC00, 0xD7A3), (0xF900, 0xFAFF), (0xFE30, 0xFE4F), (0xFF00, 0xFF60), (0xFFE0, 0xFFE6), (0x1F300, 0x1F64F),
        (0x1F900, 0x1F9FF), (0x20000, 0x3FFFD),
    ];

    public static int Of(string text)
    {
        var width = 0;
        foreach (var rune in text.EnumerateRunes()) width += Of(rune);
        return width;
    }
}
