namespace TwitchGpt.TwitchRoutines.Entities;

public class Announcement
{
    public string Message { get; set; }

    public bool IsEnabled { get; set; }

    public Condition? Condition { get; set; }

    public int Order { get; set; }
}
