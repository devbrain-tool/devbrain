namespace DevBrain.Integration.Tests;

using DevBrain.Capture;
using DevBrain.Capture.Adapters;
using DevBrain.Capture.Pipeline;
using DevBrain.Core.Enums;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;
using DevBrain.Storage;
using DevBrain.Storage.Schema;
using Microsoft.Data.Sqlite;

public class PipelineEndToEndTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqliteObservationStore _store;

    public PipelineEndToEndTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        SchemaManager.Initialize(_connection);
        _store = new SqliteObservationStore(_connection);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private PipelineOrchestrator BuildPipeline(ThreadResolver? resolver = null)
    {
        resolver ??= new ThreadResolver();
        var normalizer = new Normalizer();
        var enricher = new Enricher(resolver);
        var tagger = new Tagger(null);
        var privacyFilter = new PrivacyFilter();
        var writer = new Writer(_store);
        return new PipelineOrchestrator(normalizer, enricher, tagger, privacyFilter, writer);
    }

    [Fact]
    public async Task RawEvent_flows_through_pipeline_to_sqlite()
    {
        var orchestrator = BuildPipeline();
        var (input, pipelineTask) = orchestrator.Start(CancellationToken.None);

        var now = DateTime.UtcNow;
        var sessionId = Guid.NewGuid().ToString();

        var event1 = new RawEvent
        {
            SessionId = sessionId,
            EventType = EventType.FileChange,
            Source = CaptureSource.ClaudeCode,
            Content = "Changed file Program.cs",
            Project = "MyProject",
            Branch = "main",
            Timestamp = now,
        };

        var event2 = new RawEvent
        {
            SessionId = sessionId,
            EventType = EventType.Decision,
            Source = CaptureSource.ClaudeCode,
            Content = "Decided to use singleton pattern",
            Project = "MyProject",
            Branch = "main",
            Timestamp = now.AddSeconds(5),
        };

        await input.WriteAsync(event1);
        await input.WriteAsync(event2);
        input.Complete();

        await pipelineTask;

        var all = await _store.Query(new ObservationFilter { Limit = 50 });
        Assert.Equal(2, all.Count);

        // Same session + project + branch => same thread
        Assert.NotNull(all[0].ThreadId);
        Assert.NotNull(all[1].ThreadId);
        Assert.Equal(all[0].ThreadId, all[1].ThreadId);

        // Correct project and event types
        Assert.All(all, o => Assert.Equal("MyProject", o.Project));
        var eventTypes = all.Select(o => o.EventType).OrderBy(e => e.ToString()).ToList();
        Assert.Contains(EventType.FileChange, eventTypes);
        Assert.Contains(EventType.Decision, eventTypes);
    }

    [Fact]
    public async Task Pipeline_redacts_secrets()
    {
        var orchestrator = BuildPipeline();
        var (input, pipelineTask) = orchestrator.Start(CancellationToken.None);

        var raw = new RawEvent
        {
            SessionId = Guid.NewGuid().ToString(),
            EventType = EventType.FileChange,
            Source = CaptureSource.VSCode,
            Content = "Config: api_key = 'sk-1234567890abcdef1234567890'",
            Project = "SecretProject",
            Branch = "main",
            Timestamp = DateTime.UtcNow,
        };

        await input.WriteAsync(raw);
        input.Complete();

        await pipelineTask;

        var all = await _store.Query(new ObservationFilter { Project = "SecretProject" });
        Assert.Single(all);

        var stored = all[0];
        Assert.Contains("[REDACTED:", stored.RawContent);
        Assert.DoesNotContain("sk-1234567890", stored.RawContent);
    }

    [Fact]
    public async Task Different_projects_create_separate_threads()
    {
        var orchestrator = BuildPipeline();
        var (input, pipelineTask) = orchestrator.Start(CancellationToken.None);

        var sessionId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        var event1 = new RawEvent
        {
            SessionId = sessionId,
            EventType = EventType.ToolCall,
            Source = CaptureSource.Cursor,
            Content = "Running tests",
            Project = "ProjectAlpha",
            Branch = "main",
            Timestamp = now,
        };

        var event2 = new RawEvent
        {
            SessionId = sessionId,
            EventType = EventType.ToolCall,
            Source = CaptureSource.Cursor,
            Content = "Running lint",
            Project = "ProjectBeta",
            Branch = "main",
            Timestamp = now.AddSeconds(1),
        };

        await input.WriteAsync(event1);
        await input.WriteAsync(event2);
        input.Complete();

        await pipelineTask;

        var alpha = await _store.Query(new ObservationFilter { Project = "ProjectAlpha" });
        var beta = await _store.Query(new ObservationFilter { Project = "ProjectBeta" });

        Assert.Single(alpha);
        Assert.Single(beta);
        Assert.NotNull(alpha[0].ThreadId);
        Assert.NotNull(beta[0].ThreadId);
        Assert.NotEqual(alpha[0].ThreadId, beta[0].ThreadId);
    }

    [Fact]
    public async Task Observations_are_queryable_after_pipeline()
    {
        var orchestrator = BuildPipeline();
        var (input, pipelineTask) = orchestrator.Start(CancellationToken.None);

        var now = DateTime.UtcNow;
        var sessionId = Guid.NewGuid().ToString();

        // 3 events for TargetProj, 1 event for OtherProj
        for (int i = 0; i < 3; i++)
        {
            await input.WriteAsync(new RawEvent
            {
                SessionId = sessionId,
                EventType = EventType.Conversation,
                Source = CaptureSource.ClaudeCode,
                Content = $"Message {i}",
                Project = "TargetProj",
                Branch = "dev",
                Timestamp = now.AddSeconds(i),
            });
        }

        await input.WriteAsync(new RawEvent
        {
            SessionId = sessionId,
            EventType = EventType.Error,
            Source = CaptureSource.VSCode,
            Content = "NullRef exception",
            Project = "OtherProj",
            Branch = "main",
            Timestamp = now.AddSeconds(10),
        });

        input.Complete();
        await pipelineTask;

        var targetResults = await _store.Query(new ObservationFilter { Project = "TargetProj" });
        Assert.Equal(3, targetResults.Count);

        var otherResults = await _store.Query(new ObservationFilter { Project = "OtherProj" });
        Assert.Single(otherResults);

        var total = await _store.Count();
        Assert.Equal(4, total);
    }
}
