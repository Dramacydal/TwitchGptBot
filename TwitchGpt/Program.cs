using System.Text.RegularExpressions;
using TwitchGpt.Api;

namespace TwitchGpt;

internal abstract class Program
{
    private static Dictionary<string, string> ExtractArgs(string[] args)
    {
        Dictionary<string, string> ret = [];

        foreach (var arg in args)
        {
            var m = Regex.Match(arg, @"--([a-z0-9]+)=([a-z0-9]+)", RegexOptions.IgnoreCase);
            if (!m.Success)
                continue;

            ret[m.Groups[1].Value] = m.Groups[2].Value;
        }

        return ret;
    }

    public static async Task Main(string[] args)
    {
        var namedArgs = ExtractArgs(args);
        if (!namedArgs.TryGetValue("bot", out var strBot))
        {
            Console.WriteLine("--bot argument is missing.");
            return;
        }

        if (!int.TryParse(strBot, out var botId))
        {
            Console.WriteLine("--bot argument must be an integer.");
            return;
        }

        if (!namedArgs.TryGetValue("channel", out var channel))
        {
            Console.WriteLine("--channel argument is missing.");
            return;
        }

        if (!int.TryParse(channel, out _))
        {
            Console.WriteLine("--channel argument must be an integer.");
            return;
        }

        var api = await CredentialsFactory.GetByBotId(botId);

        var bot = new Bot(api, channel);

        Console.CancelKeyPress += (_, e) =>
        {
            Console.WriteLine("Ctrl+C received");
            e.Cancel = true;
            bot.Stop();
        };

        Console.WriteLine("Started");

        await bot.Start();

        Console.WriteLine("Stopped");
    }
}
