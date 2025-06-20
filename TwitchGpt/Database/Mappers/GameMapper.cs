using TwitchGpt.Database.Mappers.Abstraction;
using TwitchGpt.Database.Sql;
using TwitchGpt.Entities;

namespace TwitchGpt.Database.Mappers;

public class GameMapper : AbstractMapper<GameMapper, SqlConnection>
{
    public override SqlConnection Connection => SqlConnectionManager.GetConnection("twitch");

    private readonly string _collectionName = "announcements";

    public async Task<List<Game>> GetGames()
    {
        List<Game> ignoredUsers = new();
        await Connection.Query("SELECT `id`, `name` FROM `games`")
            .ExecuteReaderAsync(r =>
            {
                ignoredUsers.Add(new()
                {
                    Id = r.GetString("id"),  
                    Name = r.GetString("name"),  
                });
            });

        return ignoredUsers;
    }
}
