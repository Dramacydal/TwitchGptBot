using TwitchGpt.Database.Mappers;

namespace TwitchGpt.TwitchRoutines.Entities;

public class ChannelAnnouncementData
{
    public string ChannelId { get; set; }
    
    public int Period { get; set; }
    
    public List<Announcement>? Announcements { get; set; }
}