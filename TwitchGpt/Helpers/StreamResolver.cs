using System.Diagnostics;
using TwitchGpt.Config;

namespace TwitchGpt.Helpers;

public static class StreamResolver
{
    public static async Task<Uri> GetRmptUrl(string userName)
    {
        var streamlink = ConfigManager.GetPath<string>("streamlink");

        var info = new ProcessStartInfo()
        {
            FileName = streamlink,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            Arguments = $"--stream-url twitch.tv/{userName}"
        };

        var tcs = new TaskCompletionSource<int>();

        using var process = new Process();
        process.StartInfo = info;
        process.EnableRaisingEvents = true;

        process.Exited += (sender, args) =>
        {
            tcs.SetResult(process.ExitCode);
        };

        process.Start();

        var exitCode = await tcs.Task;
        if (exitCode != 0)
            throw new Exception($"Failed to get stream url. ExitCode: {exitCode}, message: {await process.StandardOutput.ReadToEndAsync()}");
        
        var output = await process.StandardOutput.ReadToEndAsync();

        return new Uri(output);
    }
}