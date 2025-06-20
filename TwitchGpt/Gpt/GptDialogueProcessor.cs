using System.Collections.Concurrent;
using GptLib.Exceptions;
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
    
    private Queue<string> _answers = new();

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
                var res = await _gptClient.Ask(
                    new() { Text = $"Ответь на сообщение из чата:\r\n[{chatMessage.Username}]: {text}" }, role);
                if (!res.Success)
                {
                    Logger.Error("Empty response or not a succes, requeueing");
                    if (res?.Success == false)
                        Logger.Error($"Error: {res.Answer}");
                    _messages.Enqueue(payload);
                }

                var responseText = res.Answer.Text;
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
            catch (SafetyException ex)
            {
                Logger.Error(
                    $"Safety error for user \"{chatMessage.Username}\" \"{text}\": {ex.Message}");
                _gptClient.Conversation.History.Lock(h => h.RollbackLastQuestion());
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
        _answers.Clear();
        _gptClient.Conversation.History.Lock(h => h.Reset());
        
        base.Reset();
    }
}