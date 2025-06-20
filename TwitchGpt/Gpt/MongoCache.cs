using GptLib;
using GptLib.Uploads;
using TwitchGpt.Database.Mappers;

namespace TwitchGpt.Gpt;

public class MongoCache : IUploadedFileCache
{
    public async Task<UploadFile> Load(UploadFile fileInfo)
    {
        return await FileCacheMapper.Instance.GetUploadedFile(fileInfo);
    }

    public async Task Store(UploadFile file)
    {
        await FileCacheMapper.Instance.SaveUploadedFile(file);
    }
}