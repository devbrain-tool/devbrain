namespace DevBrain.Core.Models;

using DevBrain.Core.Enums;

public record DeveloperMetric
{
    public required string Id { get; init; }
    public required string Dimension { get; init; }
    public required double Value { get; init; }
    public required DateTime PeriodStart { get; init; }
    public required DateTime PeriodEnd { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

public record GrowthMilestone
{
    public required string Id { get; init; }
    public required MilestoneType Type { get; init; }
    public required string Description { get; init; }
    public required DateTime AchievedAt { get; init; }
    public string? ObservationId { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

public record GrowthReport
{
    public required string Id { get; init; }
    public required DateTime PeriodStart { get; init; }
    public required DateTime PeriodEnd { get; init; }
    public IReadOnlyList<DeveloperMetric> Metrics { get; init; } = [];
    public IReadOnlyList<GrowthMilestone> Milestones { get; init; } = [];
    public string? Narrative { get; init; }
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
}
