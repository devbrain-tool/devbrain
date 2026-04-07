namespace DevBrain.Core.Interfaces;

using DevBrain.Core.Models;

public interface IAlertSink
{
    Task Send(DejaVuAlert alert, CancellationToken ct = default);
}
