namespace TwitchGpt.Helpers;

public class DisposableContext : IDisposable
{
    private readonly Action _exit;

    public DisposableContext(Action entry, Action exit)
    {
        _exit = exit;
        entry.Invoke();
    }

    public void Dispose() => _exit.Invoke();
}
