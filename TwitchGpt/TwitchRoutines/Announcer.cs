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
            if (_announcementDelay <= TimeSpan.Zero || _nextAnnouncementTime > DateTime.Now)
            {
                await Task.Delay(200);
                continue;
            }

            var announcement = await Dequeue();
            if (announcement == null)
            {
                await Task.Delay(200);
                continue;
            }

            _announcements.Enqueue(announcement);

            try
            {
                if (bot.IsDryDun())
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

    private async Task<Announcement?> Dequeue()
    {
        if (!_announcements.TryDequeue(out var announcement))
            return null;

        if (announcement.Condition == null)
            return announcement;

        if (!announcement.IsEnabled || !await announcement.Condition.IsMatch(bot, user))
        {
            _announcements.Enqueue(announcement);
            return null;
        }

        return announcement;
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