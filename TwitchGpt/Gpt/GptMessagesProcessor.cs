using System.Collections.Concurrent;
using TwitchGpt.Exceptions;
using TwitchGpt.Gpt.Abstraction;
using TwitchGpt.Gpt.Entities;
using TwitchGpt.Gpt.Enums;
using TwitchGpt.Gpt.Responses;
using TwitchGpt.Handlers;
using TwitchGpt.Helpers;
using TwitchLib.Api.Helix.Models.Users.GetUsers;
using TwitchLib.Client.Models;

namespace TwitchGpt.Gpt;

public class GptMessagesProcessor : AbstractProcessor
{
    public override Client GptClient { get; protected set; }

    private ConcurrentStack<ChatMessage> _messages = new();

    private GptMessagesProcessor(Bot bot, User channelUser) : base(bot, channelUser)
    {
    }

    public static async Task<GptMessagesProcessor> Create(Bot bot, User channelUser)
    {
        return new GptMessagesProcessor(bot, channelUser)
        {
            GptClient = await ClientFactory.CreateClient(ClientType.ChatWatcher)
        };
    }

    public void EnqueueChatMessage(ChatMessage message) => _messages.Push(message);

    public int ProcessPeriod { get; set; } = 30;
    // public int ProcessPeriod { get; set; } = 10;
    
    public override async Task Run(CancellationToken token, params AbstractStreamInfo?[] streamInfos)
    {
        Logger.Info($"{nameof(GptMessagesProcessor)} started");
        for (; !token.IsCancellationRequested;)
        {
            if (MessageHandler.IsSuspended || ProcessPeriod <= 0 || IsProcessingDelayed)
            {
                await Task.Delay(200);
                continue;
            }

            List<ChatMessage> messages = new();
            for (var i = 0; i < 10 && _messages.TryPop(out var msg);)
            {
                if (msg.Message.Length < 10)
                    continue;

                messages.Add(msg);
                ++i;
            }

            if (messages.Count == 0)
            {
                DelayProcessing(TimeSpan.FromSeconds(ProcessPeriod));
                continue;
            }

            messages.Reverse();

            var formatted = "Проанализируй лог чата:\r\n" + string.Join("\r\n", messages.Select(_ => $"[{_.Username}]: {_.Message}"));

            var currentProviderHash = GptClient.ProviderHash;
            try
            {
                if (GptClient.HistoryHolder.Count() > 100)
                    GptClient.HistoryHolder.Reset();

                Logger.Warn(formatted);
                Logger.Warn("---------");
                
                var res = await GptClient.Ask(formatted, streamInfos);
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
                GptClient.RotateClient(currentProviderHash);
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
        Logger.Info($"{nameof(GptMessagesProcessor)} stopped");
    }

    private async Task Respond(string text)
    {
        await SendMessage(text);
    }
    
    private async Task Respond(ChatLogResponse response)
    {
        var selected = response.ChatterReplies.Where(r =>
            r.ChatterUserName.Length > 0 && r.Reply.Length > 0).Random();

        if (selected == null)
            return;

        var text = selected.Reply;
        if (!text.ToLower().Contains($"{selected.ChatterUserName.ToLower()}"))
            text = $"@{selected.ChatterUserName} {text}";

        await SendMessage(text);
    }

    public override void Reset()
    {
        _messages.Clear();
        GptClient.Reset();
        
        base.Reset();
        
        DelayProcessing(TimeSpan.FromSeconds(0));
    }
}