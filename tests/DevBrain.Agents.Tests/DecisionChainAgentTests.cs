using DevBrain.Agents;
using DevBrain.Core;
using DevBrain.Core.Enums;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;
using DevBrain.Storage;
using DevBrain.Storage.Schema;
using Microsoft.Data.Sqlite;

namespace DevBrain.Agents.Tests;

public class DecisionChainAgentTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private SqliteObservationStore _obsStore = null!;
    private SqliteGraphStore _graphStore = null!;
    private SqliteDeadEndStore _deadEndStore = null!;
    private DecisionChainAgent _agent = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        SchemaManager.Initialize(_connection);
        _obsStore = new SqliteObservationStore(_connection);
        _graphStore = new SqliteGraphStore(_connection);
        _deadEndStore = new SqliteDeadEndStore(_connection);
        _agent = new DecisionChainAgent();
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
            Llm: llm ?? new ClassifyingLlmService("caused_by"),
            DeadEnds: _deadEndStore,
            Settings: new Settings()
        );
    }

    [Fact]
    public async Task Run_CreatesEdgeBetweenRelatedDecisions()
    {
        var obs1 = new Observation
        {
            Id = "obs-1", SessionId = "s1", Timestamp = DateTime.UtcNow.AddMinutes(-5),
            Project = "proj", EventType = EventType.Decision, Source = CaptureSource.ClaudeCode,
            RawContent = "Decided to use SQLite", Summary = "Use SQLite for storage",
            FilesInvolved = ["src/Storage.cs"]
        };
        var obs2 = new Observation
        {
            Id = "obs-2", SessionId = "s1", Timestamp = DateTime.UtcNow,
            Project = "proj", EventType = EventType.Decision, Source = CaptureSource.ClaudeCode,
            RawContent = "Added WAL mode", Summary = "Enable WAL mode for concurrency",
            FilesInvolved = ["src/Storage.cs"]
        };
        await _obsStore.Add(obs1);
        await _obsStore.Add(obs2);

        // Pre-create the LinkerAgent's node for obs1
        await _graphStore.AddNode("Decision", "Use SQLite for storage", sourceId: "obs-1");
        // Also create the File node so GetRelatedToFile works
        var fileNode = await _graphStore.AddNode("File", "src/Storage.cs");
        var decNode = (await _graphStore.GetNodesByType("Decision"))[0];
        await _graphStore.AddEdge(decNode.Id, fileNode.Id, "references");

        var ctx = CreateContext();
        var results = await _agent.Run(ctx, CancellationToken.None);

        Assert.Contains(results, r => r.Type == AgentOutputType.DecisionChainBuilt);

        var decisionNodes = await _graphStore.GetNodesByType("Decision");
        Assert.True(decisionNodes.Count >= 2);

        var newNode = decisionNodes.FirstOrDefault(n => n.SourceId == "obs-2");
        Assert.NotNull(newNode);

        var neighbors = await _graphStore.GetNeighbors(newNode!.Id, hops: 1, edgeTypes: ["caused_by"]);
        Assert.Single(neighbors);
    }

    [Fact]
    public async Task Run_SkipsWhenLlmClassifiesAsUnrelated()
    {
        var obs1 = new Observation
        {
            Id = "obs-1", SessionId = "s1", Timestamp = DateTime.UtcNow.AddMinutes(-5),
            Project = "proj", EventType = EventType.Decision, Source = CaptureSource.ClaudeCode,
            RawContent = "Use Redis for cache", Summary = "Redis caching",
            FilesInvolved = ["src/Cache.cs"]
        };
        var obs2 = new Observation
        {
            Id = "obs-2", SessionId = "s1", Timestamp = DateTime.UtcNow,
            Project = "proj", EventType = EventType.Decision, Source = CaptureSource.ClaudeCode,
            RawContent = "Add logging", Summary = "Structured logging",
            FilesInvolved = ["src/Cache.cs"]
        };
        await _obsStore.Add(obs1);
        await _obsStore.Add(obs2);

        await _graphStore.AddNode("Decision", "Redis caching", sourceId: "obs-1");
        var fileNode = await _graphStore.AddNode("File", "src/Cache.cs");
        var decNode = (await _graphStore.GetNodesByType("Decision"))[0];
        await _graphStore.AddEdge(decNode.Id, fileNode.Id, "references");

        var ctx = CreateContext(new ClassifyingLlmService("unrelated"));
        var results = await _agent.Run(ctx, CancellationToken.None);

        var decisionNodes = await _graphStore.GetNodesByType("Decision");
        var newNode = decisionNodes.FirstOrDefault(n => n.SourceId == "obs-2");
        if (newNode is not null)
        {
            var neighbors = await _graphStore.GetNeighbors(newNode.Id, hops: 1, edgeTypes: ["caused_by", "supersedes", "resolved_by"]);
            Assert.Empty(neighbors);
        }
    }

    private class ClassifyingLlmService : ILlmService
    {
        private readonly string _response;
        public ClassifyingLlmService(string response) => _response = response;
        public bool IsLocalAvailable => true;
        public bool IsCloudAvailable => false;
        public int CloudRequestsToday => 0;
        public int QueueDepth => 0;
        public Task<LlmResult> Submit(LlmTask task, CancellationToken ct = default)
            => Task.FromResult(new LlmResult { TaskId = task.Id, Success = true, Content = _response });
        public Task<float[]> Embed(string text, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<float>());
    }

    private class NullVectorStore : IVectorStore
    {
        public Task Index(string id, string text, VectorCategory category) => Task.CompletedTask;
        public Task<IReadOnlyList<VectorMatch>> Search(string query, int topK = 20, VectorCategory? filter = null)
            => Task.FromResult<IReadOnlyList<VectorMatch>>(Array.Empty<VectorMatch>());
        public Task Remove(string id) => Task.CompletedTask;
        public Task Rebuild() => Task.CompletedTask;
        public Task<long> GetSizeBytes() => Task.FromResult(0L);
    }
}
