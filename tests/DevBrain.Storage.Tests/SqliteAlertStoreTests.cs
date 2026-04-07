using DevBrain.Core.Enums;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;
using DevBrain.Storage;
using DevBrain.Storage.Schema;
using Microsoft.Data.Sqlite;

namespace DevBrain.Storage.Tests;

public class SqliteAlertStoreTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private SqliteAlertStore _store = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        SchemaManager.Initialize(_connection);
        _store = new SqliteAlertStore(_connection);
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Add_And_GetActive_RoundTrips()
    {
        var alert = new DejaVuAlert
        {
            Id = "alert-1",
            ThreadId = "t1",
            MatchedDeadEndId = "de-1",
            Confidence = 0.75,
            Message = "You tried this before!",
            Strategy = MatchStrategy.FileOverlap
        };

        await _store.Add(alert);

        var active = await _store.GetActive();
        Assert.Single(active);
        Assert.Equal("alert-1", active[0].Id);
        Assert.Equal(0.75, active[0].Confidence);
        Assert.Equal(MatchStrategy.FileOverlap, active[0].Strategy);
        Assert.False(active[0].Dismissed);
    }

    [Fact]
    public async Task Dismiss_RemovesFromActive()
    {
        var alert = new DejaVuAlert
        {
            Id = "alert-1",
            ThreadId = "t1",
            MatchedDeadEndId = "de-1",
            Confidence = 0.8,
            Message = "Warning",
            Strategy = MatchStrategy.FileOverlap
        };

        await _store.Add(alert);
        await _store.Dismiss("alert-1");

        var active = await _store.GetActive();
        Assert.Empty(active);

        var all = await _store.GetAll();
        Assert.Single(all);
        Assert.True(all[0].Dismissed);
    }

    [Fact]
    public async Task Exists_DetectsDuplicates()
    {
        await _store.Add(new DejaVuAlert
        {
            Id = "alert-1", ThreadId = "t1", MatchedDeadEndId = "de-1",
            Confidence = 0.6, Message = "Dup check", Strategy = MatchStrategy.FileOverlap
        });

        Assert.True(await _store.Exists("t1", "de-1"));
        Assert.False(await _store.Exists("t1", "de-999"));
        Assert.False(await _store.Exists("t-other", "de-1"));
    }

    [Fact]
    public async Task Exists_IgnoresDismissedAlerts()
    {
        await _store.Add(new DejaVuAlert
        {
            Id = "alert-1", ThreadId = "t1", MatchedDeadEndId = "de-1",
            Confidence = 0.7, Message = "Will dismiss", Strategy = MatchStrategy.FileOverlap
        });

        await _store.Dismiss("alert-1");
        Assert.False(await _store.Exists("t1", "de-1"));
    }

    [Fact]
    public async Task GetAll_ReturnsAllIncludingDismissed()
    {
        await _store.Add(new DejaVuAlert
        {
            Id = "a1", ThreadId = "t1", MatchedDeadEndId = "de-1",
            Confidence = 0.5, Message = "First", Strategy = MatchStrategy.FileOverlap
        });
        await _store.Add(new DejaVuAlert
        {
            Id = "a2", ThreadId = "t2", MatchedDeadEndId = "de-2",
            Confidence = 0.9, Message = "Second", Strategy = MatchStrategy.Semantic
        });
        await _store.Dismiss("a1");

        var all = await _store.GetAll();
        Assert.Equal(2, all.Count);
    }
}
