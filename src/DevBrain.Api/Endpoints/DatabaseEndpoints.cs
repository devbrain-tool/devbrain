namespace DevBrain.Api.Endpoints;

using Microsoft.Data.Sqlite;

public static class DatabaseEndpoints
{
    private static readonly HashSet<string> ExcludedSuffixes = ["_fts", "_content", "_docsize", "_data", "_idx", "_config"];

    public static void MapDatabaseEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/db");

        group.MapGet("/tables", (ReadOnlyDb db) => Results.Ok(ListTables(db)));
    }

    public static List<TableInfo> ListTables(ReadOnlyDb db)
    {
        var tables = new List<TableInfo>();
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";

        using var reader = cmd.ExecuteReader();
        var tableNames = new List<string>();
        while (reader.Read())
        {
            var name = reader.GetString(0);
            if (name.StartsWith("sqlite_") || name == "_meta")
                continue;
            if (ExcludedSuffixes.Any(suffix => name.EndsWith(suffix)) || name.EndsWith("_fts"))
                continue;
            tableNames.Add(name);
        }

        foreach (var name in tableNames)
        {
            using var countCmd = db.Connection.CreateCommand();
            countCmd.CommandText = $"SELECT COUNT(*) FROM \"{name}\"";
            var count = Convert.ToInt64(countCmd.ExecuteScalar());
            tables.Add(new TableInfo { Name = name, RowCount = count });
        }

        return tables;
    }

    public record TableInfo
    {
        public required string Name { get; init; }
        public required long RowCount { get; init; }
    }
}
