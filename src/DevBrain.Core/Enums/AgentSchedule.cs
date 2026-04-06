namespace DevBrain.Core.Enums;

public abstract record AgentSchedule
{
    public record Cron(string Expression) : AgentSchedule;
    public record OnEvent(params EventType[] Types) : AgentSchedule;
    public record OnDemand : AgentSchedule;
    public record Idle(TimeSpan After) : AgentSchedule;
}
