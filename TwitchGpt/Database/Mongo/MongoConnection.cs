using MongoDB.Driver;

namespace TwitchGpt.Database.Mongo;

public class MongoConnection(MongoConfig config)
{
    private MongoClient CreateConnection()
    {
        var conn = new MongoClient(config.Uri);
        return conn;
    }

    public IMongoDatabase Database => CreateConnection().GetDatabase(config.Database);

    public IMongoCollection<T> GetCollection<T>(string collectionName) => Database.GetCollection<T>(collectionName);
}
