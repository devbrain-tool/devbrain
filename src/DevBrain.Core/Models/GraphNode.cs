namespace DevBrain.Core.Models;

public record GraphNode
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required string Name { get; init; }
    public string? Data { get; init; }
    public string? SourceId { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
