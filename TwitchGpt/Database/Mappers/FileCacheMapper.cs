using GptLib;
using GptLib.Uploads;
using MongoDB.Driver;
using TwitchGpt.Database.Mappers.Abstraction;
using TwitchGpt.Database.Mongo;

namespace TwitchGpt.Database.Mappers;

public class FileCacheMapper : AbstractMapper<FileCacheMapper, MongoConnection>
{
    private string _uploadedFilesCollectionName = "uploaded_files";
    public override MongoConnection Connection => MongoConnectionManager.GetClient("mongo_gpt");
    
    
    public async Task SaveUploadedFile(UploadFile file)
    {
        var coll = Connection.GetCollection<UploadFile>(_uploadedFilesCollectionName);

        await coll.InsertOneAsync(file);
    }

    public async Task<UploadFile> GetUploadedFile(UploadFile file)
    {
        var coll = Connection.GetCollection<UploadFile>(_uploadedFilesCollectionName);

        var res = await coll.FindAsync(e => e.Equals(file));

        return res.FirstOrDefault();
    }
}