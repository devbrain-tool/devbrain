namespace DevBrain.Core.Interfaces;

using DevBrain.Core.Enums;
using DevBrain.Core.Models;

public interface IVectorStore
{
    Task Index(string id, string text, VectorCategory category);
    Task<IReadOnlyList<VectorMatch>> Search(string query, int topK = 20, VectorCategory? filter = null);
    Task Remove(string id);
    Task Rebuild();
    Task<long> GetSizeBytes();
}
