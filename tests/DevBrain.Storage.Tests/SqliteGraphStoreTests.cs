using DevBrain.Storage;
using Microsoft.Data.Sqlite;

namespace DevBrain.Storage.Tests;

public class SqliteGraphStoreTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private SqliteGraphStore _store = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        await InitializeSchema();
        _store = new SqliteGraphStore(_connection);
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private async Task InitializeSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE graph_nodes (
                id TEXT PRIMARY KEY,
                type TEXT NOT NULL,
                name TEXT NOT NULL,
                data TEXT,
                source_id TEXT,
                created_at TEXT DEFAULT (datetime('now'))
            );
            CREATE TABLE graph_edges (
                id TEXT PRIMARY KEY,
                source_id TEXT NOT NULL REFERENCES graph_nodes(id),
                target_id TEXT NOT NULL REFERENCES graph_nodes(id),
                type TEXT NOT NULL,
                data TEXT,
                weight REAL DEFAULT 1.0,
                created_at TEXT DEFAULT (datetime('now'))
            );
            CREATE INDEX idx_ge_source ON graph_edges(source_id);
            CREATE INDEX idx_ge_target ON graph_edges(target_id);
            CREATE INDEX idx_ge_type ON graph_edges(type);
            CREATE INDEX idx_gn_type ON graph_nodes(type);
            CREATE INDEX idx_gn_source ON graph_nodes(source_id);
        ";
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task AddNode_And_GetNode_RoundTrips()
    {
        var node = await _store.AddNode("Class", "MyClass", new { lang = "C#" });

        Assert.NotNull(node.Id);
        Assert.Equal("Class", node.Type);
        Assert.Equal("MyClass", node.Name);
        Assert.Contains("C#", node.Data!);

        var fetched = await _store.GetNode(node.Id);
        Assert.NotNull(fetched);
        Assert.Equal(node.Id, fetched.Id);
        Assert.Equal("Class", fetched.Type);
        Assert.Equal("MyClass", fetched.Name);
    }

    [Fact]
    public async Task AddEdge_CreatesRelationship()
    {
        var a = await _store.AddNode("Class", "A");
        var b = await _store.AddNode("Class", "B");

        var edge = await _store.AddEdge(a.Id, b.Id, "DependsOn");

        Assert.NotNull(edge.Id);
        Assert.Equal(a.Id, edge.SourceId);
        Assert.Equal(b.Id, edge.TargetId);
        Assert.Equal("DependsOn", edge.Type);
        Assert.Equal(1.0, edge.Weight);
    }

    [Fact]
    public async Task GetNeighbors_ReturnsBothDirections()
    {
        var a = await _store.AddNode("Class", "A");
        var b = await _store.AddNode("Class", "B");
        var c = await _store.AddNode("Class", "C");

        // A -> B (outbound from A)
        await _store.AddEdge(a.Id, b.Id, "DependsOn");
        // C -> A (inbound to A)
        await _store.AddEdge(c.Id, a.Id, "DependsOn");

        var neighbors = await _store.GetNeighbors(a.Id);

        Assert.Equal(2, neighbors.Count);
        var names = neighbors.Select(n => n.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "B", "C" }, names);
    }

    [Fact]
    public async Task GetNeighbors_WithEdgeTypeFilter()
    {
        var a = await _store.AddNode("Class", "A");
        var b = await _store.AddNode("Class", "B");
        var c = await _store.AddNode("Class", "C");

        await _store.AddEdge(a.Id, b.Id, "DependsOn");
        await _store.AddEdge(a.Id, c.Id, "Contains");

        var neighbors = await _store.GetNeighbors(a.Id, edgeType: "DependsOn");

        Assert.Single(neighbors);
        Assert.Equal("B", neighbors[0].Name);
    }

    [Fact]
    public async Task GetNeighbors_MultiHop_FindsNodesAtDistance2()
    {
        var a = await _store.AddNode("Class", "A");
        var b = await _store.AddNode("Class", "B");
        var c = await _store.AddNode("Class", "C");

        await _store.AddEdge(a.Id, b.Id, "DependsOn");
        await _store.AddEdge(b.Id, c.Id, "DependsOn");

        // 1 hop from A should find only B
        var hop1 = await _store.GetNeighbors(a.Id, hops: 1);
        Assert.Single(hop1);
        Assert.Equal("B", hop1[0].Name);

        // 2 hops from A should find B and C
        var hop2 = await _store.GetNeighbors(a.Id, hops: 2);
        Assert.Equal(2, hop2.Count);
        var names = hop2.Select(n => n.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "B", "C" }, names);
    }

    [Fact]
    public async Task FindPaths_FindsRouteBetweenNodes()
    {
        var a = await _store.AddNode("Class", "A");
        var b = await _store.AddNode("Class", "B");
        var c = await _store.AddNode("Class", "C");

        await _store.AddEdge(a.Id, b.Id, "DependsOn");
        await _store.AddEdge(b.Id, c.Id, "DependsOn");

        var paths = await _store.FindPaths(a.Id, c.Id);

        Assert.Single(paths);
        Assert.Equal(3, paths[0].Nodes.Count); // A, B, C
        Assert.Equal(2, paths[0].Edges.Count);
        Assert.Equal(2, paths[0].Depth);
    }

    [Fact]
    public async Task RemoveNode_CascadesEdges()
    {
        var a = await _store.AddNode("Class", "A");
        var b = await _store.AddNode("Class", "B");
        var c = await _store.AddNode("Class", "C");

        await _store.AddEdge(a.Id, b.Id, "DependsOn");
        await _store.AddEdge(b.Id, c.Id, "DependsOn");

        await _store.RemoveNode(b.Id);

        // B should be gone
        Assert.Null(await _store.GetNode(b.Id));

        // A and C should have no neighbors (edges deleted)
        var aNeighbors = await _store.GetNeighbors(a.Id);
        Assert.Empty(aNeighbors);
        var cNeighbors = await _store.GetNeighbors(c.Id);
        Assert.Empty(cNeighbors);
    }

    [Fact]
    public async Task GetNodesByType_FiltersCorrectly()
    {
        await _store.AddNode("Class", "MyClass");
        await _store.AddNode("Method", "DoWork");
        await _store.AddNode("Class", "OtherClass");
        await _store.AddNode("File", "Program.cs");

        var classes = await _store.GetNodesByType("Class");
        Assert.Equal(2, classes.Count);
        Assert.All(classes, n => Assert.Equal("Class", n.Type));

        var files = await _store.GetNodesByType("File");
        Assert.Single(files);
        Assert.Equal("Program.cs", files[0].Name);
    }

    [Fact]
    public async Task GetRelatedToFile_FindsNeighborsByFileName()
    {
        var file = await _store.AddNode("File", "Program.cs");
        var cls = await _store.AddNode("Class", "Program");
        var method = await _store.AddNode("Method", "Main");

        await _store.AddEdge(file.Id, cls.Id, "Contains");
        await _store.AddEdge(cls.Id, method.Id, "Contains");

        var related = await _store.GetRelatedToFile("Program.cs");

        // 2 hops: cls (hop 1), method (hop 2)
        Assert.Equal(2, related.Count);
        var names = related.Select(n => n.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "Main", "Program" }, names);
    }
}
