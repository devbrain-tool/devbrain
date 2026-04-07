namespace DevBrain.Storage;

using System.Globalization;
using DevBrain.Core.Enums;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;
using Microsoft.Data.Sqlite;

public class SqliteAlertStore : IAlertStore
{
    private readonly SqliteConnection _connection;

    public SqliteAlertStore(SqliteConnection connection)
    {
        _connection = connection;
    }

    public async Task<DejaVuAlert> Add(DejaVuAlert alert)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO deja_vu_alerts (id, thread_id, matched_dead_end_id, confidence,
                message, strategy, dismissed, created_at)
            VALUES (@id, @threadId, @deadEndId, @confidence,
                @message, @strategy, @dismissed, @createdAt)
            """;

        cmd.Parameters.AddWithValue("@id", alert.Id);
        cmd.Parameters.AddWithValue("@threadId", alert.ThreadId);
        cmd.Parameters.AddWithValue("@deadEndId", alert.MatchedDeadEndId);
        cmd.Parameters.AddWithValue("@confidence", alert.Confidence);
        cmd.Parameters.AddWithValue("@message", alert.Message);
        cmd.Parameters.AddWithValue("@strategy", alert.Strategy.ToString());
        cmd.Parameters.AddWithValue("@dismissed", alert.Dismissed ? 1 : 0);
        cmd.Parameters.AddWithValue("@createdAt", alert.CreatedAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
        return alert;
    }

    public async Task<IReadOnlyList<DejaVuAlert>> GetActive()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM deja_vu_alerts WHERE dismissed = 0 ORDER BY created_at DESC";
        return await ReadAlerts(cmd);
    }

    public async Task<IReadOnlyList<DejaVuAlert>> GetAll(int limit = 100)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM deja_vu_alerts ORDER BY created_at DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);
        return await ReadAlerts(cmd);
    }

    public async Task Dismiss(string id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE deja_vu_alerts SET dismissed = 1 WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> Exists(string threadId, string deadEndId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM deja_vu_alerts
            WHERE thread_id = @threadId AND matched_dead_end_id = @deadEndId AND dismissed = 0
            """;
        cmd.Parameters.AddWithValue("@threadId", threadId);
        cmd.Parameters.AddWithValue("@deadEndId", deadEndId);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result) > 0;
    }

    private static async Task<IReadOnlyList<DejaVuAlert>> ReadAlerts(SqliteCommand cmd)
    {
        var results = new List<DejaVuAlert>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new DejaVuAlert
            {
                Id = reader.GetString(reader.GetOrdinal("id")),
                ThreadId = reader.GetString(reader.GetOrdinal("thread_id")),
                MatchedDeadEndId = reader.GetString(reader.GetOrdinal("matched_dead_end_id")),
                Confidence = reader.GetDouble(reader.GetOrdinal("confidence")),
                Message = reader.GetString(reader.GetOrdinal("message")),
                Strategy = Enum.Parse<MatchStrategy>(reader.GetString(reader.GetOrdinal("strategy"))),
                Dismissed = reader.GetInt32(reader.GetOrdinal("dismissed")) == 1,
                CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at")),
                    CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            });
        }
        return results;
    }
}
