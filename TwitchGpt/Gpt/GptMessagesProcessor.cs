using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using GptLib.Exceptions;
using TwitchGpt.Gpt.Abstraction;
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

            var formatted = string.Join("\r\n", messages.Select(_ => $"[{_.Username}]: {_.Message}"));

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

    class LineData
    {
        public float Probability { get; set; }
        public string UseName { get; set; }
        public string Text { get; set; }
    }

    private void Respond(string resText)
    {
        var lines = resText.Split("\n").Select(_ => _.Trim()).ToList();

        List<LineData> parsed = new();
        foreach (var line in lines)
        {
            var m = Regex.Match(line, @"\[(\d\.\d)\:([a-z0-9_]+)](.+)");
            if (m.Success)
            {
                parsed.Add(new()
                {
                    Probability = float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                    UseName = m.Groups[2].Value,
                    Text = m.Groups[3].Value.Trim(' ', ':')
                });
            }
        }

        if (parsed.Count == 0)
            return;

        var selected = parsed.OrderByDescending(_ => _.Probability).FirstOrDefault(_ =>
            _.UseName.Length > 0 && _.Text.Length > 0);

        if (selected == null)
            return;

        var text = char.ToLower(selected.Text[0]) + selected.Text[1..];

        SendMessage($"@{selected.UseName} {text}");
    }

    public override void Reset()
    {
        DelayProcessing(TimeSpan.FromSeconds(0));
        _messages.Clear();
        _gptClient.Conversation.History.Lock(h => h.Reset());
        
        base.Reset();
    }
}