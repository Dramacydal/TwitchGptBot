using System.Text.Json.Serialization;

namespace TwitchGpt.Gpt.Entities;

public class Proxy
{
    [JsonPropertyName("url")]
    public string Url { get; set; }
    
    [JsonPropertyName("user")]
    public string User { get; set; }
    
    [JsonPropertyName("password")]
    public string Password { get; set; }
}