using System.Text.Json;
using System.Text.Json.Nodes;

namespace TwitchGpt.Config;

public static class ConfigManager
{
    private static JsonObject config;

    static ConfigManager()
    {
        if (!File.Exists("config.json"))
            throw new Exception("The config file doesn't exist.");

        try
        {
            config = JsonObject.Parse(File.ReadAllText("config.json")).AsObject();
        }
        catch (Exception e)
        {
            throw new Exception("The config file could not be parsed.");
        }
    }

    public static T? GetPath<T>(string path)
    {
        JsonNode node = config;
        foreach (var section in path.Split('.'))
        {
            if (int.TryParse(section, out var index))
            {
                if (node.GetValueKind() == JsonValueKind.Array)
                {
                    if (node.AsArray().Count > index)
                        node = node.AsArray()[index];
                    else
                        throw new Exception($"Array index out of range.");
                }
                else
                    throw new Exception("Node not an array.");
            }
            else
            {
                if (node.GetValueKind() == JsonValueKind.Object)
                {
                    if (node.AsObject().TryGetPropertyValue(section, out var value))
                        node = value;
                    else
                        throw new Exception($"Section '{section}' of path '{path}' could not be found.");
                }
                else
                    throw new Exception($"Node not an object.");
            }
        }

        return node.Deserialize<T>();
    }
}