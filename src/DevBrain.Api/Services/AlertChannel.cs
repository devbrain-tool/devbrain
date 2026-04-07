namespace DevBrain.Api.Services;

using System.Threading.Channels;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;

public class AlertChannel : IAlertSink
{
    private readonly Channel<DejaVuAlert> _channel = Channel.CreateBounded<DejaVuAlert>(100);

    public async Task Send(DejaVuAlert alert, CancellationToken ct = default)
    {
        await _channel.Writer.WriteAsync(alert, ct);
    }

    public IAsyncEnumerable<DejaVuAlert> ReadAllAsync(CancellationToken ct)
    {
        return _channel.Reader.ReadAllAsync(ct);
    }
}
