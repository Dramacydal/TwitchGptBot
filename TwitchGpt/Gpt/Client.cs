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
    
    public HistoryHolder HistoryHolder { get; private set; }

    public bool IsBusy { get; set; }

    public int ProviderHash => _aiPool[_poolIndex].Item1;

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
            _model = GetAi().CreateGeminiModel("models/gemini-2.5-flash-lite-preview-06-17", config: new()
                {
                    ResponseMimeType = "text/plain",
                },
                systemInstruction: null,
                safetyRatings: Role!.SafetySettings);
        }

        return _model;
    }

    private ChatSession GetChatSession()
    {
        if (_chatSession == null)
        {
            var preparedInstructions = string.Join("\r\n", Role.Instructions.Split("\n")
                .Select(l => l.Trim())
                .Where(l => !l.StartsWith("#"))
            );
            
            _chatSession = GetModel()
                .StartChat(systemInstruction: preparedInstructions);
        }

        return _chatSession;
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
        var proxiedHttpClientHandler = new HttpClientHandler() { UseProxy = proxy != null };
        proxiedHttpClientHandler.Proxy = proxy;
        return new HttpClient(proxiedHttpClientHandler);
    }

    public Client(ClientType type, IEnumerable<string> tokenPool, RoleModel? role = null)
    {
        ClientType = type;

        HistoryHolder = HistoryFactory.CreateHistory(type);

        Role = role ?? ClientFactory.DefaultRole;

        foreach (var token in tokenPool)
            _aiPool.Add(new(token.GetHashCode(), new GoogleAi(token, client: CreateHttpClient(GetProxy()))));
    }

    public async Task<string?> Ask(string question, List<FileSourceInfo>? files = null)
    {
        return await Ask<string>(question, files);
    }

    public async Task<T?> Ask<T>(string question, List<FileSourceInfo>? files = null) where T : class
    {
        // if (!await WaitHelper.WaitUntil(() => !IsBusy, TimeSpan.FromSeconds(2)))
        //     throw new ClientBusyException();

        // using var ctx = new DisposableContext(() => IsBusy = true, () => IsBusy = false);

        var now = DateTime.Now;

        try
        {
            var request = new GenerateContentRequest();
            request.AddText(question);
            foreach (var file in files ?? [])
            {
                if (file.Blob != null)
                    request.AddInlineData(Convert.ToBase64String(file.Blob), file.MimeType);
                else
                {
                    request.AddInlineData(Convert.ToBase64String(File.ReadAllBytes(file.FilePath)), file.MimeType);
                    // request.AddInlineFile(file.FilePath);
                }
            }

            GetChatSession().History = HistoryHolder.CopyEntries();

            var res = await GetChatSession().GenerateObjectAsync<T>(request);
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
    
    protected ILogger Logger => Logging.Logger.Instance(nameof(GptWatcher));

    public void Reset()
    {
        _model = null;
        _chatSession = null;
        HistoryHolder.Reset();
    }
}
