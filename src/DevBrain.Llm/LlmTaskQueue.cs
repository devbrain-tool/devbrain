namespace DevBrain.Llm;

using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;

public class LlmTaskQueue : ILlmService
{
    private readonly Func<LlmTask, Task<LlmResult>> _localHandler;
    private readonly Func<LlmTask, Task<LlmResult>> _cloudHandler;
    private readonly Func<bool> _isLocalAvailable;
    private readonly Func<bool> _isCloudAvailable;
    private readonly Func<string, CancellationToken, Task<float[]>>? _embedHandler;
    private readonly int _maxDailyCloudRequests;
    private int _cloudRequestsToday;

    public LlmTaskQueue(
        Func<LlmTask, Task<LlmResult>> localHandler,
        Func<LlmTask, Task<LlmResult>> cloudHandler,
        Func<bool> isLocalAvailable,
        Func<bool> isCloudAvailable,
        int maxDailyCloudRequests,
        Func<string, CancellationToken, Task<float[]>>? embedHandler = null)
    {
        _localHandler = localHandler;
        _cloudHandler = cloudHandler;
        _isLocalAvailable = isLocalAvailable;
        _isCloudAvailable = isCloudAvailable;
        _maxDailyCloudRequests = maxDailyCloudRequests;
        _embedHandler = embedHandler;
    }

    public int CloudRequestsToday => _cloudRequestsToday;
    public int QueueDepth => 0;
    public bool IsLocalAvailable => _isLocalAvailable();
    public bool IsCloudAvailable => _isCloudAvailable();

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

    public async Task<float[]> Embed(string text, CancellationToken ct = default)
    {
        if (_embedHandler is not null)
            return await _embedHandler(text, ct);
        return new float[384];
    }

    public void ResetDailyCounter() => Interlocked.Exchange(ref _cloudRequestsToday, 0);

    private async Task<LlmResult> TryCloud(LlmTask task)
    {
        if (Interlocked.CompareExchange(ref _cloudRequestsToday, 0, 0) >= _maxDailyCloudRequests)
            return Failure(task, "quota exceeded");
        if (!_isCloudAvailable())
            return Failure(task, "Cloud LLM not available");
        Interlocked.Increment(ref _cloudRequestsToday);
        return await _cloudHandler(task);
    }

    private async Task<LlmResult?> TryCloudNoFail(LlmTask task)
    {
        if (Interlocked.CompareExchange(ref _cloudRequestsToday, 0, 0) >= _maxDailyCloudRequests) return null;
        if (!_isCloudAvailable()) return null;
        Interlocked.Increment(ref _cloudRequestsToday);
        return await _cloudHandler(task);
    }

    private static LlmResult Failure(LlmTask task, string error) => new()
    {
        TaskId = task.Id,
        Success = false,
        Error = error
    };
}
