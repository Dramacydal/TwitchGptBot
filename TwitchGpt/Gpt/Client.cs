using System.Net;
using NLog;
using OpenRouter.NET;
using OpenRouter.NET.Models;
using TwitchGpt.Config;
using TwitchGpt.Exceptions;
using TwitchGpt.Gpt.Entities;
using TwitchGpt.Gpt.Enums;

namespace TwitchGpt.Gpt;

public class Client
{
    private readonly List<Tuple<int, OpenRouterClient>> _aiPool = new();

    private int _poolIndex = 0;
    
    public RoleModel? Role { get; set; }
    
    public ClientType ClientType { get; private set; }

    public HistoryHolder HistoryHolder { get; private set; } = new();

    public bool IsBusy { get; set; }

    public int ProviderHash => _aiPool[_poolIndex].Item1;

    public string Model { get; private set; } = "google/gemini-2.5-flash-lite";
    
    private List<ModelInfo>? _modelInfos;

    public async Task SetModel(string modelId)
    {
        if (_modelInfos == null)
            _modelInfos = await GetOpenRouter().GetModelsAsync();

        if (_modelInfos.All(m => m.Id != modelId))
            throw new Exception($"Model {modelId} not found");

        Model = modelId;
    }

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

    private OpenRouterClient GetOpenRouter()
    {
        return _aiPool[_poolIndex].Item2;
    }

    private ChatCompletionRequest CreateChatCompletionRequest()
    {
        return new ChatCompletionRequest()
        {
            Model = Model,
            Reasoning = new ReasoningConfig() { Enabled = false },
        };
    }

    private string GetPreparedInstructions()
    {
        return string.Join("\r\n", Role.Instructions.Split("\n")
            .Select(l => l.Trim())
            .Where(l => !l.StartsWith("#"))
        );
    }

    public void RotateClient(int excludeHash = 0)
    {
        if (_aiPool.Count == 0)
            return;

        ++_poolIndex;
        if (_poolIndex > _aiPool.Count - 1)
            _poolIndex = 0;

        if (excludeHash != 0 && ProviderHash == excludeHash)
            RotateClient();
    }

    private HttpClient CreateHttpClient(IWebProxy? proxy)
    {
        return new HttpClient(new HttpClientHandler
        {
            UseProxy = proxy != null,
            Proxy = proxy
        });
    }

    private Client(IEnumerable<string> tokenPool)
    {
        foreach (var token in tokenPool)
        {
            _aiPool.Add(new(token.GetHashCode(), new OpenRouterClient(new OpenRouterClientOptions()
            {
                ApiKey = token,
                HttpClient = CreateHttpClient(GetProxy()),
            })));
        }
    }

    public static async Task<Client> Create(ClientType type, IEnumerable<string> tokenPool, RoleModel? role = null)
    {
        return new Client(tokenPool)
        {
            HistoryHolder = HistoryFactory.Create(type),
            Role = role ?? await ClientFactory.GetDefaultRole()
        };
    }

    public async Task<string?> Ask(string question, params AbstractStreamInfo?[] streamInfos)
    {
        return await Ask<string>(question, streamInfos);
    }

    public async Task<T?> Ask<T>(string question, params AbstractStreamInfo?[] streamInfos) where T : class
    {
        // if (!await WaitHelper.WaitUntil(() => !IsBusy, TimeSpan.FromSeconds(2)))
        //     throw new ClientBusyException();

        // using var ctx = new DisposableContext(() => IsBusy = true, () => IsBusy = false);

        var now = DateTime.Now;

        try
        {
            var historyEntries = HistoryHolder.CopyEntries();
            
            PrependStreamInfos(historyEntries, streamInfos);
            
            PrependInstructions(historyEntries);

            var request = CreateChatCompletionRequest();
            request.Messages = historyEntries;

            request.Messages.AddUserMessage(question);

            T? res;
            if (typeof(T) == typeof(string))
            {
                var response = await GetOpenRouter()!.CreateChatCompletionAsync(request);

                var answerText = response.Choices[0].Message.Content?.ToString() ?? "";
                res = (T)(object)answerText;

                HistoryHolder.AddEntries([Message.FromUser(question), Message.FromAssistant(answerText)]);
            }
            else
                throw new Exception("Not implemented yet");

            Logger.Info($"GPT request process in {(DateTime.Now - now).TotalSeconds}");


            return res;
        }
        catch (OpenRouterRateLimitException ex)
        {
            Logger.Info($"GPT request process in {(DateTime.Now - now).TotalSeconds}, with error: {ex.Message}");
            throw new TooManyRequestsException(ex.Message, ex);
        }
        catch (Exception ex)
        {
            Logger.Info($"GPT request process in {(DateTime.Now - now).TotalSeconds}, with error: {ex.Message}");

            if (ex.Message.Contains("The response was blocked due to unknown reasons"))
                throw new UnknownGeminiException(ex.Message, ex);
            if (ex.Message.Contains("RESOURCE_EXHAUSTED"))
                throw new TooManyRequestsException(ex.Message, ex);
            if (ex.Message.Contains("UNAVAILABLE"))
                throw new UnavailableException(ex.Message, ex);
            if (ex.Message.Contains("The response was blocked due to prohibited content"))
                throw new SafetyException(ex.Message, ex);
                                
            throw;
        }
    }

    private void PrependInstructions(List<Message> contents)
    {
        contents.Insert(0, Message.FromSystem(GetPreparedInstructions()));
    }

    private void PrependStreamInfos(List<Message> contents, AbstractStreamInfo?[] streamInfos)
    {
        foreach (var streamInfo in streamInfos)
        {
            if (streamInfo == null)
                continue;

            var streamInfoContent = new List<ContentPart>();
            streamInfoContent.Add(new TextContent(streamInfo.BuildMessage()));
            foreach (var (_, file) in streamInfo.SnapShots)
                streamInfoContent.Add(new ImageContent(
                    $"data:{file.MimeType};base64,{Convert.ToBase64String(file.Blob)}"));

            contents.Insert(0, Message.FromUser(streamInfoContent));
        }
    }

    protected ILogger Logger => Logging.Logger.Instance(nameof(GptWatcher));

    public void Reset()
    {
        HistoryHolder.Reset();
    }
}
