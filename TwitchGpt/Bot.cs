using System.Text.Json;
using BoostyLib;
using NLog;
using TwitchGpt.Api;
using TwitchGpt.Entities;
using TwitchGpt.Gpt;
using TwitchGpt.Handlers;
using TwitchGpt.TwitchRoutines;
using TwitchLib.Api.Helix.Models.Users.GetUsers;
using TwitchLib.Client;

namespace TwitchGpt;

public class Bot
{
    private TwitchClient? _client;

    private MessageHandler? _messageHandler;

    private CancellationTokenSource cts = new();
    private readonly TwitchApiCredentials _credentials;
    private readonly string _channelId;
    
    private GptWatcher _gptHandler;
    
    private Announcer _announcer;

    public Bot(TwitchApiCredentials credentials, string channelId)
    {
        _credentials = credentials;
        _channelId = channelId;

        TwitchApi = new TwitchApiCaller(credentials);
    }

    public TwitchApiCaller TwitchApi { get; private set; }
    
    public BoostyApiCaller? BoostyApi { get; set; }
    
    public StreamClient? BoostyClient { get; set; }

    public TwitchClient Client => _client;

    private Task? _gptTask;
    private Task? _announcerTask;

    public async Task Start()
    {
        var response = await TwitchApi.Call(api => api.Helix.Users.GetUsersAsync(ids: [_channelId]));
        if (response.Users.Length == 0)
            throw new Exception($"User with id {_channelId} not found");

        var user = response.Users.First();

        _gptHandler = new GptWatcher(this, user);
        _announcer = new Announcer(this, user);

        _messageHandler = new MessageHandler(this, _credentials, user);

        InitializeClient(user);

        _gptTask = _gptHandler.RunAsync(cts.Token);
        _announcerTask = _announcer.RunAsync(cts.Token);
    }

    public async Task WaitForCompletion()
    {
        if (_gptTask != null)
            await _gptTask;
        if (_announcerTask != null)
            await _announcerTask;
    }

    private static int clientCounter = 0;
    
    private void InitializeClient(User user)
    {
        ++clientCounter;

        var currentClientCounter = clientCounter;
        
        _client = new TwitchClient();
        _client.Initialize(new(_credentials.ApiUserName, _credentials.AccessToken), autoReListenOnExceptions: false);

        var credenatialsProblem = false;
        
        _client.OnConnected += (sender, args) =>
        {
            Logger.Trace($"{currentClientCounter} OnConnected");
            _client.JoinChannel(user.Login);
        };
        _client.OnJoinedChannel += (sender, args) => { Logger.Trace($"{currentClientCounter} OnJoinedChannel {user.Login}"); };
        _client.OnLog += (sender, args) => { Logger.Trace(args.Data); };
        _client.OnError += (sender, args) => { Logger.Error($"{args.Exception.GetType()}: {args.Exception.Message}"); };
        _client.OnReconnected += (sender, args) => { Logger.Trace($"{currentClientCounter} OnReconnected"); };
        _client.OnDisconnected += (sender, args) =>
        {
            Logger.Trace($"{currentClientCounter} OnDisconnected");

            if (credenatialsProblem)
            {
                credenatialsProblem = false;
                CredentialsFactory.Reload(_credentials).Wait();
                _client.SetConnectionCredentials(new(_credentials.ApiUserName, _credentials.AccessToken));    
            }
            // InitializeClient(user);
        };
        _client.OnMessageReceived += async (sender, args) =>
        {
            try
            {
                await _messageHandler?.HandleMessage(args.ChatMessage, _gptHandler);
            }
            catch (Exception ex)
            {
                Logger.Error($"Uncaugh exception when handling chat message: {ex.GetType()}: {ex.Message}");
            }
        };
        _client.OnChatCommandReceived += async (sender, args) =>
        {
            try
            {
                await _messageHandler?.HandleCommand(args.Command, _gptHandler);
            }
            catch (Exception ex)
            {
                Logger.Error($"Uncaugh exception when handling command: {ex.GetType()}: {ex.Message}");
            }
        };
        _client.OnUnaccountedFor += async (sender, args) => Logger.Trace($"{currentClientCounter} Unaccounted For: {args.RawIRC}");
        _client.OnIncorrectLogin += (sender, args) =>
        {
            Logger.Trace($"{currentClientCounter} Incorrect login, {args.Exception.GetType()}: {args.Exception.Message}");
            // _client.Disconnect();
            
            Logger.Info(JsonSerializer.Serialize(_client.ConnectionCredentials));
            credenatialsProblem = true;
        };
        
        _client.Connect();
    }

    private async Task SomeTask()
    {
        for (; !cts.Token.IsCancellationRequested;)
        {
            await Task.Delay(500);
        }
    }

    private ILogger Logger => Logging.Logger.Instance(_credentials.ApiUserName);

    public async Task Restart()
    {
        Stop();
        await Start();
    }

    public void Stop()
    {
        cts.Cancel();
        if (_client == null)
            return;

        _client.Disconnect();
    }

    public void SetWatchEnabled(bool on)
    {
        _messageHandler.SetWatchEnabled(on);
    }
    
    public void SetDialogsEnabled(bool on)
    {
        _messageHandler.SetDialogsEnabled(on);
    }

    public async Task ReloadAnnouncements()
    {
        await _announcer.Reload();
    }
}
