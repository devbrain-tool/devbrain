namespace DevBrain.Api.Services;

using System.Threading.Channels;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;

/// <summary>
/// In-memory broadcast channel for SSE alert delivery.
/// Note: ReadAllAsync is single-consumer — only one SSE client receives each alert.
/// The dashboard also polls GET /alerts as a fallback, so this is acceptable for v1.
/// </summary>
public class AlertChannel : IAlertSink
{
    private readonly Channel<DejaVuAlert> _channel = Channel.CreateBounded<DejaVuAlert>(
        new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

    public async Task Send(DejaVuAlert alert, CancellationToken ct = default)
    {
        await _channel.Writer.WriteAsync(alert, ct);
    }

    public IAsyncEnumerable<DejaVuAlert> ReadAllAsync(CancellationToken ct)
    {
        return _channel.Reader.ReadAllAsync(ct);
    }
}
