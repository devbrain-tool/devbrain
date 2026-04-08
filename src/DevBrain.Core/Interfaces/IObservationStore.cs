namespace DevBrain.Core.Interfaces;

using DevBrain.Core.Enums;
using DevBrain.Core.Models;

public record ObservationFilter
{
    public string? Project { get; init; }
    public EventType? EventType { get; init; }
    public string? ThreadId { get; init; }
    public string? ToolName { get; init; }
    public string? Outcome { get; init; }
    public DateTime? After { get; init; }
    public DateTime? Before { get; init; }
    public int Limit { get; init; } = 50;
    public int Offset { get; init; } = 0;
}

public interface IObservationStore
{
    Task<Observation> Add(Observation observation);
    Task<Observation?> GetById(string id);
    Task<IReadOnlyList<Observation>> Query(ObservationFilter filter);
    Task<IReadOnlyList<Observation>> GetUnenriched(int limit = 50);
    Task Update(Observation observation);
    Task Delete(string id);
    Task<IReadOnlyList<Observation>> SearchFts(string query, int limit = 20);
    Task<long> Count();
    Task<long> GetDatabaseSizeBytes();
    Task DeleteByProject(string project);
    Task DeleteBefore(DateTime before);
    Task<IReadOnlyList<Observation>> GetSessionObservations(string sessionId, int limit = 500);
}
