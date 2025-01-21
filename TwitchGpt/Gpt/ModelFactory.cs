using System.Collections.Concurrent;
using TwitchGpt.Database.Mappers;
using TwitchGpt.Gpt.Entities;

namespace TwitchGpt.Gpt;

public class ModelFactory
{
    private static ConcurrentDictionary<string, RoleModel> _models = new();

    public static async Task<RoleModel?> Get(string name)
    {
        if (_models.TryGetValue(name, out var model))
            return model;

        model = await RoleModelMapper.Instance.GetRoleModel(name);

        _models[name] = model;
        
        return model;
    }

    public static async Task Reload()
    {
        foreach (var (name, model) in _models)
        {
            var result = await RoleModelMapper.Instance.GetRoleModel(name);
            if (result == null)
                continue;
            
            model.Instructions = result.Instructions;
            model.Name = result.Name;
            model.Scopes = result.Scopes;
            model.SafetySettings = result.SafetySettings;
        }
    }
}