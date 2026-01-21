using NLog;
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

    public abstract Client GptClient { get; protected set; }

    public abstract Task Run(CancellationToken token, params AbstractStreamInfo?[] streamInfos);

    public static int SnapshotHistoryCount = 3;

    protected async Task SendMessage(string text)
    {
        try
        {
            if (_bot.IsDryDun())
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
}
