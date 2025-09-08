namespace TwitchGpt.Helpers;

public static class Extensions
{
    public static T Random<T>(this IEnumerable<T> enumerable)
    {
        if (enumerable == null)
            throw new ArgumentNullException(nameof(enumerable));

        var r = new Random();
        var count = enumerable.Count();
        return count == 0 ? default : enumerable.ElementAt(r.Next(0, count - 1));
    }
}