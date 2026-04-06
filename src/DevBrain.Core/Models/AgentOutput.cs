namespace DevBrain.Core.Models;

public enum AgentOutputType
{
    DeadEndDetected,
    BriefingGenerated,
    EdgeCreated,
    ThreadCompressed,
    PatternDetected
}

public record AgentOutput(AgentOutputType Type, string Content, object? Data = null);
