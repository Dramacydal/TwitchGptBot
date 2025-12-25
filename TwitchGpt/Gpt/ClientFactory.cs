using System.Collections.Concurrent;
using TwitchGpt.Database.Mappers;
using TwitchGpt.Gpt.Entities;
using TwitchGpt.Gpt.Enums;

namespace TwitchGpt.Gpt;

public static class ClientFactory
{
    private static RoleModel? _defaultRole;
    
    private static List<string>? _geminiTokens;

    private static readonly ConcurrentDictionary<ClientType, Client> clients = new();

    private static async Task<List<string>> GetGeminiTokensAsync()
    {
        if (_geminiTokens == null)
            _geminiTokens = await TokenMapper.Instance.GetGeminiTokenPool();

        return _geminiTokens;
    }

    public static async Task<RoleModel> GetDefaultRole()
    {
        if (_defaultRole != null)
            return _defaultRole;

        _defaultRole = await ModelFactory.Get("default")!;
        return _defaultRole!;
    }

    public static async Task<Client> CreateClient(ClientType type)
    {
        // if (clients.TryGetValue(type, out var client))
        //     return client;

        var client = await Client.Create(type, await GetGeminiTokensAsync());
        clients[type] = client;

        if (type == ClientType.ChatWatcher)
            client.Role = await ModelFactory.Get("chat_watcher");

        return client;
    }
}
