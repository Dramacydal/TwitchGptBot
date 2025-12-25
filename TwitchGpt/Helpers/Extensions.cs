namespace TwitchGpt.Helpers;

public static class Extensions
{
    public static T Random<T>(this IEnumerable<T> enumerable)
    {
        var r = new Random();
        var count = enumerable.Count();
        return count == 0 ? default : enumerable.ElementAt(r.Next(0, count - 1));
    }
    
    public static async Task<T> ConfigureAwaitFalse<T>(this Task<T> task)
    {
        return await task.ConfigureAwait(false);
    }

    public static async Task ConfigureAwaitFalse(this Task task)
    {
        await task.ConfigureAwait(false);
    }
}