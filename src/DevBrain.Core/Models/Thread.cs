namespace DevBrain.Core.Models;

public enum ThreadState
{
    Active,
    Paused,
    Closed,
    Archived
}

public record DevBrainThread
{
    public required string Id { get; init; }
    public required string Project { get; init; }
    public string? Branch { get; init; }
    public string? Title { get; init; }
    public required ThreadState State { get; init; }
    public required DateTime StartedAt { get; init; }
    public required DateTime LastActivity { get; init; }
    public int ObservationCount { get; init; }
    public string? Summary { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
