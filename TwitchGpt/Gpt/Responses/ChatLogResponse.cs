namespace TwitchGpt.Gpt.Responses;

public class ChatLogResponse
{
    public class ChatterReply
    {
        public string ChatterUserName { get; set; }

        public string ChatterOriginalMessage { get; set; }

        public string Reply { get; set; }
    }

    public List<ChatterReply> ChatterReplies { get; set; } = new();
}
