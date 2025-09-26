using System.Collections.Concurrent;
using NLog;
using TwitchGpt.Database.Mappers;
using TwitchGpt.TwitchRoutines.Entities;
using TwitchLib.Api.Helix.Models.Users.GetUsers;

namespace TwitchGpt.TwitchRoutines;

public class Announcer(Bot bot, User user)
{
    private ConcurrentQueue<Announcement> _announcements = new();

    private TimeSpan _announcementDelay = TimeSpan.Zero;

    private DateTime _nextAnnouncementTime = DateTime.Now;

    public async Task RunAsync(CancellationToken token)
    {
        await Reload();

        for (; !token.IsCancellationRequested;)
        {
            if (_announcementDelay <= TimeSpan.Zero || _nextAnnouncementTime > DateTime.Now || !Dequeue(out var announcement))
            {
                await Task.Delay(200);
                continue;
            }

            _announcements.Enqueue(announcement);

            try
            {
                if (bot.GetMessagesToLog())
                    Logger.Trace($">> {announcement.Message}");
                else
                    await bot.Client.SendMessageAsync(user.Login, announcement.Message);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to send announcement: {ex.Message}");
            }

            _nextAnnouncementTime = DateTime.Now.Add(_announcementDelay);
        }
    }

    private bool Dequeue(out Announcement? announcement)
    {
        if (!_announcements.TryDequeue(out announcement))
            return false;

        if (announcement.Condition == null)
            return true;

        if (!announcement.IsEnabled || !announcement.Condition.IsMatch(bot, user).Result)
        {
            _announcements.Enqueue(announcement);
            return false;
        }

        return true;
    }

    public async Task Reload()
    {
        _announcements.Clear();
        var announcementData = await AnnouncementMapper.Instance.GetAnnouncementData(user.Id);

        var announcements = announcementData?.Announcements?.OrderBy(a => a.Order).ToList();
        foreach (var ann in announcements ?? [])
            _announcements.Enqueue(ann);

        _announcementDelay = TimeSpan.FromSeconds(announcementData?.Period ?? 0);
    }

    private ILogger Logger => Logging.Logger.Instance(nameof(Announcer));
}