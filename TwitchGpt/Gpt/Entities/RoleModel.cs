namespace TwitchGpt.Gpt.Entities;

public class RoleModel
{
    public string Name { get; set; }

    public List<string> Scopes { get; set; } = new();
    
    public List<string> Instructions { get; set; } = new();

    public Dictionary<string, string> SafetySettings { get; set; } = new();
}
