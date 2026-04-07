namespace DevBrain.Api.Endpoints;

using Microsoft.Data.Sqlite;

public static class DatabaseEndpoints
{
    private static readonly HashSet<string> ExcludedSuffixes = ["_fts", "_content", "_docsize", "_data", "_idx", "_config"];

    public static void MapDatabaseEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/db");

        group.MapGet("/tables", (ReadOnlyDb db) => Results.Ok(ListTables(db)));

        group.MapGet("/tables/{name}", (string name, ReadOnlyDb db) =>
        {
            var detail = GetTableDetail(db, name);
            return detail is not null ? Results.Ok(detail) : Results.NotFound();
        });

        group.MapPost("/query", (QueryRequest request, ReadOnlyDb db) =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var result = ExecuteQuery(db, request.Sql, cts.Token);
                return Results.Ok(result);
            }
            catch (OperationCanceledException)
            {
                return Results.BadRequest(new { error = "Query timed out after 30 seconds" });
            }
            catch (SqliteException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
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
            if (ExcludedSuffixes.Any(suffix => name.EndsWith(suffix)))
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

    public static TableDetail? GetTableDetail(ReadOnlyDb db, string tableName)
    {
        // Validate table exists in sqlite_master (prevents injection)
        using var checkCmd = db.Connection.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name";
        checkCmd.Parameters.AddWithValue("@name", tableName);
        if (Convert.ToInt64(checkCmd.ExecuteScalar()) == 0)
            return null;

        // Row count
        using var countCmd = db.Connection.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM \"{tableName}\"";
        var rowCount = Convert.ToInt64(countCmd.ExecuteScalar());

        // Columns via PRAGMA
        var columns = new List<ColumnInfo>();
        using var colCmd = db.Connection.CreateCommand();
        colCmd.CommandText = $"PRAGMA table_info(\"{tableName}\")";
        using var colReader = colCmd.ExecuteReader();
        while (colReader.Read())
        {
            columns.Add(new ColumnInfo
            {
                Name = colReader.GetString(1),       // name
                Type = colReader.GetString(2),       // type
                Nullable = colReader.GetInt32(3) == 0, // notnull (0 = nullable)
                PrimaryKey = colReader.GetInt32(5) > 0  // pk
            });
        }

        // Indexes via PRAGMA
        var indexes = new List<string>();
        using var idxCmd = db.Connection.CreateCommand();
        idxCmd.CommandText = $"PRAGMA index_list(\"{tableName}\")";
        using var idxReader = idxCmd.ExecuteReader();
        while (idxReader.Read())
        {
            var idxName = idxReader.GetString(1);
            if (!idxName.StartsWith("sqlite_"))
                indexes.Add(idxName);
        }

        return new TableDetail
        {
            Name = tableName,
            RowCount = rowCount,
            Columns = columns,
            Indexes = indexes
        };
    }

    public static QueryResult ExecuteQuery(ReadOnlyDb db, string sql, CancellationToken ct = default)
    {
        const int maxRows = 1000;

        var sw = System.Diagnostics.Stopwatch.StartNew();

        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = sql;

        using var reader = cmd.ExecuteReader();

        var columns = new List<string>();
        for (var i = 0; i < reader.FieldCount; i++)
            columns.Add(reader.GetName(i));

        var rows = new List<List<object?>>();
        while (reader.Read() && rows.Count < maxRows)
        {
            ct.ThrowIfCancellationRequested();
            var row = new List<object?>();
            for (var i = 0; i < reader.FieldCount; i++)
                row.Add(reader.IsDBNull(i) ? null : reader.GetValue(i));
            rows.Add(row);
        }

        sw.Stop();

        return new QueryResult
        {
            Columns = columns,
            Rows = rows,
            RowCount = rows.Count,
            ExecutionMs = sw.ElapsedMilliseconds
        };
    }

    public record QueryRequest
    {
        public required string Sql { get; init; }
    }

    public record QueryResult
    {
        public required List<string> Columns { get; init; }
        public required List<List<object?>> Rows { get; init; }
        public required int RowCount { get; init; }
        public required long ExecutionMs { get; init; }
    }

    public record TableInfo
    {
        public required string Name { get; init; }
        public required long RowCount { get; init; }
    }

    public record TableDetail
    {
        public required string Name { get; init; }
        public required long RowCount { get; init; }
        public required List<ColumnInfo> Columns { get; init; }
        public required List<string> Indexes { get; init; }
    }

    public record ColumnInfo
    {
        public required string Name { get; init; }
        public required string Type { get; init; }
        public required bool PrimaryKey { get; init; }
        public required bool Nullable { get; init; }
    }
}
