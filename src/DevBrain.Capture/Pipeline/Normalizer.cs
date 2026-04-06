namespace DevBrain.Capture.Pipeline;

using System.Threading.Channels;
using DevBrain.Capture.Adapters;
using DevBrain.Core.Models;

public class Normalizer
{
    public async Task Run(ChannelReader<RawEvent> input, ChannelWriter<Observation> output, CancellationToken ct)
    {
        try
        {
            await foreach (var raw in input.ReadAllAsync(ct))
            {
                var obs = new Observation
                {
                    Id = Guid.NewGuid().ToString(),
                    SessionId = raw.SessionId,
                    Timestamp = raw.Timestamp,
                    Project = raw.Project ?? "unknown",
                    Branch = raw.Branch,
                    EventType = raw.EventType,
                    Source = raw.Source,
                    RawContent = raw.Content,
                };
                await output.WriteAsync(obs, ct);
            }
        }
        finally
        {
            output.Complete();
        }
    }
}
