﻿using System.Net;
using GptLib;
using GptLib.Providers;
using GptLib.Providers.GoogleGemini;
using NLog;
using TwitchGpt.Config;
using TwitchGpt.Gpt.Entities;
using TwitchGpt.Gpt.Enums;

namespace TwitchGpt.Gpt;

public class Client
{
    private readonly GptClient _client;

    private readonly Settings _settings;

    public RoleModel? Role { get; set; }
    
    public ClientType ClientType { get; private set; }

    public bool IsBusy { get; set; }

    public Conversation Conversation { get; set; }

    private IWebProxy? GetProxy()
    {
        try
        {
            var config = ConfigManager.GetPath<Proxy>("gpt_proxy");
            if (string.IsNullOrEmpty(config?.Url))
                return null;

            return new WebProxy()
            {
                Address = new Uri(config.Url),
                Credentials =
                    new NetworkCredential(config.User, config.Password)
            };
        }
        catch (Exception ex)
        {
            return null;
        }
    }

    public Client(ClientType type, string token, RoleModel? role = null)
    {
        ClientType = type;

        Conversation = new() { UsageContext = $"twitch_bots_{type.GetType()}" };
        
        Role = role ?? ClientFactory.DefaultRole;

        
        _client = new GptClient(type.ToString(), new MongoCache())
        {
            Provider = new GoogleGeminiProvider(token),
            ModelName = GoogleGeminiProvider.ModelGemini20FlashExp,
            // Model = GoogleGeminiProvider.ModelGemini15Flash,
            Proxy = GetProxy(),
        };
        
        _settings = new Settings();
        _settings.Instructions = Role.Instructions.Where(_ => !_.StartsWith("#")).ToList();

        _settings.ResponseMimeType = "text/plain";
    }
    
    public async Task<GptResponse?> Ask(GptQuestion question, RoleModel model)
    {
        // if (!await WaitHelper.WaitUntil(() => !IsBusy, TimeSpan.FromSeconds(2)))
        //     throw new ClientBusyException();

        // using var ctx = new DisposableContext(() => IsBusy = true, () => IsBusy = false);

        _settings.Instructions = model.Instructions;
        _settings.SafetySettings = model.SafetySettings;

        var now = DateTime.Now;

        try
        {
            var copy = Conversation.History.Lock(h => h.Copy());
                
            var res = await _client.AskQuestion(question, copy, _settings);
            Logger.Info($"GPT request process in {(DateTime.Now - now).TotalSeconds}");

            if (res != null && res.Success)
                Conversation.History.Lock(h => h.Add([res.Question, res.Answer]));

            return res;
        }
        catch
        {
            Logger.Info($"GPT request process in {(DateTime.Now - now).TotalSeconds}, with error");
            throw;
        }
    }
    
    protected ILogger Logger => Logging.Logger.Instance(nameof(GptWatcher));
}
