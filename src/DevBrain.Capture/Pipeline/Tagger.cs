namespace DevBrain.Capture.Pipeline;

using System.Threading.Channels;
using DevBrain.Core.Enums;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;

public class Tagger : IPipelineStage
{
    private readonly ILlmService? _llm;

    public Tagger(ILlmService? llm = null)
    {
        _llm = llm;
    }

    public async Task Run(ChannelReader<Observation> input, ChannelWriter<Observation> output, CancellationToken ct)
    {
        try
        {
            await foreach (var obs in input.ReadAllAsync(ct))
            {
                if (_llm is null || (!_llm.IsLocalAvailable && !_llm.IsCloudAvailable))
                {
                    await output.WriteAsync(obs, ct);
                    continue;
                }

                try
                {
                    var task = new LlmTask
                    {
                        AgentName = "Tagger",
                        Priority = Priority.Low,
                        Type = LlmTaskType.Classification,
                        Prompt = $"Classify this development observation into tags (comma-separated). Content: {obs.RawContent}",
                        Preference = LlmPreference.PreferLocal,
                    };

                    var result = await _llm.Submit(task, ct);
                    if (result.Success && result.Content is not null)
                    {
                        var tags = result.Content
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .ToList();
                        await output.WriteAsync(obs with { Tags = tags }, ct);
                    }
                    else
                    {
                        await output.WriteAsync(obs, ct);
                    }
                }
                catch
                {
                    // Graceful degradation: pass through unchanged
                    await output.WriteAsync(obs, ct);
                }
            }
        }
        finally
        {
            output.Complete();
        }
    }
}
