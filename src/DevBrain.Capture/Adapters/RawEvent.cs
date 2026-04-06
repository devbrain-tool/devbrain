namespace DevBrain.Capture.Adapters;

using DevBrain.Core.Enums;

public record RawEvent
{
    public required string SessionId { get; init; }
    public required EventType EventType { get; init; }
    public required CaptureSource Source { get; init; }
    public required string Content { get; init; }
    public string? Project { get; init; }
    public string? Branch { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string ContentHash { get; init; } = "";
}
