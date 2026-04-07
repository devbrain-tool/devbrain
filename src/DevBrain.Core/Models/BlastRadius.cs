namespace DevBrain.Core.Models;

public record BlastRadius
{
    public required string SourceFile { get; init; }
    public IReadOnlyList<BlastRadiusEntry> AffectedFiles { get; init; } = [];
    public IReadOnlyList<string> DeadEndsAtRisk { get; init; } = [];
    public string? Summary { get; init; }
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
}

public record BlastRadiusEntry
{
    public required string FilePath { get; init; }
    public required double RiskScore { get; init; }
    public required int ChainLength { get; init; }
    public required string Reason { get; init; }
    public IReadOnlyList<string> DecisionChain { get; init; } = [];
}
