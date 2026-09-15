using System.Threading.Channels;

namespace PrinterWLAN.Printing;

public sealed class PrintJobQueue
{
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });
    public ValueTask EnqueueAsync(string jobId, CancellationToken cancellationToken) => _channel.Writer.WriteAsync(jobId, cancellationToken);
    public IAsyncEnumerable<string> ReadAllAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAllAsync(cancellationToken);
}
