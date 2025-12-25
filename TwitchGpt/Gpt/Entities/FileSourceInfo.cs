namespace TwitchGpt.Gpt.Entities;

public class FileSourceInfo
{
    private FileSourceInfo() {}
    
    public required string MimeType { get; init; }

    public required byte[] Blob { get; init; }

    public static FileSourceInfo FromFilePath(string filePath)
    {
        var info = new FileInfo(filePath);

        return new()
        {
            MimeType = MimeKit.MimeTypes.GetMimeType(info.Name),
            Blob = File.ReadAllBytes(filePath),
        };
    }

    public static FileSourceInfo FromBlob(string mimeType, byte[] blob)
    {
        return new()
        {
            MimeType = mimeType,
            Blob = blob,
        };
    }
}
