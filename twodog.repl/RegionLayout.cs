using System.Text;

namespace twodog.Repl;

/// <summary>One terminal row: styled cells that never exceed the row's width.</summary>
internal sealed class RowBuilder(int width)
{
    private readonly StringBuilder ansi = new();
    private readonly StringBuilder plain = new();
    private TextStyle current;
    public int Width { get; private set; }
    public int Capacity => width;
    public string Plain => plain.ToString();

    /// <summary>Appends whole runes while they fit; returns the number of UTF-16 chars consumed.</summary>
    public int Append(string text, TextStyle style = default, bool color = true)
    {
        var consumed = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var cells = CellWidth.Of(rune);
            if (Width + cells > width) break;
            if (color && style != current)
            {
                if (!current.IsPlain) ansi.Append("\x1b[0m");
                ansi.Append(style.Sgr());
                current = style;
            }
            ansi.Append(rune.ToString());
            plain.Append(rune.ToString());
            Width += cells;
            consumed += rune.Utf16SequenceLength;
        }
        return consumed;
    }

    public void Pad(int column, TextStyle style = default, bool color = true)
    {
        if (column > Width) Append(new string(' ', Math.Min(column, width) - Width), style, color);
    }

    public string ToAnsi() => current.IsPlain ? ansi.ToString() : ansi + "\x1b[0m";
}

internal sealed record RegionFrame(IReadOnlyList<string> Rows, IReadOnlyList<string> PlainRows, int CaretRow, int CaretColumn);

/// <summary>Lays out the input region: the prompt and draft, signature help, the completion menu and a status line.
/// Rows are at most width - 1 cells, so no row ever triggers the terminal's own wrapping.</summary>
internal static class RegionLayout
{
    private static readonly TextStyle Selected = new(TermColor.Rgb(0x1e, 0x1e, 0x1e), TermColor.Rgb(0x8f, 0xc1, 0xe3));
    private static readonly TextStyle MenuStyle = new(Background: TermColor.Rgb(0x2b, 0x2d, 0x30));
    private static readonly TextStyle Dim = new(Dim: true);

    public static RegionFrame Build(StyledText prompt, EditorView view, StyledText? status, int terminalWidth, int terminalHeight, bool color)
    {
        var width = Math.Max(8, terminalWidth - 1);
        var height = Math.Max(2, terminalHeight);
        var promptWidth = Math.Min(CellWidth.Of(prompt.Text), width / 2);
        var (input, caretRow, caretColumn) = Input(prompt, promptWidth, view, width, color);

        var signature = view.Signature is { } help ? Lines(help, width - promptWidth, promptWidth, color, 2) : [];
        var menu = view.Menu is { } open ? Menu(open, width, Math.Min(caretColumn, width), color) : [];
        var statusRows = status is null ? 0 : 1;
        // The caret row stays visible: trim the menu, then signature help, then input rows away from the caret.
        var available = height - statusRows;
        if (input.Count + signature.Count + menu.Count > available) menu = menu.Take(Math.Max(0, available - input.Count - signature.Count)).ToList();
        if (input.Count + signature.Count > available) signature = signature.Take(Math.Max(0, available - input.Count)).ToList();
        if (input.Count > available)
        {
            var start = Math.Clamp(caretRow - available / 2, 0, input.Count - available);
            input = input.Skip(start).Take(available).ToList();
            caretRow -= start;
        }
        var rows = input.Concat(signature).Concat(menu).ToList();
        // Above the input, the status line stays put while the menu and signature help come and go below.
        if (status is not null)
        {
            var row = new RowBuilder(width);
            Append(row, status, color);
            rows.Insert(0, row);
            caretRow++;
        }
        return new(rows.Select(r => r.ToAnsi()).ToArray(), rows.Select(r => r.Plain).ToArray(), caretRow, caretColumn);
    }

    private static (List<RowBuilder> Rows, int CaretRow, int CaretColumn) Input(StyledText prompt, int promptWidth, EditorView view, int width, bool color)
    {
        var styles = new TextStyle[view.Text.Length];
        foreach (var span in view.Highlights)
            for (var i = Math.Max(0, span.Start); i < Math.Min(span.End, styles.Length); i++) styles[i] = span.Style;
        if (view.Selection is { } selection)
            for (var i = selection.Start; i < selection.End; i++) styles[i] = styles[i] with { Inverse = true };

        var rows = new List<RowBuilder>();
        var row = new RowBuilder(width);
        Append(row, prompt, color, promptWidth);
        rows.Add(row);
        int caretRow = 0, caretColumn = row.Width;
        var text = view.Text;
        for (var i = 0; i <= text.Length;)
        {
            if (i == view.Caret)
            {
                caretRow = rows.Count - 1;
                caretColumn = row.Width;
            }
            if (i == text.Length) break;
            if (text[i] == '\n' || row.Width + CellWidth.Of(Rune.GetRuneAt(text, i)) > width)
            {
                row = new RowBuilder(width);
                row.Pad(promptWidth, color: color);
                rows.Add(row);
                // A caret at a wrap point moves to the new row when the loop revisits this position.
                if (text[i] == '\n') i++;
                continue;
            }
            var length = Rune.GetRuneAt(text, i).Utf16SequenceLength;
            row.Append(text.Substring(i, length), styles[i], color);
            i += length;
        }
        return (rows, caretRow, caretColumn);
    }

    private static List<RowBuilder> Lines(StyledText text, int width, int indent, bool color, int limit)
    {
        var rows = new List<RowBuilder>();
        var offset = 0;
        foreach (var line in text.Text.Split('\n'))
        {
            if (rows.Count == limit) break;
            var row = new RowBuilder(width + indent);
            row.Pad(indent, color: color);
            Append(row, text.Substring(offset, line.Length), color);
            rows.Add(row);
            offset += line.Length + 1;
        }
        return rows;
    }

    private static List<RowBuilder> Menu(MenuView menu, int width, int column, bool color)
    {
        var itemWidth = Math.Min(Math.Max(12, menu.Items.Max(i => CellWidth.Of(i.Text)) + 2), Math.Max(12, width / 2));
        var left = Math.Clamp(column, 0, Math.Max(0, width - itemWidth));
        var docLeft = left + itemWidth + 1;
        var docWidth = width - docLeft;
        var documentation = docWidth >= 16 ? Wrap(menu.Description, docWidth, menu.Items.Count) : [];
        var rows = new List<RowBuilder>();
        for (var i = 0; i < menu.Items.Count; i++)
        {
            var row = new RowBuilder(width);
            row.Pad(left, color: color);
            var style = i == menu.Selected ? Selected : MenuStyle;
            row.Append(" ", style, color);
            var item = menu.Items[i];
            var map = item.StyleMap();
            var start = row.Width;
            for (var c = 0; c < item.Text.Length && row.Width < left + itemWidth - 1;)
            {
                var length = Rune.GetRuneAt(item.Text, c).Utf16SequenceLength;
                var cell = i == menu.Selected ? Selected : map[c] with { Background = MenuStyle.Background };
                if (row.Append(item.Text.Substring(c, length), cell, color) == 0) break;
                c += length;
            }
            row.Pad(left + itemWidth, style, color);
            // A thumb shows the visible slice of a longer list.
            if (menu.Total > menu.Items.Count)
            {
                var thumb = menu.First * menu.Items.Count / menu.Total;
                row.Append(i == thumb ? "▐" : " ", Dim, color);
            }
            if (i < documentation.Count)
            {
                row.Pad(docLeft, color: color);
                Append(row, documentation[i], color);
            }
            rows.Add(row);
        }
        return rows;
    }

    private static List<StyledText> Wrap(StyledText text, int width, int limit)
    {
        var lines = new List<StyledText>();
        var offset = 0;
        foreach (var paragraph in text.Text.Split('\n'))
        {
            var start = 0;
            while (lines.Count < limit && start < paragraph.Length)
            {
                var length = 0;
                var cells = 0;
                var lastBreak = -1;
                while (start + length < paragraph.Length)
                {
                    var rune = Rune.GetRuneAt(paragraph, start + length);
                    if (cells + CellWidth.Of(rune) > width) break;
                    cells += CellWidth.Of(rune);
                    if (rune.Value == ' ') lastBreak = length;
                    length += rune.Utf16SequenceLength;
                }
                if (start + length < paragraph.Length && lastBreak > 0) length = lastBreak + 1;
                lines.Add(text.Substring(offset + start, length));
                start += length;
            }
            offset += paragraph.Length + 1;
        }
        return lines;
    }

    private static void Append(RowBuilder row, StyledText text, bool color, int? limit = null)
    {
        var map = text.StyleMap();
        var stop = limit is { } cells ? row.Width + cells : row.Capacity;
        for (var i = 0; i < text.Length;)
        {
            var length = Rune.GetRuneAt(text.Text, i).Utf16SequenceLength;
            if (text.Text[i] == '\n' || row.Width + CellWidth.Of(Rune.GetRuneAt(text.Text, i)) > stop) break;
            row.Append(text.Text.Substring(i, length), map[i], color);
            i += length;
        }
    }
}
