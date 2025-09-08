namespace TwitchGpt.Gpt.Entities;

public class FileSourceInfo
{
    public string Name { get; set; }
    
    public long Size { get; set; }
    
    public string FilePath { get; set; }

    public string MimeType { get; set; }
    
    public DateTime ModifyDate { get; set; }

    public byte[]? Blob { get; set; }

    public static FileSourceInfo FromFilePath(string filePath)
    {
        var info = new FileInfo(filePath);

        return new()
        {
            Name = info.Name,
            FilePath = Path.GetFullPath(filePath),
            MimeType = MimeKit.MimeTypes.GetMimeType(filePath),
            Size = info.Length,
            ModifyDate = info.LastWriteTime,
        };
    }

    public static FileSourceInfo FromBlob(string name, byte[] blob)
    {
        return new()
        {
            Name = name,
            MimeType = MimeKit.MimeTypes.GetMimeType(name),
            Size = blob.Length,
            Blob = blob,
            ModifyDate = DateTime.Now,
        };
    }
}
