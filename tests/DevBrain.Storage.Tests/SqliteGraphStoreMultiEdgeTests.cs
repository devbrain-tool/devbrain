using DevBrain.Storage;
using DevBrain.Storage.Schema;
using Microsoft.Data.Sqlite;

namespace DevBrain.Storage.Tests;

public class SqliteGraphStoreMultiEdgeTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private SqliteGraphStore _store = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        SchemaManager.Initialize(_connection);
        _store = new SqliteGraphStore(_connection);
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task GetNeighbors_MultiEdgeTypes_FiltersCorrectly()
    {
        var a = await _store.AddNode("Decision", "A");
        var b = await _store.AddNode("Decision", "B");
        var c = await _store.AddNode("Decision", "C");
        var d = await _store.AddNode("Decision", "D");

        await _store.AddEdge(a.Id, b.Id, "caused_by");
        await _store.AddEdge(a.Id, c.Id, "supersedes");
        await _store.AddEdge(a.Id, d.Id, "references");

        var neighbors = await _store.GetNeighbors(a.Id, hops: 1, edgeTypes: ["caused_by", "supersedes"]);

        Assert.Equal(2, neighbors.Count);
        var names = neighbors.Select(n => n.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "B", "C" }, names);
    }

    [Fact]
    public async Task GetNeighbors_MultiEdgeTypes_MultiHop()
    {
        var a = await _store.AddNode("Decision", "A");
        var b = await _store.AddNode("Decision", "B");
        var c = await _store.AddNode("Decision", "C");

        await _store.AddEdge(a.Id, b.Id, "caused_by");
        await _store.AddEdge(b.Id, c.Id, "resolved_by");

        var neighbors = await _store.GetNeighbors(a.Id, hops: 2, edgeTypes: ["caused_by", "resolved_by"]);

        Assert.Equal(2, neighbors.Count);
        var names = neighbors.Select(n => n.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "B", "C" }, names);
    }

    [Fact]
    public async Task GetNeighbors_MultiEdgeTypes_EmptyList_ReturnsAll()
    {
        var a = await _store.AddNode("Decision", "A");
        var b = await _store.AddNode("Decision", "B");
        var c = await _store.AddNode("Decision", "C");

        await _store.AddEdge(a.Id, b.Id, "caused_by");
        await _store.AddEdge(a.Id, c.Id, "references");

        var neighbors = await _store.GetNeighbors(a.Id, hops: 1, edgeTypes: []);

        Assert.Equal(2, neighbors.Count);
    }
}
