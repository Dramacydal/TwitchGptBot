using System.Diagnostics;
using System.Runtime.InteropServices;
using TwitchGpt.Config;

namespace TwitchGpt.Helpers;

public static class SnapshotHelper
{
    public static async Task TakeTwitchSnapshot(string channelName, string outPath)
    {
        await ExecuteCommand(
            $"streamlink --twitch-disable-ads https://www.twitch.tv/{channelName} best -O | ffmpeg -y -i pipe:0 -vframes 1 {outPath}");
    }

    public static async Task TakeBoostySnapshot(Uri path, string outPath)
    {
        await ExecuteCommand($"ffmpeg -y -i {path} -filter:v fps=4 -frames:v 1 {outPath}");
    }

    private static async Task ExecuteCommand(string command)
    {
        var streamlink = ConfigManager.GetPath<string>("streamlink");

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        var info = new ProcessStartInfo()
        {
            FileName = isWindows ? "wsl.exe" : "/bin/bash",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        if (isWindows)
            info.Arguments = "-- eval '" + command + "'";
        else
            info.Arguments = "-c \"" + command + "\"";
        
        Console.WriteLine(info.Arguments);

        using var process = new Process();
        process.StartInfo = info;
        process.EnableRaisingEvents = true;

        if (!process.Start())
            throw new Exception("Failed to start process");
        
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new Exception(
                $"Failed to get stream snapshot. ExitCode: {process.ExitCode}, message: {error}");
    }
}