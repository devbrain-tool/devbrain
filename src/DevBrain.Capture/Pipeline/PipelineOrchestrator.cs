namespace DevBrain.Capture.Pipeline;

using System.Threading.Channels;
using DevBrain.Capture.Adapters;
using DevBrain.Core.Models;

public class PipelineOrchestrator
{
    private readonly Normalizer _normalizer;
    private readonly Enricher _enricher;
    private readonly Tagger _tagger;
    private readonly PrivacyFilter _privacyFilter;
    private readonly Writer _writer;

    public PipelineOrchestrator(
        Normalizer normalizer,
        Enricher enricher,
        Tagger tagger,
        PrivacyFilter privacyFilter,
        Writer writer)
    {
        _normalizer = normalizer;
        _enricher = enricher;
        _tagger = tagger;
        _privacyFilter = privacyFilter;
        _writer = writer;
    }

    /// <summary>
    /// Pipeline order per spec: Normalizer → Enricher → Tagger → PrivacyFilter → Writer
    /// Tagger runs before PrivacyFilter so classification sees original content.
    /// PrivacyFilter redacts before Writer stores to disk.
    /// </summary>
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
        var taggedChannel = Channel.CreateBounded<Observation>(options);
        var filteredChannel = Channel.CreateBounded<Observation>(options);

        var pipelineTask = Task.WhenAll(
            _normalizer.Run(rawChannel.Reader, normalizedChannel.Writer, ct),
            _enricher.Run(normalizedChannel.Reader, enrichedChannel.Writer, ct),
            _tagger.Run(enrichedChannel.Reader, taggedChannel.Writer, ct),
            _privacyFilter.Run(taggedChannel.Reader, filteredChannel.Writer, ct),
            _writer.Run(filteredChannel.Reader, ct)
        );

        return (rawChannel.Writer, pipelineTask);
    }
}
