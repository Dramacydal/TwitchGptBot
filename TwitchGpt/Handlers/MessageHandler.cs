using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using NLog;
using TwitchGpt.Config;
using TwitchGpt.Database.Mappers;
using TwitchGpt.Entities;
using TwitchGpt.Gpt;
using TwitchGpt.Gpt.Abstraction;
using TwitchGpt.Gpt.Entities;
using TwitchGpt.Helpers;
using TwitchLib.Api.Helix.Models.Channels.ModifyChannelInformation;
using TwitchLib.Api.Helix.Models.Users.GetUsers;
using TwitchLib.Client.Models;

namespace TwitchGpt.Handlers;

public class MessageHandler
{
    private readonly ConcurrentDictionary<string, DateTime> _openDialogues = new();

    private RoleModel _role = ModelFactory.Get("default").Result!;

    private readonly List<string> _admins = ConfigManager.GetPath<List<string>>("admins") ?? [];

    private bool _messageWatchEnabled = true;

    private bool _dialogsEnabled = true;

    private List<string> _ignoredUsers;

    private Dictionary<string, Game> _gamesById;
    
    private Dictionary<string, List<Game>> _gamesByName;

    private async Task LoadGames()
    {
        var games = await GameMapper.Instance.GetGames();

        Dictionary<string, List<Game>> result = new();
        _gamesById = new();
        foreach (var game in games)
        {
            var normalized = NormalizeGameName(game.Name);

            if (result.TryGetValue(normalized, out var gamesList))
                gamesList.Add(game);
            else
                result[normalized] = [game];

            _gamesById[game.Id] = game;
        }

        _gamesByName = result;
    }

    private static string NormalizeGameName(string gameName)
    {
        return Regex.Replace(gameName, @"[^a-zа-я0-9]", string.Empty, RegexOptions.IgnoreCase).ToLowerInvariant();
    }

    public async Task HandleMessage(ChatMessage args, GptWatcher gptWatcher)
    {
        var userId = args.UserId;
        var msg = args.Message;

        if (IsIgnoredUser(userId))
            return;

        var replyToPos = msg.ToLower().IndexOf($"@{_credentials.ApiUserName}".ToLower());
        if (replyToPos == 0)
        {
            UpdateDialogue(userId, true);

            msg = msg.Replace($"@{_credentials.ApiUserName}", "");
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

        if (_messageWatchEnabled)
            gptWatcher.messagesProcessor.EnqueueChatMessage(args);
    }

    public static bool IsSuspended { get; private set; }

    public async Task HandleCommand(CommandInfo command, ChatMessage msg, GptWatcher gptWatcher)
    {
        if (IsSuspended && command.Name != "suspend")
            return;

        var messageUserId = msg.UserId;
        if (IsIgnoredUser(messageUserId) && command.Name != "ignoreme")
            return;

        switch (command.Name)
        {
            case "suspend":
            {
                if (!IsAdmin(messageUserId))
                    return;

                IsSuspended = !IsSuspended;
                await SendReply(msg, "Бот " + (IsSuspended ? "приостановлен" : "запущен"));
                break;
            }
            case "start":
            {
                UpdateDialogue(messageUserId);
                if (!string.IsNullOrEmpty(command.ArgumentsAsString))
                    gptWatcher.dialogueProcessor.EnqueueDirectMessage(command.ArgumentsAsString, msg, _role);
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
                gptWatcher.dialogueProcessor.EnqueueDirectMessage(command.ArgumentsAsString, msg, _role);
                break;
            }
            case "reset":
            {
                if (!IsAdmin(messageUserId))
                    return;

                gptWatcher.Reset();
                Logger.Info("Everything reset");
                break;
            }
            case "role":
            {
                if (!IsAdmin(messageUserId))
                    return;

                if (string.IsNullOrEmpty(command.ArgumentsAsString))
                {
                    await SendMessage($"Current role: '{_role.Name}'");
                    return;
                }

                if (await SetRole(command.ArgumentsAsString))
                    await SendMessage($"Role changed to '{command.ArgumentsAsString}'");
                else
                    await SendMessage($"Role not found");

                break;
            }
            case "reload":
            {
                if (!IsAdmin(messageUserId))
                    return;

                await ModelFactory.Reload();
                _ignoredUsers = await IgnoredUsersMapper.Instance.GetIgnoredUsers(_channelUser.Id);
                Logger.Info("Everything reloaded");
                await _bot.ReloadAnnouncements();
                break;
            }
            case "safety":
            case "ss":
            {
                if (!IsAdmin(messageUserId))
                    return;

                await SendMessage("SS: " + JsonSerializer.Serialize(_role.SafetySettings));
                break;
            }
            case "resolve":
            {
                var userNameOrId = command.ArgumentsAsString;
                if (string.IsNullOrEmpty(userNameOrId))
                    return;

                try
                {
                    var user = await ResolveUser(userNameOrId);
                    if (user != null)
                        await SendReply(msg, $"'{user.DisplayName}' ('{user.Login}', {user.Id})");
                    else
                        await SendReply(msg, "Пользователь не найден");
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

                _messageWatchEnabled = !_messageWatchEnabled;
                if (gptWatcher.messagesProcessor.ProcessPeriod <= 0)
                    gptWatcher.messagesProcessor.ProcessPeriod = 25;

                await SendMessage($"Реакция на чат каждые {gptWatcher.messagesProcessor.ProcessPeriod} сек " +
                            (_messageWatchEnabled ? "ON" : "OFF"));
                break;
            }
            case "watchperiod":
            {
                if (!IsAdmin(messageUserId))
                    return;

                if (string.IsNullOrEmpty(command.ArgumentsAsString) ||
                    !int.TryParse(command.ArgumentsAsString, out var period))
                {
                    await SendMessage($"Реакция на чат каждые {gptWatcher.messagesProcessor.ProcessPeriod} сек " +
                                (_messageWatchEnabled ? "ON" : "OFF"));
                    return;
                }

                if (period == 0)
                {
                    period = gptWatcher.messagesProcessor.ProcessPeriod;
                    _messageWatchEnabled = false;
                }

                if (period < 10)
                    period = 10;

                gptWatcher.messagesProcessor.ProcessPeriod = period;

                await SendMessage($"Реакция на чат каждые {period} сек " + (_messageWatchEnabled ? "ON" : "OFF"));
                break;
            }
            case "toggledialog":
            {
                if (!IsAdmin(messageUserId))
                    return;

                _dialogsEnabled = !_dialogsEnabled;
                await SendMessage($"Диалоги " + (_dialogsEnabled ? "ON" : "OFF"));
                break;
            }
            case "ignore":
            {
                if (!IsAdmin(messageUserId))
                    return;

                if (string.IsNullOrEmpty(command.ArgumentsAsString))
                    return;

                var user = await ResolveUser(command.ArgumentsAsString);
                if (user == null)
                {
                    await SendMessage("Пользователь не найдет");
                    return;
                }

                IgnoreUser(user.Id, user.Login);
                await SendMessage($"Пользователь '{user.Login}' игнорируется");
                break;
            }
            case "ignoreme":
            {
                if (ToggleIgnore(msg.UserId, msg.Username))
                    await SendReply(msg.Username, "теперь буду тебя игнорировать!");
                else
                    await SendReply(msg.Username, "больше не буду тебя игнорировать!");
                break;
            }
            case "unignore":
            {
                if (!IsAdmin(messageUserId))
                    return;

                if (string.IsNullOrEmpty(command.ArgumentsAsString))
                    return;

                var user = await ResolveUser(command.ArgumentsAsString);
                if (user == null)
                {
                    await SendMessage("Пользователь не найдет");
                    return;
                }

                try
                {
                    UnIgnoreUser(user);
                    await SendMessage($"Пользователь '{user.Login}' больше не игнорируется");
                }
                catch (Exception ex)
                {
                    Logger.Error($"{ex.GetType()}: {ex.Message}");
                }

                break;
            }
            case "snapshotcount":
            {
                if (!IsAdmin(messageUserId))
                    return;

                if (string.IsNullOrEmpty(command.ArgumentsAsString))
                    await SendMessage($"Количество: {AbstractProcessor.SnapshotHistoryCount}");
                else if (uint.TryParse(command.ArgumentsAsString, out var value))
                {
                    AbstractProcessor.SnapshotHistoryCount = (int)Math.Clamp(value, 1, 10);
                    await SendMessage($"Количество установлено в {AbstractProcessor.SnapshotHistoryCount}");
                    return;
                }
                else
                    await SendMessage("Некорректный параметр");
                return;
            }
            case "category":
            {
                if (!IsAdmin(messageUserId))
                    return;

                if (command.ArgumentsAsList.Count < 2)
                    return;

                var type = command.ArgumentsAsList[0].ToLowerInvariant();
                if (type != "set" && type != "search")
                    return;

                var gameNamePart = string.Join(" ", command.ArgumentsAsList.Skip(1));

                var games = LookupGames(gameNamePart).ToList();
                if (games.Count == 0)
                {
                    await SendMessage("Категория не найдена");
                    return;
                }

                var exact = games.FirstOrDefault(g => g.Exact);

                if (exact == null && games.Count != 1 || type == "search")
                {
                    var variants = games.Where(g => exact == null || exact.Game.Id != g.Game.Id).Take(5).ToList();

                    var exactStr = exact != null ? $"Точное совпадение: \"{exact.Game.Name}\" ({exact.Game.Id})" : "";
                    var variantsStr = "";
                    if (variants.Count > 0)
                    {
                        var gameCnt = games.Count;
                        if (exact != null)
                            --gameCnt;
                        if (gameCnt > 5)
                            variantsStr = $"Первые {variants.Count} из {gameCnt}";
                        else
                            variantsStr = $"Найдено {variants.Count} категорий";
                        
                        variantsStr += $": " + string.Join(", ",
                            variants.Select(v => $"\"{v.Game.Name}\" ({v.Game.Id})"));
                    }

                    await SendMessage(string.Join(", ",
                        new[] { exactStr, variantsStr }.Where(s => !string.IsNullOrEmpty(s))));
                    return;
                }

                var game = exact != null ? exact.Game : games.First().Game;

                try
                {
                    await _bot.TwitchApi.Call(api => api.Helix.Channels.ModifyChannelInformationAsync(_channelUser.Id,
                        new ModifyChannelInformationRequest()
                        {
                            GameId = game.Id
                        }));
                    await SendMessage($"Категория изменена на \"{game.Name}\"");
                }
                catch (Exception ex)
                {
                    await SendMessage($"Ошибка изменения категории: {ex.Message}");
                }
                break;
            }
        }
    }

    class GameMatch
    {
        public bool Exact;

        public Game Game;
    }

    private IEnumerable<GameMatch> LookupGames(string gameNamePart)
    {
        if (uint.TryParse(gameNamePart, out _))
        {
            if (_gamesById.TryGetValue(gameNamePart, out var game))
            {
                return
                [
                    new GameMatch()
                    {
                        Exact = true,
                        Game = game
                    }
                ];
            }
        }

        if (true)
        {
            var games = _gamesById.Where(g =>
            {
                var parts = gameNamePart.Split(' ').Select(p => Regex.Escape(p));

                var regexp = string.Join(@".* .*", parts);

                return Regex.IsMatch(g.Value.Name, regexp, RegexOptions.IgnoreCase);
            });

            return games.Select(g => new GameMatch()
                {
                    Exact = NormalizeGameName(g.Value.Name) == NormalizeGameName(gameNamePart),
                    Game = g.Value
                }
            );
        }
        else
        {
            var games = _gamesByName.Where(g => g.Key.Contains(NormalizeGameName(gameNamePart)));

            return games.SelectMany(g =>
            {
                return g.Value.Select(g2 => new GameMatch()
                    {
                        Exact = NormalizeGameName(gameNamePart) == g.Key,
                        Game = g2
                    }
                );
            });
        }
    }

    private async Task<User?> ResolveUser(string userNameOrId)
    {
        GetUsersResponse? response;

        if (Regex.IsMatch(userNameOrId, "^[0-9]+$"))
            response = await _bot.TwitchApi.Call(api => api.Helix.Users.GetUsersAsync(ids: [userNameOrId]));
        else if (Regex.IsMatch(userNameOrId, "^[a-z0-9_]+$", RegexOptions.IgnoreCase))
            response = await _bot.TwitchApi.Call(api => api.Helix.Users.GetUsersAsync(logins: [userNameOrId.ToLower()]));
        else
            return null;

        return response.Users.FirstOrDefault();
    }

    private Locker ignoreLock = new();
    
    private readonly Bot _bot;
    
    private readonly TwitchApiCredentials _credentials;
    
    private readonly User _channelUser;

    public MessageHandler(Bot bot, TwitchApiCredentials credentials, User channelUser)
    {
        _bot = bot;
        _credentials = credentials;
        _channelUser = channelUser;
        _ignoredUsers = IgnoredUsersMapper.Instance.GetIgnoredUsers(channelUser.Id).Result;

        LoadGames().Wait();
    }

    private bool ToggleIgnore(string chatMessageUserId, string chatMessageUsername)
    {
        using var l = ignoreLock.Acquire();
        if (_ignoredUsers.Any(u => u == chatMessageUserId))
        {
            _ignoredUsers.Remove(chatMessageUserId);
            IgnoredUsersMapper.Instance.RemoveIgnoredUser(_channelUser.Id, chatMessageUserId).Wait();
            return false;
        }
        else
        {
            _ignoredUsers.Add(chatMessageUserId);
            IgnoredUsersMapper.Instance.InsertIgnoredUser(_channelUser.Id, chatMessageUserId, chatMessageUsername)
                .Wait();
            return true;
        }
    }

    private void UnIgnoreUser(User chatUser)
    {
        using var l = ignoreLock.Acquire();
        _ignoredUsers.RemoveAll(e => e == chatUser.Id);

        IgnoredUsersMapper.Instance.RemoveIgnoredUser(_channelUser.Id, chatUser.Id).Wait();
    }

    private void IgnoreUser(string userId, string userName)
    {
        // if (IsAdmin(userId))
        //     return;

        using var l = ignoreLock.Acquire();
        if (_ignoredUsers.Contains(userId))
            return;

        _ignoredUsers.Add(userId);
        IgnoredUsersMapper.Instance.InsertIgnoredUser(_channelUser.Id, userId, userName).Wait();
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

    private ILogger Logger => Logging.Logger.Instance(_credentials.ApiUserName);

    private async Task SendReply(ChatMessage msg, string text)
    {
        await SendReply(msg.Username, text);
    }
    
    private async Task SendReply(string userName, string text)
    {
        await SendMessage($"@{userName} {text}");
    }

    private async Task SendMessage(string text)
    {
        try
        {
            if (_bot.GetMessagesToLog())
                Logger.Trace($">> {text}");
            else
                await _bot.Client.SendMessageAsync(_channelUser.Login, text);
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
        return userId == _channelUser.Id || _admins.Contains(userId);
    }

    private bool IsIgnoredUser(string userId)
    {
        using var l = ignoreLock.Acquire();

        return _ignoredUsers.Contains(userId);
    }

    public void SetWatchEnabled(bool on) => _messageWatchEnabled = on;
    
    public void SetDialogsEnabled(bool on) => _dialogsEnabled = on;
}
