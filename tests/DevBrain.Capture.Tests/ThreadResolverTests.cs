namespace DevBrain.Capture.Tests;

using DevBrain.Core.Enums;
using DevBrain.Core.Models;

public class ThreadResolverTests
{
    private static Observation MakeObs(
        string sessionId = "s1",
        string project = "proj",
        string? branch = "main",
        DateTime? timestamp = null)
    {
        return new Observation
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = sessionId,
            Timestamp = timestamp ?? DateTime.UtcNow,
            Project = project,
            Branch = branch,
            EventType = EventType.FileChange,
            Source = CaptureSource.ClaudeCode,
            RawContent = "test content",
        };
    }

    [Fact]
    public void Same_Session_And_Project_Continues_Thread()
    {
        var resolver = new ThreadResolver();
        var now = DateTime.UtcNow;

        var a1 = resolver.Resolve(MakeObs(timestamp: now));
        var a2 = resolver.Resolve(MakeObs(timestamp: now.AddMinutes(5)));

        Assert.True(a1.IsNewThread);
        Assert.False(a2.IsNewThread);
        Assert.Equal(a1.ThreadId, a2.ThreadId);
    }

    [Fact]
    public void Different_Session_Creates_New_Thread()
    {
        var resolver = new ThreadResolver();
        var now = DateTime.UtcNow;

        var a1 = resolver.Resolve(MakeObs(sessionId: "s1", timestamp: now));
        var a2 = resolver.Resolve(MakeObs(sessionId: "s2", timestamp: now.AddMinutes(1)));

        Assert.True(a1.IsNewThread);
        Assert.True(a2.IsNewThread);
        Assert.NotEqual(a1.ThreadId, a2.ThreadId);
    }

    [Fact]
    public void Different_Project_Creates_New_Thread()
    {
        var resolver = new ThreadResolver();
        var now = DateTime.UtcNow;

        var a1 = resolver.Resolve(MakeObs(project: "projA", timestamp: now));
        var a2 = resolver.Resolve(MakeObs(project: "projB", timestamp: now.AddMinutes(1)));

        Assert.True(a1.IsNewThread);
        Assert.True(a2.IsNewThread);
        Assert.NotEqual(a1.ThreadId, a2.ThreadId);
    }

    [Fact]
    public void Different_Branch_Creates_New_Thread()
    {
        var resolver = new ThreadResolver();
        var now = DateTime.UtcNow;

        var a1 = resolver.Resolve(MakeObs(branch: "main", timestamp: now));
        var a2 = resolver.Resolve(MakeObs(branch: "feature", timestamp: now.AddMinutes(1)));

        Assert.True(a1.IsNewThread);
        Assert.True(a2.IsNewThread);
        Assert.NotEqual(a1.ThreadId, a2.ThreadId);
    }

    [Fact]
    public void Time_Gap_Exceeding_Threshold_Creates_New_Thread()
    {
        var resolver = new ThreadResolver(gapThreshold: TimeSpan.FromHours(1));
        var now = DateTime.UtcNow;

        var a1 = resolver.Resolve(MakeObs(timestamp: now));
        var a2 = resolver.Resolve(MakeObs(timestamp: now.AddHours(2)));

        Assert.True(a1.IsNewThread);
        Assert.True(a2.IsNewThread);
        Assert.NotEqual(a1.ThreadId, a2.ThreadId);
    }

    [Fact]
    public void Time_Gap_Within_Threshold_Continues_Thread()
    {
        var resolver = new ThreadResolver(gapThreshold: TimeSpan.FromHours(2));
        var now = DateTime.UtcNow;

        var a1 = resolver.Resolve(MakeObs(timestamp: now));
        var a2 = resolver.Resolve(MakeObs(timestamp: now.AddHours(1)));

        Assert.True(a1.IsNewThread);
        Assert.False(a2.IsNewThread);
        Assert.Equal(a1.ThreadId, a2.ThreadId);
    }
}
