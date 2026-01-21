using System.Collections.Concurrent;
using TwitchGpt.Exceptions;
using TwitchGpt.Gpt.Abstraction;
using TwitchGpt.Gpt.Entities;
using TwitchGpt.Gpt.Enums;
using TwitchGpt.Gpt.Responses;
using TwitchGpt.Handlers;
using TwitchLib.Api.Helix.Models.Users.GetUsers;
using TwitchLib.Client.Models;

namespace TwitchGpt.Gpt;

public class GptDialogueProcessor : AbstractProcessor
{
    // protected override Client _gptClient => ClientFactory.CreateClient(ClientType.ChatBot).Result;
    protected override Client _gptClient { get; set; }
    
    private ConcurrentQueue<Tuple<string, ChatMessage, RoleModel>> _messages = new();

    private GptDialogueProcessor(Bot bot, User channelUser) : base(bot, channelUser)
    {
    }

    public static async Task<GptDialogueProcessor> Create(Bot bot, User channelUser)
    {
        return new GptDialogueProcessor(bot, channelUser)
        {
            _gptClient = await ClientFactory.CreateClient(ClientType.ChatWatcher)
        };
    }

    public void EnqueueDirectMessage(string text, ChatMessage chatMessage, RoleModel role) => _messages.Enqueue(new (text, chatMessage, role));
    
    public override async Task Run(CancellationToken token, params AbstractStreamInfo?[] streamInfos)
    {
        Logger.Info($"{nameof(GptDialogueProcessor)} started");
        for (; !token.IsCancellationRequested;)
        {
            if (MessageHandler.IsSuspended || IsProcessingDelayed)
            {
                await Task.Delay(200);
                continue;
            }

            if (!_messages.TryDequeue(out var payload))
            {
                await Task.Delay(25);
                continue;
            }

            var (text, chatMessage, role) = payload;
            
            Logger.Debug($"Answering direct message: {text}");

            var currentProviderHash = _gptClient.ProviderHash;
            try
            {
                var responseText = await _gptClient.Ask($"Ответь на сообщение из чата:\r\n[{chatMessage.Username}]: {text}", streamInfos);
                if (string.IsNullOrWhiteSpace(responseText))
                    throw new UnknownGeminiException("Response text is empty");

                if (!responseText.ToLower().Contains($"{chatMessage.Username.ToLower()}"))
                    responseText = $"@{chatMessage.Username} {responseText}";

                await SendMessage(responseText);
            }
            catch (TooManyRequestsException ex)
            {
                Logger.Error($"{ex.GetType()}: {ex.Message}");
                _gptClient.RotateClient(currentProviderHash);
                _messages.Enqueue(payload);
            }
            catch (ClientBusyException ex)
            {
                Logger.Warn("Client is busy. Requeueing");
                _messages.Enqueue(payload);
            }
            catch (UnavailableException ex)
            {
                Logger.Warn("Model is busy. Requeueing");
                DelayProcessing(TimeSpan.FromMilliseconds(2500));
                _messages.Enqueue(payload);
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
                _messages.Enqueue(payload);
                Logger.Error($"{ex.GetType()}: {ex.Message}");
            }
        }
        
        Logger.Info($"{nameof(GptDialogueProcessor)} stopped");
    }

    public override void Reset()
    {
        DelayProcessing(TimeSpan.FromSeconds(0));
        _messages.Clear();
        _gptClient.Reset();
        
        base.Reset();
    }
}