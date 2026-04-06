namespace DevBrain.Core.Models;

public record HealthStatus
{
    public required string Status { get; init; }
    public required long UptimeSeconds { get; init; }
    public required StorageHealth Storage { get; init; }
    public required Dictionary<string, AgentHealth> Agents { get; init; }
    public required LlmHealth Llm { get; init; }
}

public record StorageHealth
{
    public required long SqliteSizeMb { get; init; }
    public required long LanceDbSizeMb { get; init; }
    public required long TotalObservations { get; init; }
}

public record AgentHealth
{
    public required DateTime? LastRun { get; init; }
    public required string Status { get; init; }
}

public record LlmHealth
{
    public required LlmProviderHealth Local { get; init; }
    public required LlmProviderHealth Cloud { get; init; }
}

public record LlmProviderHealth
{
    public required string Status { get; init; }
    public string? Model { get; init; }
    public int? QueueDepth { get; init; }
    public int? RequestsToday { get; init; }
    public int? Limit { get; init; }
}
