using GptLib;
using TwitchGpt.Database.Mappers;

namespace TwitchGpt.Gpt;

public class MongoCache : IUploadedFileCache
{
    public async Task<UploadFileInfo> Load(UploadFileInfo fileInfo)
    {
        return await FileCacheMapper.Instance.GetUploadedFile(fileInfo);
    }

    public async Task Store(UploadFileInfo file)
    {
        await FileCacheMapper.Instance.SaveUploadedFile(file);
    }
}