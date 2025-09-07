using System.Text.RegularExpressions;

namespace TwitchGpt.Gpt.Entities;

public class WeightedMessageData
{
    public string UserName { get; set; }
    public string Text { get; set; }

    public static WeightedMessageData? Extract(string text)
    {
        var m = Regex.Match(text, @"\[([a-z0-9_]+)](.+)");
        if (!m.Success)
            return null;

        return new()
        {
            UserName = m.Groups[1].Value,
            Text = m.Groups[2].Value.Trim(' ', ':')
        };
    }
}
