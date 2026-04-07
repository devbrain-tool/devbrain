namespace DevBrain.Agents.Tests;

using DevBrain.Core.Enums;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;

public class NullVectorStore : IVectorStore
{
    public Task Index(string id, string text, VectorCategory category) => Task.CompletedTask;
    public Task<IReadOnlyList<VectorMatch>> Search(string query, int topK = 20, VectorCategory? filter = null)
        => Task.FromResult<IReadOnlyList<VectorMatch>>(Array.Empty<VectorMatch>());
    public Task Remove(string id) => Task.CompletedTask;
    public Task Rebuild() => Task.CompletedTask;
    public Task<long> GetSizeBytes() => Task.FromResult(0L);
}

public class NullLlmService : ILlmService
{
    public bool IsLocalAvailable => false;
    public bool IsCloudAvailable => false;
    public int CloudRequestsToday => 0;
    public int QueueDepth => 0;
    public Task<LlmResult> Submit(LlmTask task, CancellationToken ct = default)
        => Task.FromResult(new LlmResult { TaskId = task.Id, Success = false });
    public Task<float[]> Embed(string text, CancellationToken ct = default)
        => Task.FromResult(Array.Empty<float>());
}

public class NullDeadEndStore : IDeadEndStore
{
    public Task<DeadEnd> Add(DeadEnd deadEnd) => Task.FromResult(deadEnd);
    public Task<IReadOnlyList<DeadEnd>> Query(DeadEndFilter filter)
        => Task.FromResult<IReadOnlyList<DeadEnd>>(Array.Empty<DeadEnd>());
    public Task<IReadOnlyList<DeadEnd>> FindByFiles(IReadOnlyList<string> filePaths)
        => Task.FromResult<IReadOnlyList<DeadEnd>>(Array.Empty<DeadEnd>());
    public Task<IReadOnlyList<DeadEnd>> FindSimilar(string description, int limit = 5)
        => Task.FromResult<IReadOnlyList<DeadEnd>>(Array.Empty<DeadEnd>());
}
