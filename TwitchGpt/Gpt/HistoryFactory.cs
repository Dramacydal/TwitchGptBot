using System.Collections.Concurrent;
using TwitchGpt.Gpt.Entities;
using TwitchGpt.Gpt.Enums;

namespace TwitchGpt.Gpt;

public static class HistoryFactory
{
    private static ConcurrentDictionary<ClientType, HistoryHolder> _histories = new();

    public static HistoryHolder Create(ClientType clientType)
    {
        if (_histories.TryGetValue(clientType, out var holder))
            return holder;

        holder = new HistoryHolder();
        _histories[clientType] = holder;

        return holder;
    }
}