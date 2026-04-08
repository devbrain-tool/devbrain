namespace DevBrain.Core.Interfaces;

using DevBrain.Core.Models;

public record DeadEndFilter
{
    public string? Project { get; init; }
    public string? ThreadId { get; init; }
    public DateTime? After { get; init; }
    public DateTime? Before { get; init; }
    public int Limit { get; init; } = 50;
    public int Offset { get; init; } = 0;
}

public interface IDeadEndStore
{
    Task<DeadEnd> Add(DeadEnd deadEnd);
    Task<IReadOnlyList<DeadEnd>> Query(DeadEndFilter filter);
    Task<IReadOnlyList<DeadEnd>> FindByFiles(IReadOnlyList<string> filePaths);
    Task<IReadOnlyList<DeadEnd>> FindSimilar(string description, int limit = 5);
}
