namespace DevBrain.Api.Endpoints;

using DevBrain.Core.Interfaces;
using Microsoft.Data.Sqlite;

public static class ContextEndpoints
{
    public static void MapContextEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/context");

        // Aggregated view for a file: graph relations + dead ends mentioning the file
        group.MapGet("/file/{*path}", async (string path, IGraphStore graph, SqliteConnection connection) =>
        {
            var relatedNodes = await graph.GetRelatedToFile(path);

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM dead_ends WHERE files_involved LIKE @pattern ORDER BY detected_at DESC LIMIT 20";
            cmd.Parameters.AddWithValue("@pattern", $"%{path}%");

            var deadEnds = new List<Dictionary<string, object?>>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                deadEnds.Add(row);
            }

            return Results.Ok(new { graphNodes = relatedNodes, deadEnds });
        });
    }
}
