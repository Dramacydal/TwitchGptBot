using System.Collections.Concurrent;
using GptLib.Exceptions;
using TwitchGpt.Gpt.Abstraction;
using TwitchGpt.Gpt.Entities;
using TwitchGpt.Gpt.Enums;
using TwitchGpt.Handlers;
using TwitchLib.Api.Helix.Models.Users.GetUsers;
using TwitchLib.Client.Models;

namespace TwitchGpt.Gpt;

public class GptMessagesProcessor(Bot bot, User channelUser) : AbstractProcessor(bot, channelUser)
{
    protected override Client _gptClient => ClientFactory.CreateClient(ClientType.ChatWatcher).Result;
    
    private ConcurrentStack<ChatMessage> _messages = new();
    
    public void EnqueueChatMessage(ChatMessage message) => _messages.Push(message);

    public int ProcessPeriod { get; set; } = 60;
    // public int ProcessPeriod { get; set; } = 10;
    
    public override async Task Run(CancellationToken token)
    {
        Logger.Info($"{nameof(GptMessagesProcessor)} started");
        for (; !token.IsCancellationRequested;)
        {
            if (MessageHandler.IsSuspended || ProcessPeriod <= 0)
            {
                await Task.Delay(200);
                continue;
            }
            
            while (IsProcessingDelayed)
                await Task.Delay(100);
            
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

            var currentProviderHash = _gptClient.ProviderHash;
            try
            {
                _gptClient.Conversation.History.Lock(h =>
                {
                    if (h.Length > 100)
                        h.Reset();
                });

                var res = await _gptClient.Ask(new() { Text = formatted }, _gptClient.Role);
                if (res == null || !res.Success)
                    throw new Exception("Client responded with null or error");

                Logger.Warn(formatted);
                Logger.Warn("---------");
                Logger.Warn(res.Answer.Text);

                Respond(res.Answer.Text);

                DelayProcessing(TimeSpan.FromSeconds(ProcessPeriod));
            }
            catch (TooManyRequestsException ex)
            {
                Logger.Error($"{ex.GetType()}: {ex.Message}");
                Logger.Error(formatted);
                _gptClient.RotateClient(currentProviderHash);
            }
            catch (SafetyException ex)
            {
                _gptClient.Conversation.History.Lock(h => h.RollbackLastQuestion());
                Logger.Error($"{ex.GetType()}: {ex.Message}");
                Logger.Error(formatted);
            }
            catch (Exception ex)
            {
                Logger.Error($"{ex.GetType()}: {ex.Message}");
                Logger.Error(formatted);
                DelayProcessing(TimeSpan.FromSeconds(10));
            }
        }
        Logger.Info($"{nameof(GptMessagesProcessor)} stopped");
    }

    private void Respond(string resText)
    {
        var lines = resText.Split("\n").Select(_ => _.Trim()).ToList();

        List<WeightedMessageData> parsed = new();
        foreach (var line in lines)
        {
            var lineData = WeightedMessageData.Extract(line);
            if (lineData != null)
                parsed.Add(lineData);
        }

        if (parsed.Count == 0)
            return;

        var selected = parsed.OrderByDescending(_ => _.Probability).FirstOrDefault(_ =>
            _.UserName.Length > 0 && _.Text.Length > 0);

        if (selected == null)
            return;

        var text = char.ToLower(selected.Text[0]) + selected.Text[1..];

        if (!text.ToLower().Contains($"@{selected.UserName.ToLower()}"))
            text = $"@{selected.UserName} {text}";

        SendMessage(text);
    }

    public override void Reset()
    {
        DelayProcessing(TimeSpan.FromSeconds(0));
        _messages.Clear();
        _gptClient.Conversation.History.Lock(h => h.Reset());
        
        base.Reset();
    }
}