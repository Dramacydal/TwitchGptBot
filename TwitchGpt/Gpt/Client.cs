using System.Net;
using GenerativeAI;
using GenerativeAI.Types;
using NLog;
using TwitchGpt.Config;
using TwitchGpt.Exceptions;
using TwitchGpt.Gpt.Entities;
using TwitchGpt.Gpt.Enums;

namespace TwitchGpt.Gpt;

public class Client
{
    private readonly List<Tuple<int, GoogleAi>> _aiPool = new();

    private GenerativeModel? _model;
    
    private ChatSession? _chatSession;

    private int _poolIndex = 0;
    
    public RoleModel? Role { get; set; }
    
    public ClientType ClientType { get; private set; }

    public HistoryHolder HistoryHolder { get; private set; } = new();

    public bool IsBusy { get; set; }

    public int ProviderHash => _aiPool[_poolIndex].Item1;

    private bool _supportsInstructions;

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

    private GoogleAi GetAi()
    {
        return _aiPool[_poolIndex].Item2;
    }

    private GenerativeModel GetModel()
    {
        if (_model == null)
        {
            // _model = GetAi().CreateGeminiModel("models/gemini-2.5-flash-lite-preview-09-2025", config: new()
            _model = GetAi().CreateGeminiModel("models/gemma-3-27b-it", config: new()
                {
                    ResponseMimeType = "text/plain",
                },
                // systemInstruction: GetPreparedInstructions(),
                safetyRatings: Role!.SafetySettings);
            _supportsInstructions = false;
        }

        return _model;
    }

    private ChatSession GetChatSession()
    {
        if (_chatSession == null)
            _chatSession = GetModel().StartChat();

        return _chatSession;
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
            _aiPool.Add(new(token.GetHashCode(), new GoogleAi(token, client: CreateHttpClient(GetProxy()))));
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

            if (!_supportsInstructions)
                PrependInstructions(historyEntries);

            PrependStreamInfos(historyEntries, streamInfos);
            GetChatSession().History = historyEntries;

            var request = new GenerateContentRequest();

            request.AddText(question);
            
            T? res;
            if (typeof(T) == typeof(string))
            {
                var r = await GetChatSession().GenerateContentAsync(request);
                res = (T)(object)r.Text()!;
            }
            else
                res = await GetChatSession().GenerateObjectAsync<T>(request);

            Logger.Info($"GPT request process in {(DateTime.Now - now).TotalSeconds}");
        
            HistoryHolder.AddEntries(GetChatSession().History.TakeLast(2));
        
            return res;
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

    private void PrependInstructions(List<Content> contents)
    {
        var request = new GenerateContentRequest();
        request.AddText(GetPreparedInstructions());

        for (var i = 0; i < request.Contents.Count; ++i)
            contents.Insert(i, request.Contents[i]);
    }

    private void PrependStreamInfos(List<Content> contents, AbstractStreamInfo?[] streamInfos)
    {
        foreach (var streamInfo in streamInfos)
        {
            if (streamInfo == null)
                continue;

            var request = new GenerateContentRequest();
            request.AddText(streamInfo.BuildMessage());
            foreach (var (_, file) in streamInfo.SnapShots)
                request.AddInlineData(Convert.ToBase64String(file.Blob), file.MimeType);

            for (var i = 0; i < request.Contents.Count; ++i)
                contents.Insert(i, request.Contents[i]);
        }
    }

    protected ILogger Logger => Logging.Logger.Instance(nameof(GptWatcher));

    public void Reset()
    {
        _model = null;
        _chatSession = null;
        HistoryHolder.Reset();
    }
}
