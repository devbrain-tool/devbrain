namespace DevBrain.Storage;

using System.Globalization;
using System.Text.Json;
using DevBrain.Core.Enums;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;
using Microsoft.Data.Sqlite;

public class SqliteGrowthStore : IGrowthStore
{
    private readonly SqliteConnection _connection;

    public SqliteGrowthStore(SqliteConnection connection)
    {
        _connection = connection;
    }

    public async Task<DeveloperMetric> AddMetric(DeveloperMetric metric)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO developer_metrics (id, dimension, value, period_start, period_end, created_at)
            VALUES (@id, @dimension, @value, @periodStart, @periodEnd, @createdAt)
            """;
        cmd.Parameters.AddWithValue("@id", metric.Id);
        cmd.Parameters.AddWithValue("@dimension", metric.Dimension);
        cmd.Parameters.AddWithValue("@value", metric.Value);
        cmd.Parameters.AddWithValue("@periodStart", metric.PeriodStart.ToString("o"));
        cmd.Parameters.AddWithValue("@periodEnd", metric.PeriodEnd.ToString("o"));
        cmd.Parameters.AddWithValue("@createdAt", metric.CreatedAt.ToString("o"));
        await cmd.ExecuteNonQueryAsync();
        return metric;
    }

    public async Task<IReadOnlyList<DeveloperMetric>> GetMetrics(string dimension, int weeks = 12)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM developer_metrics
            WHERE dimension = @dimension
            ORDER BY period_start DESC LIMIT @limit
            """;
        cmd.Parameters.AddWithValue("@dimension", dimension);
        cmd.Parameters.AddWithValue("@limit", weeks);

        var results = new List<DeveloperMetric>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(MapMetric(reader));
        return results;
    }

    public async Task<IReadOnlyList<DeveloperMetric>> GetLatestMetrics()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT m.* FROM developer_metrics m
            INNER JOIN (
                SELECT dimension, MAX(period_start) as max_start
                FROM developer_metrics GROUP BY dimension
            ) latest ON m.dimension = latest.dimension AND m.period_start = latest.max_start
            ORDER BY m.dimension
            """;

        var results = new List<DeveloperMetric>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(MapMetric(reader));
        return results;
    }

    public async Task<GrowthMilestone> AddMilestone(GrowthMilestone milestone)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO milestones (id, type, description, achieved_at, observation_id, created_at)
            VALUES (@id, @type, @description, @achievedAt, @observationId, @createdAt)
            """;
        cmd.Parameters.AddWithValue("@id", milestone.Id);
        cmd.Parameters.AddWithValue("@type", milestone.Type.ToString());
        cmd.Parameters.AddWithValue("@description", milestone.Description);
        cmd.Parameters.AddWithValue("@achievedAt", milestone.AchievedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@observationId", (object?)milestone.ObservationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", milestone.CreatedAt.ToString("o"));
        await cmd.ExecuteNonQueryAsync();
        return milestone;
    }

    public async Task<IReadOnlyList<GrowthMilestone>> GetMilestones(int limit = 50)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM milestones ORDER BY achieved_at DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<GrowthMilestone>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(MapMilestone(reader));
        return results;
    }

    public async Task<GrowthReport> AddReport(GrowthReport report)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO growth_reports (id, period_start, period_end, metrics, milestones, narrative, generated_at)
            VALUES (@id, @periodStart, @periodEnd, @metrics, @milestones, @narrative, @generatedAt)
            """;
        cmd.Parameters.AddWithValue("@id", report.Id);
        cmd.Parameters.AddWithValue("@periodStart", report.PeriodStart.ToString("o"));
        cmd.Parameters.AddWithValue("@periodEnd", report.PeriodEnd.ToString("o"));
        cmd.Parameters.AddWithValue("@metrics", JsonSerializer.Serialize(report.Metrics.Select(m => m.Id)));
        cmd.Parameters.AddWithValue("@milestones", JsonSerializer.Serialize(report.Milestones.Select(m => m.Id)));
        cmd.Parameters.AddWithValue("@narrative", (object?)report.Narrative ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@generatedAt", report.GeneratedAt.ToString("o"));
        await cmd.ExecuteNonQueryAsync();
        return report;
    }

    public async Task<GrowthReport?> GetLatestReport()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM growth_reports ORDER BY generated_at DESC LIMIT 1";

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            return await HydrateReport(MapReportShell(reader));
        return null;
    }

    public async Task<IReadOnlyList<GrowthReport>> GetReports(int limit = 12)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM growth_reports ORDER BY generated_at DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<GrowthReport>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(await HydrateReport(MapReportShell(reader)));
        return results;
    }

    private async Task<GrowthReport> HydrateReport(
        (GrowthReport Report, List<string> MetricIds, List<string> MilestoneIds) shell)
    {
        var metrics = new List<DeveloperMetric>();
        foreach (var id in shell.MetricIds)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM developer_metrics WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                metrics.Add(MapMetric(reader));
        }

        var milestones = new List<GrowthMilestone>();
        foreach (var id in shell.MilestoneIds)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM milestones WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                milestones.Add(MapMilestone(reader));
        }

        return shell.Report with { Metrics = metrics, Milestones = milestones };
    }

    public async Task Clear()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM developer_metrics; DELETE FROM milestones; DELETE FROM growth_reports;";
        await cmd.ExecuteNonQueryAsync();
    }

    private static DeveloperMetric MapMetric(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(reader.GetOrdinal("id")),
        Dimension = reader.GetString(reader.GetOrdinal("dimension")),
        Value = reader.GetDouble(reader.GetOrdinal("value")),
        PeriodStart = DateTime.Parse(reader.GetString(reader.GetOrdinal("period_start")),
            CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        PeriodEnd = DateTime.Parse(reader.GetString(reader.GetOrdinal("period_end")),
            CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at")),
            CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
    };

    private static GrowthMilestone MapMilestone(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(reader.GetOrdinal("id")),
        Type = Enum.Parse<MilestoneType>(reader.GetString(reader.GetOrdinal("type"))),
        Description = reader.GetString(reader.GetOrdinal("description")),
        AchievedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("achieved_at")),
            CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        ObservationId = reader.IsDBNull(reader.GetOrdinal("observation_id"))
            ? null : reader.GetString(reader.GetOrdinal("observation_id")),
        CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at")),
            CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
    };

    private static (GrowthReport Report, List<string> MetricIds, List<string> MilestoneIds) MapReportShell(
        SqliteDataReader reader)
    {
        var metricIds = JsonSerializer.Deserialize<List<string>>(
            reader.GetString(reader.GetOrdinal("metrics"))) ?? [];
        var milestoneIds = JsonSerializer.Deserialize<List<string>>(
            reader.GetString(reader.GetOrdinal("milestones"))) ?? [];

        var report = new GrowthReport
        {
            Id = reader.GetString(reader.GetOrdinal("id")),
            PeriodStart = DateTime.Parse(reader.GetString(reader.GetOrdinal("period_start")),
                CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            PeriodEnd = DateTime.Parse(reader.GetString(reader.GetOrdinal("period_end")),
                CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            Narrative = reader.IsDBNull(reader.GetOrdinal("narrative"))
                ? null : reader.GetString(reader.GetOrdinal("narrative")),
            GeneratedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("generated_at")),
                CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
        };

        return (report, metricIds, milestoneIds);
    }
}
