namespace DevBrain.Core.Models;

using DevBrain.Core.Enums;

public record VectorMatch
{
    public required string Id { get; init; }
    public required string Text { get; init; }
    public required VectorCategory Category { get; init; }
    public required double Score { get; init; }
}
