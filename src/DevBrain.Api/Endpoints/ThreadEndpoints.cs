namespace DevBrain.Api.Endpoints;

using DevBrain.Core.Interfaces;
using Microsoft.Data.Sqlite;

public static class ThreadEndpoints
{
    public static void MapThreadEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/threads");

        // List threads with optional project filter
        group.MapGet("/", (string? project, int? limit, SqliteConnection connection) =>
        {
            using var cmd = connection.CreateCommand();
            if (project is not null)
            {
                cmd.CommandText = "SELECT * FROM threads WHERE project = @project ORDER BY last_activity DESC LIMIT @limit";
                cmd.Parameters.AddWithValue("@project", project);
            }
            else
            {
                cmd.CommandText = "SELECT * FROM threads ORDER BY last_activity DESC LIMIT @limit";
            }
            cmd.Parameters.AddWithValue("@limit", limit ?? 50);

            var threads = new List<Dictionary<string, object?>>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                threads.Add(row);
            }

            return Results.Ok(threads);
        });

        // Get thread by id with its observations
        group.MapGet("/{id}", async (string id, SqliteConnection connection, IObservationStore store) =>
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM threads WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return Results.NotFound(new { error = "Thread not found" });

            var thread = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
                thread[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);

            var observations = await store.Query(new ObservationFilter { ThreadId = id, Limit = 200 });
            return Results.Ok(new { thread, observations });
        });
    }
}
