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
                title TEXT,
                started_at TEXT NOT NULL,
                last_activity TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'active'
            );

            CREATE TABLE IF NOT EXISTS dead_ends (
                id TEXT PRIMARY KEY,
                observation_id TEXT NOT NULL,
                project TEXT NOT NULL,
                description TEXT NOT NULL,
                reason TEXT,
                created_at TEXT NOT NULL,
                FOREIGN KEY (observation_id) REFERENCES observations(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS graph_nodes (
                id TEXT PRIMARY KEY,
                project TEXT NOT NULL,
                label TEXT NOT NULL,
                node_type TEXT NOT NULL,
                metadata TEXT NOT NULL DEFAULT '{}',
                created_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS graph_edges (
                id TEXT PRIMARY KEY,
                source_id TEXT NOT NULL,
                target_id TEXT NOT NULL,
                edge_type TEXT NOT NULL,
                weight REAL NOT NULL DEFAULT 1.0,
                metadata TEXT NOT NULL DEFAULT '{}',
                created_at TEXT NOT NULL,
                FOREIGN KEY (source_id) REFERENCES graph_nodes(id) ON DELETE CASCADE,
                FOREIGN KEY (target_id) REFERENCES graph_nodes(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_observations_project ON observations(project);
            CREATE INDEX IF NOT EXISTS idx_observations_event_type ON observations(event_type);
            CREATE INDEX IF NOT EXISTS idx_observations_thread_id ON observations(thread_id);
            CREATE INDEX IF NOT EXISTS idx_observations_timestamp ON observations(timestamp);
            CREATE INDEX IF NOT EXISTS idx_observations_summary ON observations(summary);
            CREATE INDEX IF NOT EXISTS idx_dead_ends_project ON dead_ends(project);
            CREATE INDEX IF NOT EXISTS idx_graph_nodes_project ON graph_nodes(project);
            CREATE INDEX IF NOT EXISTS idx_graph_edges_source ON graph_edges(source_id);
            CREATE INDEX IF NOT EXISTS idx_graph_edges_target ON graph_edges(target_id);

            CREATE VIRTUAL TABLE IF NOT EXISTS observations_fts USING fts5(
                id UNINDEXED,
                raw_content,
                summary,
                content='observations',
                content_rowid='rowid'
            );

            CREATE TRIGGER IF NOT EXISTS observations_ai AFTER INSERT ON observations BEGIN
                INSERT INTO observations_fts(rowid, id, raw_content, summary)
                VALUES (new.rowid, new.id, new.raw_content, COALESCE(new.summary, ''));
            END;

            CREATE TRIGGER IF NOT EXISTS observations_ad AFTER DELETE ON observations BEGIN
                INSERT INTO observations_fts(observations_fts, rowid, id, raw_content, summary)
                VALUES ('delete', old.rowid, old.id, old.raw_content, COALESCE(old.summary, ''));
            END;

            CREATE TRIGGER IF NOT EXISTS observations_au AFTER UPDATE ON observations BEGIN
                INSERT INTO observations_fts(observations_fts, rowid, id, raw_content, summary)
                VALUES ('delete', old.rowid, old.id, old.raw_content, COALESCE(old.summary, ''));
                INSERT INTO observations_fts(rowid, id, raw_content, summary)
                VALUES (new.rowid, new.id, new.raw_content, COALESCE(new.summary, ''));
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
