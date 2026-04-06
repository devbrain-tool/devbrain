namespace DevBrain.Core.Interfaces;

using System.Threading.Channels;
using DevBrain.Core.Models;

public interface IPipelineStage
{
    Task Run(ChannelReader<Observation> input, ChannelWriter<Observation> output, CancellationToken ct);
}
