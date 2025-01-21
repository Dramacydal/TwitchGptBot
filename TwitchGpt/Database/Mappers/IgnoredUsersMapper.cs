using TwitchGpt.Database.Mappers.Abstraction;
using TwitchGpt.Database.Sql;

namespace TwitchGpt.Database.Mappers;

public class IgnoredUsersMapper : AbstractMapper<IgnoredUsersMapper, SqlConnection>
{
    public override SqlConnection Connection => SqlConnectionManager.GetConnection("gpt");

    public async Task<List<string>> GetIgnoredUsers()
    {
        List<string> ignoredUsers = new();
        await Connection.Query("SELECT `id` FROM `ignored_users` WHERE `enabled` = 1").ExecuteReaderAsync(r => ignoredUsers.Add(r.GetString(0)));

        return ignoredUsers;
    }
}
