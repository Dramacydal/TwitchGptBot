using System.Text.Json.Nodes;
using NLog;
using SixLabors.ImageSharp;
using TwitchGpt.Gpt.Entities;
using TwitchGpt.Handlers;
using TwitchGpt.Helpers;
using TwitchLib.Api.Helix.Models.Users.GetUsers;

namespace TwitchGpt.Gpt;

public class GptWatcher(Bot bot, User channelUser)
{
    public GptMessagesProcessor messagesProcessor { get; } = new GptMessagesProcessor(bot, channelUser);
    public GptDialogueProcessor dialogueProcessor { get; } = new GptDialogueProcessor(bot, channelUser);

    public void Reset()
    {
        messagesProcessor.Reset();
        dialogueProcessor.Reset();
    }

    private StreamInfo info = new();

    public async Task RunAsync(CancellationToken token)
    {
        var t1 = messagesProcessor.Run(token);
        var t2 = dialogueProcessor.Run(token);

        var t3 = Task.Run(async () =>
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
                    if (info.AvailableBttvEmotes == null)
                    {
                        try
                        {
                            info.AvailableBttvEmotes = await LoadBetterTtvEmotes();
                        }
                        catch (Exception ex)
                        {
                            this.Logger.Error($"Failed to load BTTV emote list: {ex.Message}");
                        }
                    }
                    
                    var streams =
                        await bot.Api.Call(api => api.Helix.Streams.GetStreamsAsync(userLogins: [channelUser.Login]));
                    if (streams.Streams.Length > 0)
                    {
                        info.Online = true;
                        var stream = streams.Streams[0];
                        info.Stream = stream;

                        var uri = await StreamResolver.GetRmptUrl(channelUser.Login);
                        if (string.IsNullOrEmpty(uri.ToString()))
                            throw new Exception("Empty uri resolved");

                        var res = await FFMpegHelper.SnapshotAsync(uri, "temp.png");
                        if (!res)
                            throw new Exception("Failed to snapshot");

                        using var img = await Image.LoadAsync("temp.png");
                        // img.Mutate(x => x.Resize(1280, 720));
                        await img.SaveAsync("temp.jpg");

                        info.Snapshot = "temp.jpg";

                        info.NeedUpdate = true;
                    }
                    else
                    {
                        info.NeedUpdate = info.Online || firstRun;
                        info.Online = false;
                    }

                    firstRun = false;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Snapshot watcher task error: {ex.GetType()}: {ex.Message}");
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
        }, token);
        
        for (; !token.IsCancellationRequested;)
        {
            await ProcessInfo().ConfigureAwait(false);

            await Task.Delay(200);
        }

        await t1;
        await t2;
        await t3;
    }

    private async Task<List<string>?> LoadBetterTtvEmotes()
    {
        var client = new HttpClient();

        var result = await client.GetAsync($"https://api.betterttv.net/3/cached/users/twitch/{channelUser.Id}");
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
        try
        {
            if (MessageHandler.IsSuspended)
                return;
            
            if (!info.NeedUpdate)
                return;

            info.NeedUpdate = false;

            await messagesProcessor.OnStreamInfo(info).ConfigureAwait(false);
            await dialogueProcessor.OnStreamInfo(info).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Error($"{ex.GetType()}: {ex.Message}");
            return;
        }
    }
    
    protected ILogger Logger => Logging.Logger.Instance(nameof(GptWatcher));
}
