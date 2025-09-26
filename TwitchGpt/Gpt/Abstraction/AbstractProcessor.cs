using NLog;
using TwitchGpt.Exceptions;
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

    public static int SnapshotHistoryCount = 3;

    protected async Task SendMessage(string text)
    {
        try
        {
            if (_bot.GetMessagesToLog())
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

    protected ILogger Logger => Logging.Logger.Instance(nameof(GptWatcher));

    public virtual void Reset()
    {
    }

    private bool? _lastTwitchStreamStatus = null;
    
    private bool? _lastBoostyStreamStatus = null;

    public async Task OnTwitchStreamInfo(TwitchStreamInfo info)
    {
        var msgTag = "twitch_stream_info";
        
        if (_lastTwitchStreamStatus.HasValue && _lastTwitchStreamStatus.Value == false && !info.Online)
            return;

        _lastTwitchStreamStatus = info.Online;

        var emoteLine = "Доступные на канале BetterTTV Emotes: ";
        if (info.AvailableBttvEmotes != null && info.AvailableBttvEmotes.Count > 0)
            emoteLine += string.Join(", ", info.AvailableBttvEmotes);
        else
            emoteLine += "нет";

        var msgText = "";
        FileSourceInfo fileSourceInfo = null;
        if (info.Online)
        {
            msgText = "Стрим на Twitch сейчас онлайн, ты можешь по нему дать информацию и о том, что происходит на экране.\r\n" +
                      $"Канал Twitch стримера: '{info.Stream.UserName}'\r\n" +
                      $"Название стрима на Twitch: '{info.Stream.Title}'\r\n" +
                      $"Категория (игра) на стриме Twitch: {info.Stream.GameName}\r\n" +
                      $"Теги стрима Twitch: {string.Join(", ", info.Stream.Tags)}\r\n" +
                      $"Количество зрителей на Twitch стриме: {info.Stream.ViewerCount}\r\n" +
                      $"Время начала Twitch стрима: {info.Stream.StartedAt.ToLocalTime()}\r\n" +
                      $"Текущее время: {DateTime.Now}\r\n" +
                      emoteLine + "\r\n";

            if (!string.IsNullOrEmpty(info.Snapshot))
                msgText +=
                    "Кадр стрима на Twitch на текущий момент, по которому ты можешь определить, что на нем происходит, прикрепляю файлом";
            fileSourceInfo = FileSourceInfo.FromBlob(Path.GetFileName(info.Snapshot), await File.ReadAllBytesAsync(info.Snapshot));
        }
        else
            msgText =
                "Стрим на Twitch сейчас оффлайн. Ты не можешь по нему дать информацию, и сказать, что сейчас на экране.\r\n" +
                emoteLine;

        var currentProviderHash = _gptClient.ProviderHash;
        try
        {
            if (GetType().Name == nameof(GptMessagesProcessor))
                msgText = $"~{msgText}";

            Logger.Info($"Sending twitch stream info message in {GetType().Name}: {msgText}");

            var countByTag = _gptClient.HistoryHolder.CountByTag(msgTag);
            if (countByTag > SnapshotHistoryCount)
                _gptClient.HistoryHolder.RemoveEntriesWithContentTag(msgTag, countByTag - SnapshotHistoryCount);

            var res = await _gptClient.Ask(msgText, fileSourceInfo != null ? [fileSourceInfo] : []);

            Logger.Info("Response: " + res);
        }
        catch (TooManyRequestsException ex)
        {
            Logger.Warn("Quota limit exceeded while processing twitch snapshot");
            _gptClient.RotateClient(currentProviderHash);
        }
        catch (Exception ex)
        {
            Logger.Error($"Error sending stream info message, {ex.GetType()}: {ex.Message}");
        }
    }

    public async Task OnBoostyStreamInfo(BoostyStreamInfo info)
    {
        var msgTag = "boosty_stream_info";

        if (_lastBoostyStreamStatus.HasValue && _lastBoostyStreamStatus.Value == false && !info.Online)
            return;

        _lastBoostyStreamStatus = info.Online;

        var msgText = "";
        FileSourceInfo fileSourceInfo = null;
        if (info.Online)
        {
            msgText =
                "Стрим на Бусти сейчас онлайн, ты можешь по нему дать информацию и о том, что происходит на экране.\r\n" +
                $"Канал Бусти стримера: '{info.Stream.User.Name}'\r\n" +
                $"Название стрима на Бусти: '{info.Stream.Title}'\r\n" +
                // $"Описание стрима на Бусти: '{info.Stream.Description}'\r\n" +
                $"Количество зрителей на стриме Бусти: {info.Stream.Count.Viewers}\r\n" +
                $"Количество лайков на стриме Бусти: {info.Stream.Count.Likes}\r\n" +
                $"Время начала стрима на Бусти: {DateTime.UnixEpoch.AddSeconds(info.Stream.StartTime).ToLocalTime()}\r\n" +
                $"Текущее время: {DateTime.Now}\r\n";

            if (!string.IsNullOrEmpty(info.Snapshot))
                msgText +=
                    "Кадр стрима на Бусти текущий момент, по которому ты можешь определить, что на нем происходит, прикрепляю файлом";
            fileSourceInfo = FileSourceInfo.FromBlob(Path.GetFileName(info.Snapshot),
                await File.ReadAllBytesAsync(info.Snapshot));
        }
        else
            msgText =
                "Стрим на Бусти сейчас оффлайн. Ты не можешь по нему дать информацию, и сказать, что сейчас на экране.\r\n";

        var currentProviderHash = _gptClient.ProviderHash;
        try
        {
            if (GetType().Name == nameof(GptMessagesProcessor))
                msgText = $"~{msgText}";

            Logger.Info($"Sending boosty stream info message in {GetType().Name}: {msgText}");

            var countByTag = _gptClient.HistoryHolder.CountByTag(msgTag);
            if (countByTag > SnapshotHistoryCount)
                _gptClient.HistoryHolder.RemoveEntriesWithContentTag(msgTag, countByTag - SnapshotHistoryCount);

            var res = await _gptClient.Ask(msgText, fileSourceInfo != null ? [fileSourceInfo] : []);

            Logger.Info("Response: " + res);
        }
        catch (TooManyRequestsException ex)
        {
            Logger.Warn("Quota limit exceeded while processing boosty snapshot");
            _gptClient.RotateClient(currentProviderHash);
        }
        catch (Exception ex)
        {
            Logger.Error($"Error sending stream info message, {ex.GetType()}: {ex.Message}");
        }
    }
}
