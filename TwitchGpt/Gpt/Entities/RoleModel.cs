using GenerativeAI.Types;

namespace TwitchGpt.Gpt.Entities;

public class RoleModel
{
    public string Name { get; set; }

    public List<string> Scopes { get; set; } = new();
    
    public string Instructions { get; set; } = "";

    public List<SafetySetting>? SafetySettings { get; set; } = new();
}
