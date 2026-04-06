namespace DevBrain.Capture.Pipeline;

using System.Threading.Channels;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;

public class Enricher : IPipelineStage
{
    private readonly ThreadResolver _resolver;

    public Enricher(ThreadResolver resolver)
    {
        _resolver = resolver;
    }

    public async Task Run(ChannelReader<Observation> input, ChannelWriter<Observation> output, CancellationToken ct)
    {
        try
        {
            await foreach (var obs in input.ReadAllAsync(ct))
            {
                var assignment = _resolver.Resolve(obs);
                var enriched = obs with { ThreadId = assignment.ThreadId };
                await output.WriteAsync(enriched, ct);
            }
        }
        finally
        {
            output.Complete();
        }
    }
}
