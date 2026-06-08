using System.Text.RegularExpressions;
using BoostyLib;
using Newtonsoft.Json.Linq;
using TwitchGpt.Api;
using TwitchGpt.Entities;
using TwitchGpt.Gpt.Factories;
using TwitchGpt.Handlers;
using TwitchGpt.Tui;

namespace TwitchGpt;

internal abstract class Program
{
    class RunParams
    {
        private Dictionary<string, string> _args = new();

        private RunParams(Dictionary<string, string> args)
        {
            _args = args;
        }

        public static RunParams Load(string[] commandLineArgs)
        {
            var cliArgs = ParseCli(commandLineArgs);
            var dict = new Dictionary<string, string>();

            if (cliArgs.TryGetValue("channel-config", out var configPath))
            {
                if (!File.Exists(configPath))
                    throw new FileNotFoundException($"Channel config file not found: {configPath}");

                var obj = JObject.Parse(File.ReadAllText(configPath));
                foreach (var p in obj.Properties().Where(p => p.Value.Type != JTokenType.Null))
                    dict[p.Name] = p.Value.ToString();
            }

            // CLI args override file values
            foreach (var (key, value) in cliArgs)
                dict[key] = value;

            return new RunParams(dict);
        }

        private static Dictionary<string, string> ParseCli(string[] args)
        {
            var dict = new Dictionary<string, string>();
            foreach (var arg in args)
            {
                var m = Regex.Match(arg, @"--([a-z0-9_\-]+)=(.+)", RegexOptions.IgnoreCase);
                if (m.Success)
                    dict[m.Groups[1].Value] = m.Groups[2].Value;
            }
            return dict;
        }

        public bool TryGetString(string key, out string? value)
        {
            if (_args.TryGetValue(key, out value) && !string.IsNullOrEmpty(value))
                return true;
            value = null;
            return false;
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
        RunParams namedArgs;
        try
        {
            namedArgs = RunParams.Load(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return;
        }

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

        if (namedArgs.TryGetString("roles-dir", out var rolesDir))
            ModelFactory.RolesDir = rolesDir;

        // Initialize TUI before anything else so NLog output goes there
        TuiApplication.Init();

        // Run the bot on a background task — TUI owns the main thread
        var botTask = Task.Run(async () =>
        {
            try
            {
                var api = await CredentialsFactory.GetTwitchBotCredentials(botId);
                var bot = await Bot.Create(api, channel.ToString());

                if (namedArgs.TryGetString("boosty-channel", out var boostyChannel) &&
                    namedArgs.TryGetString("boosty-api-name", out var boostyApiName))
                {
                    BoostyApiCredentials boostyApiCredentials;
                    try
                    {
                        boostyApiCredentials = await CredentialsFactory.GetBoostyCredentials(boostyApiName);
                    }
                    catch (Exception ex)
                    {
                        TuiApplication.AppendLog($"Boosty error: {ex.Message}");
                        return;
                    }

                    var boostyApi = new BoostyApi(new()
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
                                ["User-Agent"] =
                                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"
                            }
                        }
                    );

                    bot.BoostyApi = new BoostyApiCaller(boostyApiCredentials);
                    bot.BoostyClient = new StreamClient(boostyChannel, boostyApi);
                }

                if (namedArgs.TryGetBool("messages-to-log", out var messagesToLog))
                    bot.SetDryRun(messagesToLog);

                if (namedArgs.TryGetString("voice-trigger-words", out var triggerWordsRaw))
                    bot.VoiceTriggerWords = triggerWordsRaw!
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                // Wire up TUI commands once we have a bot instance
                TuiApplication.OnCommand = cmd => HandleLocalCommand(cmd, bot);
                TuiApplication.OnQuit = () => _ = Task.Run(bot.Stop);

                await bot.Start();

                if (namedArgs.TryGetInt("watch", out var watchEnabled))
                    bot.SetWatchEnabled(watchEnabled > 0);

                if (namedArgs.TryGetInt("dialogs", out var dialogsEnabled))
                    bot.SetDialogsEnabled(dialogsEnabled > 0);

                TuiApplication.AppendLog("Bot started. Type 'help' for commands.");

                await bot.WaitForCompletion();

                TuiApplication.AppendLog("Bot stopped.");
            }
            catch (Exception ex)
            {
                TuiApplication.AppendLog($"Fatal error: {ex.GetType().Name}: {ex.Message}");
                await Task.Delay(3000);
            }
            finally
            {
                TuiApplication.RequestStop();
            }
        });

        // TUI event loop — blocks until the window closes
        TuiApplication.Run();
        TuiApplication.Shutdown();

        // Wait for the bot task to finish cleanly
        await botTask;
    }

    private static async Task HandleLocalCommand(string input, Bot bot)
    {
        var cmd = input.Split(' ', 2)[0].ToLowerInvariant();

        switch (cmd)
        {
            case "q":
            case "quit":
            case "exit":
                await bot.Stop();
                TuiApplication.RequestStop();
                return;
            case "help":
                TuiApplication.AppendLog(
                    "Commands: quit, reload, suspend, resume, reset, role, " +
                    "togglewatch, watchperiod, toggledialog, ignore, unignore, " +
                    "resolve, category, snapshotcount, messagelogsize, model");
                return;
        }

        // Forward everything else to MessageHandler
        await bot.HandleLocalCommand(input, msg => TuiApplication.AppendLog(msg));
    }
}
