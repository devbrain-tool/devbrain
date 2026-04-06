namespace DevBrain.Agents;

using DevBrain.Core.Models;

public class EventBus
{
    private readonly List<Action<Observation>> _handlers = new();
    private readonly object _lock = new();

    public void Subscribe(Action<Observation> handler)
    {
        lock (_lock)
        {
            _handlers.Add(handler);
        }
    }

    public void Publish(Observation observation)
    {
        List<Action<Observation>> snapshot;
        lock (_lock)
        {
            snapshot = new List<Action<Observation>>(_handlers);
        }

        foreach (var handler in snapshot)
        {
            try
            {
                handler(observation);
            }
            catch
            {
                // Swallow handler exceptions to prevent pipeline crashes.
                // In production, inject ILogger and log here.
            }
        }
    }
}
