namespace DevBrain.Core.Models;

public record SessionSummary
{
    public required string Id { get; init; }
    public required string SessionId { get; init; }
    public required string Narrative { get; init; }
    public required string Outcome { get; init; }
    public required TimeSpan Duration { get; init; }
    public required int ObservationCount { get; init; }
    public required int FilesTouched { get; init; }
    public required int DeadEndsHit { get; init; }
    public IReadOnlyList<string> Phases { get; init; } = [];
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
