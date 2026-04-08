using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;
using DevBrain.Storage;
using DevBrain.Storage.Schema;
using Microsoft.Data.Sqlite;

namespace DevBrain.Storage.Tests;

public class SqliteDeadEndStoreTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private SqliteDeadEndStore _store = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        SchemaManager.Initialize(_connection);
        _store = new SqliteDeadEndStore(_connection);
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Add_And_Query_RoundTrips()
    {
        var deadEnd = new DeadEnd
        {
            Id = "de-1",
            Project = "test-project",
            Description = "SQLite FTS doesn't support CJK",
            Approach = "Tried FTS5 with default tokenizer",
            Reason = "Default tokenizer can't segment CJK characters",
            FilesInvolved = ["src/Search.cs", "src/Index.cs"],
            DetectedAt = DateTime.UtcNow
        };

        await _store.Add(deadEnd);

        var results = await _store.Query(new DeadEndFilter { Project = "test-project" });
        Assert.Single(results);
        Assert.Equal("de-1", results[0].Id);
        Assert.Equal("SQLite FTS doesn't support CJK", results[0].Description);
        Assert.Equal(2, results[0].FilesInvolved.Count);
    }

    [Fact]
    public async Task FindByFiles_MatchesOverlappingFiles()
    {
        var de1 = new DeadEnd
        {
            Id = "de-1", Project = "proj",
            Description = "Dead end 1", Approach = "approach", Reason = "reason",
            FilesInvolved = ["src/A.cs", "src/B.cs"], DetectedAt = DateTime.UtcNow
        };
        var de2 = new DeadEnd
        {
            Id = "de-2", Project = "proj",
            Description = "Dead end 2", Approach = "approach", Reason = "reason",
            FilesInvolved = ["src/C.cs", "src/D.cs"], DetectedAt = DateTime.UtcNow
        };

        await _store.Add(de1);
        await _store.Add(de2);

        var results = await _store.FindByFiles(["src/A.cs", "src/X.cs"]);
        Assert.Single(results);
        Assert.Equal("de-1", results[0].Id);
    }

    [Fact]
    public async Task FindByFiles_ReturnsEmptyWhenNoMatch()
    {
        var de = new DeadEnd
        {
            Id = "de-1", Project = "proj",
            Description = "Dead end", Approach = "approach", Reason = "reason",
            FilesInvolved = ["src/A.cs"], DetectedAt = DateTime.UtcNow
        };
        await _store.Add(de);

        var results = await _store.FindByFiles(["src/Z.cs"]);
        Assert.Empty(results);
    }

    [Fact]
    public async Task Query_FiltersByDateRange()
    {
        var old = new DeadEnd
        {
            Id = "de-old", Project = "proj",
            Description = "Old dead end", Approach = "approach", Reason = "reason",
            FilesInvolved = [], DetectedAt = DateTime.UtcNow.AddDays(-10)
        };
        var recent = new DeadEnd
        {
            Id = "de-recent", Project = "proj",
            Description = "Recent dead end", Approach = "approach", Reason = "reason",
            FilesInvolved = [], DetectedAt = DateTime.UtcNow
        };

        await _store.Add(old);
        await _store.Add(recent);

        var results = await _store.Query(new DeadEndFilter { After = DateTime.UtcNow.AddDays(-1) });
        Assert.Single(results);
        Assert.Equal("de-recent", results[0].Id);
    }
}
