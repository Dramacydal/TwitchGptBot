using System.Collections.Concurrent;
using TwitchGpt.Config;
using TwitchGpt.Exceptions;
using TwitchGpt.Gpt.Abstraction;
using TwitchGpt.Gpt.Entities;
using TwitchGpt.Gpt.Enums;
using TwitchGpt.Gpt.Factories;
using TwitchGpt.Handlers;
using TwitchLib.Api.Helix.Models.Users.GetUsers;
using TwitchLib.Client.Models;

namespace TwitchGpt.Gpt;

public class AiMessagesProcessor : AbstractProcessor
{
    public override AiClient AiClient { get; protected set; }

    private ConcurrentStack<ChatMessageData> _messageLog = new();
    
    private ConcurrentQueue<Tuple<string, ChatMessage, RoleModel>> _directMessages = new();
    
    public AbstractStreamInfo?[] _streamInfos;
    private AiMessagesProcessor(Bot bot, User channelUser) : base(bot, channelUser)
    {
        ProcessPeriod = ConfigManager.GetPath<int>("message_process_period");
    }

    public static async Task<AiMessagesProcessor> Create(Bot bot, User channelUser, params AbstractStreamInfo?[] streamInfos)
    {
        return new AiMessagesProcessor(bot, channelUser)
        {
            AiClient = await ClientFactory.CreateClient(ClientType.ChatWatcher, bot.BotUserName),
            _streamInfos =  streamInfos
        };
    }

    public void AddMessageToLog(ChatMessage message, bool isBot = false) => _messageLog.Push(new()
    {
        Date = message.TmiSent,
        UserName = message.Username,
        Message = message.Message,
        IsBot = isBot,
    });
    
    public void EnqueueDirectMessage(string text, ChatMessage chatMessage, RoleModel role) => _directMessages.Enqueue(new (text, chatMessage, role));

    public int ProcessPeriod { get; set; }
    
    public override async Task Run(CancellationToken token)
    {
        var t1 = RunMessageWatcher(token);
        var t2 = RunDialogueWatcher(token);

        await Task.WhenAll(t1, t2);
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

            var formatted = string.Join("\r\n", _messageLog.Select(_ => $"[{_.Date:yyyy-MM-dd HH:mm:ss}] [{_.UserName}]: {_.Message}"));

            var currentProviderHash = AiClient.ProviderHash;
            try
            {
                if (AiClient.HistoryHolder.Count() > 100)
                    AiClient.HistoryHolder.Reset();

                Logger.Warn(formatted);
                Logger.Warn("---------");
                
                var res = await AiClient.Ask(formatted);
                if (string.IsNullOrWhiteSpace(res))
                    throw new UnknownGeminiException("Response text is empty");

                Logger.Warn(res);

                await Respond(res);

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

    private async Task RunDialogueWatcher(CancellationToken token)
    {
        Logger.Info($"{nameof(RunDialogueWatcher)} started");
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
                var responseText = await AiClient.Ask($"Ответь на сообщение из чата:\r\n[{chatMessage.Username}]: {text}", _streamInfos);
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
                Logger.Error(
                    $"Unknown gemini error for user \"{chatMessage.Username}\" \"{text}\": {ex.Message}");
            }
            catch (SafetyException ex)
            {
                Logger.Error(
                    $"Safety error for user \"{chatMessage.Username}\" \"{text}\": {ex.Message}");
            }
            catch (Exception ex)
            {
                DelayProcessing(TimeSpan.FromMilliseconds(500));
                _directMessages.Enqueue(payload);
                Logger.Error($"{ex.GetType()}: {ex.Message}");
            }
        }
        
        Logger.Info($"{nameof(RunDialogueWatcher)} stopped");
    }

    private async Task Respond(string text)
    {
        await SendMessage(text);
    }
    
    public override void Reset()
    {
        _messageLog.Clear();
        _directMessages.Clear();
        AiClient.Reset();
        
        base.Reset();
        
        DelayProcessing(TimeSpan.FromSeconds(0));
    }
}
