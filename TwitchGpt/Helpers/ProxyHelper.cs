using System.Net;
using TwitchGpt.Config;
using TwitchGpt.Gpt.Entities;

namespace TwitchGpt.Helpers;

public static class ProxyHelper
{
    public static IWebProxy? GetGptProxy() => GetProxy("gpt_proxy");

    public static IWebProxy? GetRapidApiProxy() => GetProxy("rapidapi_proxy");

    private static IWebProxy? GetProxy(string configKey)
    {
        try
        {
            var config = ConfigManager.GetPath<Proxy>(configKey);
            if (string.IsNullOrEmpty(config?.Url))
                return null;

            return new WebProxy
            {
                Address = new Uri(config.Url),
                Credentials = new NetworkCredential(config.User, config.Password)
            };
        }
        catch
        {
            return null;
        }
    }
}
