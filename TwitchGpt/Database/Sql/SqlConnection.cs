using MySqlConnector;

namespace TwitchGpt.Database.Sql;

public class SqlConnection(SqlConfig config)
{
    private MySqlConnection CreateConnection()
    {
        var conn = new MySqlConnection(
            $"Server={config.Host};User ID={config.User};Password={config.Password};Database={config.Database};Port={config.Port}");
        conn.Open();
        return conn;
    }

    public SqlCommand Query(string text, Dictionary<string, object?> parameters)
    {
        var command = CreateConnection().CreateCommand();
        command.CommandText = text;

        foreach (var (key, value) in parameters)
            command.Parameters.AddWithValue(key, value);

        return new SqlCommand(command);
    }

    public SqlCommand Query(string text, params object[] parameters)
    {
        var command = CreateConnection().CreateCommand();
        command.CommandText = text;

        for (var i = 0; i < parameters.Length; ++i)
            command.Parameters.Add(new MySqlParameter(null, parameters[i]));

        return new SqlCommand(command);
    }
}