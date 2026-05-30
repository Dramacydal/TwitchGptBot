namespace TwitchGpt.Gpt.Entities;

public class ChatMessageData
{
    public DateTimeOffset Date { get; set; }
    public string UserName { get; set; } = "";
    public string Message { get; set; } = "";
    public bool IsBot { get; set; }
}
