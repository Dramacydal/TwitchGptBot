namespace TwitchGpt.Exceptions;

public class SafetyException : Exception
{
    public SafetyException(string message) : base(message)
    {
    }

    public SafetyException(string message, Exception inner) : base(message, inner)
    {
    }
}