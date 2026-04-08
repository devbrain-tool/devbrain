namespace DevBrain.Core.Interfaces;

using DevBrain.Core.Models;

public interface ISessionStore
{
    Task<SessionSummary> Add(SessionSummary summary);
    Task<SessionSummary?> GetBySessionId(string sessionId);
    Task<IReadOnlyList<SessionSummary>> GetAll(int limit = 50);
    Task<SessionSummary?> GetLatest();
    Task<IReadOnlyList<SessionSummary>> GetByDateRange(DateTime after, DateTime before);
}
