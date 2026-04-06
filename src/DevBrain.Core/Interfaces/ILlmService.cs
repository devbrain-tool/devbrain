namespace DevBrain.Core.Interfaces;

using DevBrain.Core.Models;

public interface ILlmService
{
    Task<LlmResult> Submit(LlmTask task, CancellationToken ct = default);
    Task<float[]> Embed(string text, CancellationToken ct = default);
    bool IsLocalAvailable { get; }
    bool IsCloudAvailable { get; }
    int CloudRequestsToday { get; }
    int QueueDepth { get; }
}
