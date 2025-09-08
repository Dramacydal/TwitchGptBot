namespace TwitchGpt.Exceptions;

public class UnknownGeminiException : Exception
{
    public UnknownGeminiException(string message) : base(message)
    {
    }

    public UnknownGeminiException(string message, Exception inner) : base(message, inner)
    {
    }
}
