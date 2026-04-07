using DevBrain.Agents;
using DevBrain.Core.Enums;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;
using DevBrain.Storage;
using DevBrain.Storage.Schema;
using Microsoft.Data.Sqlite;

namespace DevBrain.Agents.Tests;

public class GrowthAgentTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private SqliteObservationStore _obsStore = null!;
    private SqliteGraphStore _graphStore = null!;
    private SqliteDeadEndStore _deadEndStore = null!;
    private SqliteGrowthStore _growthStore = null!;
    private GrowthAgent _agent = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        SchemaManager.Initialize(_connection);
        _obsStore = new SqliteObservationStore(_connection);
        _graphStore = new SqliteGraphStore(_connection);
        _deadEndStore = new SqliteDeadEndStore(_connection);
        _growthStore = new SqliteGrowthStore(_connection);
        _agent = new GrowthAgent(_growthStore);
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
    public async Task Run_GeneratesMetricsAndReport()
    {
        var now = DateTime.UtcNow;
        for (int i = 0; i < 10; i++)
        {
            await _obsStore.Add(new Observation
            {
                Id = $"obs-{i}", SessionId = "s1", ThreadId = "t1",
                Timestamp = now.AddMinutes(-60 + i * 5), Project = "proj",
                EventType = i % 3 == 0 ? EventType.Error : EventType.FileChange,
                Source = CaptureSource.ClaudeCode,
                RawContent = $"Activity {i}",
                FilesInvolved = [$"src/File{i % 3}.cs"]
            });
        }

        var ctx = CreateContext();
        var results = await _agent.Run(ctx, CancellationToken.None);

        Assert.Contains(results, r => r.Type == AgentOutputType.GrowthReportGenerated);

        var report = await _growthStore.GetLatestReport();
        Assert.NotNull(report);

        var metrics = await _growthStore.GetLatestMetrics();
        Assert.True(metrics.Count >= 6);
    }

    [Fact]
    public async Task Run_SkipsWhenNoObservations()
    {
        var ctx = CreateContext();
        var results = await _agent.Run(ctx, CancellationToken.None);

        Assert.Empty(results);
        Assert.Null(await _growthStore.GetLatestReport());
    }

    [Fact]
    public void ComputeDebuggingSpeed_MeasuresErrorDuration()
    {
        var now = DateTime.UtcNow;
        var obs = new List<Observation>
        {
            MakeObs("1", "t1", now, EventType.Error),
            MakeObs("2", "t1", now.AddMinutes(10), EventType.Error),
            MakeObs("3", "t1", now.AddMinutes(15), EventType.FileChange),
        };

        var speed = GrowthAgent.ComputeDebuggingSpeed(obs);
        Assert.Equal(10.0, speed);
    }

    [Fact]
    public void ComputeDecisionVelocity_MeasuresFileChangeToDecision()
    {
        var now = DateTime.UtcNow;
        var obs = new List<Observation>
        {
            MakeObs("1", "t1", now, EventType.FileChange),
            MakeObs("2", "t1", now.AddMinutes(20), EventType.Decision),
        };

        var velocity = GrowthAgent.ComputeDecisionVelocity(obs);
        Assert.Equal(20.0, velocity);
    }

    [Fact]
    public void ComputeRetryRate_DetectsRepeatedEdits()
    {
        var now = DateTime.UtcNow;
        var obs = new List<Observation>
        {
            MakeObs("1", "t1", now, EventType.FileChange, files: ["src/A.cs"]),
            MakeObs("2", "t1", now.AddMinutes(1), EventType.FileChange, files: ["src/A.cs"]),
            MakeObs("3", "t1", now.AddMinutes(2), EventType.FileChange, files: ["src/A.cs"]),
        };

        var rate = GrowthAgent.ComputeRetryRate(obs);
        Assert.Equal(1.0, rate); // 100% of sessions have retries
    }

    [Fact]
    public void ComputeHeuristicComplexity_ReturnsNormalizedScore()
    {
        var now = DateTime.UtcNow;
        var obs = new List<Observation>
        {
            MakeObs("1", "t1", now, EventType.FileChange, files: ["a.cs", "b.cs", "c.cs"]),
            MakeObs("2", "t1", now.AddHours(2), EventType.Decision),
            MakeObs("3", "t1", now.AddHours(3), EventType.FileChange, files: ["d.cs"]),
        };

        var complexity = GrowthAgent.ComputeHeuristicComplexity(obs);
        Assert.InRange(complexity, 1.0, 5.0);
    }

    [Fact]
    public async Task Run_DetectsFirstProjectMilestone()
    {
        var now = DateTime.UtcNow;
        await _obsStore.Add(new Observation
        {
            Id = "obs-new", SessionId = "s1", ThreadId = "t1",
            Timestamp = now.AddMinutes(-10), Project = "brand-new-project",
            EventType = EventType.FileChange, Source = CaptureSource.ClaudeCode,
            RawContent = "First work on new project",
            FilesInvolved = ["src/App.cs"]
        });

        var ctx = CreateContext();
        var results = await _agent.Run(ctx, CancellationToken.None);

        Assert.Contains(results, r =>
            r.Type == AgentOutputType.MilestoneAchieved &&
            r.Content.Contains("brand-new-project"));

        var milestones = await _growthStore.GetMilestones();
        Assert.Contains(milestones, m =>
            m.Type == MilestoneType.First &&
            m.Description.Contains("brand-new-project"));
    }

    private static Observation MakeObs(string id, string threadId, DateTime timestamp,
        EventType type, string[]? files = null) => new()
    {
        Id = id, SessionId = "s1", ThreadId = threadId,
        Timestamp = timestamp, Project = "proj",
        EventType = type, Source = CaptureSource.ClaudeCode,
        RawContent = $"Content for {id}",
        FilesInvolved = files ?? []
    };
}
