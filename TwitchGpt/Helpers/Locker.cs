namespace TwitchGpt.Helpers;

public class Locker
{
    private readonly Lock _lock = new();

    public class LockObject : IDisposable
    {
        private readonly Lock _lock;

        public LockObject(Lock l)
        {
            _lock = l;
            _lock.Enter();
        }

        public void Dispose() => _lock.Exit();
    }

    public LockObject Acquire() => new(_lock);
}