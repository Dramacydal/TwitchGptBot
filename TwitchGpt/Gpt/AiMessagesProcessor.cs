using System.Collections.Concurrent;
using NLog;
using TwitchGpt.Config;
using TwitchGpt.Exceptions;
using TwitchGpt.Gpt.Entities;
using TwitchGpt.Gpt.Enums;
using TwitchGpt.Gpt.Factories;
using TwitchGpt.Handlers;
using TwitchLib.Api.Helix.Models.Users.GetUsers;
using TwitchLib.Client.Models;

namespace TwitchGpt.Gpt;

public class AiMessagesProcessor
{
    public static int SnapshotHistoryCount = 3;

    private DateTime _skipProcessingTime = DateTime.Now;

    private readonly Bot _bot;

    private readonly User _channelUser;

    public AiClient AiClient { get; private set; }

    private ConcurrentQueue<ChatMessageData> _messageLog = new();

    private ConcurrentQueue<Tuple<string, ChatMessage, RoleModel>> _directMessages = new();

    public AbstractStreamInfo?[] _streamInfos;

    private AiMessagesProcessor(Bot bot, User channelUser)
    {
        _bot = bot;
        _channelUser = channelUser;
        ProcessPeriod = ConfigManager.GetPath<int>("message_process_period");
    }

    public static async Task<AiMessagesProcessor> Create(Bot bot, User channelUser, params AbstractStreamInfo?[] streamInfos)
    {
        return new AiMessagesProcessor(bot, channelUser)
        {
            AiClient = await ClientFactory.CreateClient(ClientType.ChatWatcher, bot.BotUserName),
            _streamInfos = streamInfos
        };
    }

    public void AddMessageToLog(ChatMessage message, bool isBot = false) => _messageLog.Enqueue(new()
    {
        Date = message.TmiSent,
        UserName = message.Username,
        Message = message.Message,
        IsBot = isBot,
    });

    public void EnqueueDirectMessage(string text, ChatMessage chatMessage, RoleModel role) =>
        _directMessages.Enqueue(new(text, chatMessage, role));

    public int ProcessPeriod { get; set; }

    protected void DelayProcessing(TimeSpan delay) => _skipProcessingTime = DateTime.Now.Add(delay);

    protected bool IsProcessingDelayed => _skipProcessingTime > DateTime.Now;

    public async Task Run(CancellationToken token)
    {
        await Task.WhenAll(RunMessageWatcher(token), RunReplyWatcher(token));
    }

    private async Task RunMessageWatcher(CancellationToken token)
    {
        Logger.Info($"{nameof(RunMessageWatcher)} started");
        for (; !token.IsCancellationRequested;)
        {
            if (MessageHandler.IsSuspended || ProcessPeriod <= 0 || IsProcessingDelayed)
            {
                await Task.Delay(200);
                continue;
            }

            if (_messageLog.Count == 0)
            {
                DelayProcessing(TimeSpan.FromSeconds(ProcessPeriod));
                continue;
            }

            var allMessages = _messageLog.ToList();
            var lastBotIndex = allMessages.FindLastIndex(m => m.IsBot);

            var contextMessages = lastBotIndex >= 0 ? allMessages.Take(lastBotIndex + 1).ToList() : [];
            var newMessages = lastBotIndex >= 0 ? allMessages.Skip(lastBotIndex + 1).ToList() : allMessages;

            if (newMessages.Count == 0)
            {
                DelayProcessing(TimeSpan.FromSeconds(ProcessPeriod));
                continue;
            }

            static string FormatMessage(ChatMessageData m) => $"[{m.Date:yyyy-MM-dd HH:mm:ss}] [{m.UserName}]: {m.Message}";

            var formatted = contextMessages.Count > 0
                ? $"Контекст (уже обработано, не реагируй):\r\n{string.Join("\r\n", contextMessages.Select(FormatMessage))}\r\n\r\nНовые сообщения (только на них реагируй):\r\n{string.Join("\r\n", newMessages.Select(FormatMessage))}"
                : string.Join("\r\n", newMessages.Select(FormatMessage));

            var currentProviderHash = AiClient.ProviderHash;
            try
            {
                Logger.Warn(formatted);
                Logger.Warn("---------");

                var res = await AiClient.Ask(formatted, _streamInfos, useHistory: false);
                if (string.IsNullOrWhiteSpace(res))
                    throw new UnknownGeminiException("Response text is empty");

                Logger.Warn(res);

                res = string.Join(" ", res.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));

                if (!res.Trim().Equals("PASS", StringComparison.OrdinalIgnoreCase))
                {
                    await SendMessage(res);

                    _messageLog.Enqueue(new ChatMessageData
                    {
                        Date = DateTimeOffset.Now,
                        UserName = AiClient.ActorName,
                        Message = res,
                        IsBot = true,
                    });
                }

                DelayProcessing(TimeSpan.FromSeconds(ProcessPeriod));
            }
            catch (TooManyRequestsException ex)
            {
                Logger.Error($"{ex.GetType()}: {ex.Message}");
                Logger.Error(formatted);
                AiClient.RotateClient(currentProviderHash);
            }
            catch (ClientBusyException ex)
            {
                Logger.Error($"{ex.GetType()}: {ex.Message}");
                Logger.Error(formatted);
            }
            catch (UnavailableException ex)
            {
                Logger.Warn($"{ex.GetType()}: {ex.Message}");
                DelayProcessing(TimeSpan.FromMilliseconds(2500));
            }
            catch (UnknownGeminiException ex)
            {
                Logger.Warn($"{ex.GetType()}: {ex.Message}");
            }
            catch (SafetyException ex)
            {
                Logger.Error($"{ex.GetType()}: {ex.Message}");
                Logger.Error(formatted);
            }
            catch (Exception ex)
            {
                Logger.Error($"{ex.GetType()}: {ex.Message}");
                Logger.Error(formatted);
                DelayProcessing(TimeSpan.FromMilliseconds(500));
            }
        }
        Logger.Info($"{nameof(RunMessageWatcher)} stopped");
    }

    private async Task RunReplyWatcher(CancellationToken token)
    {
        Logger.Info($"{nameof(RunReplyWatcher)} started");
        for (; !token.IsCancellationRequested;)
        {
            if (MessageHandler.IsSuspended || IsProcessingDelayed)
            {
                await Task.Delay(200);
                continue;
            }

            if (!_directMessages.TryDequeue(out var payload))
            {
                await Task.Delay(25);
                continue;
            }

            var (text, chatMessage, role) = payload;

            Logger.Debug($"Answering direct message from {chatMessage.Username}: {text}");

            var currentProviderHash = AiClient.ProviderHash;
            try
            {
                var responseText = await AiClient.Ask($"Ответь на сообщение из чата:\r\n[{chatMessage.Username}]: {text}", _streamInfos, useHistory: true);
                if (string.IsNullOrWhiteSpace(responseText))
                    throw new UnknownGeminiException("Response text is empty");

                if (!responseText.ToLower().Contains($"{chatMessage.Username.ToLower()}"))
                    responseText = $"@{chatMessage.Username} {responseText}";

                await SendMessage(responseText);
            }
            catch (TooManyRequestsException ex)
            {
                Logger.Error($"{ex.GetType()}: {ex.Message}");
                AiClient.RotateClient(currentProviderHash);
                _directMessages.Enqueue(payload);
            }
            catch (ClientBusyException ex)
            {
                Logger.Warn("Client is busy. Requeueing");
                _directMessages.Enqueue(payload);
            }
            catch (UnavailableException ex)
            {
                Logger.Warn("Model is busy. Requeueing");
                DelayProcessing(TimeSpan.FromMilliseconds(2500));
                _directMessages.Enqueue(payload);
            }
            catch (UnknownGeminiException ex)
            {
                Logger.Error($"Unknown gemini error for user \"{chatMessage.Username}\" \"{text}\": {ex.Message}");
            }
            catch (SafetyException ex)
            {
                Logger.Error($"Safety error for user \"{chatMessage.Username}\" \"{text}\": {ex.Message}");
            }
            catch (Exception ex)
            {
                DelayProcessing(TimeSpan.FromMilliseconds(500));
                _directMessages.Enqueue(payload);
                Logger.Error($"{ex.GetType()}: {ex.Message}");
            }
        }

        Logger.Info($"{nameof(RunReplyWatcher)} stopped");
    }

    public void Reset()
    {
        _messageLog.Clear();
        _directMessages.Clear();
        AiClient.Reset();
        DelayProcessing(TimeSpan.FromSeconds(0));
    }

    private async Task SendMessage(string text)
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

    private ILogger Logger => Logging.Logger.Instance(nameof(AiMessagesProcessor));
}
