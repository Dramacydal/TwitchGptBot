using TwitchGpt.Database.Mappers.Abstraction;
using TwitchGpt.Database.Sql;
using TwitchGpt.Entities;

namespace TwitchGpt.Database.Mappers;

public class TwitchApiMapper : AbstractMapper<TwitchApiMapper, SqlConnection>
{
    public override SqlConnection Connection => SqlConnectionManager.GetConnection("twitch");

    public SqlConnection GetConnection() => Connection;
    
    public async Task<TwitchApiCredentials?> GetBotCredentials(int botId)
    {
        TwitchApiCredentials? credentials = null;

        await Connection.Query("SELECT * FROM api_pool WHERE bot_id = ? AND enabled = 1", botId)
            .ExecuteReaderAsync(
                reader =>
                {
                    credentials = new()
                    {
                        BotId = reader.GetInt32("bot_id"),
                        ApiUserName = reader.GetString("api_user_name"),
                        ApiUserId = reader.GetString("api_user_id"),
                        ClientId = reader.GetString("client_id"),
                        Secret = reader.GetString("secret"),
                        AccessToken = reader.GetString("access_token"),
                    };
                });

        if (credentials == null)
            throw new Exception($"Credentials for bot {botId} do not exist or not enabled");

        return credentials;
    }
}