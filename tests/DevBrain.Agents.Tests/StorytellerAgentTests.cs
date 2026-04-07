using DevBrain.Agents;
using DevBrain.Core.Enums;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;
using DevBrain.Storage;
using DevBrain.Storage.Schema;
using Microsoft.Data.Sqlite;

namespace DevBrain.Agents.Tests;

public class StorytellerAgentTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private SqliteObservationStore _obsStore = null!;
    private SqliteGraphStore _graphStore = null!;
    private SqliteDeadEndStore _deadEndStore = null!;
    private SqliteSessionStore _sessionStore = null!;
    private StorytellerAgent _agent = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        SchemaManager.Initialize(_connection);
        _obsStore = new SqliteObservationStore(_connection);
        _graphStore = new SqliteGraphStore(_connection);
        _deadEndStore = new SqliteDeadEndStore(_connection);
        _sessionStore = new SqliteSessionStore(_connection);
        _agent = new StorytellerAgent(_sessionStore);
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private AgentContext CreateContext(ILlmService? llm = null)
    {
        return new AgentContext(
            Observations: _obsStore,
            Graph: _graphStore,
            Vectors: new NullVectorStore(),
            Llm: llm ?? new StoryLlmService(),
            Settings: new Settings(),
            DeadEnds: _deadEndStore
        );
    }

    [Fact]
    public async Task Run_GeneratesStoryForSessionWithEnoughObservations()
    {
        var now = DateTime.UtcNow;
        for (int i = 0; i < 5; i++)
        {
            await _obsStore.Add(new Observation
            {
                Id = $"obs-{i}", SessionId = "session-1", ThreadId = "t1",
                Timestamp = now.AddMinutes(-30 + i * 5), Project = "proj",
                EventType = i < 3 ? EventType.FileChange : EventType.Decision,
                Source = CaptureSource.ClaudeCode,
                RawContent = $"Activity {i}", Summary = $"Step {i}",
                FilesInvolved = [$"src/File{i}.cs"]
            });
        }

        var ctx = CreateContext();
        var results = await _agent.Run(ctx, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(AgentOutputType.StoryGenerated, results[0].Type);

        var story = await _sessionStore.GetBySessionId("session-1");
        Assert.NotNull(story);
        Assert.Equal(5, story.ObservationCount);
        Assert.True(story.FilesTouched >= 5);
    }

    [Fact]
    public async Task Run_SkipsSessionWithTooFewObservations()
    {
        await _obsStore.Add(new Observation
        {
            Id = "obs-1", SessionId = "session-short", ThreadId = "t1",
            Timestamp = DateTime.UtcNow, Project = "proj",
            EventType = EventType.Conversation, Source = CaptureSource.ClaudeCode,
            RawContent = "Just one observation"
        });

        var ctx = CreateContext();
        var results = await _agent.Run(ctx, CancellationToken.None);

        Assert.Empty(results);
        Assert.Null(await _sessionStore.GetBySessionId("session-short"));
    }

    [Fact]
    public async Task Run_SkipsAlreadyGeneratedSession()
    {
        var now = DateTime.UtcNow;
        for (int i = 0; i < 4; i++)
        {
            await _obsStore.Add(new Observation
            {
                Id = $"obs-{i}", SessionId = "session-done", ThreadId = "t1",
                Timestamp = now.AddMinutes(-20 + i * 5), Project = "proj",
                EventType = EventType.FileChange, Source = CaptureSource.ClaudeCode,
                RawContent = $"Activity {i}"
            });
        }

        // Pre-create a story
        await _sessionStore.Add(new SessionSummary
        {
            Id = "ss-existing", SessionId = "session-done",
            Narrative = "Already generated", Outcome = "Done",
            Duration = TimeSpan.FromMinutes(15), ObservationCount = 4,
            FilesTouched = 0, DeadEndsHit = 0, Phases = []
        });

        var ctx = CreateContext();
        var results = await _agent.Run(ctx, CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public void DetectPhases_IdentifiesPhaseTransitions()
    {
        var now = DateTime.UtcNow;
        var observations = new List<Observation>
        {
            MakeObs("1", now, EventType.Conversation),
            MakeObs("2", now.AddMinutes(5), EventType.Conversation),
            MakeObs("3", now.AddMinutes(15), EventType.FileChange),
            MakeObs("4", now.AddMinutes(20), EventType.FileChange),
            MakeObs("5", now.AddMinutes(25), EventType.Error),
            MakeObs("6", now.AddMinutes(30), EventType.FileChange),
        };

        var phases = StorytellerAgent.DetectPhases(observations);

        Assert.Contains("Exploration", phases);
        Assert.True(phases.Count >= 2);
    }

    [Fact]
    public void DetectTurningPoints_FindsDecisionsAndResolutions()
    {
        var now = DateTime.UtcNow;
        var observations = new List<Observation>
        {
            MakeObs("1", now, EventType.Error),
            MakeObs("2", now.AddMinutes(15), EventType.Decision,
                summary: "Decided to use mutex"),
            MakeObs("3", now.AddMinutes(20), EventType.FileChange),
        };

        var points = StorytellerAgent.DetectTurningPoints(observations);

        Assert.Contains(points, p => p.Contains("Decision"));
        Assert.Contains(points, p => p.Contains("resolved"));
    }

    private static Observation MakeObs(string id, DateTime timestamp, EventType type,
        string? summary = null) => new()
    {
        Id = id, SessionId = "s1", ThreadId = "t1",
        Timestamp = timestamp, Project = "proj",
        EventType = type, Source = CaptureSource.ClaudeCode,
        RawContent = $"Content for {id}",
        Summary = summary
    };

    private class StoryLlmService : ILlmService
    {
        public bool IsLocalAvailable => true;
        public bool IsCloudAvailable => true;
        public int CloudRequestsToday => 0;
        public int QueueDepth => 0;
        public Task<LlmResult> Submit(LlmTask task, CancellationToken ct = default)
            => Task.FromResult(new LlmResult
            {
                TaskId = task.Id,
                Success = true,
                Content = "The developer started by exploring the codebase.\n" +
                          "They implemented several changes across multiple files.\n" +
                          "Session outcome: Successfully completed the task."
            });
        public Task<float[]> Embed(string text, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<float>());
    }
}
