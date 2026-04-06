namespace DevBrain.Api;

using DevBrain.Core.Enums;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;

/// <summary>
/// Placeholder IVectorStore that does nothing.
/// Will be replaced by a real implementation (e.g. LanceDB) in a later task.
/// </summary>
public class NullVectorStore : IVectorStore
{
    public Task Index(string id, string text, VectorCategory category) => Task.CompletedTask;

    public Task<IReadOnlyList<VectorMatch>> Search(string query, int topK = 20, VectorCategory? filter = null)
        => Task.FromResult<IReadOnlyList<VectorMatch>>(Array.Empty<VectorMatch>());

    public Task Remove(string id) => Task.CompletedTask;

    public Task Rebuild() => Task.CompletedTask;

    public Task<long> GetSizeBytes() => Task.FromResult(0L);
}
