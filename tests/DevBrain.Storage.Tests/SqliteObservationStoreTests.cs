namespace DevBrain.Storage.Tests;

using DevBrain.Core.Enums;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;
using DevBrain.Storage.Schema;
using Microsoft.Data.Sqlite;

public class SqliteObservationStoreTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqliteObservationStore _store;

    public SqliteObservationStoreTests()
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

    private static Observation CreateObservation(
        string? id = null,
        string? project = null,
        EventType eventType = EventType.ToolCall,
        string? summary = null,
        string? rawContent = null)
    {
        return new Observation
        {
            Id = id ?? Guid.NewGuid().ToString(),
            SessionId = "session-1",
            Timestamp = DateTime.UtcNow,
            Project = project ?? "TestProject",
            EventType = eventType,
            Source = CaptureSource.ClaudeCode,
            RawContent = rawContent ?? "Some raw content",
            Summary = summary,
            Tags = ["tag1", "tag2"],
            FilesInvolved = ["file1.cs", "file2.cs"],
            CreatedAt = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task Add_And_GetById_ReturnsObservation()
    {
        var obs = CreateObservation(id: "obs-1", rawContent: "test content");

        await _store.Add(obs);
        var result = await _store.GetById("obs-1");

        Assert.NotNull(result);
        Assert.Equal("obs-1", result.Id);
        Assert.Equal("test content", result.RawContent);
        Assert.Equal("TestProject", result.Project);
        Assert.Equal(EventType.ToolCall, result.EventType);
        Assert.Equal(2, result.Tags.Count);
        Assert.Contains("tag1", result.Tags);
        Assert.Equal(2, result.FilesInvolved.Count);
        Assert.Contains("file1.cs", result.FilesInvolved);
    }

    [Fact]
    public async Task Query_ByProject_ReturnsMatchingObservations()
    {
        await _store.Add(CreateObservation(project: "ProjectA"));
        await _store.Add(CreateObservation(project: "ProjectA"));
        await _store.Add(CreateObservation(project: "ProjectB"));

        var results = await _store.Query(new ObservationFilter { Project = "ProjectA" });

        Assert.Equal(2, results.Count);
        Assert.All(results, o => Assert.Equal("ProjectA", o.Project));
    }

    [Fact]
    public async Task Query_ByEventType_ReturnsMatchingObservations()
    {
        await _store.Add(CreateObservation(eventType: EventType.Error));
        await _store.Add(CreateObservation(eventType: EventType.Error));
        await _store.Add(CreateObservation(eventType: EventType.Decision));

        var results = await _store.Query(new ObservationFilter { EventType = EventType.Error });

        Assert.Equal(2, results.Count);
        Assert.All(results, o => Assert.Equal(EventType.Error, o.EventType));
    }

    [Fact]
    public async Task GetUnenriched_ReturnsObservationsWithoutSummary()
    {
        await _store.Add(CreateObservation(summary: null));
        await _store.Add(CreateObservation(summary: null));
        await _store.Add(CreateObservation(summary: "Has summary"));

        var results = await _store.GetUnenriched();

        Assert.Equal(2, results.Count);
        Assert.All(results, o => Assert.Null(o.Summary));
    }

    [Fact]
    public async Task SearchFts_FindsMatchingContent()
    {
        await _store.Add(CreateObservation(rawContent: "refactoring the database layer"));
        await _store.Add(CreateObservation(rawContent: "fixing a UI bug in the header"));
        await _store.Add(CreateObservation(rawContent: "database migration script added"));

        var results = await _store.SearchFts("database", limit: 10);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task Count_ReturnsTotalObservations()
    {
        await _store.Add(CreateObservation());
        await _store.Add(CreateObservation());
        await _store.Add(CreateObservation());

        var count = await _store.Count();

        Assert.Equal(3, count);
    }

    [Fact]
    public async Task DeleteByProject_RemovesCorrectObservations()
    {
        await _store.Add(CreateObservation(project: "ToDelete"));
        await _store.Add(CreateObservation(project: "ToDelete"));
        await _store.Add(CreateObservation(project: "ToKeep"));

        await _store.DeleteByProject("ToDelete");

        var count = await _store.Count();
        Assert.Equal(1, count);

        var remaining = await _store.Query(new ObservationFilter { Project = "ToKeep" });
        Assert.Single(remaining);
    }

    [Fact]
    public async Task GetById_NonExistent_ReturnsNull()
    {
        var result = await _store.GetById("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task Update_ModifiesObservation()
    {
        var obs = CreateObservation(id: "update-1");
        await _store.Add(obs);

        var updated = obs with { Summary = "New summary" };
        await _store.Update(updated);

        var result = await _store.GetById("update-1");
        Assert.NotNull(result);
        Assert.Equal("New summary", result.Summary);
    }

    [Fact]
    public async Task Delete_RemovesObservation()
    {
        var obs = CreateObservation(id: "delete-1");
        await _store.Add(obs);

        await _store.Delete("delete-1");

        var result = await _store.GetById("delete-1");
        Assert.Null(result);
    }

    [Fact]
    public void SchemaManager_GetSchemaVersion_ReturnsOne()
    {
        var version = SchemaManager.GetSchemaVersion(_connection);
        Assert.Equal(1, version);
    }
}
