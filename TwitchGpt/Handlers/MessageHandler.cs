using System.Text.RegularExpressions;
using NLog;
using TwitchGpt.Config;
using TwitchGpt.Database.Mappers;
using TwitchGpt.Entities;
using TwitchGpt.Gpt;
using TwitchGpt.Gpt.Entities;
using TwitchGpt.Gpt.Factories;
using TwitchGpt.Gpt.Music;
using TwitchLib.Api.Helix.Models.Channels.ModifyChannelInformation;
using TwitchLib.Api.Helix.Models.Users.GetUsers;
using TwitchLib.Client.Models;

namespace TwitchGpt.Handlers;

public class MessageHandler
{
    private RoleModel _role;

    private readonly List<string> _admins = ConfigManager.GetPath<List<string>>("admins") ?? [];

    private bool _messageWatchEnabled = true;

    private bool _dialogsEnabled = true;

    private List<string> _ignoredUsers;

    private Dictionary<string, Game> _gamesById = [];

    private Dictionary<string, List<Game>> _gamesByName = [];

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

    public async Task HandleMessage(ChatMessage args)
    {
        var userId = args.UserId;
        var msg = args.Message;

        if (IsIgnoredUser(userId))
            return;

        var replyToPos = msg.ToLower().IndexOf($"@{_credentials.ApiUserName}".ToLower());
        if (replyToPos == 0)
        {
            msg = msg.Replace($"@{_credentials.ApiUserName}", "");
            _streamWatcher.MessagesProcessor.EnqueueDirectMessage(msg, args, _role);
        }

        if (replyToPos >= 0)
            return;

        if (msg.StartsWith("!"))
            return;

        if (_messageWatchEnabled)
            _streamWatcher.MessagesProcessor.AddMessageToLog(args);
    }

    public static bool IsSuspended { get; set; }

    public class BotCommandInfo
    {
        public string Name { get; set; }

        public List<string> ArgumentsAsList { get; set; } = [];

        public string ArgumentsAsString => string.Join(" ", ArgumentsAsList);
    }

    public class CommandContext
    {
        public string UserId { get; set; }
        public string UserName { get; set; }
        public Func<string, Task> Respond { get; set; }
    }
    
    public async Task HandleCommand(BotCommandInfo command, CommandContext msg)
    {
        if (IsSuspended && command.Name != "suspend")
            return;

        var messageUserId = msg.UserId;
        if (!string.IsNullOrEmpty(messageUserId) && IsIgnoredUser(messageUserId) && command.Name != "ignoreme")
            return;
        
        var isAdmin = () => string.IsNullOrEmpty(messageUserId) || IsAdmin(messageUserId);

        switch (command.Name)
        {
            case "suspend":
            {
                if (!isAdmin())
                    return;

                IsSuspended = !IsSuspended;
                await msg.Respond("Бот " + (IsSuspended ? "приостановлен" : "запущен"));
                break;
            }
            case "reset":
            {
                if (!isAdmin())
                    return;

                _streamWatcher.Reset();
                Logger.Info("Everything reset");
                break;
            }
            case "role":
            {
                if (!isAdmin())
                    return;

                if (string.IsNullOrEmpty(command.ArgumentsAsString))
                {
                    await msg.Respond($"Current role: '{_role.Name}'");
                    return;
                }

                if (await SetRole(command.ArgumentsAsString))
                    await msg.Respond($"Role changed to '{command.ArgumentsAsString}'");
                else
                    await msg.Respond($"Role not found");

                break;
            }
            case "reload":
            {
                if (!isAdmin())
                    return;

                await ModelFactory.Reload();
                _ignoredUsers = await IgnoredUsersMapper.Instance.GetIgnoredUsers(_channelUser.Id);
                Logger.Info("Everything reloaded");
                await _bot.ReloadAnnouncements();
                break;
            }
            // case "safety":
            // case "ss":
            // {
            //     if (!isAdmin())
            //         return;
            //
            //     await SendMessage("SS: " + JsonSerializer.Serialize(_role.SafetySettings));
            //     break;
            // }
            case "resolve":
            {
                var userNameOrId = command.ArgumentsAsString;
                if (string.IsNullOrEmpty(userNameOrId))
                    return;

                try
                {
                    var user = await ResolveUser(userNameOrId);
                    if (user != null)
                        await msg.Respond($"'{user.DisplayName}' ('{user.Login}', {user.Id})");
                    else
                        await msg.Respond($"Пользователь {userNameOrId} не найден");
                }
                catch (Exception ex)
                {
                    Logger.Error($"{ex.GetType()}: {ex.Message}");
                }

                break;
            }
            case "togglewatch":
            {
                if (!isAdmin())
                    return;

                _messageWatchEnabled = !_messageWatchEnabled;
                if (_streamWatcher.MessagesProcessor.ProcessPeriod <= 0)
                    _streamWatcher.MessagesProcessor.ProcessPeriod = 25;

                await msg.Respond($"Реакция на чат каждые {_streamWatcher.MessagesProcessor.ProcessPeriod} сек " +
                                  (_messageWatchEnabled ? "ON" : "OFF"));
                break;
            }
            case "watchperiod":
            {
                if (!isAdmin())
                    return;

                if (string.IsNullOrEmpty(command.ArgumentsAsString) ||
                    !int.TryParse(command.ArgumentsAsString, out var period))
                {
                    await msg.Respond($"Реакция на чат каждые {_streamWatcher.MessagesProcessor.ProcessPeriod} сек " +
                                      (_messageWatchEnabled ? "ON" : "OFF"));
                    return;
                }

                if (period == 0)
                {
                    period = _streamWatcher.MessagesProcessor.ProcessPeriod;
                    _messageWatchEnabled = false;
                }

                if (period < 10)
                    period = 10;

                _streamWatcher.MessagesProcessor.ProcessPeriod = period;

                await msg.Respond($"Реакция на чат каждые {period} сек " + (_messageWatchEnabled ? "ON" : "OFF"));
                break;
            }
            case "toggledialog":
            {
                if (!isAdmin())
                    return;

                _dialogsEnabled = !_dialogsEnabled;
                await msg.Respond($"Диалоги " + (_dialogsEnabled ? "ON" : "OFF"));
                break;
            }
            case "ignore":
            {
                if (!isAdmin())
                    return;

                if (string.IsNullOrEmpty(command.ArgumentsAsString))
                    return;

                var user = await ResolveUser(command.ArgumentsAsString);
                if (user == null)
                {
                    await msg.Respond("Пользователь не найдет");
                    return;
                }

                IgnoreUser(user.Id, user.Login);
                await msg.Respond($"Пользователь '{user.Login}' игнорируется");
                break;
            }
            case "ignoreme":
            {
                if (string.IsNullOrEmpty(msg.UserId))
                    break;
                if (ToggleIgnore(msg.UserId, msg.UserName))
                    await msg.Respond($"@{msg.UserName} теперь буду тебя игнорировать!");
                else
                    await msg.Respond($"@{msg.UserName}больше не буду тебя игнорировать!");
                break;
            }
            case "unignore":
            {
                if (!isAdmin())
                    return;

                if (string.IsNullOrEmpty(command.ArgumentsAsString))
                    return;

                var user = await ResolveUser(command.ArgumentsAsString);
                if (user == null)
                {
                    await msg.Respond("Пользователь не найдет");
                    return;
                }

                try
                {
                    UnIgnoreUser(user);
                    await msg.Respond($"Пользователь '{user.Login}' больше не игнорируется");
                }
                catch (Exception ex)
                {
                    Logger.Error($"{ex.GetType()}: {ex.Message}");
                }

                break;
            }
            case "snapshotcount":
            {
                if (!isAdmin())
                    return;

                if (string.IsNullOrEmpty(command.ArgumentsAsString))
                    await msg.Respond($"Количество: {AiMessagesProcessor.SnapshotHistoryCount}");
                else if (uint.TryParse(command.ArgumentsAsString, out var value))
                {
                    AiMessagesProcessor.SnapshotHistoryCount = (int)Math.Clamp(value, 1, 10);
                    await msg.Respond($"Количество установлено в {AiMessagesProcessor.SnapshotHistoryCount}");
                    return;
                }
                else
                    await msg.Respond("Некорректный параметр");

                return;
            }
            case "messagelogsize":
            {
                if (!isAdmin())
                    return;

                if (string.IsNullOrEmpty(command.ArgumentsAsString))
                    await msg.Respond($"Максимальный размер лога: {AiMessagesProcessor.MaxMessageLogSize}");
                else if (int.TryParse(command.ArgumentsAsString, out var histSize) && histSize > 0)
                {
                    AiMessagesProcessor.MaxMessageLogSize = histSize;
                    await msg.Respond($"Максимальный размер лога установлен в {histSize}");
                }
                else
                    await msg.Respond("Некорректный параметр");

                return;
            }
            case "category":
            {
                if (!isAdmin())
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
                    await msg.Respond("Категория не найдена");
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

                    await msg.Respond(string.Join(", ",
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
                    await msg.Respond($"Категория изменена на \"{game.Name}\"");
                }
                catch (Exception ex)
                {
                    await msg.Respond($"Ошибка изменения категории: {ex.Message}");
                }

                break;
            }
            case "model":
            {
                if (!isAdmin())
                    return;

                if (string.IsNullOrEmpty(command.ArgumentsAsString))
                {
                    await msg.Respond($"Current model: '{_streamWatcher.MessagesProcessor.AiClient.Model}'");
                    return;
                }

                try
                {
                    await _streamWatcher.MessagesProcessor.AiClient.SetModel(command.ArgumentsAsString);

                    await msg.Respond($"Model changed to '{command.ArgumentsAsString}'");
                }
                catch (Exception ex)
                {
                    await msg.Respond($"Model {command.ArgumentsAsString} not found");
                }
                break;
            }
            case "shazam":
            case "track":
            case "трек":
            case "шазам":
            {
                if (_trackRecognizer == null)
                    return;

                var chunks = _streamWatcher.RecentChunkPaths;
                if (chunks.Count == 0)
                {
                    await msg.Respond("Не удалось распознать трек");
                    return;
                }

                try
                {
                    var track = await _trackRecognizer.RecognizeAsync(chunks[^1]);
                    if (track == null)
                        await msg.Respond("Не удалось распознать трек");
                    else
                        await msg.Respond($"{track.Artist} - {track.Title}");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Shazam recognition error: {ex.Message}");
                    await msg.Respond("Не удалось распознать трек");
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

    private readonly Lock _ignoreLock = new();
    
    private readonly Bot _bot;
    
    private readonly TwitchApiCredentials _credentials;
    
    private readonly User _channelUser;

    private MessageHandler(Bot bot, TwitchApiCredentials credentials, User channelUser)
    {
        _bot = bot;
        _credentials = credentials;
        _channelUser = channelUser;
    }

    public static async Task<MessageHandler> Create(Bot bot, TwitchApiCredentials credentials, User channelUser)
    {
        var instance = new MessageHandler(bot, credentials, channelUser)
        {
            _ignoredUsers = await IgnoredUsersMapper.Instance.GetIgnoredUsers(channelUser.Id),
            _role = (await ModelFactory.Get("default"))!,
            _streamWatcher = await StreamWatcher.Create(bot, channelUser),
        };

        var rapidApiKeys = await RapidApiKeyMapper.Instance.GetRapidApiKeyPool();
        if (rapidApiKeys.Count > 0)
            instance._trackRecognizer = new ShazamClient(rapidApiKeys[0]);

        await instance.LoadGames();

        return instance;
    }

    private bool ToggleIgnore(string chatMessageUserId, string chatMessageUsername)
    {
        using var l = _ignoreLock.EnterScope();
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
        using var l = _ignoreLock.EnterScope();
        _ignoredUsers.RemoveAll(e => e == chatUser.Id);

        IgnoredUsersMapper.Instance.RemoveIgnoredUser(_channelUser.Id, chatUser.Id).Wait();
    }

    private void IgnoreUser(string userId, string userName)
    {
        // if (IsAdmin(userId))
        //     return;

        using var l = _ignoreLock.EnterScope();
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

    public async Task SendMessage(string text)
    {
        try
        {
            if (_bot.IsDryDun())
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

    private bool IsAdmin(string userId)
    {
        return userId == _channelUser.Id || _admins.Contains(userId);
    }

    private bool IsIgnoredUser(string userId)
    {
        using var l = _ignoreLock.EnterScope();

        return _ignoredUsers.Contains(userId);
    }

    public void SetWatchEnabled(bool on) => _messageWatchEnabled = on;
    
    public void SetDialogsEnabled(bool on) => _dialogsEnabled = on;
    
    private StreamWatcher _streamWatcher;
    private ITrackRecognizer? _trackRecognizer;

    public async Task RunAsync(CancellationToken token)
    {
        await _streamWatcher.RunAsync(token).ConfigureAwait(false);
    }
}
