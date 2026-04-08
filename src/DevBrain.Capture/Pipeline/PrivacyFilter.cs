namespace DevBrain.Capture.Pipeline;

using System.Threading.Channels;
using DevBrain.Capture.Privacy;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;

public class PrivacyFilter : IPipelineStage
{
    private readonly PrivateTagRedactor _privateTagRedactor;
    private readonly SecretPatternRedactor _secretPatternRedactor;
    private readonly FieldAwareRedactor _fieldAwareRedactor;
    private readonly IgnoreFileRedactor? _ignoreFileRedactor;

    public PrivacyFilter(
        PrivateTagRedactor? privateTagRedactor = null,
        SecretPatternRedactor? secretPatternRedactor = null,
        FieldAwareRedactor? fieldAwareRedactor = null,
        IgnoreFileRedactor? ignoreFileRedactor = null)
    {
        _privateTagRedactor = privateTagRedactor ?? new PrivateTagRedactor();
        _secretPatternRedactor = secretPatternRedactor ?? new SecretPatternRedactor();
        _fieldAwareRedactor = fieldAwareRedactor ?? new FieldAwareRedactor();
        _ignoreFileRedactor = ignoreFileRedactor;
    }

    public async Task Run(ChannelReader<Observation> input, ChannelWriter<Observation> output, CancellationToken ct)
    {
        try
        {
            await foreach (var obs in input.ReadAllAsync(ct))
            {
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

                // Layer 1: blanket regex on metadata
                var metadata = _secretPatternRedactor.Redact(obs.Metadata);
                // Layer 2: field-aware redaction on metadata
                metadata = _fieldAwareRedactor.Redact(obs.ToolName, metadata);

                await output.WriteAsync(obs with
                {
                    RawContent = rawContent,
                    Summary = summary,
                    Metadata = metadata,
                }, ct);
            }
        }
        finally
        {
            output.Complete();
        }
    }
}
