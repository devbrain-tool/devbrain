namespace DevBrain.Storage.Schema;

using Microsoft.Data.Sqlite;

public static class SchemaManager
{
    private const int CurrentSchemaVersion = 1;

    public static void Initialize(SqliteConnection connection)
    {
        using var pragmaCmd = connection.CreateCommand();
        pragmaCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        pragmaCmd.ExecuteNonQuery();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS _meta (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            INSERT OR IGNORE INTO _meta (key, value) VALUES ('schema_version', '1');

            CREATE TABLE IF NOT EXISTS observations (
                id TEXT PRIMARY KEY,
                session_id TEXT NOT NULL,
                thread_id TEXT,
                parent_id TEXT,
                timestamp TEXT NOT NULL,
                project TEXT NOT NULL,
                branch TEXT,
                event_type TEXT NOT NULL,
                source TEXT NOT NULL,
                raw_content TEXT NOT NULL,
                summary TEXT,
                tags TEXT NOT NULL DEFAULT '[]',
                files_involved TEXT NOT NULL DEFAULT '[]',
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS threads (
                id TEXT PRIMARY KEY,
                project TEXT NOT NULL,
                branch TEXT,
                title TEXT,
                state TEXT NOT NULL DEFAULT 'Active',
                started_at TEXT NOT NULL,
                last_activity TEXT NOT NULL,
                observation_count INTEGER DEFAULT 0,
                summary TEXT,
                created_at TEXT DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS dead_ends (
                id TEXT PRIMARY KEY,
                thread_id TEXT REFERENCES threads(id),
                project TEXT NOT NULL,
                description TEXT NOT NULL,
                approach TEXT NOT NULL,
                reason TEXT NOT NULL,
                files_involved TEXT,
                detected_at TEXT NOT NULL,
                created_at TEXT DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS graph_nodes (
                id TEXT PRIMARY KEY,
                type TEXT NOT NULL,
                name TEXT NOT NULL,
                data TEXT,
                source_id TEXT,
                created_at TEXT DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS graph_edges (
                id TEXT PRIMARY KEY,
                source_id TEXT NOT NULL REFERENCES graph_nodes(id),
                target_id TEXT NOT NULL REFERENCES graph_nodes(id),
                type TEXT NOT NULL,
                data TEXT,
                weight REAL DEFAULT 1.0,
                created_at TEXT DEFAULT (datetime('now'))
            );

            CREATE INDEX IF NOT EXISTS idx_obs_thread ON observations(thread_id);
            CREATE INDEX IF NOT EXISTS idx_obs_session ON observations(session_id);
            CREATE INDEX IF NOT EXISTS idx_obs_project ON observations(project);
            CREATE INDEX IF NOT EXISTS idx_obs_timestamp ON observations(timestamp);
            CREATE INDEX IF NOT EXISTS idx_obs_event_type ON observations(event_type);
            CREATE INDEX IF NOT EXISTS idx_de_project ON dead_ends(project);
            CREATE INDEX IF NOT EXISTS idx_ge_source ON graph_edges(source_id);
            CREATE INDEX IF NOT EXISTS idx_ge_target ON graph_edges(target_id);
            CREATE INDEX IF NOT EXISTS idx_ge_type ON graph_edges(type);
            CREATE INDEX IF NOT EXISTS idx_gn_type ON graph_nodes(type);
            CREATE INDEX IF NOT EXISTS idx_gn_source ON graph_nodes(source_id);

            CREATE VIRTUAL TABLE IF NOT EXISTS observations_fts USING fts5(
                summary,
                raw_content,
                tags,
                content=observations,
                content_rowid=rowid
            );

            CREATE TRIGGER IF NOT EXISTS observations_ai AFTER INSERT ON observations BEGIN
                INSERT INTO observations_fts(rowid, summary, raw_content, tags)
                VALUES (new.rowid, new.summary, new.raw_content, new.tags);
            END;

            CREATE TRIGGER IF NOT EXISTS observations_ad AFTER DELETE ON observations BEGIN
                INSERT INTO observations_fts(observations_fts, rowid, summary, raw_content, tags)
                VALUES ('delete', old.rowid, old.summary, old.raw_content, old.tags);
            END;

            CREATE TRIGGER IF NOT EXISTS observations_au AFTER UPDATE ON observations BEGIN
                INSERT INTO observations_fts(observations_fts, rowid, summary, raw_content, tags)
                VALUES ('delete', old.rowid, old.summary, old.raw_content, old.tags);
                INSERT INTO observations_fts(rowid, summary, raw_content, tags)
                VALUES (new.rowid, new.summary, new.raw_content, new.tags);
            END;

            CREATE TABLE IF NOT EXISTS deja_vu_alerts (
                id TEXT PRIMARY KEY,
                thread_id TEXT NOT NULL,
                matched_dead_end_id TEXT NOT NULL,
                confidence REAL NOT NULL,
                message TEXT NOT NULL,
                strategy TEXT NOT NULL,
                dismissed INTEGER DEFAULT 0,
                created_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_dva_dedup ON deja_vu_alerts(thread_id, matched_dead_end_id);
            CREATE INDEX IF NOT EXISTS idx_dva_active ON deja_vu_alerts(dismissed);

            CREATE TABLE IF NOT EXISTS session_summaries (
                id TEXT PRIMARY KEY,
                session_id TEXT NOT NULL UNIQUE,
                narrative TEXT NOT NULL,
                outcome TEXT NOT NULL,
                duration_seconds INTEGER NOT NULL,
                observation_count INTEGER NOT NULL,
                files_touched INTEGER NOT NULL,
                dead_ends_hit INTEGER NOT NULL,
                phases TEXT NOT NULL,
                created_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_ss_session ON session_summaries(session_id);

            CREATE TABLE IF NOT EXISTS developer_metrics (
                id TEXT PRIMARY KEY,
                dimension TEXT NOT NULL,
                value REAL NOT NULL,
                period_start TEXT NOT NULL,
                period_end TEXT NOT NULL,
                created_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_dm_dimension ON developer_metrics(dimension, period_start);

            CREATE TABLE IF NOT EXISTS milestones (
                id TEXT PRIMARY KEY,
                type TEXT NOT NULL,
                description TEXT NOT NULL,
                achieved_at TEXT NOT NULL,
                observation_id TEXT,
                created_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_ms_type ON milestones(type, achieved_at);

            CREATE TABLE IF NOT EXISTS growth_reports (
                id TEXT PRIMARY KEY,
                period_start TEXT NOT NULL,
                period_end TEXT NOT NULL,
                metrics TEXT NOT NULL,
                milestones TEXT NOT NULL,
                narrative TEXT,
                generated_at TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();

        MigrateToV2(connection);
    }

    private static void MigrateToV2(SqliteConnection connection)
    {
        using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "PRAGMA table_info(observations)";
        using var reader = checkCmd.ExecuteReader();
        var columns = new HashSet<string>();
        while (reader.Read())
            columns.Add(reader.GetString(1));

        if (columns.Contains("metadata"))
            return;

        using var tx = connection.BeginTransaction();
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                ALTER TABLE observations ADD COLUMN metadata TEXT NOT NULL DEFAULT '{}';
                ALTER TABLE observations ADD COLUMN tool_name TEXT;
                ALTER TABLE observations ADD COLUMN outcome TEXT;
                ALTER TABLE observations ADD COLUMN duration_ms INTEGER;
                ALTER TABLE observations ADD COLUMN turn_number INTEGER;

                CREATE INDEX IF NOT EXISTS idx_obs_tool_name ON observations(tool_name);
                CREATE INDEX IF NOT EXISTS idx_obs_outcome ON observations(outcome);
                """;
            cmd.ExecuteNonQuery();

            using var versionCmd = connection.CreateCommand();
            versionCmd.Transaction = tx;
            versionCmd.CommandText = "UPDATE _meta SET value = '2' WHERE key = 'schema_version'";
            versionCmd.ExecuteNonQuery();

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public static int GetSchemaVersion(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM _meta WHERE key = 'schema_version'";
        var result = cmd.ExecuteScalar();
        return result is string s ? int.Parse(s) : 0;
    }
}
