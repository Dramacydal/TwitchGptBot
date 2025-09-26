using System.Text.RegularExpressions;
using BoostyLib;
using TwitchGpt.Api;
using TwitchGpt.Database.Mappers;
using TwitchGpt.Entities;
using TwitchGpt.Gpt;
using TwitchGpt.Gpt.Enums;

namespace TwitchGpt;

internal abstract class Program
{
    class RunParams
    {
        private Dictionary<string, string> _args = new();
        
        public RunParams(string[] args)
        {
            foreach (var arg in args)
            {
                var m = Regex.Match(arg, @"--([a-z0-9\-]+)=([a-z0-9]+)", RegexOptions.IgnoreCase);
                if (!m.Success)
                    continue;

                _args[m.Groups[1].Value] = m.Groups[2].Value;
            }            
        }

        public bool TryGetString(string key, out string? value)
        {
            return _args.TryGetValue(key, out value);
        }

        public bool TryGetInt(string key, out int value)
        {
            value = 0;
            return _args.TryGetValue(key, out var strVal) && int.TryParse(strVal, out value);
        }
        
        public bool TryGetBool(string key, out bool value)
        {
            value = false;
            return _args.TryGetValue(key, out var strVal) && bool.TryParse(strVal, out value);
        }
    }
    
    public static async Task Main(string[] args)
    {
        var namedArgs = new RunParams(args);
        if (!namedArgs.TryGetString("bot", out var strBot))
        {
            Console.WriteLine("--bot argument is missing.");
            return;
        }

        if (!int.TryParse(strBot, out var botId))
        {
            Console.WriteLine("--bot argument must be an integer.");
            return;
        }

        if (!namedArgs.TryGetInt("channel", out var channel))
        {
            Console.WriteLine("--channel argument is missing or not an integer.");
            return;
        }

        var api = await CredentialsFactory.GetTwitchBotCredentials(botId);

        var bot = new Bot(api, channel.ToString());

        if (namedArgs.TryGetString("boosty-channel", out var boostyChannel) &&
            namedArgs.TryGetString("boosty-api-name", out var boostyApiName))
        {
            BoostyApiCredentials? boostyApiCredentials;
            try
            {
                boostyApiCredentials = await CredentialsFactory.GetBoostyCredentials(boostyApiName);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return;
            }

            BoostyApi boostyApi = new(new()
                {
                    Credentials = new()
                    {
                        AccessToken = boostyApiCredentials.AccessToken,
                        RefreshToken = boostyApiCredentials.RefreshToken,
                        DeviceId = boostyApiCredentials.DeviceId,
                        ExpiresAt = boostyApiCredentials.ExpiresAt,
                    },
                    Headers = new()
                    {
                        ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36" 
                    }
                }
            );

            bot.BoostyApi = new BoostyApiCaller(boostyApiCredentials);
            bot.BoostyClient = new StreamClient(boostyChannel, boostyApi);
        }
        
        Console.CancelKeyPress += async (_, e) =>
        {
            Console.WriteLine("Ctrl+C received");
            e.Cancel = true;
            await bot.Stop();
        };

        if (namedArgs.TryGetBool("messages-to-log", out var messagesToLog))
            bot.SetDryRun(messagesToLog);

        Console.WriteLine("Started");

        await bot.Start();

        if (namedArgs.TryGetInt("watch", out var watchEnabled))
            bot.SetWatchEnabled(watchEnabled > 0);
        
        if (namedArgs.TryGetInt("dialogs", out var dialogsEnabled))
            bot.SetDialogsEnabled(dialogsEnabled > 0);
        
        await bot.WaitForCompletion();

        Console.WriteLine("Stopped");
    }
}
