using System.Collections.Concurrent;
using TwitchGpt.Database.Mappers;
using TwitchGpt.Entities;

namespace TwitchGpt.Api;

public static class CredentialsFactory
{
    private static ConcurrentDictionary<int, ApiCredentials> _credentials = new();

    public static async Task<ApiCredentials> GetByBotId(int botId)
    {
        if (_credentials.TryGetValue(botId, out var credentials))
            return credentials;

        credentials = await ApiPoolMapper.Instance.GetBotCredentials(botId);
        if (credentials == null)
            return null;

        _credentials[botId] = credentials;

        return credentials;
    }

    public static async Task<bool> Reload(ApiCredentials credentials)
    {
        var c = await ApiPoolMapper.Instance.GetBotCredentials(credentials.BotId);
        if (c == null)
            return false;

        credentials.AccessToken = c.AccessToken;
        credentials.Secret = c.Secret;
        credentials.ApiUserId = c.ApiUserId;
        credentials.ApiUserName = c.ApiUserName;
        credentials.ClientId = c.ClientId;

        return true;
    }
}