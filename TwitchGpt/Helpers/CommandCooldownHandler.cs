namespace TwitchGpt.Helpers;

/// <summary>
/// Tracks per-group command cooldowns. All aliases for a command share the same group key.
/// </summary>
public class CommandCooldownHandler
{
    private readonly Dictionary<string, DateTime> _expirations = new();
    private readonly Lock _lock = new();

    /// <summary>Returns true if the group is still on cooldown.</summary>
    public bool IsOnCooldown(string group)
    {
        lock (_lock)
            return _expirations.TryGetValue(group, out var exp) && exp > DateTime.Now;
    }

    /// <summary>Starts a cooldown for the group.</summary>
    public void Set(string group, TimeSpan duration)
    {
        lock (_lock)
            _expirations[group] = DateTime.Now.Add(duration);
    }

    /// <summary>Returns how much time is left on the cooldown, or zero if not on cooldown.</summary>
    public TimeSpan Remaining(string group)
    {
        lock (_lock)
        {
            if (_expirations.TryGetValue(group, out var exp) && exp > DateTime.Now)
                return exp - DateTime.Now;
            return TimeSpan.Zero;
        }
    }
}
