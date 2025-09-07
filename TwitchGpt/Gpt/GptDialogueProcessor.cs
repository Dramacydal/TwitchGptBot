using System.Collections.Concurrent;
using TwitchGpt.Exceptions;
using TwitchGpt.Gpt.Abstraction;
using TwitchGpt.Gpt.Entities;
using TwitchGpt.Gpt.Enums;
using TwitchGpt.Handlers;
using TwitchLib.Api.Helix.Models.Users.GetUsers;
using TwitchLib.Client.Models;

namespace TwitchGpt.Gpt;

public class GptDialogueProcessor(Bot bot, User channelUser) : AbstractProcessor(bot, channelUser)
{
    // protected override Client _gptClient => ClientFactory.CreateClient(ClientType.ChatBot).Result;
    protected override Client _gptClient => ClientFactory.CreateClient(ClientType.ChatWatcher).Result;
    
    private ConcurrentQueue<Tuple<string, ChatMessage, RoleModel>> _messages = new();
    
    public void EnqueueDirectMessage(string text, ChatMessage chatMessage, RoleModel role) => _messages.Enqueue(new (text, chatMessage, role));
    
    public override async Task Run(CancellationToken token)
    {
        Logger.Info($"{nameof(GptDialogueProcessor)} started");
        for (; !token.IsCancellationRequested;)
        {
            if (MessageHandler.IsSuspended)
            {
                await Task.Delay(200);
                continue;
            }
            
            while (IsProcessingDelayed)
                await Task.Delay(50);
            
            if (!_messages.TryDequeue(out var payload))
            {
                await Task.Delay(25);
                continue;
            }

            var (text, chatMessage, role) = payload;

            var currentProviderHash = _gptClient.ProviderHash;
            try
            {
                var responseText = await _gptClient.Ask($"Ответь на сообщение из чата:\r\n[{chatMessage.Username}]: {text}");
                var data = WeightedMessageData.Extract(responseText);
                if (data != null)
                    responseText = data.Text;

                if (!responseText.ToLower().Contains($"@{chatMessage.Username.ToLower()}"))
                    responseText = $"@{chatMessage.Username} {responseText}";

                SendMessage(responseText);
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