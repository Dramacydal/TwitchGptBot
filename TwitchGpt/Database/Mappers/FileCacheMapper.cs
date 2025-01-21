using GptLib;
using MongoDB.Driver;
using TwitchGpt.Database.Mappers.Abstraction;
using TwitchGpt.Database.Mongo;

namespace TwitchGpt.Database.Mappers;

public class FileCacheMapper : AbstractMapper<FileCacheMapper, MongoConnection>
{
    private string _uploadedFilesCollectionName = "uploaded_files";
    public override MongoConnection Connection => MongoConnectionManager.GetClient("mongo_gpt");
    
    
    public async Task SaveUploadedFile(UploadFileInfo file)
    {
        var coll = Connection.GetCollection<UploadFileInfo>(_uploadedFilesCollectionName);

        await coll.InsertOneAsync(file);
    }

    public async Task<UploadFileInfo> GetUploadedFile(UploadFileInfo file)
    {
        var coll = Connection.GetCollection<UploadFileInfo>(_uploadedFilesCollectionName);

        var res = await coll.FindAsync(e =>
            e.FilePath == file.FilePath &&
            e.ModifyDate == file.ModifyDate &&
            e.Size == file.Size &&
            e.ModifyDate > DateTime.Now);

        return res.FirstOrDefault();
    }
}