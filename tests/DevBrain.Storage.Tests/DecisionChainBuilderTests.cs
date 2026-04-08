using DevBrain.Core.Enums;
using DevBrain.Core.Models;
using DevBrain.Storage;
using DevBrain.Storage.Schema;
using Microsoft.Data.Sqlite;

namespace DevBrain.Storage.Tests;

public class DecisionChainBuilderTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private SqliteObservationStore _obsStore = null!;
    private SqliteGraphStore _graphStore = null!;
    private DecisionChainBuilder _builder = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        SchemaManager.Initialize(_connection);
        _obsStore = new SqliteObservationStore(_connection);
        _graphStore = new SqliteGraphStore(_connection);
        _builder = new DecisionChainBuilder(_graphStore, _obsStore);
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task BuildForFile_ReturnsChainWithDecisions()
    {
        // Create file node + two decision nodes linked by causal edge
        var fileNode = await _graphStore.AddNode("File", "src/Storage.cs");
        var dec1 = await _graphStore.AddNode("Decision", "Use SQLite", sourceId: "obs-1");
        var dec2 = await _graphStore.AddNode("Decision", "Add WAL mode", sourceId: "obs-2");

        await _graphStore.AddEdge(dec1.Id, fileNode.Id, "references");
        await _graphStore.AddEdge(dec2.Id, fileNode.Id, "references");
        await _graphStore.AddEdge(dec2.Id, dec1.Id, "caused_by");

        // Create backing observations
        await _obsStore.Add(new Observation
        {
            Id = "obs-1", SessionId = "s1", Timestamp = DateTime.UtcNow.AddHours(-2),
            Project = "proj", EventType = EventType.Decision, Source = CaptureSource.ClaudeCode,
            RawContent = "Decided to use SQLite", FilesInvolved = ["src/Storage.cs"]
        });
        await _obsStore.Add(new Observation
        {
            Id = "obs-2", SessionId = "s1", Timestamp = DateTime.UtcNow.AddHours(-1),
            Project = "proj", EventType = EventType.Decision, Source = CaptureSource.ClaudeCode,
            RawContent = "Added WAL mode", FilesInvolved = ["src/Storage.cs"]
        });

        var chain = await _builder.BuildForFile("src/Storage.cs");

        Assert.NotNull(chain);
        Assert.Equal(2, chain.Steps.Count);
        Assert.Equal("Use SQLite", chain.Steps[0].Summary); // chronological order
        Assert.Equal("Add WAL mode", chain.Steps[1].Summary);
        Assert.All(chain.Steps, s => Assert.Equal(DecisionStepType.Decision, s.StepType));
    }

    [Fact]
    public async Task BuildForFile_IncludesDeadEndNodes()
    {
        var fileNode = await _graphStore.AddNode("File", "src/Search.cs");
        var dec = await _graphStore.AddNode("Decision", "Use FTS5", sourceId: "obs-1");
        var bug = await _graphStore.AddNode("Bug", "FTS tokenizer issue", sourceId: "de-1");

        await _graphStore.AddEdge(dec.Id, fileNode.Id, "references");
        await _graphStore.AddEdge(bug.Id, fileNode.Id, "references");
        await _graphStore.AddEdge(dec.Id, bug.Id, "resolved_by");

        await _obsStore.Add(new Observation
        {
            Id = "obs-1", SessionId = "s1", Timestamp = DateTime.UtcNow,
            Project = "proj", EventType = EventType.Decision, Source = CaptureSource.ClaudeCode,
            RawContent = "Use FTS5", FilesInvolved = ["src/Search.cs"]
        });

        var chain = await _builder.BuildForFile("src/Search.cs");

        Assert.NotNull(chain);
        Assert.Contains(chain.Steps, s => s.StepType == DecisionStepType.DeadEnd);
        Assert.Contains(chain.Steps, s => s.StepType == DecisionStepType.Decision);
    }

    [Fact]
    public async Task BuildForFile_ReturnsNullWhenNoDecisions()
    {
        var fileNode = await _graphStore.AddNode("File", "src/Empty.cs");

        var chain = await _builder.BuildForFile("src/Empty.cs");

        Assert.Null(chain);
    }

    [Fact]
    public async Task BuildForDecision_TraversesCausalChain()
    {
        var dec1 = await _graphStore.AddNode("Decision", "Choose SQLite", sourceId: "obs-1");
        var dec2 = await _graphStore.AddNode("Decision", "Add WAL", sourceId: "obs-2");
        var dec3 = await _graphStore.AddNode("Decision", "Add connection pooling", sourceId: "obs-3");

        await _graphStore.AddEdge(dec2.Id, dec1.Id, "caused_by");
        await _graphStore.AddEdge(dec3.Id, dec2.Id, "caused_by");

        await _obsStore.Add(new Observation
        {
            Id = "obs-1", SessionId = "s1", Timestamp = DateTime.UtcNow.AddHours(-3),
            Project = "proj", EventType = EventType.Decision, Source = CaptureSource.ClaudeCode,
            RawContent = "Choose SQLite"
        });
        await _obsStore.Add(new Observation
        {
            Id = "obs-2", SessionId = "s1", Timestamp = DateTime.UtcNow.AddHours(-2),
            Project = "proj", EventType = EventType.Decision, Source = CaptureSource.ClaudeCode,
            RawContent = "Add WAL"
        });
        await _obsStore.Add(new Observation
        {
            Id = "obs-3", SessionId = "s1", Timestamp = DateTime.UtcNow.AddHours(-1),
            Project = "proj", EventType = EventType.Decision, Source = CaptureSource.ClaudeCode,
            RawContent = "Add connection pooling"
        });

        var chain = await _builder.BuildForDecision(dec3.Id);

        Assert.NotNull(chain);
        Assert.Equal(3, chain.Steps.Count);
        Assert.Equal("Choose SQLite", chain.Steps[0].Summary);
        Assert.Equal("Add connection pooling", chain.Steps[2].Summary);
    }

    [Fact]
    public async Task BuildForDecision_ReturnsNullForNonexistentNode()
    {
        var chain = await _builder.BuildForDecision("nonexistent-id");
        Assert.Null(chain);
    }

    [Fact]
    public async Task BuildForDecision_RejectsNonDecisionNodeType()
    {
        var fileNode = await _graphStore.AddNode("File", "src/Program.cs");

        var chain = await _builder.BuildForDecision(fileNode.Id);

        Assert.Null(chain);
    }

    [Fact]
    public async Task BuildForFile_RootNodeIdIsChronologicallyEarliest()
    {
        var fileNode = await _graphStore.AddNode("File", "src/App.cs");
        var dec1 = await _graphStore.AddNode("Decision", "Early decision", sourceId: "obs-early");
        var dec2 = await _graphStore.AddNode("Decision", "Late decision", sourceId: "obs-late");

        await _graphStore.AddEdge(dec1.Id, fileNode.Id, "references");
        await _graphStore.AddEdge(dec2.Id, fileNode.Id, "references");

        await _obsStore.Add(new Observation
        {
            Id = "obs-early", SessionId = "s1", Timestamp = DateTime.UtcNow.AddHours(-5),
            Project = "proj", EventType = EventType.Decision, Source = CaptureSource.ClaudeCode,
            RawContent = "Early", FilesInvolved = ["src/App.cs"]
        });
        await _obsStore.Add(new Observation
        {
            Id = "obs-late", SessionId = "s1", Timestamp = DateTime.UtcNow.AddHours(-1),
            Project = "proj", EventType = EventType.Decision, Source = CaptureSource.ClaudeCode,
            RawContent = "Late", FilesInvolved = ["src/App.cs"]
        });

        var chain = await _builder.BuildForFile("src/App.cs");

        Assert.NotNull(chain);
        Assert.Equal("obs-early", chain.RootNodeId);
    }

    [Fact]
    public async Task BuildForDecision_HopsLimitsTraversalDepth()
    {
        var dec1 = await _graphStore.AddNode("Decision", "Root", sourceId: "obs-1");
        var dec2 = await _graphStore.AddNode("Decision", "Hop 1", sourceId: "obs-2");
        var dec3 = await _graphStore.AddNode("Decision", "Hop 2", sourceId: "obs-3");

        await _graphStore.AddEdge(dec2.Id, dec1.Id, "caused_by");
        await _graphStore.AddEdge(dec3.Id, dec2.Id, "caused_by");

        await _obsStore.Add(new Observation
        {
            Id = "obs-1", SessionId = "s1", Timestamp = DateTime.UtcNow.AddHours(-3),
            Project = "proj", EventType = EventType.Decision, Source = CaptureSource.ClaudeCode,
            RawContent = "Root"
        });
        await _obsStore.Add(new Observation
        {
            Id = "obs-2", SessionId = "s1", Timestamp = DateTime.UtcNow.AddHours(-2),
            Project = "proj", EventType = EventType.Decision, Source = CaptureSource.ClaudeCode,
            RawContent = "Hop 1"
        });
        await _obsStore.Add(new Observation
        {
            Id = "obs-3", SessionId = "s1", Timestamp = DateTime.UtcNow.AddHours(-1),
            Project = "proj", EventType = EventType.Decision, Source = CaptureSource.ClaudeCode,
            RawContent = "Hop 2"
        });

        // With hops=1, should only reach dec1 (direct neighbor)
        var chain = await _builder.BuildForDecision(dec3.Id, maxHops: 1);

        Assert.NotNull(chain);
        // dec3 + dec2 (1 hop away) but NOT dec1 (2 hops away)
        Assert.Equal(2, chain.Steps.Count);
    }
}
