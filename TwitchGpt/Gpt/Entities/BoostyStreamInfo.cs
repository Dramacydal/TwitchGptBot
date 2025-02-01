using BoostyLib.Endpoints.Responses;

namespace TwitchGpt.Gpt.Entities;

public class BoostyStreamInfo
{
    public bool Online { get; set; }

    public string Snapshot { get; set; }

    public VideoStreamResponse Stream { get; set; }

    public bool NeedUpdate { get; set; }
}
