namespace DevBrain.Storage;

using System.Globalization;
using System.Text.Json;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;
using Microsoft.Data.Sqlite;

public class SqliteDeadEndStore : IDeadEndStore
{
    private readonly SqliteConnection _connection;

    public SqliteDeadEndStore(SqliteConnection connection)
    {
        _connection = connection;
    }

    public async Task<DeadEnd> Add(DeadEnd deadEnd)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO dead_ends (id, thread_id, project, description, approach, reason,
                files_involved, detected_at, created_at)
            VALUES (@id, @threadId, @project, @description, @approach, @reason,
                @filesInvolved, @detectedAt, @createdAt)
            """;

        cmd.Parameters.AddWithValue("@id", deadEnd.Id);
        cmd.Parameters.AddWithValue("@threadId", (object?)deadEnd.ThreadId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@project", deadEnd.Project);
        cmd.Parameters.AddWithValue("@description", deadEnd.Description);
        cmd.Parameters.AddWithValue("@approach", deadEnd.Approach);
        cmd.Parameters.AddWithValue("@reason", deadEnd.Reason);
        cmd.Parameters.AddWithValue("@filesInvolved", JsonSerializer.Serialize(deadEnd.FilesInvolved));
        cmd.Parameters.AddWithValue("@detectedAt", deadEnd.DetectedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@createdAt", deadEnd.CreatedAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
        return deadEnd;
    }

    public async Task<IReadOnlyList<DeadEnd>> Query(DeadEndFilter filter)
    {
        using var cmd = _connection.CreateCommand();
        var clauses = new List<string>();

        if (filter.Project is not null)
        {
            clauses.Add("project = @project");
            cmd.Parameters.AddWithValue("@project", filter.Project);
        }
        if (filter.ThreadId is not null)
        {
            clauses.Add("thread_id = @threadId");
            cmd.Parameters.AddWithValue("@threadId", filter.ThreadId);
        }
        if (filter.After is not null)
        {
            clauses.Add("detected_at > @after");
            cmd.Parameters.AddWithValue("@after", filter.After.Value.ToString("o"));
        }
        if (filter.Before is not null)
        {
            clauses.Add("detected_at < @before");
            cmd.Parameters.AddWithValue("@before", filter.Before.Value.ToString("o"));
        }

        var where = clauses.Count > 0 ? "WHERE " + string.Join(" AND ", clauses) : "";
        cmd.CommandText = $"SELECT * FROM dead_ends {where} ORDER BY detected_at DESC LIMIT @limit OFFSET @offset";
        cmd.Parameters.AddWithValue("@limit", filter.Limit);
        cmd.Parameters.AddWithValue("@offset", filter.Offset);

        return await ReadDeadEnds(cmd);
    }

    public async Task<IReadOnlyList<DeadEnd>> FindByFiles(IReadOnlyList<string> filePaths)
    {
        if (filePaths.Count == 0)
            return [];

        // Pre-filter in SQL using LIKE on the JSON text column, then verify client-side.
        // This avoids a full-table scan while handling JSON array storage correctly.
        using var cmd = _connection.CreateCommand();
        var likeClauses = new List<string>();
        for (int i = 0; i < filePaths.Count; i++)
        {
            likeClauses.Add($"files_involved LIKE @fp{i}");
            cmd.Parameters.AddWithValue($"@fp{i}", $"%{EscapeLike(filePaths[i])}%");
        }

        cmd.CommandText = $"SELECT * FROM dead_ends WHERE {string.Join(" OR ", likeClauses)} ORDER BY detected_at DESC LIMIT 200";

        var candidates = await ReadDeadEnds(cmd);
        var fileSet = filePaths.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Exact match after JSON deserialization (LIKE may produce false positives)
        return candidates
            .Where(de => de.FilesInvolved.Any(f => fileSet.Contains(f)))
            .ToList();
    }

    private static string EscapeLike(string value)
    {
        return value.Replace("%", "").Replace("_", "").Replace("[", "");
    }

    public async Task<IReadOnlyList<DeadEnd>> FindSimilar(string description, int limit = 5)
    {
        var keywords = description.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2)
            .Take(5)
            .ToList();

        if (keywords.Count == 0)
            return [];

        using var cmd = _connection.CreateCommand();
        var likeClause = string.Join(" OR ", keywords.Select((_, i) => $"description LIKE @kw{i}"));
        cmd.CommandText = $"SELECT * FROM dead_ends WHERE {likeClause} ORDER BY detected_at DESC LIMIT @limit";

        for (int i = 0; i < keywords.Count; i++)
            cmd.Parameters.AddWithValue($"@kw{i}", $"%{keywords[i]}%");
        cmd.Parameters.AddWithValue("@limit", limit);

        return await ReadDeadEnds(cmd);
    }

    private static async Task<IReadOnlyList<DeadEnd>> ReadDeadEnds(SqliteCommand cmd)
    {
        var results = new List<DeadEnd>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(MapDeadEnd(reader));
        }
        return results;
    }

    private static DeadEnd MapDeadEnd(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(reader.GetOrdinal("id")),
        ThreadId = reader.IsDBNull(reader.GetOrdinal("thread_id")) ? null : reader.GetString(reader.GetOrdinal("thread_id")),
        Project = reader.GetString(reader.GetOrdinal("project")),
        Description = reader.GetString(reader.GetOrdinal("description")),
        Approach = reader.GetString(reader.GetOrdinal("approach")),
        Reason = reader.GetString(reader.GetOrdinal("reason")),
        FilesInvolved = reader.IsDBNull(reader.GetOrdinal("files_involved"))
            ? []
            : JsonSerializer.Deserialize<List<string>>(reader.GetString(reader.GetOrdinal("files_involved"))) ?? [],
        DetectedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("detected_at")), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at")), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
    };
}
