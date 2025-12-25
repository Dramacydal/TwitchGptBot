namespace TwitchGpt.Gpt.Entities;

public abstract class AbstractStreamInfo
{
    public bool Online { get; set; }

    public List<Tuple<DateTime, FileSourceInfo>> SnapShots { get; } = [];

    public void AddSnapShot(FileSourceInfo data)
    {
        while (SnapShots.Count > 3)
            SnapShots.RemoveAt(0);

        SnapShots.Add(new(DateTime.Now, data));
    }

    public abstract string BuildMessage();
}
