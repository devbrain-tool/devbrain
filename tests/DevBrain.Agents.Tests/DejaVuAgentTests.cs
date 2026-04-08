using DevBrain.Agents;
using DevBrain.Core.Enums;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;
using DevBrain.Storage;
using DevBrain.Storage.Schema;
using Microsoft.Data.Sqlite;

namespace DevBrain.Agents.Tests;

public class DejaVuAgentTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private SqliteObservationStore _obsStore = null!;
    private SqliteGraphStore _graphStore = null!;
    private SqliteDeadEndStore _deadEndStore = null!;
    private SqliteAlertStore _alertStore = null!;
    private DejaVuAgent _agent = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        SchemaManager.Initialize(_connection);
        _obsStore = new SqliteObservationStore(_connection);
        _graphStore = new SqliteGraphStore(_connection);
        _deadEndStore = new SqliteDeadEndStore(_connection);
        _alertStore = new SqliteAlertStore(_connection);
        _agent = new DejaVuAgent(_alertStore);
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private AgentContext CreateContext()
    {
        return new AgentContext(
            Observations: _obsStore,
            Graph: _graphStore,
            Vectors: new NullVectorStore(),
            Llm: new NullLlmService(),
            Settings: new Settings(),
            DeadEnds: _deadEndStore
        );
    }

    [Fact]
    public async Task Run_FiresAlertOnFileOverlapMatch()
    {
        await _deadEndStore.Add(new DeadEnd
        {
            Id = "de-1", Project = "proj",
            Description = "FTS doesn't support CJK",
            Approach = "Used default tokenizer", Reason = "No CJK support",
            FilesInvolved = ["src/Search.cs", "src/Index.cs"],
            DetectedAt = DateTime.UtcNow.AddDays(-1)
        });

        await _obsStore.Add(new Observation
        {
            Id = "obs-1", SessionId = "s1", ThreadId = "t1",
            Timestamp = DateTime.UtcNow, Project = "proj",
            EventType = EventType.FileChange, Source = CaptureSource.ClaudeCode,
            RawContent = "Editing search", FilesInvolved = ["src/Search.cs"]
        });

        var ctx = CreateContext();
        var results = await _agent.Run(ctx, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(AgentOutputType.AlertFired, results[0].Type);

        var active = await _alertStore.GetActive();
        Assert.Single(active);
        Assert.Equal("de-1", active[0].MatchedDeadEndId);
        Assert.Equal(MatchStrategy.FileOverlap, active[0].Strategy);
        Assert.True(active[0].Confidence >= 0.5);
    }

    [Fact]
    public async Task Run_DoesNotFireWhenOverlapBelowThreshold()
    {
        await _deadEndStore.Add(new DeadEnd
        {
            Id = "de-1", Project = "proj",
            Description = "Complex issue", Approach = "approach", Reason = "reason",
            FilesInvolved = ["src/A.cs", "src/B.cs", "src/C.cs"],
            DetectedAt = DateTime.UtcNow.AddDays(-1)
        });

        await _obsStore.Add(new Observation
        {
            Id = "obs-1", SessionId = "s1", ThreadId = "t1",
            Timestamp = DateTime.UtcNow, Project = "proj",
            EventType = EventType.FileChange, Source = CaptureSource.ClaudeCode,
            RawContent = "Editing A", FilesInvolved = ["src/A.cs"]
        });

        var ctx = CreateContext();
        var results = await _agent.Run(ctx, CancellationToken.None);

        Assert.Empty(results);
        Assert.Empty(await _alertStore.GetActive());
    }

    [Fact]
    public async Task Run_DeduplicatesAlerts()
    {
        await _deadEndStore.Add(new DeadEnd
        {
            Id = "de-1", Project = "proj",
            Description = "Known issue", Approach = "approach", Reason = "reason",
            FilesInvolved = ["src/A.cs"],
            DetectedAt = DateTime.UtcNow.AddDays(-1)
        });

        await _obsStore.Add(new Observation
        {
            Id = "obs-1", SessionId = "s1", ThreadId = "t1",
            Timestamp = DateTime.UtcNow.AddMinutes(-5), Project = "proj",
            EventType = EventType.FileChange, Source = CaptureSource.ClaudeCode,
            RawContent = "First edit", FilesInvolved = ["src/A.cs"]
        });

        var ctx = CreateContext();
        await _agent.Run(ctx, CancellationToken.None);

        await _obsStore.Add(new Observation
        {
            Id = "obs-2", SessionId = "s1", ThreadId = "t1",
            Timestamp = DateTime.UtcNow, Project = "proj",
            EventType = EventType.FileChange, Source = CaptureSource.ClaudeCode,
            RawContent = "Second edit", FilesInvolved = ["src/A.cs"]
        });

        var results2 = await _agent.Run(ctx, CancellationToken.None);

        Assert.Empty(results2);
        Assert.Single(await _alertStore.GetActive());
    }

    [Fact]
    public async Task Run_PushesToAlertSinkWhenProvided()
    {
        var sink = new CapturingAlertSink();
        var agentWithSink = new DejaVuAgent(_alertStore, sink);

        await _deadEndStore.Add(new DeadEnd
        {
            Id = "de-1", Project = "proj",
            Description = "Known issue", Approach = "approach", Reason = "reason",
            FilesInvolved = ["src/A.cs"],
            DetectedAt = DateTime.UtcNow.AddDays(-1)
        });

        await _obsStore.Add(new Observation
        {
            Id = "obs-1", SessionId = "s1", ThreadId = "t1",
            Timestamp = DateTime.UtcNow, Project = "proj",
            EventType = EventType.FileChange, Source = CaptureSource.ClaudeCode,
            RawContent = "Editing A", FilesInvolved = ["src/A.cs"]
        });

        var ctx = CreateContext();
        await agentWithSink.Run(ctx, CancellationToken.None);

        Assert.Single(sink.SentAlerts);
        Assert.Equal("de-1", sink.SentAlerts[0].MatchedDeadEndId);
    }

    [Fact]
    public async Task Run_SkipsDeadEndsWithEmptyFiles()
    {
        await _deadEndStore.Add(new DeadEnd
        {
            Id = "de-empty", Project = "proj",
            Description = "Empty files", Approach = "approach", Reason = "reason",
            FilesInvolved = [],
            DetectedAt = DateTime.UtcNow.AddDays(-1)
        });

        await _obsStore.Add(new Observation
        {
            Id = "obs-1", SessionId = "s1", ThreadId = "t1",
            Timestamp = DateTime.UtcNow, Project = "proj",
            EventType = EventType.FileChange, Source = CaptureSource.ClaudeCode,
            RawContent = "Editing", FilesInvolved = ["src/A.cs"]
        });

        var ctx = CreateContext();
        var results = await _agent.Run(ctx, CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task Run_SkipsObservationsWithoutThreadId()
    {
        await _deadEndStore.Add(new DeadEnd
        {
            Id = "de-1", Project = "proj",
            Description = "Issue", Approach = "approach", Reason = "reason",
            FilesInvolved = ["src/A.cs"],
            DetectedAt = DateTime.UtcNow.AddDays(-1)
        });

        await _obsStore.Add(new Observation
        {
            Id = "obs-1", SessionId = "s1", ThreadId = null,
            Timestamp = DateTime.UtcNow, Project = "proj",
            EventType = EventType.FileChange, Source = CaptureSource.ClaudeCode,
            RawContent = "No thread", FilesInvolved = ["src/A.cs"]
        });

        var ctx = CreateContext();
        var results = await _agent.Run(ctx, CancellationToken.None);

        Assert.Empty(results);
    }

    private class CapturingAlertSink : IAlertSink
    {
        public List<DejaVuAlert> SentAlerts { get; } = [];

        public Task Send(DejaVuAlert alert, CancellationToken ct = default)
        {
            SentAlerts.Add(alert);
            return Task.CompletedTask;
        }
    }
}
