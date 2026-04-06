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
            """;
        cmd.ExecuteNonQuery();
    }

    public static int GetSchemaVersion(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM _meta WHERE key = 'schema_version'";
        var result = cmd.ExecuteScalar();
        return result is string s ? int.Parse(s) : 0;
    }
}
