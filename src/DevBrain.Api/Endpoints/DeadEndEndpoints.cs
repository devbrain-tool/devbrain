namespace DevBrain.Api.Endpoints;

using Microsoft.Data.Sqlite;

public static class DeadEndEndpoints
{
    public static void MapDeadEndEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/dead-ends");

        // List dead ends, filterable by project
        group.MapGet("/", (string? project, int? limit, SqliteConnection connection) =>
        {
            using var cmd = connection.CreateCommand();
            if (project is not null)
            {
                cmd.CommandText = "SELECT * FROM dead_ends WHERE project = @project ORDER BY detected_at DESC LIMIT @limit";
                cmd.Parameters.AddWithValue("@project", project);
            }
            else
            {
                cmd.CommandText = "SELECT * FROM dead_ends ORDER BY detected_at DESC LIMIT @limit";
            }
            cmd.Parameters.AddWithValue("@limit", limit ?? 50);

            var deadEnds = new List<Dictionary<string, object?>>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                deadEnds.Add(row);
            }

            return Results.Ok(deadEnds);
        });
    }
}
