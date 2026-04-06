namespace DevBrain.Core.Models;

using DevBrain.Core.Enums;

public enum LlmTaskType
{
    Classification,
    Summarization,
    Synthesis,
    Embedding
}

public enum LlmPreference
{
    Local,
    Cloud,
    PreferLocal,
    PreferCloud
}

public record LlmTask
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public required string AgentName { get; init; }
    public required Priority Priority { get; init; }
    public required LlmTaskType Type { get; init; }
    public required string Prompt { get; init; }
    public required LlmPreference Preference { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

public record LlmResult
{
    public required string TaskId { get; init; }
    public required bool Success { get; init; }
    public string? Content { get; init; }
    public string? Error { get; init; }
    public string? Provider { get; init; }
}
