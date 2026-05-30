using System.Collections.Concurrent;
using TwitchGpt.Database.Mappers;
using TwitchGpt.Gpt.Entities;

namespace TwitchGpt.Gpt.Factories;

public class ModelFactory
{
    private static ConcurrentDictionary<string, RoleModel> _models = new();

    public static string? RolesDir { get; set; }

    public static async Task<RoleModel?> Get(string name)
    {
        if (_models.TryGetValue(name, out var model))
            return model;

        model = await RoleModelMapper.Instance.GetRoleModel(name);

        TryOverrideFromFile(model, name);

        _models[name] = model;

        return model;
    }

    public static async Task Reload()
    {
        foreach (var (name, model) in _models)
        {
            if (TryOverrideFromFile(model, name))
                continue;

            var result = await RoleModelMapper.Instance.GetRoleModel(name);
            if (result == null)
                continue;

            model.Instructions = result.Instructions;
            model.Name = result.Name;
            model.Scopes = result.Scopes;
        }
    }

    // Returns true if instructions were loaded from a local file
    private static bool TryOverrideFromFile(RoleModel? model, string name)
    {
        if (model == null || string.IsNullOrEmpty(RolesDir))
            return false;

        var path = Path.Combine(RolesDir, $"{name}.txt");
        if (!File.Exists(path))
            return false;

        model.Instructions = File.ReadAllText(path);
        return true;
    }
}
