namespace DevBrain.Llm;

using DevBrain.Core.Models;

public class LlmTaskQueue
{
    private readonly Func<LlmTask, Task<LlmResult>> _localHandler;
    private readonly Func<LlmTask, Task<LlmResult>> _cloudHandler;
    private readonly Func<bool> _isLocalAvailable;
    private readonly Func<bool> _isCloudAvailable;
    private readonly int _maxDailyCloudRequests;

    public LlmTaskQueue(
        Func<LlmTask, Task<LlmResult>> localHandler,
        Func<LlmTask, Task<LlmResult>> cloudHandler,
        Func<bool> isLocalAvailable,
        Func<bool> isCloudAvailable,
        int maxDailyCloudRequests)
    {
        _localHandler = localHandler;
        _cloudHandler = cloudHandler;
        _isLocalAvailable = isLocalAvailable;
        _isCloudAvailable = isCloudAvailable;
        _maxDailyCloudRequests = maxDailyCloudRequests;
    }

    public int CloudRequestsToday { get; private set; }
    public int QueueDepth => 0;

    public async Task<LlmResult> Submit(LlmTask task, CancellationToken ct = default)
    {
        switch (task.Preference)
        {
            case LlmPreference.Local:
                if (_isLocalAvailable())
                    return await _localHandler(task);
                return Failure(task, "Local LLM not available");

            case LlmPreference.Cloud:
                return await TryCloud(task);

            case LlmPreference.PreferLocal:
                if (_isLocalAvailable())
                {
                    var result = await _localHandler(task);
                    if (result.Success) return result;
                }
                return await TryCloud(task);

            case LlmPreference.PreferCloud:
                var cloudResult = await TryCloudNoFail(task);
                if (cloudResult != null) return cloudResult;
                if (_isLocalAvailable())
                    return await _localHandler(task);
                return Failure(task, "No LLM provider available");

            default:
                return Failure(task, "Unknown preference");
        }
    }

    public void ResetDailyCounter() => CloudRequestsToday = 0;

    private async Task<LlmResult> TryCloud(LlmTask task)
    {
        if (CloudRequestsToday >= _maxDailyCloudRequests)
            return Failure(task, "quota exceeded");
        if (!_isCloudAvailable())
            return Failure(task, "Cloud LLM not available");
        CloudRequestsToday++;
        return await _cloudHandler(task);
    }

    private async Task<LlmResult?> TryCloudNoFail(LlmTask task)
    {
        if (CloudRequestsToday >= _maxDailyCloudRequests) return null;
        if (!_isCloudAvailable()) return null;
        CloudRequestsToday++;
        return await _cloudHandler(task);
    }

    private static LlmResult Failure(LlmTask task, string error) => new()
    {
        TaskId = task.Id,
        Success = false,
        Error = error
    };
}
