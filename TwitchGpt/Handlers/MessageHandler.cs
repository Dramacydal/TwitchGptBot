using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using GptLib;
using NLog;
using TwitchGpt.Config;
using TwitchGpt.Database.Mappers;
using TwitchGpt.Entities;
using TwitchGpt.Gpt;
using TwitchGpt.Gpt.Entities;
using TwitchLib.Api.Helix.Models.Users.GetUsers;
using TwitchLib.Client.Models;

namespace TwitchGpt.Handlers;

public class MessageHandler(Bot bot, ApiCredentials credentials, User user)
{
    private readonly ConcurrentDictionary<string, DateTime> _openDialogues = new();
    
    private RoleModel _role = ModelFactory.Get("default").Result!;

    private readonly List<string> _admins = ConfigManager.GetPath<List<string>>("admins") ?? [];

    private bool _globalChatEnabled;
    
    private List<string> _ignoredUsers = IgnoredUsersMapper.Instance.GetIgnoredUsers(user.Id).Result;

    public async Task HandleMessage(ChatMessage args, GptWatcher gptWatcher)
    {
        var userId = args.UserId;
        var msg = args.Message;

        if (IsIgnoredUser(userId))
            return;
        
        var replyToPos = msg.ToLower().IndexOf($"@{credentials.ApiUserName}".ToLower());
        if (replyToPos == 0)
        {
            UpdateDialogue(userId, true);

            msg = msg.Replace($"@{credentials.ApiUserName}", "");
            gptWatcher.dialogueProcessor.EnqueueDirectMessage(msg, args, _role);
        }
        else if (IsDialogueOpen(userId))
        {
            UpdateDialogue(userId);
            gptWatcher.dialogueProcessor.EnqueueDirectMessage(msg, args, _role);
        }

        if (replyToPos >= 0)
            return;

        if (msg.StartsWith("!"))
            return;

        if (_globalChatEnabled)
            gptWatcher.messagesProcessor.EnqueueChatMessage(args);
    }

    public static bool IsSuspended { get; private set; }

    public async Task HandleCommand(ChatCommand args, GptWatcher gptWatcher)
    {
        if (IsSuspended && args.CommandText != "suspend")
            return;

        var messageUserId = args.ChatMessage.UserId;
        if (IsIgnoredUser(messageUserId))
            return;

        switch (args.CommandText)
        {
            case "suspend":
            {
                if (!IsAdmin(messageUserId))
                    return;

                IsSuspended = !IsSuspended;
                SendReply(args.ChatMessage, "Бот " + (IsSuspended ? "приостановлен" : "запущен"));
                break;
            }
            case "start":
            {
                UpdateDialogue(messageUserId);
                if (!string.IsNullOrEmpty(args.ArgumentsAsString))
                    gptWatcher.dialogueProcessor.EnqueueDirectMessage(args.ArgumentsAsString, args.ChatMessage, _role);
                break;
            }
            case "stop":
            {
                RemoveDialog(messageUserId);
                break;
            }
            case "ask":
            case "say":
            case "gpt":
            {
                UpdateDialogue(messageUserId, true);
                gptWatcher.dialogueProcessor.EnqueueDirectMessage(args.ArgumentsAsString, args.ChatMessage, _role);
                break;
            }
            case "reset":
            {
                if (!IsAdmin(messageUserId))
                    return;

                gptWatcher.Reset();
                break;
            }
            case "role":
            {
                if (!IsAdmin(messageUserId))
                    return;

                if (string.IsNullOrEmpty(args.ArgumentsAsString))
                {
                    SendMessage($"Current role: '{_role.Name}'");
                    return;
                }

                if (await SetRole(args.ArgumentsAsString))
                    SendMessage($"Role changed to '{args.ArgumentsAsString}'");
                else
                    SendMessage($"Role not found");

                break;
            }
            case "reload":
            {
                if (!IsAdmin(messageUserId))
                    return;

                await ModelFactory.Reload();
                _ignoredUsers = await IgnoredUsersMapper.Instance.GetIgnoredUsers(user.Id);
                break;
            }
            case "safety":
            case "ss":
            {
                if (!IsAdmin(messageUserId))
                    return;

                SendMessage("SS: " + JsonSerializer.Serialize(_role.SafetySettings));
                break;
            }
            case "resolve":
            {
                var userNameOrId = args.ArgumentsAsString;
                if (string.IsNullOrEmpty(userNameOrId))
                    return;
                
                try
                {
                    var user = await ResolveUser(userNameOrId);
                    if (user != null)
                        SendReply(args.ChatMessage, $"'{user.DisplayName}' ('{user.Login}', {user.Id})");
                    else
                        SendReply(args.ChatMessage, "Пользователь не найден");
                }
                catch (Exception ex)
                {
                    Logger.Error($"{ex.GetType()}: {ex.Message}");
                }

                break;
            }
            case "togglewatch":
            {
                if (!IsAdmin(messageUserId))
                    return;

                _globalChatEnabled = !_globalChatEnabled;
                if (gptWatcher.messagesProcessor.ProcessPeriod <= 0)
                    gptWatcher.messagesProcessor.ProcessPeriod = 25;
                
                SendMessage($"Реакция на чат каждые {gptWatcher.messagesProcessor.ProcessPeriod} сек " + (_globalChatEnabled ? "ON" : "OFF"));
                break;
            }
            case "watchperiod":
            {
                if (!IsAdmin(messageUserId))
                    return;

                if (string.IsNullOrEmpty(args.ArgumentsAsString) ||
                    !int.TryParse(args.ArgumentsAsString, out var period))
                {
                    SendMessage($"Реакция на чат каждые {gptWatcher.messagesProcessor.ProcessPeriod} сек " + (_globalChatEnabled ? "ON" : "OFF"));
                    return;
                }

                if (period == 0)
                {
                    period = gptWatcher.messagesProcessor.ProcessPeriod;
                    _globalChatEnabled = false;
                }

                if (period < 10)
                    period = 10;
                
                gptWatcher.messagesProcessor.ProcessPeriod = period;
                
                SendMessage($"Реакция на чат каждые {period} сек " + (_globalChatEnabled ? "ON" : "OFF"));
                break;
            }
            case "ignore":
            {
                if (!IsAdmin(messageUserId))
                    return;

                if (string.IsNullOrEmpty(args.ArgumentsAsString))
                    return;

                var user = await ResolveUser(args.ArgumentsAsString);
                if (user == null)
                {
                    SendMessage("Пользователь не найдет");
                    return;
                }

                await IgnoreUser(user);
                SendMessage($"Пользователь '{user.Login}' игнорируется");
                break;
            }
            case "unignore":
            {
                if (!IsAdmin(messageUserId))
                    return;

                if (string.IsNullOrEmpty(args.ArgumentsAsString))
                    return;

                var user = await ResolveUser(args.ArgumentsAsString);
                if (user == null)
                {
                    SendMessage("Пользователь не найдет");
                    return;
                }

                try
                {
                    await UnIgnoreUser(user);
                    SendMessage($"Пользователь '{user.Login}' больше не игнорируется");
                }
                catch (Exception ex)
                {
                    Logger.Error($"{ex.GetType()}: {ex.Message}");
                }

                break;
            }
        }
    }

    private async Task<User?> ResolveUser(string userNameOrId)
    {
        GetUsersResponse? response;

        if (Regex.IsMatch(userNameOrId, "^[0-9]+$"))
            response = await bot.Api.Call(api => api.Helix.Users.GetUsersAsync(ids: [userNameOrId]));
        else if (Regex.IsMatch(userNameOrId, "^[a-z0-9_]+$", RegexOptions.IgnoreCase))
            response = await bot.Api.Call(api => api.Helix.Users.GetUsersAsync(logins: [userNameOrId.ToLower()]));
        else
            return null;

        return response.Users.FirstOrDefault();
    }

    private Locker ignoreLock = new();

    private async Task UnIgnoreUser(User chatUser)
    {
        // using var l = ignoreLock.Acquire();
        _ignoredUsers.RemoveAll(e => e == chatUser.Id);

        await IgnoredUsersMapper.Instance.RemoveIgnoredUser(user.Id, chatUser.Id);
    }

    private async Task IgnoreUser(User chatUser)
    {
        if (IsAdmin(chatUser.Id))
            return;

        // using var l = ignoreLock.Acquire();
        if (_ignoredUsers.Contains(chatUser.Id))
            return;

        _ignoredUsers.Add(chatUser.Id);
        await IgnoredUsersMapper.Instance.InsertIgnoredUser(user.Id, chatUser.Id, chatUser.Login);
    }

    private async Task<bool> SetRole(string name)
    {
        var role = await ModelFactory.Get(name);
        if (role != null)
        {
            if (!role.Scopes.Contains("chat_answer"))
                Logger.Error("Tried to set wrong scope model");

            _role = role;
            return true;
        }

        return false;
    }
    
    private ILogger Logger => Logging.Logger.Instance(credentials.ApiUserName);

    private void SendReply(ChatMessage msg, string text)
    {
        SendMessage($"@{msg.Username}: {text}");
    }

    private void SendMessage(string text)
    {
        try
        {
            bot.Client.SendMessage(user.Login, text);
        }
        catch (Exception ex)
        {
            Logger.Error($"Error sending chat message: {ex.Message}");
            Logger.Error(text);
        }
    }

    private void UpdateDialogue(string userId, bool ifOpen = false)
    {
        if (ifOpen && !_openDialogues.ContainsKey(userId))
            return;

        _openDialogues[userId] = DateTime.Now;
    }

    private void RemoveDialog(string chatMessageUserId)
    {
        _openDialogues.TryRemove(chatMessageUserId, out _);
    }

    private bool IsDialogueOpen(string userId)
    {
        if (_openDialogues.TryGetValue(userId, out var date))
            return DateTime.Now - date <= TimeSpan.FromSeconds(15);

        return false;
    }

    private bool IsAdmin(string userId)
    {
        return userId == user.Id || _admins.Contains(userId);
    }

    private bool IsIgnoredUser(string userId)
    {
        // using var l = ignoreLock.Acquire();

        return _ignoredUsers.Contains(userId);
    }
}