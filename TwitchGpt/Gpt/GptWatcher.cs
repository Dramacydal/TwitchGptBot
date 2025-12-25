using System.Text.Json.Nodes;
using NLog;
using SixLabors.ImageSharp;
using TwitchGpt.Gpt.Entities;
using TwitchGpt.Handlers;
using TwitchGpt.Helpers;
using TwitchLib.Api.Helix.Models.Users.GetUsers;

namespace TwitchGpt.Gpt;

public class GptWatcher
{
    public GptMessagesProcessor MessagesProcessor { get; private init; }
    public GptDialogueProcessor DialogueProcessor { get; private init; }

    public void Reset()
    {
        MessagesProcessor.Reset();
        DialogueProcessor.Reset();
    }

    private TwitchStreamInfo _twitchStream = new();

    private BoostyStreamInfo _boostyStream = new();
    
    private readonly Bot _bot;
    
    private readonly User _channelUser;

    private GptWatcher(Bot bot, User channelUser)
    {
        _bot = bot;
        _channelUser = channelUser;
    }

    public static async Task<GptWatcher> Create(Bot bot, User channelUser)
    {
        return new GptWatcher(bot, channelUser)
        {
            MessagesProcessor = await GptMessagesProcessor.Create(bot, channelUser),
            DialogueProcessor = await GptDialogueProcessor.Create(bot, channelUser),
        };
    }

    public async Task RunAsync(CancellationToken token)
    {
        var t1 = MessagesProcessor.Run(token);
        var t2 = DialogueProcessor.Run(token);

        var t3 = Task.Run(async () => await TwitchStreamChecker(token), token);
        var t4 = Task.Run(async () => await BoostyStreamChecker(token), token);

        for (; !token.IsCancellationRequested;)
        {
            await ProcessInfo().ConfigureAwait(false);

            await Task.Delay(200);
        }

        await Task.WhenAll(t1, t2, t3, t4);
    }

    private async Task TwitchStreamChecker(CancellationToken token)
    {
        var firstRun = true;
        for (; !token.IsCancellationRequested;)
        {
            if (MessageHandler.IsSuspended)
            {
                await Task.Delay(200);
                continue;
            }

            try
            {
                if (_twitchStream.AvailableBttvEmotes == null)
                {
                    try
                    {
                        _twitchStream.AvailableBttvEmotes = await LoadBetterTtvEmotes();
                    }
                    catch (Exception ex)
                    {
                        this.Logger.Error($"Failed to load BTTV emote list: {ex.Message}");
                    }
                }

                var streams = await _bot.TwitchApi.Call(api =>
                    api.Helix.Streams.GetStreamsAsync(userLogins: [_channelUser.Login]));
                if (streams.Streams.Length > 0)
                {
                    _twitchStream.Online = true;
                    var stream = streams.Streams[0];
                    _twitchStream.Stream = stream;

                    var fileName = "temp_twitch.jpg";
                    await SnapshotHelper.TakeTwitchSnapshot(_channelUser.Login, fileName);

                    // using var img = await Image.LoadAsync(fileName);
                    // img.Mutate(x => x.Resize(1280, 720));
                    
                    // var jpgName = Path.ChangeExtension(fileName, "jpg");
                    // await img.SaveAsync(jpgName);

                    _twitchStream.Snapshot = fileName;

                    _twitchStream.NeedUpdate = true;
                }
                else
                {
                    _twitchStream.NeedUpdate = _twitchStream.Online || firstRun;
                    _twitchStream.Online = false;
                }

                firstRun = false;
            }
            catch (Exception ex)
            {
                _twitchStream.NeedUpdate = false;
                Logger.Error($"Twitch snapshot watcher task error: {ex.GetType()}: {ex.Message}");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(25), token);
            }
            catch
            {
                break;
            }
        }
    }

    private async Task BoostyStreamChecker(CancellationToken token)
    {
        if (_bot.BoostyClient == null)
            return;
        
        var firstRun = true;
        for (; !token.IsCancellationRequested;)
        {
            if (MessageHandler.IsSuspended)
            {
                await Task.Delay(200);
                continue;
            }
            
            try
            {
                var stream = await _bot.BoostyApi.Call(api => api.VideoStream.Get(_bot.BoostyClient.ChannelName));
                if (stream != null && stream.VideoStreamData.Count > 0)
                {
                    var playerData = stream.VideoStreamData[0].PlayerUrls.FirstOrDefault(p => p.Type == "live_hls");
                    if (playerData == null || playerData.Url == null)
                    {
                        _boostyStream.NeedUpdate = _boostyStream.Online;
                        _boostyStream.Online = false;
                    }
                    else
                    {
                        var fileName = "temp_boosty.jpg";
                    
                        await SnapshotHelper.TakeBoostySnapshot(playerData.Url, fileName, new()
                        {
                            ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"
                        });
                        
                        using var img = await Image.LoadAsync(fileName);
                        // img.Mutate(x => x.Resize(1280, 720));

                        var jpgName = Path.ChangeExtension(fileName, "jpg");
                        await img.SaveAsync(jpgName);

                        _boostyStream.Snapshot = jpgName;
                        _boostyStream.Stream = stream;
                        _boostyStream.Online = true;
                        _boostyStream.NeedUpdate = true;
                    }
                }
                else
                {
                    _boostyStream.NeedUpdate = _boostyStream.Online || firstRun;
                    _boostyStream.Online = false;
                }

                firstRun = false;
            }
            catch (Exception ex)
            {
                _boostyStream.NeedUpdate = false;
                Logger.Error($"Boosty snapshot watcher task error: {ex.GetType()}: {ex.Message}");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(25), token);
            }
            catch
            {
                break;
            }
        }
    }

    private async Task<List<string>?> LoadBetterTtvEmotes()
    {
        var client = new HttpClient();

        var result = await client.GetAsync($"https://api.betterttv.net/3/cached/users/twitch/{_channelUser.Id}");
        if (!result.IsSuccessStatusCode)
            return null;

        var node = await JsonNode.ParseAsync(await result.Content.ReadAsStreamAsync());

        if (!node.AsObject().TryGetPropertyValue("channelEmotes", out var channelEmotes))
            return null;

        List<string> emotes = new();
        foreach (var item in channelEmotes.AsArray())
        {
            emotes.Add(item["code"].GetValue<string>());
        }

        return emotes;
    }

    private async Task ProcessInfo()
    {
        if (MessageHandler.IsSuspended)
            return;
        
        try
        {
            if (_twitchStream.NeedUpdate)
            {
                _twitchStream.NeedUpdate = false;

                await MessagesProcessor.OnTwitchStreamInfo(_twitchStream).ConfigureAwait(false);
                // await dialogueProcessor.OnTwitchStreamInfo(_twitchStream).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"{ex.GetType()}: {ex.Message}");
            return;
        }
        
        try
        {
            if (_boostyStream.NeedUpdate)
            {
                _boostyStream.NeedUpdate = false;

                await MessagesProcessor.OnBoostyStreamInfo(_boostyStream).ConfigureAwait(false);
                // await dialogueProcessor.OnBoostyStreamInfo(_boostyStream).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"{ex.GetType()}: {ex.Message}");
            return;
        }
    }

    protected ILogger Logger => Logging.Logger.Instance(nameof(GptWatcher));
}
