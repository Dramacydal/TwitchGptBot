using System.Globalization;
using System.Text.RegularExpressions;

namespace TwitchGpt.Gpt.Entities;

public class WeightedMessageData
{
    public float Probability { get; set; }
    public string UserName { get; set; }
    public string Text { get; set; }

    public static WeightedMessageData? Extract(string text)
    {
        var m = Regex.Match(text, @"\[(\d\.\d)\:([a-z0-9_]+)](.+)");
        if (!m.Success)
            return null;

        return new()
        {
            Probability = float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
            UserName = m.Groups[2].Value,
            Text = m.Groups[3].Value.Trim(' ', ':')
        };
    }
}
