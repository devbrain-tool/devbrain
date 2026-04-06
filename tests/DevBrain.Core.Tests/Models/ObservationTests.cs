namespace DevBrain.Core.Tests.Models;

using DevBrain.Core.Enums;
using DevBrain.Core.Models;

public class ObservationTests
{
    [Fact]
    public void Can_create_observation_with_required_fields()
    {
        var obs = new Observation
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow,
            Project = "test-project",
            EventType = EventType.Decision,
            Source = CaptureSource.ClaudeCode,
            RawContent = "Chose approach A because X"
        };

        Assert.Equal("test-project", obs.Project);
        Assert.Equal(EventType.Decision, obs.EventType);
        Assert.Null(obs.Summary);
        Assert.Empty(obs.Tags);
        Assert.Empty(obs.FilesInvolved);
    }

    [Fact]
    public void Can_create_observation_with_all_fields()
    {
        var obs = new Observation
        {
            Id = "obs-1",
            SessionId = "sess-1",
            ThreadId = "thread-1",
            ParentId = "obs-0",
            Timestamp = new DateTime(2026, 4, 5, 12, 0, 0, DateTimeKind.Utc),
            Project = "devbrain",
            Branch = "main",
            EventType = EventType.ToolCall,
            Source = CaptureSource.Cursor,
            RawContent = "Read file src/main.cs",
            Summary = "Read the main entry point",
            Tags = ["exploration", "setup"],
            FilesInvolved = ["src/main.cs"]
        };

        Assert.Equal("thread-1", obs.ThreadId);
        Assert.Equal("main", obs.Branch);
        Assert.Equal("Read the main entry point", obs.Summary);
        Assert.Equal(2, obs.Tags.Count);
        Assert.Single(obs.FilesInvolved);
    }
}
