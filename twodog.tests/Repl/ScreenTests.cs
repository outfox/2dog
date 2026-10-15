using twodog.Repl;

namespace twodog.tests.ReplTests;

public class ScreenTests
{
    private static EditorView Draft(string text, int? caret = null, MenuView? menu = null, StyledText? signature = null)
        => new(text, caret ?? text.Length, null, [], menu, signature, false);

    private static Func<int, int, RegionFrame> Region(Func<EditorView> view, string status = "status")
        => (width, height) => RegionLayout.Build("godot> ", view(), status, width, height, color: true);

    [Fact]
    public void OutputFromAnyThreadLandsAboveTheDraftWithoutTouchingIt()
    {
        var terminal = new VirtualTerminal(40, 10);
        using var screen = new ReplScreen(terminal, color: true);
        screen.SetRegion(Region(() => Draft("abcdef", 3)));
        screen.Flush();
        Assert.Equal(["status", "godot> abcdef"], terminal.Screen.Where(l => l.Length > 0));
        Assert.Equal((1, 10), terminal.Cursor);

        screen.Write(null);
        screen.Write("");
        screen.Write("one\n");
        screen.Write("par");
        screen.Write("tial\ntwo\n");
        screen.Write("three", error: true);
        Parallel.For(0, 4, i => screen.Write($"thread {i}\n"));
        screen.Flush();
        var lines = terminal.Text.Split('\n');
        Assert.Equal("one", lines[0]);
        Assert.Contains("partial", lines);
        Assert.Contains("two", lines);
        Assert.Contains("three", lines);
        Assert.Equal(4, lines.Count(l => l.StartsWith("thread ", StringComparison.Ordinal)));
        Assert.Equal("status", lines[^2]);
        Assert.Equal("godot> abcdef", lines[^1]);
        Assert.Equal(lines.Length - 1, terminal.Cursor.Row + terminal.Scrollback.Length);
        Assert.Equal(10, terminal.Cursor.Column);
        Assert.True(terminal.CursorVisible);
    }

    [Fact]
    public void ManyLinesScrollIntoScrollbackInOrderAndTheRegionStaysAtTheBottom()
    {
        var terminal = new VirtualTerminal(30, 6);
        using var screen = new ReplScreen(terminal, color: false);
        screen.SetRegion(Region(() => Draft("draft")));
        for (var i = 0; i < 200; i++) screen.Write($"line {i}\n");
        screen.Flush();
        var all = terminal.Text.Split('\n');
        Assert.Equal(Enumerable.Range(0, 200).Select(i => $"line {i}"), all.Take(200));
        Assert.Equal(["status", "godot> draft"], all[200..]);
        Assert.Equal("godot> draft", terminal.Screen[^1]);
    }

    [Fact]
    public void AShrinkingRegionClearsItsFormerRowsAndWideCharactersPlaceTheCaret()
    {
        var terminal = new VirtualTerminal(40, 12);
        using var screen = new ReplScreen(terminal, color: true);
        var view = Draft("x", menu: new MenuView(["alpha", "beta", "gamma"], 0, 0, 3, "first item"));
        screen.SetRegion(Region(() => view));
        screen.Flush();
        Assert.Contains(terminal.Screen, line => line.Contains("alpha", StringComparison.Ordinal) && line.Contains("first item", StringComparison.Ordinal));
        // The status line keeps its row while the menu below the input opens and closes.
        var statusRow = Array.IndexOf(terminal.Screen, "status");
        view = Draft("世界x", 2);
        screen.Invalidate();
        screen.Flush();
        Assert.Equal(["status", "godot> 世界x"], terminal.Screen.Where(l => l.Length > 0));
        Assert.Equal(statusRow, Array.IndexOf(terminal.Screen, "status"));
        Assert.Equal((1, 11), terminal.Cursor);
    }

    [Fact]
    public void LongDraftsWrapWithinTheRowWidthAndKeepTheCaretOnItsRow()
    {
        var terminal = new VirtualTerminal(20, 10);
        using var screen = new ReplScreen(terminal, color: false);
        var text = new string('a', 30) + "\nend";
        screen.SetRegion(Region(() => Draft(text, 25)));
        screen.Flush();
        var rows = terminal.Screen.Where(l => l.Length > 0).ToArray();
        Assert.All(rows, row => Assert.True(row.Length < 20, row));
        Assert.Equal("godot> " + new string('a', 12), rows[1]);
        Assert.Equal("       " + new string('a', 12), rows[2]);
        Assert.Equal("       " + new string('a', 6), rows[3]);
        Assert.Equal("       end", rows[4]);
        Assert.Equal((3, 7 + 1), terminal.Cursor);
    }

    [Fact]
    public void RegionLayoutTrimsMenuAndSignatureToFitAndKeepsTheCaretRowVisible()
    {
        var menu = new MenuView(Enumerable.Range(0, 8).Select(i => (StyledText)$"item{i}").ToArray(), 2, 4, 40, "doc");
        var frame = RegionLayout.Build("> ", Draft("call(", menu: menu, signature: "void Call(int first, int second)\nfirst: the first"), "status", 80, 6, true);
        Assert.Equal(6, frame.Rows.Count);
        Assert.Equal("status", frame.PlainRows[0]);
        Assert.Equal(1, frame.CaretRow);
        Assert.Contains("void Call", frame.PlainRows[2], StringComparison.Ordinal);
        Assert.Contains("▐", string.Concat(frame.PlainRows));
        var start = RegionLayout.Build("", Draft("x", 0), null, 40, 5, false);
        Assert.Equal((0, 0), (start.CaretRow, start.CaretColumn));
        var tall = RegionLayout.Build("> ", Draft(string.Join("\n", Enumerable.Range(0, 20)), 2), null, 40, 5, false);
        Assert.Equal(5, tall.Rows.Count);
        Assert.InRange(tall.CaretRow, 0, 4);
        Assert.Equal("> 0", tall.PlainRows[0]);
        Assert.DoesNotContain(tall.Rows, row => row.Contains('\x1b'));
        var narrow = RegionLayout.Build("> ", Draft("x", menu: menu), null, 20, 20, true);
        Assert.All(narrow.PlainRows, row => Assert.True(CellWidth.Of(row) < 20, row));
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("\x1b[31mred\x1b[0m", "\x1b[31mred\x1b[0m")]
    [InlineData("progress 10%\rprogress 100%", "progress 100%")]
    [InlineData("a\x1b[2Jb\x1b[1;1Hc\x1b]0;title\u0007d\u001b7e\x1b", "abcde")]
    [InlineData("tab\there\u0007bell", "tab\therebell")]
    [InlineData("crlf\r", "crlf")]
    public void ForeignOutputKeepsColorsButNotCursorControl(string line, string expected)
        => Assert.Equal(expected, ReplScreen.Clean(line));

    [Fact]
    public void IdlePartialLinesClearAndControlSequencesReachTheTerminal()
    {
        var terminal = new VirtualTerminal(40, 8);
        using var screen = new ReplScreen(terminal, color: true);
        screen.SetRegion(Region(() => Draft("")));
        screen.Write("Loading...");
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!terminal.Text.Contains("Loading...", StringComparison.Ordinal) && DateTime.UtcNow < deadline) Thread.Sleep(10);
        Assert.StartsWith("Loading...", terminal.Text, StringComparison.Ordinal);
        screen.Control("\x1b]52;c;dGV4dA==\x07");
        screen.Clear();
        screen.Flush();
        Assert.Equal(1, terminal.Clears);
        Assert.Equal("godot>", terminal.Screen[1]);
        Assert.Contains("\x1b]52;c;dGV4dA==\x07", terminal.Raw, StringComparison.Ordinal);
    }

    [Fact]
    public void ResizingRedrawsTheRegionAndAnimationEndsWithAFinalFrame()
    {
        var terminal = new VirtualTerminal(40, 8);
        using var screen = new ReplScreen(terminal, color: false);
        var busy = true;
        var frames = 0;
        screen.SetRegion((width, height) =>
        {
            frames++;
            return RegionLayout.Build("> ", Draft("text"), busy ? "busy" : "idle", width, height, false);
        });
        screen.Animating = () => busy;
        screen.Flush();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (frames < 3 && DateTime.UtcNow < deadline) Thread.Sleep(10);
        Assert.True(frames >= 3);
        busy = false;
        deadline = DateTime.UtcNow.AddSeconds(5);
        while (terminal.Screen[0] != "idle" && DateTime.UtcNow < deadline) Thread.Sleep(10);
        Assert.Equal("idle", terminal.Screen[0]);
        terminal.Resize(60, 8);
        deadline = DateTime.UtcNow.AddSeconds(5);
        var before = frames;
        while (frames == before && DateTime.UtcNow < deadline) Thread.Sleep(10);
        screen.Flush();
        Assert.Equal(["idle", "> text"], terminal.Screen.Where(l => l.Length > 0));
        Assert.Equal((60, 8), screen.Size);
    }

    [Fact]
    public void DisposeFlushesOutputRemovesTheRegionAndATerminalFailureStopsTheScreen()
    {
        var terminal = new VirtualTerminal(40, 8);
        var screen = new ReplScreen(terminal, color: true);
        screen.SetRegion(Region(() => Draft("draft")));
        screen.Write("last words");
        screen.Dispose();
        screen.Dispose();
        Assert.Equal(["last words"], terminal.Screen.Where(l => l.Length > 0));
        Assert.Equal((1, 0), terminal.Cursor);

        var failing = new VirtualTerminal(40, 8) { BeforeWrite = _ => throw new IOException("gone") };
        using var broken = new ReplScreen(failing, color: true);
        broken.Write("lost\n");
        Assert.Equal("gone", Assert.IsType<IOException>(Assert.Throws<IOException>(broken.Flush).InnerException).Message);
    }

    [Fact]
    public void StylesRenderAsMinimalSgrAndCellWidthsMatchTheTerminal()
    {
        var text = new StyledText("ab", [new StyledSpan(0, 1, TermColor.Red), new StyledSpan(1, 1, new TextStyle(TermColor.BrightBlue, TermColor.Rgb(1, 2, 3), Bold: true, Dim: true, Italic: true, Underline: true, Inverse: true))]);
        Assert.Equal("\x1b[31ma\x1b[0m\x1b[1;2;3;4;7;94;48;2;1;2;3mb\x1b[0m", text.ToAnsi(color: true));
        Assert.Equal("ab", text.ToAnsi(color: false));
        Assert.Equal("", new TextStyle().Sgr());
        Assert.Equal("ab", text.ToString());
        var joined = new StyledText("x", TermColor.Green) + "y";
        Assert.Equal("xy", joined.Text);
        Assert.Equal(new StyledSpan(0, 1, TermColor.Green), Assert.Single(joined.Spans));
        Assert.Empty(new StyledText("short", [new StyledSpan(10, 5, TermColor.Red)]).Spans);
        Assert.Equal(new StyledSpan(0, 1, TermColor.Red), Assert.Single(new StyledText("abc", [new StyledSpan(-1, 3, TermColor.Red)]).Substring(1, 2).Spans));
        Assert.Equal(2, CellWidth.Of("世"));
        Assert.Equal(2, CellWidth.Of("🦊"));
        Assert.Equal(1, CellWidth.Of("é"));
        Assert.Equal(0, CellWidth.Of("​\u0007"));
        Assert.Equal(1, CellWidth.Of("é"));
        Assert.True(new StyledSpan(2, 3, default).Contains(4));
        Assert.False(new StyledSpan(2, 3, default).Contains(5));
    }
}
