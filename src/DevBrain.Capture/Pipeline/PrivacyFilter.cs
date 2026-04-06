namespace DevBrain.Capture.Pipeline;

using System.Threading.Channels;
using DevBrain.Capture.Privacy;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;

public class PrivacyFilter : IPipelineStage
{
    private readonly PrivateTagRedactor _privateTagRedactor;
    private readonly SecretPatternRedactor _secretPatternRedactor;
    private readonly IgnoreFileRedactor? _ignoreFileRedactor;

    public PrivacyFilter(
        PrivateTagRedactor? privateTagRedactor = null,
        SecretPatternRedactor? secretPatternRedactor = null,
        IgnoreFileRedactor? ignoreFileRedactor = null)
    {
        _privateTagRedactor = privateTagRedactor ?? new PrivateTagRedactor();
        _secretPatternRedactor = secretPatternRedactor ?? new SecretPatternRedactor();
        _ignoreFileRedactor = ignoreFileRedactor;
    }

    public async Task Run(ChannelReader<Observation> input, ChannelWriter<Observation> output, CancellationToken ct)
    {
        try
        {
            await foreach (var obs in input.ReadAllAsync(ct))
            {
                // Drop observations matching ignore rules
                if (_ignoreFileRedactor is not null && obs.FilesInvolved.Count > 0
                    && _ignoreFileRedactor.ShouldIgnore(obs.FilesInvolved))
                {
                    continue;
                }

                var rawContent = _privateTagRedactor.Redact(obs.RawContent);
                rawContent = _secretPatternRedactor.Redact(rawContent);

                string? summary = obs.Summary;
                if (summary is not null)
                {
                    summary = _privateTagRedactor.Redact(summary);
                    summary = _secretPatternRedactor.Redact(summary);
                }

                await output.WriteAsync(obs with { RawContent = rawContent, Summary = summary }, ct);
            }
        }
        finally
        {
            output.Complete();
        }
    }
}
