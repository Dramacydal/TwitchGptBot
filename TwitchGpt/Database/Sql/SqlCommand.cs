using MySqlConnector;

namespace TwitchGpt.Database.Sql;

public class SqlCommand(MySqlCommand command)
{
    public delegate void ReaderDelegate(MySqlDataReader reader);

    public async Task ExecuteReaderAsync(Action<MySqlDataReader> callback)
    {
        await using var reader = await command.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
            callback(reader);
    }

    public async Task<int> ExecuteNonQuery()
    {
        return await command.ExecuteNonQueryAsync();
    }
}
