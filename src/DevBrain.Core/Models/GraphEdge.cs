namespace DevBrain.Core.Models;

public record GraphEdge
{
    public required string Id { get; init; }
    public required string SourceId { get; init; }
    public required string TargetId { get; init; }
    public required string Type { get; init; }
    public string? Data { get; init; }
    public double Weight { get; init; } = 1.0;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
