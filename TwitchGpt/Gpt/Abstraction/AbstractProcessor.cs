using GptLib;
using NLog;
using TwitchGpt.Database.Mappers;
using TwitchGpt.Gpt.Entities;
using TwitchLib.Api.Helix.Models.Users.GetUsers;

namespace TwitchGpt.Gpt.Abstraction;

public abstract class AbstractProcessor
{
    public AbstractProcessor(Bot bot, User channelUser)
    {
        _bot = bot;
        _channelUser = channelUser;
    }
    
    private DateTime _skipProcessingTime = DateTime.Now;
    
    private readonly Bot _bot;
    
    private readonly User _channelUser;

    protected void DelayProcessing(TimeSpan delay) => _skipProcessingTime = DateTime.Now.Add(delay);
    
    protected bool IsProcessingDelayed => _skipProcessingTime > DateTime.Now;
    
    protected abstract Client _gptClient { get; }

    public abstract Task Run(CancellationToken token);
    
    protected void SendMessage(string text)
    {
        try
        {
            _bot.Client.SendMessage(_channelUser.Login, text);
        }
        catch (Exception ex)
        {
            Logger.Error($"Error sending chat message: {ex.Message}");
            Logger.Error(text);
        }
    }

    protected ILogger Logger => Logging.Logger.Instance(nameof(GptWatcher));

    public virtual void Reset()
    {
    }

    private bool? _lastStatus = null;
    
    public async Task OnStreamInfo(StreamInfo info)
    {
        var msg = new GptQuestion()
        {
            Tag = "channel_info"
        };

        if (_lastStatus.HasValue && _lastStatus.Value == false && !info.Online)
            return;

        _lastStatus = info.Online;

        var emoteLine = "Доступные на канале BetterTTV Emotes: " + string.Join(", ", info.AvailableBttvEmotes ?? ["нет"]);

        if (info.Online)
        {
            msg.Text = "Стрим сейчас онлайн, ты можешь по нему дать информацию и о том, что происходит на экране.\r\n" +
                       $"Канал стримера: '{info.Stream.UserName}'\r\n" +
                       $"Название стрима: '{info.Stream.Title}'\r\n" +
                       $"Категория (игра) на стриме: {info.Stream.GameName}\r\n" +
                       $"Теги стрима: {string.Join(", ", info.Stream.Tags)}\r\n" +
                       $"Количество зрителей на стриме: {info.Stream.ViewerCount}\r\n" +
                       $"Время начала стрима: {info.Stream.StartedAt.ToLocalTime()}\r\n" +
                       $"Текущее время: {DateTime.Now}\r\n" +
                       emoteLine + "\r\n";

            if (!string.IsNullOrEmpty(info.Snapshot))
                msg.Text += "Кадр стрима, по которому ты можешь определить, что на нем происходит, прикрепляю файлом";
            msg.Files.Add(info.Snapshot);
        }
        else
            msg.Text =
                "Стрим сейчас оффлайн. Ты не можешь по нему дать информацию, и сказать, что сейчас на экране.\r\n" +
                emoteLine;

        try
        {
            if (GetType().Name == nameof(GptMessagesProcessor))
                msg.Text = $"~{msg.Text}";

            Logger.Info($"Sending stream info message in {GetType().Name}: {msg.Text}");

            _gptClient.Conversation.History.Lock(h =>
            {
                for (;;)
                {
                    var pos = h.Find(_ => _.Tag == "channel_info");
                    if (pos == -1)
                        break;

                    h.RemoveAt(pos);
                    h.RemoveAt(pos);
                }
            });

            var res = await _gptClient.Ask(msg, _gptClient.Role);
            if (res == null)
                throw new Exception("Null response");
            if (!res.Success)
                throw new Exception("Not a success");

            Logger.Info("Response: " + res.Answer.Text);
        }
        catch (Exception ex)
        {
            Logger.Error($"Error sending stream info message, {ex.GetType()}: {ex.Message}");
        }
    }
}