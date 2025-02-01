using BoostyLib;
using BoostyLib.Exceptions;
using TwitchGpt.Entities;

namespace TwitchGpt.Api;

public class BoostyApiCaller : ApiCaller<BoostyApi>
{
    private readonly BoostyApiCredentials _credentials;

    public BoostyApiCaller(BoostyApiCredentials credentials)
    {
        _credentials = credentials;
        Api = new BoostyApi(new()
        {
            Credentials = new()
            {
                AccessToken = credentials.AccessToken,
                RefreshToken = credentials.RefreshToken,
                DeviceId = credentials.DeviceId,
                ExpiresAt = credentials.ExpiresAt,
            },
            Headers = new()
            {
                ["User-Agent"] =
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"
            }
        });

        _credentials.PropertyChanged += (sender, args) =>
        {
            Api.Credentials.DeviceId = credentials.DeviceId;
            Api.Credentials.AccessToken = credentials.AccessToken;
            Api.Credentials.RefreshToken = credentials.RefreshToken;
            Api.Credentials.ExpiresAt = credentials.ExpiresAt;
        };
    }

    protected override bool IsCredentialsException(Exception exception)
    {
        return exception is BoostyUnauthorizedException;
    }

    protected override async Task ReloadCredentials()
    {
        await CredentialsFactory.Reload(_credentials);
    }
}
