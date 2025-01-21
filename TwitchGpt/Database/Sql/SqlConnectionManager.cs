using System.Collections.Concurrent;
using TwitchGpt.Config;

namespace TwitchGpt.Database.Sql;

public static class SqlConnectionManager
{
    private static readonly ConcurrentDictionary<string, SqlConnection> Connections = new ();

    public static SqlConnection GetConnection(string section)
    {
        if (Connections.TryGetValue(section, out var result))
            return result;

        var conn = new SqlConnection(ConfigManager.GetPath<SqlConfig>($"db.{section}")!);
        Connections[section] = conn;

        return conn;
    }
}