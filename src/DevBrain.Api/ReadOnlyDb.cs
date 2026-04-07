namespace DevBrain.Api;

using Microsoft.Data.Sqlite;

public sealed class ReadOnlyDb(SqliteConnection connection) : IDisposable
{
    public SqliteConnection Connection => connection;

    public void Dispose() => connection.Dispose();
}
