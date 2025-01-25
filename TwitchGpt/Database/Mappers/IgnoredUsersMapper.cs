using TwitchGpt.Database.Mappers.Abstraction;
using TwitchGpt.Database.Sql;

namespace TwitchGpt.Database.Mappers;

public class IgnoredUsersMapper : AbstractMapper<IgnoredUsersMapper, SqlConnection>
{
    public override SqlConnection Connection => SqlConnectionManager.GetConnection("gpt");

    public async Task<List<string>> GetIgnoredUsers(string channelId)
    {
        List<string> ignoredUsers = new();
        await Connection.Query("SELECT `id` FROM `ignored_users` WHERE `enabled` = 1 AND (`channel_id` = ? OR `channel_id` = '')", [channelId])
            .ExecuteReaderAsync(r => ignoredUsers.Add(r.GetString(0)));

        return ignoredUsers;
    }

    public async Task InsertIgnoredUser(string channelId, string userId, string userName)
    {
        await Connection.Query(
            "REPLACE INTO `ignored_users` (`id`, `channel_id`, `name`, `enabled`) VALUES (?, ?, ?, ?)",
            [userId, channelId, userName, 1]).ExecuteNonQuery();
    }

    public async Task RemoveIgnoredUser(string channelId, string userId)
    {
        await Connection.Query(
            "DELETE FROM `ignored_users` WHERE `channel_id` = ? AND `id` = ?",
            [channelId, userId]).ExecuteNonQuery();
    }
}
