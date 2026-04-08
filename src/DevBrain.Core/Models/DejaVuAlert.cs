namespace DevBrain.Core.Models;

using DevBrain.Core.Enums;

public record DejaVuAlert
{
    public required string Id { get; init; }
    public required string ThreadId { get; init; }
    public required string MatchedDeadEndId { get; init; }
    public required double Confidence { get; init; }
    public required string Message { get; init; }
    public required MatchStrategy Strategy { get; init; }
    public bool Dismissed { get; init; } = false;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
