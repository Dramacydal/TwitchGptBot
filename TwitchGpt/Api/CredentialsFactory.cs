using System.Collections.Concurrent;
using TwitchGpt.Database.Mappers;
using TwitchGpt.Entities;

namespace TwitchGpt.Api;

public static class CredentialsFactory
{
    private static ConcurrentDictionary<int, TwitchApiCredentials> _twitchBotCredentials = new();
    
    private static ConcurrentDictionary<string, TwitchApiCredentials> _twitchChannelCredentials = new();
    
    private static ConcurrentDictionary<string, BoostyApiCredentials> _boostyCredentials = new();

    public static async Task<TwitchApiCredentials?> GetTwitchBotCredentials(int botId)
    {
        if (_twitchBotCredentials.TryGetValue(botId, out var credentials))
            return credentials;

        credentials = await TwitchApiMapper.Instance.GetBotCredentials(botId);
        if (credentials == null)
            return null;

        _twitchBotCredentials[botId] = credentials;

        return credentials;
    }
    
    public static async Task<TwitchApiCredentials?> GetTwitchChannelCredentials(string channelId)
    {
        if (_twitchChannelCredentials.TryGetValue(channelId, out var credentials))
            return credentials;

        credentials = await TwitchApiMapper.Instance.GetChannelCredentials(channelId);
        if (credentials == null)
            return null;

        _twitchChannelCredentials[channelId] = credentials;

        return credentials;
    }

    public static async Task<BoostyApiCredentials> GetBoostyCredentials(string userName)
    {
        if (_boostyCredentials.TryGetValue(userName, out var credentials))
            return credentials;

        credentials = await BoostyApiMapper.Instance.GetCredentials(userName);
        if (credentials == null)
            return null;

        _boostyCredentials[userName] = credentials;

        return credentials;
    }

    public static async Task<bool> Reload(TwitchApiCredentials credentials)
    {
        var c = await TwitchApiMapper.Instance.GetChannelCredentials(credentials.ApiUserId);
        if (c == null)
            return false;

        credentials.AccessToken = c.AccessToken;
        credentials.Secret = c.Secret;
        credentials.ApiUserId = c.ApiUserId;
        credentials.ApiUserName = c.ApiUserName;
        credentials.ClientId = c.ClientId;

        return true;
    }

    public static async Task<bool> Reload(BoostyApiCredentials credentials)
    {
        var c = await BoostyApiMapper.Instance.GetCredentials(credentials.UserName);
        if (c == null)
            return false;

        credentials.AccessToken = c.AccessToken;
        credentials.UserId = c.UserId;
        credentials.DeviceId = c.DeviceId;
        credentials.AccessToken = c.AccessToken;
        credentials.RefreshToken = c.RefreshToken;
        credentials.ExpiresAt = c.ExpiresAt;

        return true;
    }
}