using System.Collections.Concurrent;
using TwitchGpt.Database.Mappers;
using TwitchGpt.Gpt.Entities;
using TwitchGpt.Gpt.Enums;

namespace TwitchGpt.Gpt.Factories;

public static class ClientFactory
{
    private static RoleModel? _defaultRole;
    
    private static List<string>? _geminiTokens;

    private static readonly ConcurrentDictionary<ClientType, AiClient> clients = new();

    private static async Task<List<string>> GetOpenRouterKeysAsync()
    {
        if (_geminiTokens == null)
            _geminiTokens = await OpenrouterTokenMapper.Instance.GetOpenRouterKeyPool();

        return _geminiTokens;
    }

    public static async Task<RoleModel> GetDefaultRole()
    {
        if (_defaultRole != null)
            return _defaultRole;

        _defaultRole = await ModelFactory.Get("default")!;
        return _defaultRole!;
    }

    public static async Task<AiClient> CreateClient(ClientType type, string actorName)
    {
        // if (clients.TryGetValue(type, out var client))
        //     return client;

        var client = await AiClient.Create(type, actorName, await GetOpenRouterKeysAsync());
        clients[type] = client;

        if (type == ClientType.ChatWatcher)
            client.Role = await ModelFactory.Get("chat_watcher");

        return client;
    }
}
