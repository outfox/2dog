using twodog.pck;

namespace twodog.cli;

/// <summary>
/// The pack verb: file-format operations on Godot .pck bundles. Runs on the GDPC reader shared with twodog.import -
/// no engine, no project, so it works on any pack lying around.
/// </summary>
internal static class PackCommand
{
    public static int Run(ParsedCommand cmd, Report report)
    {
        // The parser only lets `list` through; further operations dispatch here.
        try
        {
            var listing = GdpcPack.Read(cmd.PackFile!);
            report.Pack = new ReportPack(cmd.PackFile!, listing.FormatVersion, $"{listing.Major}.{listing.Minor}",
                listing.TotalBytes, listing.Entries.Select(e => new ReportPackEntry(e.Path, e.Size)).ToList());
            if (!Out.Mode.Json) GdpcPack.Print(cmd.PackFile!, listing, Out.Writer);
            return ExitCodes.Ok;
        }
        catch (PckFormatException ex)
        {
            throw new ToolException(ex.Message);
        }
    }
}
