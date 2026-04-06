namespace DevBrain.Capture.Pipeline;

using System.Threading.Channels;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;

public class Writer
{
    private readonly IObservationStore _store;
    private readonly IVectorStore? _vectorStore;
    private readonly Action<Observation>? _callback;

    public Writer(IObservationStore store, IVectorStore? vectorStore = null, Action<Observation>? callback = null)
    {
        _store = store;
        _vectorStore = vectorStore;
        _callback = callback;
    }

    public async Task Run(ChannelReader<Observation> input, CancellationToken ct)
    {
        await foreach (var obs in input.ReadAllAsync(ct))
        {
            await _store.Add(obs);

            if (_vectorStore is not null)
            {
                await _vectorStore.Index(obs.Id, obs.RawContent, Core.Enums.VectorCategory.ObservationSummary);
            }

            _callback?.Invoke(obs);
        }
    }
}
