using Stream = TwitchLib.Api.Helix.Models.Streams.GetStreams.Stream;

namespace TwitchGpt.Gpt.Entities;

public class StreamInfo
{
    public bool Online { get; set; }
 
    public string Snapshot { get; set; }
 
    public Stream Stream { get; set; }
    
    public bool NeedUpdate { get; set; }

    public List<string>? AvailableBttvEmotes;
}
