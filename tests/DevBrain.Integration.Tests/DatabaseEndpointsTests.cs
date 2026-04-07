namespace DevBrain.Integration.Tests;

using DevBrain.Api;
using DevBrain.Api.Endpoints;
using DevBrain.Storage.Schema;
using Microsoft.Data.Sqlite;

public class DatabaseEndpointsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ReadOnlyDb _readOnlyDb;

    public DatabaseEndpointsTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        SchemaManager.Initialize(_connection);

        // In-memory DBs are per-connection, so use the same connection for read-only
        // (Mode=ReadOnly won't work with :memory:, but the tests focus on query logic)
        _readOnlyDb = new ReadOnlyDb(_connection);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    [Fact]
    public void ListTables_ReturnsUserTables()
    {
        var tables = DatabaseEndpoints.ListTables(_readOnlyDb);

        var names = tables.Select(t => t.Name).ToList();
        Assert.Contains("observations", names);
        Assert.Contains("threads", names);
        Assert.Contains("dead_ends", names);
        Assert.Contains("graph_nodes", names);
        Assert.Contains("graph_edges", names);
        Assert.DoesNotContain("_meta", names);
        Assert.DoesNotContain("observations_fts", names);
    }

    [Fact]
    public void ListTables_ReturnsRowCounts()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO observations (id, session_id, timestamp, project, event_type, source, raw_content, created_at)
            VALUES ('t1', 's1', '2026-01-01', 'proj', 'ToolCall', 'test', 'content', '2026-01-01')
            """;
        cmd.ExecuteNonQuery();

        var tables = DatabaseEndpoints.ListTables(_readOnlyDb);
        var obs = tables.First(t => t.Name == "observations");
        Assert.Equal(1, obs.RowCount);
    }

    [Fact]
    public void GetTableDetail_ReturnsColumnsAndIndexes()
    {
        var detail = DatabaseEndpoints.GetTableDetail(_readOnlyDb, "observations");

        Assert.NotNull(detail);
        Assert.Equal("observations", detail!.Name);
        Assert.True(detail.RowCount >= 0);

        var colNames = detail.Columns.Select(c => c.Name).ToList();
        Assert.Contains("id", colNames);
        Assert.Contains("session_id", colNames);
        Assert.Contains("timestamp", colNames);

        var idCol = detail.Columns.First(c => c.Name == "id");
        Assert.True(idCol.PrimaryKey);
        Assert.Equal("TEXT", idCol.Type);

        Assert.Contains("idx_obs_thread", detail.Indexes);
    }

    [Fact]
    public void GetTableDetail_ReturnsNullForUnknownTable()
    {
        var detail = DatabaseEndpoints.GetTableDetail(_readOnlyDb, "nonexistent");
        Assert.Null(detail);
    }

    [Fact]
    public void GetTableDetail_ReturnsNullForInjectionAttempt()
    {
        var detail = DatabaseEndpoints.GetTableDetail(_readOnlyDb, "observations; DROP TABLE observations");
        Assert.Null(detail);
    }
}
