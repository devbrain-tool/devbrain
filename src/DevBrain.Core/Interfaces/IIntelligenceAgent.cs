namespace DevBrain.Core.Interfaces;

using DevBrain.Core.Enums;
using DevBrain.Core.Models;

public record AgentContext(
    IObservationStore Observations,
    IGraphStore Graph,
    IVectorStore Vectors,
    ILlmService Llm,
    IDeadEndStore DeadEnds,
    Settings Settings
);

public interface IIntelligenceAgent
{
    string Name { get; }
    AgentSchedule Schedule { get; }
    Priority Priority { get; }
    Task<IReadOnlyList<AgentOutput>> Run(AgentContext ctx, CancellationToken ct);
}
