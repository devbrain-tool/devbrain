namespace DevBrain.Core.Models;

using DevBrain.Core.Enums;

public record Observation
{
    public required string Id { get; init; }
    public required string SessionId { get; init; }
    public string? ThreadId { get; init; }
    public string? ParentId { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string Project { get; init; }
    public string? Branch { get; init; }
    public required EventType EventType { get; init; }
    public required CaptureSource Source { get; init; }
    public required string RawContent { get; init; }
    public string? Summary { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<string> FilesInvolved { get; init; } = [];
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
