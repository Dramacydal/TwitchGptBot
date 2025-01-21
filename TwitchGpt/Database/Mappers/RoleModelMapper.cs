using MongoDB.Driver;
using TwitchGpt.Database.Mappers.Abstraction;
using TwitchGpt.Database.Mongo;
using TwitchGpt.Gpt.Entities;

namespace TwitchGpt.Database.Mappers;

public class RoleModelMapper : AbstractMapper<RoleModelMapper, MongoConnection>
{
    private const string _collectionName = "role_models";
    
    public override MongoConnection Connection => MongoConnectionManager.GetClient("mongo_gpt");

    public async Task<IEnumerable<RoleModel>> GetRoleModels()
    {
        var res= await Connection.GetCollection<RoleModel>(_collectionName)
            .FindAsync(e => true);


        return res.ToEnumerable();
    }

    public async Task<RoleModel?> GetRoleModel(string name)
    {
        var res = await Connection.GetCollection<RoleModel>(_collectionName)
            .FindAsync(e => e.Name == name);

        return res.FirstOrDefault();
    }

    public async Task<RoleModel> Save(RoleModel model)
    {
        return await Connection.GetCollection<RoleModel>(_collectionName)
            .FindOneAndReplaceAsync(e => e.Name == model.Name, model,
                new FindOneAndReplaceOptions<RoleModel> { IsUpsert = true });
    }
}