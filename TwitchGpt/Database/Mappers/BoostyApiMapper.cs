using TwitchGpt.Database.Mappers.Abstraction;
using TwitchGpt.Database.Sql;
using TwitchGpt.Entities;

namespace TwitchGpt.Database.Mappers;

public class BoostyApiMapper : AbstractMapper<BoostyApiMapper, SqlConnection>
{
    public override SqlConnection Connection =>  SqlConnectionManager.GetConnection("boosty");
    
    public async Task<BoostyApiCredentials?> GetCredentials(string userName)
    {
        BoostyApiCredentials? credentials = null;

        await Connection.Query("SELECT * FROM api_pool WHERE user_name = ? AND enabled = 1", userName)
            .ExecuteReaderAsync(
                reader =>
                {
                    credentials = new()
                    {
                        Id = reader.GetInt32("id"),
                        UserName = reader.GetString("user_name"),
                        UserId = reader.GetInt64("user_id"),
                        DeviceId = reader.GetString("device_id"),
                        AccessToken = reader.GetString("access_token"),
                        RefreshToken = reader.GetString("refresh_token"),
                        ExpiresAt = reader.GetInt64("expires_at"),
                    };
                });

        if (credentials == null)
            throw new Exception($"Api credentials for user {userName} do not exist or not enabled");

        return credentials;
    }
}