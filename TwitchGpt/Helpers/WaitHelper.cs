namespace TwitchGpt.Helpers;

public class WaitHelper
{
    public static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan maxTime,
        CancellationToken? tokenSource = null)
    {
        var now = DateTime.UtcNow;

        while (DateTime.UtcNow - now < maxTime && (!tokenSource.HasValue || !tokenSource.Value.IsCancellationRequested))
        {
            if (condition.Invoke())
                return true;

            await Task.Delay(25);
        }

        return false;
    }
}