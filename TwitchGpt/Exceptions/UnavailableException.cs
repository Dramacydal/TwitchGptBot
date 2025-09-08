namespace TwitchGpt.Exceptions;

public class UnavailableException : Exception
{
    public UnavailableException(string message) : base(message)
    {
    }

    public UnavailableException(string message, Exception inner) : base(message, inner)
    {
    }
}