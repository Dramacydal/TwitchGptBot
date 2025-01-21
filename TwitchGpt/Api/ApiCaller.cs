using TwitchGpt.Entities;
using TwitchLib.Api;
using TwitchLib.Api.Core;
using TwitchLib.Api.Core.Exceptions;

namespace TwitchGpt.Api;

public class ApiCaller
{
    private TwitchAPI _api;
    private readonly ApiCredentials _credentials;

    public ApiCaller(ApiCredentials credentials)
    {
        _credentials = credentials;
        _api = new TwitchAPI(settings: new ApiSettings()
        {
            ClientId = credentials.ClientId,
            AccessToken = credentials.AccessToken,
        });

        credentials.PropertyChanged += (sender, args) =>
        {
            _api.Settings.ClientId = credentials.ClientId;
            _api.Settings.AccessToken = credentials.AccessToken;
        };
    }

    public async Task<T> Call<T>(Func<TwitchAPI, Task<T>> api)
    {
        for (var i = 0; i < 3; ++i)
        {
            try
            {
                return await api.Invoke(_api);
            }
            catch (BadScopeException ex)
            {
                await CredentialsFactory.Reload(_credentials);
            }
        }

        throw new Exception("Api query failed after 3 tries");
    }
}
