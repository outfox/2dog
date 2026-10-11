using System.Text;
using System.Threading.Channels;

namespace twodog.Repl;

// Control input and evaluation must never wait for a peer to read its pipe.
// One asynchronous writer drains all output in order, including during EOF shutdown.
internal sealed class ReplPipeOutput : TextWriter
{
    private readonly Channel<string> messages = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task writing;
    private readonly TextWriter destination;
    public override Encoding Encoding => destination.Encoding;

    internal ReplPipeOutput(TextWriter destination, CancellationTokenSource session)
    {
        this.destination = destination;
        var token = session.Token;
        writing = Task.Run(async () =>
        {
            try
            {
                await foreach (var message in messages.Reader.ReadAllAsync(token))
                    await destination.WriteAsync(message.AsMemory(), token).ConfigureAwait(false);
            }
            catch
            {
                messages.Writer.TryComplete();
                // A lost output stream ends only this session, including its running work.
                try { session.Cancel(); }
                catch (AggregateException) { /* Cleanup still runs if a user callback throws. */ }
                throw;
            }
        });
    }

    public override void Write(string? value)
    {
        if (value is not null && !messages.Writer.TryWrite(value)) throw new IOException("The pipe output is closed.");
    }
    public override void Write(char value) => Write(value.ToString());
    public override void WriteLine(string? value) => Write(value + NewLine);
    public override async ValueTask DisposeAsync()
    {
        messages.Writer.TryComplete();
        await writing.ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
