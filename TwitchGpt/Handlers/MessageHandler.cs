using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using NLog;
using TwitchGpt.Config;
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

    public async Task HandleMessage(ChatMessage args, GptWatcher gptWatcher)
    {
        var userId = args.UserId;
        var msg = args.Message;

        var replyToPos = msg.ToLower().IndexOf($"@{credentials.ApiUserName}".ToLower());
        if (replyToPos == 0)
        {
            UpdateDialogue(userId, true);

            msg = msg.Replace($"@{credentials.ApiUserName}", "");
            gptWatcher.dialogueProcessor.EnqueueDirectMessage(msg, args, _role);
        }
        else if (IsDialogueOpen(args.UserId))
        {
            UpdateDialogue(userId);
            gptWatcher.dialogueProcessor.EnqueueDirectMessage(msg, args, _role);
        }

        if (replyToPos >= 0)
            return;

        if (msg.StartsWith("!"))
            return;

        gptWatcher.messagesProcessor.EnqueueChatMessage(args);
    }

    public static bool IsSuspended { get; private set; }
    
    public async Task HandleCommand(ChatCommand args, GptWatcher gptWatcher)
    {
        if (IsSuspended && args.CommandText != "suspend")
            return;
        
        switch (args.CommandText)
        {
            case "suspend":
            {
                if (!IsAdmin(args.ChatMessage))
                    return;

                IsSuspended = !IsSuspended;
                break;
            }
            case "start":
            {
                UpdateDialogue(args.ChatMessage.UserId);
                if (!string.IsNullOrEmpty(args.ArgumentsAsString))
                    gptWatcher.dialogueProcessor.EnqueueDirectMessage(args.ArgumentsAsString, args.ChatMessage, _role);
                break;
            }
            case "stop":
            {
                RemoveDialog(args.ChatMessage.UserId);
                break;
            }
            case "ask":
            case "say":
            case "gpt":
            {
                UpdateDialogue(args.ChatMessage.UserId, true);
                gptWatcher.dialogueProcessor.EnqueueDirectMessage(args.ArgumentsAsString, args.ChatMessage, _role);
                break;
            }
            case "reset":
            {
                if (!IsAdmin(args.ChatMessage))
                    return;
                
                gptWatcher.Reset();
                break;
            }
            case "role":
            {
                if (!IsAdmin(args.ChatMessage))
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
                if (!IsAdmin(args.ChatMessage))
                    return;
                
                ModelFactory.Reload();
                break;
            }
            case "safety":
            case "ss":
            {
                if (!IsAdmin(args.ChatMessage))
                    return;
                
                SendMessage("SS: " + JsonSerializer.Serialize(_role.SafetySettings));
                break;
            }
            case "resolve":
            {
                var userNameOrId = args.ArgumentsAsString;
                try
                {
                    GetUsersResponse? response;
                    
                    if (Regex.IsMatch(userNameOrId, "^[0-9]+$"))
                        response = await bot.Api.Call(api => api.Helix.Users.GetUsersAsync(ids: [userNameOrId]));
                    else if (Regex.IsMatch(userNameOrId, "^[a-z0-9_]+$", RegexOptions.IgnoreCase))
                        response = await bot.Api.Call(api => api.Helix.Users.GetUsersAsync(logins: [userNameOrId.ToLower()]));
                    else
                    {
                        SendReply(args.ChatMessage, "bad user name or id");
                        return;
                    }

                    if (response?.Users.Length > 0)
                    {
                        var user = response.Users.FirstOrDefault();
                        SendReply(args.ChatMessage, $"'{user.DisplayName}' ('{user.Login}', {user.Id})");
                    }
                    else
                        SendReply(args.ChatMessage, "user not found");
                }
                catch (Exception ex)
                {
                    Logger.Error($"{ex.GetType()}: {ex.Message}");
                }

                break;
            }
        }
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

    private bool IsAdmin(ChatMessage chatMessage)
    {
        return chatMessage.UserId == user.Id || _admins.Contains(user.Id);
    }
}