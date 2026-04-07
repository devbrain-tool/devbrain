using DevBrain.Core.Models;
using DevBrain.Storage;
using DevBrain.Storage.Schema;
using Microsoft.Data.Sqlite;

namespace DevBrain.Storage.Tests;

public class SqliteSessionStoreTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private SqliteSessionStore _store = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        SchemaManager.Initialize(_connection);
        _store = new SqliteSessionStore(_connection);
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Add_And_GetBySessionId_RoundTrips()
    {
        var summary = new SessionSummary
        {
            Id = "ss-1",
            SessionId = "session-abc",
            Narrative = "The developer started by investigating a bug...",
            Outcome = "Fixed the race condition in auth middleware",
            Duration = TimeSpan.FromMinutes(47),
            ObservationCount = 23,
            FilesTouched = 5,
            DeadEndsHit = 1,
            Phases = ["Exploration", "Debugging", "Implementation"]
        };

        await _store.Add(summary);

        var fetched = await _store.GetBySessionId("session-abc");
        Assert.NotNull(fetched);
        Assert.Equal("ss-1", fetched.Id);
        Assert.Equal("session-abc", fetched.SessionId);
        Assert.Equal(TimeSpan.FromMinutes(47), fetched.Duration);
        Assert.Equal(23, fetched.ObservationCount);
        Assert.Equal(3, fetched.Phases.Count);
        Assert.Contains("Debugging", fetched.Phases);
    }

    [Fact]
    public async Task GetLatest_ReturnsNewest()
    {
        await _store.Add(new SessionSummary
        {
            Id = "ss-old", SessionId = "s-old",
            Narrative = "Old session", Outcome = "Done",
            Duration = TimeSpan.FromMinutes(10), ObservationCount = 5,
            FilesTouched = 2, DeadEndsHit = 0, Phases = [],
            CreatedAt = DateTime.UtcNow.AddHours(-2)
        });
        await _store.Add(new SessionSummary
        {
            Id = "ss-new", SessionId = "s-new",
            Narrative = "New session", Outcome = "Also done",
            Duration = TimeSpan.FromMinutes(30), ObservationCount = 15,
            FilesTouched = 8, DeadEndsHit = 2, Phases = ["Implementation"]
        });

        var latest = await _store.GetLatest();
        Assert.NotNull(latest);
        Assert.Equal("ss-new", latest.Id);
    }

    [Fact]
    public async Task GetBySessionId_ReturnsNullWhenNotFound()
    {
        var result = await _store.GetBySessionId("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAll_ReturnsAllSorted()
    {
        await _store.Add(new SessionSummary
        {
            Id = "ss-1", SessionId = "s1",
            Narrative = "First", Outcome = "Done",
            Duration = TimeSpan.FromMinutes(10), ObservationCount = 5,
            FilesTouched = 2, DeadEndsHit = 0, Phases = [],
            CreatedAt = DateTime.UtcNow.AddHours(-1)
        });
        await _store.Add(new SessionSummary
        {
            Id = "ss-2", SessionId = "s2",
            Narrative = "Second", Outcome = "Done",
            Duration = TimeSpan.FromMinutes(20), ObservationCount = 10,
            FilesTouched = 4, DeadEndsHit = 1, Phases = []
        });

        var all = await _store.GetAll();
        Assert.Equal(2, all.Count);
        Assert.Equal("ss-2", all[0].Id); // newest first
    }
}
