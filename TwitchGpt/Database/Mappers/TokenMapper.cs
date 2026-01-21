using TwitchGpt.Database.Mappers.Abstraction;
using TwitchGpt.Database.Sql;

namespace TwitchGpt.Database.Mappers;

public class TokenMapper : AbstractMapper<TokenMapper, SqlConnection>
{
    public override SqlConnection Connection => SqlConnectionManager.GetConnection("gpt");

    public async Task<List<string>> GetOpenRouterKeyPool()
    {
        List<string> tokens = [];
        await Connection.Query("SELECT * FROM openrouter_keys WHERE enabled = 1")
            .ExecuteReaderAsync(reader => tokens.Add(reader.GetString("token")));

        return tokens;
    }
}
