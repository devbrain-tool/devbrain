namespace DevBrain.Capture;

using DevBrain.Core.Models;

public record ThreadAssignment(string ThreadId, bool IsNewThread);

public class ThreadResolver
{
    private readonly TimeSpan _gapThreshold;
    private string? _currentSessionId;
    private string? _currentProject;
    private string? _currentBranch;
    private string? _currentThreadId;
    private DateTime _lastActivity;

    public ThreadResolver(TimeSpan? gapThreshold = null)
    {
        _gapThreshold = gapThreshold ?? TimeSpan.FromHours(2);
    }

    public ThreadAssignment Resolve(Observation obs)
    {
        bool isNew = _currentThreadId is null
            || obs.SessionId != _currentSessionId
            || obs.Project != _currentProject
            || obs.Branch != _currentBranch
            || (obs.Timestamp - _lastActivity) > _gapThreshold;

        if (isNew)
        {
            _currentThreadId = Guid.NewGuid().ToString();
        }

        _currentSessionId = obs.SessionId;
        _currentProject = obs.Project;
        _currentBranch = obs.Branch;
        _lastActivity = obs.Timestamp;

        return new ThreadAssignment(_currentThreadId!, isNew);
    }
}
