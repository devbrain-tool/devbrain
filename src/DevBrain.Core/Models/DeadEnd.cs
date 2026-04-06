namespace DevBrain.Core.Models;

public record DeadEnd
{
    public required string Id { get; init; }
    public string? ThreadId { get; init; }
    public required string Project { get; init; }
    public required string Description { get; init; }
    public required string Approach { get; init; }
    public required string Reason { get; init; }
    public IReadOnlyList<string> FilesInvolved { get; init; } = [];
    public required DateTime DetectedAt { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
