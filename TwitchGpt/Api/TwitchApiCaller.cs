using TwitchGpt.Entities;
using TwitchLib.Api;
using TwitchLib.Api.Core;
using TwitchLib.Api.Core.Exceptions;

namespace TwitchGpt.Api;

public class TwitchApiCaller : ApiCaller<TwitchAPI>
{
    private readonly TwitchApiCredentials _credentials;

    public TwitchApiCaller(TwitchApiCredentials credentials)
    {
        _credentials = credentials;
        Api = new TwitchAPI(settings: new ApiSettings()
        {
            ClientId = credentials.ClientId,
            AccessToken = credentials.AccessToken,
        });

        credentials.PropertyChanged += (sender, args) =>
        {
            Api.Settings.ClientId = credentials.ClientId;
            Api.Settings.AccessToken = credentials.AccessToken;
        };
    }

    protected override bool IsCredentialsException(Exception exception)
    {
        return exception is BadScopeException;
    }

    protected override async Task ReloadCredentials()
    {
        await CredentialsFactory.Reload(_credentials);
    }
}
