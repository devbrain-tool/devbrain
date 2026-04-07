namespace DevBrain.Api.Setup;

public record CheckResult
{
    public required string Id { get; init; }
    public required string Category { get; init; }
    public required string Name { get; init; }
    public required string Status { get; init; } // "pass", "fail", "warn", "skip"
    public required string Detail { get; init; }
    public required bool Fixable { get; init; }
}

public record SetupStatus
{
    public required List<CheckResult> Checks { get; init; }
    public required StatusSummary Summary { get; init; }
}

public record StatusSummary
{
    public int Pass { get; init; }
    public int Fail { get; init; }
    public int Warn { get; init; }
    public int Skip { get; init; }
}

public record FixResult
{
    public required bool Success { get; init; }
    public required string Detail { get; init; }
}
