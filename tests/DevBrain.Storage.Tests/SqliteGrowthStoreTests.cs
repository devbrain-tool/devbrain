using DevBrain.Core.Enums;
using DevBrain.Core.Models;
using DevBrain.Storage;
using DevBrain.Storage.Schema;
using Microsoft.Data.Sqlite;

namespace DevBrain.Storage.Tests;

public class SqliteGrowthStoreTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private SqliteGrowthStore _store = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        SchemaManager.Initialize(_connection);
        _store = new SqliteGrowthStore(_connection);
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task AddMetric_And_GetMetrics_RoundTrips()
    {
        var now = DateTime.UtcNow;
        await _store.AddMetric(new DeveloperMetric
        {
            Id = "m1", Dimension = "debugging_speed", Value = 12.5,
            PeriodStart = now.AddDays(-7), PeriodEnd = now
        });

        var metrics = await _store.GetMetrics("debugging_speed");
        Assert.Single(metrics);
        Assert.Equal(12.5, metrics[0].Value);
    }

    [Fact]
    public async Task GetLatestMetrics_ReturnsOnePerDimension()
    {
        var now = DateTime.UtcNow;
        await _store.AddMetric(new DeveloperMetric
        {
            Id = "m1", Dimension = "debugging_speed", Value = 10,
            PeriodStart = now.AddDays(-14), PeriodEnd = now.AddDays(-7)
        });
        await _store.AddMetric(new DeveloperMetric
        {
            Id = "m2", Dimension = "debugging_speed", Value = 8,
            PeriodStart = now.AddDays(-7), PeriodEnd = now
        });
        await _store.AddMetric(new DeveloperMetric
        {
            Id = "m3", Dimension = "dead_end_rate", Value = 0.5,
            PeriodStart = now.AddDays(-7), PeriodEnd = now
        });

        var latest = await _store.GetLatestMetrics();
        Assert.Equal(2, latest.Count);
        Assert.Contains(latest, m => m.Dimension == "debugging_speed" && m.Value == 8);
        Assert.Contains(latest, m => m.Dimension == "dead_end_rate");
    }

    [Fact]
    public async Task AddMilestone_And_GetMilestones_RoundTrips()
    {
        await _store.AddMilestone(new GrowthMilestone
        {
            Id = "ms1", Type = MilestoneType.First,
            Description = "First time using CTE queries",
            AchievedAt = DateTime.UtcNow
        });

        var milestones = await _store.GetMilestones();
        Assert.Single(milestones);
        Assert.Equal(MilestoneType.First, milestones[0].Type);
        Assert.Contains("CTE", milestones[0].Description);
    }

    [Fact]
    public async Task AddReport_And_GetLatest_RoundTrips()
    {
        await _store.AddReport(new GrowthReport
        {
            Id = "r1",
            PeriodStart = DateTime.UtcNow.AddDays(-7),
            PeriodEnd = DateTime.UtcNow,
            Narrative = "Great week — debugging speed improved 20%"
        });

        var latest = await _store.GetLatestReport();
        Assert.NotNull(latest);
        Assert.Equal("r1", latest.Id);
        Assert.Contains("debugging speed", latest.Narrative);
    }

    [Fact]
    public async Task Clear_RemovesAllData()
    {
        await _store.AddMetric(new DeveloperMetric
        {
            Id = "m1", Dimension = "test", Value = 1,
            PeriodStart = DateTime.UtcNow, PeriodEnd = DateTime.UtcNow
        });
        await _store.AddMilestone(new GrowthMilestone
        {
            Id = "ms1", Type = MilestoneType.First,
            Description = "test", AchievedAt = DateTime.UtcNow
        });

        await _store.Clear();

        Assert.Empty(await _store.GetLatestMetrics());
        Assert.Empty(await _store.GetMilestones());
        Assert.Null(await _store.GetLatestReport());
    }
}
