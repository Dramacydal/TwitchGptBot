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

    private bool? _lastTwitchStreamStatus = null;
    
    private bool? _lastBoostyStreamStatus = null;

    public async Task OnTwitchStreamInfo(TwitchStreamInfo info)
    {
        var msg = new GptQuestion()
        {
            Tag = "twitch_stream_info"
        };

        if (_lastTwitchStreamStatus.HasValue && _lastTwitchStreamStatus.Value == false && !info.Online)
            return;

        _lastTwitchStreamStatus = info.Online;

        var emoteLine = "Доступные на канале BetterTTV Emotes: ";
        if (info.AvailableBttvEmotes != null && info.AvailableBttvEmotes.Count > 0)
            emoteLine += string.Join(", ", info.AvailableBttvEmotes);
        else
            emoteLine += "нет";

        if (info.Online)
        {
            msg.Text = "Стрим на Twitch сейчас онлайн, ты можешь по нему дать информацию и о том, что происходит на экране.\r\n" +
                       $"Канал Twitch стримера: '{info.Stream.UserName}'\r\n" +
                       $"Название стрима на Twitch: '{info.Stream.Title}'\r\n" +
                       $"Категория (игра) на стриме Twitch: {info.Stream.GameName}\r\n" +
                       $"Теги стрима Twitch: {string.Join(", ", info.Stream.Tags)}\r\n" +
                       $"Количество зрителей на Twitch стриме: {info.Stream.ViewerCount}\r\n" +
                       $"Время начала Twitch стрима: {info.Stream.StartedAt.ToLocalTime()}\r\n" +
                       $"Текущее время: {DateTime.Now}\r\n" +
                       emoteLine + "\r\n";

            if (!string.IsNullOrEmpty(info.Snapshot))
                msg.Text +=
                    "Кадр стрима на Twitch на текущий момент, по которому ты можешь определить, что на нем происходит, прикрепляю файлом";
            msg.Files.Add(info.Snapshot);
        }
        else
            msg.Text =
                "Стрим на Twitch сейчас оффлайн. Ты не можешь по нему дать информацию, и сказать, что сейчас на экране.\r\n" +
                emoteLine;

        try
        {
            if (GetType().Name == nameof(GptMessagesProcessor))
                msg.Text = $"~{msg.Text}";

            Logger.Info($"Sending twitch stream info message in {GetType().Name}: {msg.Text}");

            _gptClient.Conversation.History.Lock(h =>
            {
                while (h.Contents.Count(e => e.Tag == "twitch_stream_info") > 3)
                {
                    var pos = h.Find(_ => _.Tag == "twitch_stream_info");
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

    public async Task OnBoostyStreamInfo(BoostyStreamInfo info)
    {
        var msg = new GptQuestion()
        {
            Tag = "boosty_stream_info"
        };

        if (_lastBoostyStreamStatus.HasValue && _lastBoostyStreamStatus.Value == false && !info.Online)
            return;

        _lastBoostyStreamStatus = info.Online;

        if (info.Online)
        {
            msg.Text =
                "Стрим на Бусти сейчас онлайн, ты можешь по нему дать информацию и о том, что происходит на экране.\r\n" +
                $"Канал Бусти стримера: '{info.Stream.User.Name}'\r\n" +
                $"Название стрима на Бусти: '{info.Stream.Title}'\r\n" +
                // $"Описание стрима на Бусти: '{info.Stream.Description}'\r\n" +
                $"Количество зрителей на стриме Бусти: {info.Stream.Count.Viewers}\r\n" +
                $"Количество лайков на стриме Бусти: {info.Stream.Count.Likes}\r\n" +
                $"Время начала стрима на Бусти: {DateTime.UnixEpoch.AddSeconds(info.Stream.StartTime).ToLocalTime()}\r\n" +
                $"Текущее время: {DateTime.Now}\r\n";

            if (!string.IsNullOrEmpty(info.Snapshot))
                msg.Text +=
                    "Кадр стрима на Бусти текущий момент, по которому ты можешь определить, что на нем происходит, прикрепляю файлом";
            msg.Files.Add(info.Snapshot);
        }
        else
            msg.Text =
                "Стрим на Бусти сейчас оффлайн. Ты не можешь по нему дать информацию, и сказать, что сейчас на экране.\r\n";

        try
        {
            if (GetType().Name == nameof(GptMessagesProcessor))
                msg.Text = $"~{msg.Text}";

            Logger.Info($"Sending boosty stream info message in {GetType().Name}: {msg.Text}");

            _gptClient.Conversation.History.Lock(h =>
            {
                while (h.Contents.Count(e => e.Tag == "boosty_stream_info") > 3)
                {
                    var pos = h.Find(_ => _.Tag == "boosty_stream_info");
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
