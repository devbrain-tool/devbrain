namespace DevBrain.Capture.Pipeline;

using System.Threading.Channels;
using DevBrain.Capture.Adapters;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;

public class PipelineOrchestrator
{
    private readonly Normalizer _normalizer;
    private readonly Enricher _enricher;
    private readonly PrivacyFilter _privacyFilter;
    private readonly Tagger _tagger;
    private readonly Writer _writer;

    public PipelineOrchestrator(
        Normalizer normalizer,
        Enricher enricher,
        PrivacyFilter privacyFilter,
        Tagger tagger,
        Writer writer)
    {
        _normalizer = normalizer;
        _enricher = enricher;
        _privacyFilter = privacyFilter;
        _tagger = tagger;
        _writer = writer;
    }

    public (ChannelWriter<RawEvent> Input, Task PipelineTask) Start(CancellationToken ct)
    {
        var options = new BoundedChannelOptions(100)
        {
            SingleWriter = false,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        };

        var rawChannel = Channel.CreateBounded<RawEvent>(options);
        var normalizedChannel = Channel.CreateBounded<Observation>(options);
        var enrichedChannel = Channel.CreateBounded<Observation>(options);
        var filteredChannel = Channel.CreateBounded<Observation>(options);
        var taggedChannel = Channel.CreateBounded<Observation>(options);

        var pipelineTask = Task.WhenAll(
            _normalizer.Run(rawChannel.Reader, normalizedChannel.Writer, ct),
            _enricher.Run(normalizedChannel.Reader, enrichedChannel.Writer, ct),
            _privacyFilter.Run(enrichedChannel.Reader, filteredChannel.Writer, ct),
            _tagger.Run(filteredChannel.Reader, taggedChannel.Writer, ct),
            _writer.Run(taggedChannel.Reader, ct)
        );

        return (rawChannel.Writer, pipelineTask);
    }
}
