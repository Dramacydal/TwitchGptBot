using System.Collections.Concurrent;
using TwitchGpt.Database.Mappers;
using TwitchGpt.Gpt.Entities;
using TwitchGpt.Gpt.Enums;

namespace TwitchGpt.Gpt;

public static class ClientFactory
{
    private static List<string> _geminiTokens = new();

    private static ConcurrentDictionary<ClientType, Client> clients = new();

    static ClientFactory()
    {
        _geminiTokens = TokenMapper.Instance.GetGeminiTokenPool().Result;
    }

    public static readonly RoleModel DefaultRole  = ModelFactory.Get("default").Result!;

    public static async Task<Client> CreateClient(ClientType type)
    {
        // if (clients.TryGetValue(type, out var client))
        //     return client;

        var client = new Client(type, _geminiTokens);
        clients[type] = client;

        if (type == ClientType.ChatWatcher)
            client.Role = await ModelFactory.Get("chat_watcher");

        return client;
    }
}
