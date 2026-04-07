namespace DevBrain.Core.Interfaces;

using DevBrain.Core.Models;

public interface IAlertStore
{
    Task<DejaVuAlert> Add(DejaVuAlert alert);
    Task<IReadOnlyList<DejaVuAlert>> GetActive();
    Task<IReadOnlyList<DejaVuAlert>> GetAll(int limit = 100);
    Task<bool> Dismiss(string id);
    Task<bool> Exists(string threadId, string deadEndId);
}
