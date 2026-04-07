namespace DevBrain.Core.Models;

using DevBrain.Core.Enums;

public record DecisionChain
{
    public required string Id { get; init; }
    public required string RootNodeId { get; init; }
    public required string Narrative { get; init; }
    public IReadOnlyList<DecisionStep> Steps { get; init; } = [];
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
}

public record DecisionStep
{
    public required string ObservationId { get; init; }
    public required string Summary { get; init; }
    public required DateTime Timestamp { get; init; }
    public required DecisionStepType StepType { get; init; }
    public IReadOnlyList<string> FilesInvolved { get; init; } = [];
    public IReadOnlyList<string> AlternativesRejected { get; init; } = [];
}
