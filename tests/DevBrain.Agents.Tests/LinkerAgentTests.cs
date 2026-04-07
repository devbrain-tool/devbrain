using DevBrain.Agents;
using DevBrain.Core.Enums;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;
using DevBrain.Storage;
using DevBrain.Storage.Schema;
using Microsoft.Data.Sqlite;

namespace DevBrain.Agents.Tests;

public class LinkerAgentTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private SqliteObservationStore _obsStore = null!;
    private SqliteGraphStore _graphStore = null!;
    private LinkerAgent _agent = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        SchemaManager.Initialize(_connection);
        _obsStore = new SqliteObservationStore(_connection);
        _graphStore = new SqliteGraphStore(_connection);
        _agent = new LinkerAgent();
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
            DeadEnds: new NullDeadEndStore(),
            Settings: new Settings()
        );
    }

    [Fact]
    public async Task Run_CreatesFileNodesAndEdgesForObservationsWithFiles()
    {
        var obs = new Observation
        {
            Id = "obs-1",
            SessionId = "s1",
            Timestamp = DateTime.UtcNow,
            Project = "test-project",
            EventType = EventType.Decision,
            Source = CaptureSource.ClaudeCode,
            RawContent = "Made a decision about architecture",
            Summary = "Architecture decision",
            FilesInvolved = ["src/Program.cs", "src/Startup.cs"]
        };
        await _obsStore.Add(obs);

        var ctx = CreateContext();
        var results = await _agent.Run(ctx, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(AgentOutputType.EdgeCreated, r.Type));

        // Verify file nodes were created
        var fileNodes = await _graphStore.GetNodesByType("File");
        Assert.Equal(2, fileNodes.Count);
        Assert.Contains(fileNodes, n => n.Name == "src/Program.cs");
        Assert.Contains(fileNodes, n => n.Name == "src/Startup.cs");

        // Verify observation node was created
        var decisionNodes = await _graphStore.GetNodesByType("Decision");
        Assert.Single(decisionNodes);
        Assert.Equal("Architecture decision", decisionNodes[0].Name);
        Assert.Equal("obs-1", decisionNodes[0].SourceId);
    }

    [Fact]
    public async Task Run_DoesNotDuplicateExistingFileNodes()
    {
        // Pre-create a file node
        await _graphStore.AddNode("File", "src/Program.cs");

        var obs1 = new Observation
        {
            Id = "obs-1",
            SessionId = "s1",
            Timestamp = DateTime.UtcNow,
            Project = "test-project",
            EventType = EventType.Decision,
            Source = CaptureSource.ClaudeCode,
            RawContent = "First decision",
            Summary = "First",
            FilesInvolved = ["src/Program.cs"]
        };
        await _obsStore.Add(obs1);

        var obs2 = new Observation
        {
            Id = "obs-2",
            SessionId = "s1",
            Timestamp = DateTime.UtcNow,
            Project = "test-project",
            EventType = EventType.Error,
            Source = CaptureSource.ClaudeCode,
            RawContent = "An error occurred",
            Summary = "Error in program",
            FilesInvolved = ["src/Program.cs"]
        };
        await _obsStore.Add(obs2);

        var ctx = CreateContext();
        var results = await _agent.Run(ctx, CancellationToken.None);

        Assert.Equal(2, results.Count);

        // Verify only one File node exists (no duplicate)
        var fileNodes = await _graphStore.GetNodesByType("File");
        Assert.Single(fileNodes);
        Assert.Equal("src/Program.cs", fileNodes[0].Name);

        // Verify both observation node types were created
        var decisionNodes = await _graphStore.GetNodesByType("Decision");
        Assert.Single(decisionNodes);

        var bugNodes = await _graphStore.GetNodesByType("Bug");
        Assert.Single(bugNodes);
    }

    [Fact]
    public async Task Run_SkipsObservationsWithoutFiles()
    {
        var obs = new Observation
        {
            Id = "obs-no-files",
            SessionId = "s1",
            Timestamp = DateTime.UtcNow,
            Project = "test-project",
            EventType = EventType.Conversation,
            Source = CaptureSource.ClaudeCode,
            RawContent = "Just a conversation",
            FilesInvolved = []
        };
        await _obsStore.Add(obs);

        var ctx = CreateContext();
        var results = await _agent.Run(ctx, CancellationToken.None);

        Assert.Empty(results);

        var fileNodes = await _graphStore.GetNodesByType("File");
        Assert.Empty(fileNodes);
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

    private class NullDeadEndStore : IDeadEndStore
    {
        public Task<DeadEnd> Add(DeadEnd deadEnd) => Task.FromResult(deadEnd);
        public Task<IReadOnlyList<DeadEnd>> Query(DeadEndFilter filter)
            => Task.FromResult<IReadOnlyList<DeadEnd>>(Array.Empty<DeadEnd>());
        public Task<IReadOnlyList<DeadEnd>> FindByFiles(IReadOnlyList<string> filePaths)
            => Task.FromResult<IReadOnlyList<DeadEnd>>(Array.Empty<DeadEnd>());
        public Task<IReadOnlyList<DeadEnd>> FindSimilar(string description, int limit = 5)
            => Task.FromResult<IReadOnlyList<DeadEnd>>(Array.Empty<DeadEnd>());
    }

    private class NullLlmService : ILlmService
    {
        public bool IsLocalAvailable => false;
        public bool IsCloudAvailable => false;
        public int CloudRequestsToday => 0;
        public int QueueDepth => 0;
        public Task<LlmResult> Submit(LlmTask task, CancellationToken ct = default)
            => Task.FromResult(new LlmResult { TaskId = task.Id, Success = false });
        public Task<float[]> Embed(string text, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<float>());
    }
}
