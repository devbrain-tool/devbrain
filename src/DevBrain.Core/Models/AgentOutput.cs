namespace DevBrain.Core.Models;

public enum AgentOutputType
{
    DeadEndDetected,
    BriefingGenerated,
    EdgeCreated,
    ThreadCompressed,
    PatternDetected,
    AlertFired,
    StoryGenerated,
    DecisionChainBuilt,
    GrowthReportGenerated,
    MilestoneAchieved
}

public record AgentOutput(AgentOutputType Type, string Content, object? Data = null);

public record DeadEndOutputData(string? ThreadId, string Project, IReadOnlyList<string> Files);
