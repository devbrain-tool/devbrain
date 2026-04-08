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
            Settings: new Settings(),
            DeadEnds: new NullDeadEndStore()
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

        var fileNodes = await _graphStore.GetNodesByType("File");
        Assert.Equal(2, fileNodes.Count);
        Assert.Contains(fileNodes, n => n.Name == "src/Program.cs");
        Assert.Contains(fileNodes, n => n.Name == "src/Startup.cs");

        var decisionNodes = await _graphStore.GetNodesByType("Decision");
        Assert.Single(decisionNodes);
        Assert.Equal("Architecture decision", decisionNodes[0].Name);
        Assert.Equal("obs-1", decisionNodes[0].SourceId);
    }

    [Fact]
    public async Task Run_DoesNotDuplicateExistingFileNodes()
    {
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

        var fileNodes = await _graphStore.GetNodesByType("File");
        Assert.Single(fileNodes);
        Assert.Equal("src/Program.cs", fileNodes[0].Name);

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
}
