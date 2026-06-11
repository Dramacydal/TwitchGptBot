using System.Net;
using TwitchGpt.Config;
using TwitchGpt.Gpt.Entities;

namespace TwitchGpt.Helpers;

public static class ProxyHelper
{
    public static IWebProxy? GetConfiguredProxy()
    {
        try
        {
            var config = ConfigManager.GetPath<Proxy>("gpt_proxy");
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
