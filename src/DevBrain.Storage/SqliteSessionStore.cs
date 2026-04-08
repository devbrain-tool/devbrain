namespace DevBrain.Storage;

using System.Globalization;
using System.Text.Json;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;
using Microsoft.Data.Sqlite;

public class SqliteSessionStore : ISessionStore
{
    private readonly SqliteConnection _connection;

    public SqliteSessionStore(SqliteConnection connection)
    {
        _connection = connection;
    }

    public async Task<SessionSummary> Add(SessionSummary summary)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO session_summaries (id, session_id, narrative, outcome,
                duration_seconds, observation_count, files_touched, dead_ends_hit,
                phases, created_at)
            VALUES (@id, @sessionId, @narrative, @outcome,
                @durationSeconds, @observationCount, @filesTouched, @deadEndsHit,
                @phases, @createdAt)
            """;

        cmd.Parameters.AddWithValue("@id", summary.Id);
        cmd.Parameters.AddWithValue("@sessionId", summary.SessionId);
        cmd.Parameters.AddWithValue("@narrative", summary.Narrative);
        cmd.Parameters.AddWithValue("@outcome", summary.Outcome);
        cmd.Parameters.AddWithValue("@durationSeconds", (int)summary.Duration.TotalSeconds);
        cmd.Parameters.AddWithValue("@observationCount", summary.ObservationCount);
        cmd.Parameters.AddWithValue("@filesTouched", summary.FilesTouched);
        cmd.Parameters.AddWithValue("@deadEndsHit", summary.DeadEndsHit);
        cmd.Parameters.AddWithValue("@phases", JsonSerializer.Serialize(summary.Phases));
        cmd.Parameters.AddWithValue("@createdAt", summary.CreatedAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
        return summary;
    }

    public async Task<SessionSummary?> GetBySessionId(string sessionId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM session_summaries WHERE session_id = @sessionId";
        cmd.Parameters.AddWithValue("@sessionId", sessionId);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            return MapSummary(reader);
        return null;
    }

    public async Task<IReadOnlyList<SessionSummary>> GetAll(int limit = 50)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM session_summaries ORDER BY created_at DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<SessionSummary>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(MapSummary(reader));
        return results;
    }

    public async Task<SessionSummary?> GetLatest()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM session_summaries ORDER BY created_at DESC LIMIT 1";

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            return MapSummary(reader);
        return null;
    }

    public async Task<IReadOnlyList<SessionSummary>> GetByDateRange(DateTime after, DateTime before)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM session_summaries WHERE created_at > @after AND created_at < @before ORDER BY created_at DESC";
        cmd.Parameters.AddWithValue("@after", after.ToString("o"));
        cmd.Parameters.AddWithValue("@before", before.ToString("o"));

        var results = new List<SessionSummary>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(MapSummary(reader));
        return results;
    }

    private static SessionSummary MapSummary(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(reader.GetOrdinal("id")),
        SessionId = reader.GetString(reader.GetOrdinal("session_id")),
        Narrative = reader.GetString(reader.GetOrdinal("narrative")),
        Outcome = reader.GetString(reader.GetOrdinal("outcome")),
        Duration = TimeSpan.FromSeconds(reader.GetInt32(reader.GetOrdinal("duration_seconds"))),
        ObservationCount = reader.GetInt32(reader.GetOrdinal("observation_count")),
        FilesTouched = reader.GetInt32(reader.GetOrdinal("files_touched")),
        DeadEndsHit = reader.GetInt32(reader.GetOrdinal("dead_ends_hit")),
        Phases = JsonSerializer.Deserialize<List<string>>(
            reader.GetString(reader.GetOrdinal("phases"))) ?? [],
        CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at")),
            CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
    };
}
