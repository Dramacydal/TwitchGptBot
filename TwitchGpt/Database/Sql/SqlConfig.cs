using System.Text.Json.Serialization;

namespace TwitchGpt.Database.Sql;

public class SqlConfig
{
    [JsonPropertyName("host")]
    public string Host { get; set; }

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("user")]
    public string User { get; set; }

    [JsonPropertyName("password")]
    public string Password { get; set; }

    [JsonPropertyName("database")]
    public string Database { get; set; }
}
