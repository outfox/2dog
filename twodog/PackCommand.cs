using twodog.pck;

namespace twodog.cli;

/// <summary>
/// The pack verb: file-format operations on Godot .pck bundles. Runs on the GDPC reader shared with twodog.import -
/// no engine, no project, so it works on any pack lying around.
/// </summary>
internal static class PackCommand
{
    public static int Run(ParsedCommand cmd)
    {
        // The parser only lets `list` through; further operations dispatch here.
        try
        {
            GdpcPack.Print(cmd.PackFile!, GdpcPack.Read(cmd.PackFile!), Console.Out);
            return 0;
        }
        catch (PckFormatException ex)
        {
            throw new ToolException(ex.Message);
        }
    }
}
