namespace DevBrain.Storage;

using System.Globalization;
using System.Text.Json;
using DevBrain.Core.Enums;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;
using Microsoft.Data.Sqlite;

public class SqliteObservationStore : IObservationStore
{
    private readonly SqliteConnection _connection;

    public SqliteObservationStore(SqliteConnection connection)
    {
        _connection = connection;
    }

    public async Task<Observation> Add(Observation observation)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO observations (id, session_id, thread_id, parent_id, timestamp, project, branch,
                event_type, source, raw_content, summary, tags, files_involved, created_at)
            VALUES (@id, @sessionId, @threadId, @parentId, @timestamp, @project, @branch,
                @eventType, @source, @rawContent, @summary, @tags, @filesInvolved, @createdAt)
            """;

        cmd.Parameters.AddWithValue("@id", observation.Id);
        cmd.Parameters.AddWithValue("@sessionId", observation.SessionId);
        cmd.Parameters.AddWithValue("@threadId", (object?)observation.ThreadId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@parentId", (object?)observation.ParentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@timestamp", observation.Timestamp.ToString("o"));
        cmd.Parameters.AddWithValue("@project", observation.Project);
        cmd.Parameters.AddWithValue("@branch", (object?)observation.Branch ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@eventType", observation.EventType.ToString());
        cmd.Parameters.AddWithValue("@source", observation.Source.ToString());
        cmd.Parameters.AddWithValue("@rawContent", observation.RawContent);
        cmd.Parameters.AddWithValue("@summary", (object?)observation.Summary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tags", JsonSerializer.Serialize(observation.Tags));
        cmd.Parameters.AddWithValue("@filesInvolved", JsonSerializer.Serialize(observation.FilesInvolved));
        cmd.Parameters.AddWithValue("@createdAt", observation.CreatedAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
        return observation;
    }

    public async Task<Observation?> GetById(string id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM observations WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            return MapObservation(reader);
        return null;
    }

    public async Task<IReadOnlyList<Observation>> Query(ObservationFilter filter)
    {
        using var cmd = _connection.CreateCommand();
        var clauses = new List<string>();

        if (filter.Project is not null)
        {
            clauses.Add("project = @project");
            cmd.Parameters.AddWithValue("@project", filter.Project);
        }
        if (filter.EventType is not null)
        {
            clauses.Add("event_type = @eventType");
            cmd.Parameters.AddWithValue("@eventType", filter.EventType.Value.ToString());
        }
        if (filter.ThreadId is not null)
        {
            clauses.Add("thread_id = @threadId");
            cmd.Parameters.AddWithValue("@threadId", filter.ThreadId);
        }
        if (filter.After is not null)
        {
            clauses.Add("timestamp > @after");
            cmd.Parameters.AddWithValue("@after", filter.After.Value.ToString("o"));
        }
        if (filter.Before is not null)
        {
            clauses.Add("timestamp < @before");
            cmd.Parameters.AddWithValue("@before", filter.Before.Value.ToString("o"));
        }

        var where = clauses.Count > 0 ? "WHERE " + string.Join(" AND ", clauses) : "";
        cmd.CommandText = $"SELECT * FROM observations {where} ORDER BY timestamp DESC LIMIT @limit OFFSET @offset";
        cmd.Parameters.AddWithValue("@limit", filter.Limit);
        cmd.Parameters.AddWithValue("@offset", filter.Offset);

        var results = new List<Observation>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(MapObservation(reader));
        return results;
    }

    public async Task<IReadOnlyList<Observation>> GetUnenriched(int limit = 50)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM observations WHERE summary IS NULL ORDER BY timestamp ASC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<Observation>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(MapObservation(reader));
        return results;
    }

    public async Task Update(Observation observation)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE observations SET
                thread_id = @threadId, summary = @summary, tags = @tags,
                files_involved = @filesInvolved
            WHERE id = @id
            """;

        cmd.Parameters.AddWithValue("@id", observation.Id);
        cmd.Parameters.AddWithValue("@threadId", (object?)observation.ThreadId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@summary", (object?)observation.Summary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tags", JsonSerializer.Serialize(observation.Tags));
        cmd.Parameters.AddWithValue("@filesInvolved", JsonSerializer.Serialize(observation.FilesInvolved));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task Delete(string id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM observations WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<Observation>> SearchFts(string query, int limit = 20)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT o.* FROM observations_fts
            JOIN observations o ON o.rowid = observations_fts.rowid
            WHERE observations_fts MATCH @query
            ORDER BY rank
            LIMIT @limit
            """;
        cmd.Parameters.AddWithValue("@query", query);
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<Observation>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(MapObservation(reader));
        return results;
    }

    public async Task<long> Count()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM observations";
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    public async Task<long> GetDatabaseSizeBytes()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT page_count * page_size FROM pragma_page_count, pragma_page_size";
        var result = await cmd.ExecuteScalarAsync();
        return result is long l ? l : Convert.ToInt64(result);
    }

    public async Task DeleteByProject(string project)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM observations WHERE project = @project";
        cmd.Parameters.AddWithValue("@project", project);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteBefore(DateTime before)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM observations WHERE timestamp < @before";
        cmd.Parameters.AddWithValue("@before", before.ToString("o"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<Observation>> GetSessionObservations(string sessionId, int limit = 500)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM observations WHERE session_id = @sessionId ORDER BY timestamp ASC LIMIT @limit";
        cmd.Parameters.AddWithValue("@sessionId", sessionId);
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<Observation>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(MapObservation(reader));
        return results;
    }

    private static Observation MapObservation(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(reader.GetOrdinal("id")),
        SessionId = reader.GetString(reader.GetOrdinal("session_id")),
        ThreadId = reader.IsDBNull(reader.GetOrdinal("thread_id")) ? null : reader.GetString(reader.GetOrdinal("thread_id")),
        ParentId = reader.IsDBNull(reader.GetOrdinal("parent_id")) ? null : reader.GetString(reader.GetOrdinal("parent_id")),
        Timestamp = DateTime.Parse(reader.GetString(reader.GetOrdinal("timestamp")), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        Project = reader.GetString(reader.GetOrdinal("project")),
        Branch = reader.IsDBNull(reader.GetOrdinal("branch")) ? null : reader.GetString(reader.GetOrdinal("branch")),
        EventType = Enum.Parse<EventType>(reader.GetString(reader.GetOrdinal("event_type"))),
        Source = Enum.Parse<CaptureSource>(reader.GetString(reader.GetOrdinal("source"))),
        RawContent = reader.GetString(reader.GetOrdinal("raw_content")),
        Summary = reader.IsDBNull(reader.GetOrdinal("summary")) ? null : reader.GetString(reader.GetOrdinal("summary")),
        Tags = JsonSerializer.Deserialize<List<string>>(reader.GetString(reader.GetOrdinal("tags"))) ?? [],
        FilesInvolved = JsonSerializer.Deserialize<List<string>>(reader.GetString(reader.GetOrdinal("files_involved"))) ?? [],
        CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at")), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
    };
}
