using System.Globalization;
using System.Text.Json;
using DevBrain.Core.Interfaces;
using DevBrain.Core.Models;
using Microsoft.Data.Sqlite;

namespace DevBrain.Storage;

public class SqliteGraphStore : IGraphStore
{
    private readonly SqliteConnection _connection;

    public SqliteGraphStore(SqliteConnection connection)
    {
        _connection = connection;
    }

    public async Task<GraphNode> AddNode(string type, string name, object? data = null, string? sourceId = null)
    {
        var id = Guid.NewGuid().ToString();
        var dataJson = data is null ? null : data is string s ? s : JsonSerializer.Serialize(data);
        var now = DateTime.UtcNow;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO graph_nodes (id, type, name, data, source_id, created_at)
            VALUES (@id, @type, @name, @data, @sourceId, @createdAt)";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@type", type);
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@data", (object?)dataJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sourceId", (object?)sourceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", now.ToString("yyyy-MM-dd HH:mm:ss"));

        await cmd.ExecuteNonQueryAsync();

        return new GraphNode
        {
            Id = id,
            Type = type,
            Name = name,
            Data = dataJson,
            SourceId = sourceId,
            CreatedAt = now
        };
    }

    public async Task<GraphNode?> GetNode(string id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, type, name, data, source_id, created_at FROM graph_nodes WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadNode(reader);
        }
        return null;
    }

    public async Task<IReadOnlyList<GraphNode>> GetNodesByType(string type)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, type, name, data, source_id, created_at FROM graph_nodes WHERE type = @type";
        cmd.Parameters.AddWithValue("@type", type);

        var nodes = new List<GraphNode>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            nodes.Add(ReadNode(reader));
        }
        return nodes;
    }

    public async Task<GraphNode?> GetNodeBySourceId(string sourceId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, type, name, data, source_id, created_at FROM graph_nodes WHERE source_id = @sourceId LIMIT 1";
        cmd.Parameters.AddWithValue("@sourceId", sourceId);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            return ReadNode(reader);
        return null;
    }

    public async Task RemoveNode(string id)
    {
        // Delete all connected edges first (cascade)
        using var edgeCmd = _connection.CreateCommand();
        edgeCmd.CommandText = "DELETE FROM graph_edges WHERE source_id = @id OR target_id = @id";
        edgeCmd.Parameters.AddWithValue("@id", id);
        await edgeCmd.ExecuteNonQueryAsync();

        using var nodeCmd = _connection.CreateCommand();
        nodeCmd.CommandText = "DELETE FROM graph_nodes WHERE id = @id";
        nodeCmd.Parameters.AddWithValue("@id", id);
        await nodeCmd.ExecuteNonQueryAsync();
    }

    public async Task<GraphEdge> AddEdge(string sourceId, string targetId, string type, object? data = null)
    {
        var id = Guid.NewGuid().ToString();
        var dataJson = data is null ? null : data is string s ? s : JsonSerializer.Serialize(data);
        var now = DateTime.UtcNow;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO graph_edges (id, source_id, target_id, type, data, weight, created_at)
            VALUES (@id, @sourceId, @targetId, @type, @data, 1.0, @createdAt)";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@sourceId", sourceId);
        cmd.Parameters.AddWithValue("@targetId", targetId);
        cmd.Parameters.AddWithValue("@type", type);
        cmd.Parameters.AddWithValue("@data", (object?)dataJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", now.ToString("yyyy-MM-dd HH:mm:ss"));

        await cmd.ExecuteNonQueryAsync();

        return new GraphEdge
        {
            Id = id,
            SourceId = sourceId,
            TargetId = targetId,
            Type = type,
            Data = dataJson,
            Weight = 1.0,
            CreatedAt = now
        };
    }

    public async Task RemoveEdge(string id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM graph_edges WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<GraphNode>> GetNeighbors(string nodeId, int hops = 1, string? edgeType = null)
    {
        var edgeFilter = edgeType is not null
            ? "AND e.type = @edgeType"
            : "";

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $@"
            WITH RECURSIVE neighbors(node_id, depth, path) AS (
                SELECT @startId, 0, @startId
                UNION
                SELECT
                    CASE WHEN e.source_id = n.node_id THEN e.target_id ELSE e.source_id END,
                    n.depth + 1,
                    n.path || ',' || CASE WHEN e.source_id = n.node_id THEN e.target_id ELSE e.source_id END
                FROM neighbors n
                JOIN graph_edges e ON (e.source_id = n.node_id OR e.target_id = n.node_id)
                    {edgeFilter}
                WHERE n.depth < @hops
                    AND instr(n.path, CASE WHEN e.source_id = n.node_id THEN e.target_id ELSE e.source_id END) = 0
            )
            SELECT DISTINCT gn.id, gn.type, gn.name, gn.data, gn.source_id, gn.created_at
            FROM neighbors nb
            JOIN graph_nodes gn ON gn.id = nb.node_id
            WHERE nb.node_id != @startId";

        cmd.Parameters.AddWithValue("@startId", nodeId);
        cmd.Parameters.AddWithValue("@hops", hops);
        if (edgeType is not null)
            cmd.Parameters.AddWithValue("@edgeType", edgeType);

        var nodes = new List<GraphNode>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            nodes.Add(ReadNode(reader));
        }
        return nodes;
    }

    public async Task<IReadOnlyList<GraphNode>> GetNeighbors(string nodeId, int hops, IReadOnlyList<string> edgeTypes)
    {
        if (edgeTypes.Count == 0)
            return await GetNeighbors(nodeId, hops);

        var edgeParams = new List<string>();
        for (int i = 0; i < edgeTypes.Count; i++)
            edgeParams.Add($"@et{i}");
        var inClause = $"AND e.type IN ({string.Join(", ", edgeParams)})";

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $@"
            WITH RECURSIVE neighbors(node_id, depth, path) AS (
                SELECT @startId, 0, @startId
                UNION
                SELECT
                    CASE WHEN e.source_id = n.node_id THEN e.target_id ELSE e.source_id END,
                    n.depth + 1,
                    n.path || ',' || CASE WHEN e.source_id = n.node_id THEN e.target_id ELSE e.source_id END
                FROM neighbors n
                JOIN graph_edges e ON (e.source_id = n.node_id OR e.target_id = n.node_id)
                    {inClause}
                WHERE n.depth < @hops
                    AND instr(n.path, CASE WHEN e.source_id = n.node_id THEN e.target_id ELSE e.source_id END) = 0
            )
            SELECT DISTINCT gn.id, gn.type, gn.name, gn.data, gn.source_id, gn.created_at
            FROM neighbors nb
            JOIN graph_nodes gn ON gn.id = nb.node_id
            WHERE nb.node_id != @startId";

        cmd.Parameters.AddWithValue("@startId", nodeId);
        cmd.Parameters.AddWithValue("@hops", hops);
        for (int i = 0; i < edgeTypes.Count; i++)
            cmd.Parameters.AddWithValue($"@et{i}", edgeTypes[i]);

        var nodes = new List<GraphNode>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            nodes.Add(ReadNode(reader));
        }
        return nodes;
    }

    public async Task<IReadOnlyList<GraphPath>> FindPaths(string fromId, string toId, int maxDepth = 4)
    {
        // Recursive CTE to find all paths (directional: source→target only)
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            WITH RECURSIVE paths(current_id, node_path, edge_path, depth) AS (
                SELECT @fromId, @fromId, '', 0
                UNION ALL
                SELECT
                    e.target_id,
                    p.node_path || ',' || e.target_id,
                    CASE WHEN p.edge_path = '' THEN e.id ELSE p.edge_path || ',' || e.id END,
                    p.depth + 1
                FROM paths p
                JOIN graph_edges e ON e.source_id = p.current_id
                WHERE p.depth < @maxDepth
                    AND instr(p.node_path, e.target_id) = 0
            )
            SELECT node_path, edge_path
            FROM paths
            WHERE current_id = @toId AND depth > 0";

        cmd.Parameters.AddWithValue("@fromId", fromId);
        cmd.Parameters.AddWithValue("@toId", toId);
        cmd.Parameters.AddWithValue("@maxDepth", maxDepth);

        var results = new List<(string nodePath, string edgePath)>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add((reader.GetString(0), reader.GetString(1)));
        }

        var paths = new List<GraphPath>();
        foreach (var (nodePath, edgePath) in results)
        {
            var nodeIds = nodePath.Split(',');
            var edgeIds = edgePath.Split(',');

            var nodes = new List<GraphNode>();
            foreach (var nid in nodeIds)
            {
                var node = await GetNode(nid);
                if (node is not null) nodes.Add(node);
            }

            var edges = new List<GraphEdge>();
            foreach (var eid in edgeIds)
            {
                var edge = await GetEdgeById(eid);
                if (edge is not null) edges.Add(edge);
            }

            paths.Add(new GraphPath { Nodes = nodes, Edges = edges });
        }

        return paths;
    }

    public async Task<IReadOnlyList<GraphNode>> GetRelatedToFile(string filePath)
    {
        // Find File node by name
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id FROM graph_nodes WHERE type = 'File' AND name = @name LIMIT 1";
        cmd.Parameters.AddWithValue("@name", filePath);

        var nodeId = (string?)await cmd.ExecuteScalarAsync();
        if (nodeId is null) return Array.Empty<GraphNode>();

        return await GetNeighbors(nodeId, hops: 2);
    }

    public async Task Clear()
    {
        using var edgeCmd = _connection.CreateCommand();
        edgeCmd.CommandText = "DELETE FROM graph_edges";
        await edgeCmd.ExecuteNonQueryAsync();

        using var nodeCmd = _connection.CreateCommand();
        nodeCmd.CommandText = "DELETE FROM graph_nodes";
        await nodeCmd.ExecuteNonQueryAsync();
    }

    private async Task<GraphEdge?> GetEdgeById(string id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, source_id, target_id, type, data, weight, created_at FROM graph_edges WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new GraphEdge
            {
                Id = reader.GetString(0),
                SourceId = reader.GetString(1),
                TargetId = reader.GetString(2),
                Type = reader.GetString(3),
                Data = reader.IsDBNull(4) ? null : reader.GetString(4),
                Weight = reader.GetDouble(5),
                CreatedAt = DateTime.Parse(reader.GetString(6), CultureInfo.InvariantCulture)
            };
        }
        return null;
    }

    private static GraphNode ReadNode(SqliteDataReader reader)
    {
        return new GraphNode
        {
            Id = reader.GetString(0),
            Type = reader.GetString(1),
            Name = reader.GetString(2),
            Data = reader.IsDBNull(3) ? null : reader.GetString(3),
            SourceId = reader.IsDBNull(4) ? null : reader.GetString(4),
            CreatedAt = DateTime.Parse(reader.GetString(5), CultureInfo.InvariantCulture)
        };
    }
}
