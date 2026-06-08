using System.Text.Json.Nodes;
using NLog;
using SixLabors.ImageSharp;
using TwitchGpt.Database.Mappers;
using TwitchGpt.Gpt.Audio;
using TwitchGpt.Gpt.Entities;
using TwitchGpt.Handlers;
using TwitchGpt.Helpers;
using TwitchLib.Api.Helix.Models.Users.GetUsers;

namespace TwitchGpt.Gpt;

public class StreamWatcher
{
    public AiMessagesProcessor MessagesProcessor { get; private init; }

    public void Reset()
    {
        MessagesProcessor.Reset();
    }

    private TwitchStreamInfo _twitchStreamInfo;

    private BoostyStreamInfo _boostyStreamInfo;

    private AudioChunkWriter? _audioChunkWriter;
    private AudioTranscriptionService? _audioTranscriptionService;
    private VoiceCommandProcessor? _voiceCommandProcessor;

    private readonly Bot _bot;

    private readonly User _channelUser;

    private StreamWatcher(Bot bot, User channelUser)
    {
        _bot = bot;
        _channelUser = channelUser;
    }

    public static async Task<StreamWatcher> Create(Bot bot, User channelUser)
    {
        var twitchStreamInfo = new TwitchStreamInfo();
        var boostyStreamInfo = new BoostyStreamInfo();

        var watcher = new StreamWatcher(bot, channelUser)
        {
            MessagesProcessor = await AiMessagesProcessor.Create(bot, channelUser, twitchStreamInfo, boostyStreamInfo),
            _boostyStreamInfo = boostyStreamInfo,
            _twitchStreamInfo = twitchStreamInfo,
        };

        await watcher.SetupAudioPipelineAsync();

        return watcher;
    }

    private async Task SetupAudioPipelineAsync()
    {
        var triggerWords = _bot.VoiceTriggerWords;
        if (triggerWords == null || triggerWords.Length == 0)
        {
            Logger.Info("Voice pipeline disabled: 'voice-trigger-words' not set in channel config");
            return;
        }

        // Reuse the first key from the existing OpenRouter pool
        var keys = await TokenMapper.Instance.GetOpenRouterKeyPool();
        if (keys.Count == 0)
        {
            Logger.Warn("Voice pipeline disabled: no OpenRouter keys available");
            return;
        }

        var audioClient = new OpenRouterAudioClient(keys[0]);
        _audioChunkWriter = new AudioChunkWriter(_channelUser.Login, 10);
        _audioTranscriptionService = new AudioTranscriptionService(_audioChunkWriter, audioClient, triggerWords);
        _voiceCommandProcessor = new VoiceCommandProcessor(_audioTranscriptionService, MessagesProcessor);

        Logger.Info($"Voice pipeline ready. Triggers: [{string.Join(", ", triggerWords)}]");
    }

    public async Task RunAsync(CancellationToken token)
    {
        var t1 = MessagesProcessor.Run(token).ConfigureAwaitFalse();
        var t2 = TwitchStreamChecker(token).ConfigureAwaitFalse();
        var t3 = BoostyStreamChecker(token).ConfigureAwaitFalse();

        // Audio pipeline tasks — only run if voice is configured
        if (_audioChunkWriter != null && _audioTranscriptionService != null && _voiceCommandProcessor != null)
        {
            await _audioChunkWriter.StartAsync(token);

            var t4 = _audioTranscriptionService.RunAsync(token).ConfigureAwaitFalse();
            var t5 = _voiceCommandProcessor.RunAsync(token).ConfigureAwaitFalse();

            await Task.WhenAll(t1, t2, t3, t4, t5);
        }
        else
        {
            await Task.WhenAll(t1, t2, t3);
        }
    }

    private async Task TwitchStreamChecker(CancellationToken token)
    {
        for (; !token.IsCancellationRequested;)
        {
            if (MessageHandler.IsSuspended)
            {
                await Task.Delay(200);
                continue;
            }

            try
            {
                if (_twitchStreamInfo.AvailableBttvEmotes == null)
                {
                    try
                    {
                        _twitchStreamInfo.AvailableBttvEmotes = await LoadBetterTtvEmotes();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to load BTTV emote list: {ex.Message}");
                    }
                }

                var streams = await _bot.TwitchApi.Call(api =>
                    api.Helix.Streams.GetStreamsAsync(userLogins: [_channelUser.Login]));
                if (streams.Streams.Length > 0)
                {
                    _twitchStreamInfo.Online = true;
                    var stream = streams.Streams[0];
                    _twitchStreamInfo.Stream = stream;

                    var fileName = "temp_twitch.jpg";
                    await SnapshotHelper.TakeTwitchSnapshot(_channelUser.Login, fileName);

                    // using var img = await Image.LoadAsync(fileName);
                    // img.Mutate(x => x.Resize(1280, 720));
                    
                    // var jpgName = Path.ChangeExtension(fileName, "jpg");
                    // await img.SaveAsync(jpgName);

                    _twitchStreamInfo.AddSnapShot(FileSourceInfo.FromFilePath(fileName));
                }
                else
                {
                    _twitchStreamInfo.Online = false;
                    _twitchStreamInfo.SnapShots.Clear();
                }
            }
            catch (Exception ex)
            {
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
                        _boostyStreamInfo.Online = false;
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

                        _boostyStreamInfo.AddSnapShot(FileSourceInfo.FromFilePath(jpgName));
                        _boostyStreamInfo.Stream = stream;
                        _boostyStreamInfo.Online = true;
                    }
                }
                else
                    _boostyStreamInfo.Online = false;
            }
            catch (Exception ex)
            {
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
            emotes.Add(item["code"].GetValue<string>());

        return emotes;
    }

    protected ILogger Logger => Logging.Logger.Instance(nameof(StreamWatcher));
}
