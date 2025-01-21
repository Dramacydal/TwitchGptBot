using System.Text.Json.Serialization;

namespace TwitchGpt.Database.Mongo;

public class MongoConfig
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; }
    
    [JsonPropertyName("database")]
    public string Database { get; set; }
}
