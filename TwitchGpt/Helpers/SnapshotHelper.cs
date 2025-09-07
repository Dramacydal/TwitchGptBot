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

    public static async Task TakeBoostySnapshot(Uri path, string outPath, Dictionary<string, string>? headers = null)
    {
        var strHeaders = "";
        foreach (var (key, value) in headers ?? [])
            strHeaders += $"-headers \"{key}: " + EscapeQuotes(value) + "\" ";

        await ExecuteCommand($"ffmpeg -y {strHeaders} -i {path} -filter:v fps=4 -frames:v 1 {outPath}");
    }

    private static string EscapeQuotes(string str)
    {
        return str.Replace("\"", "\\\"");
    }

    public static async Task ExecuteCommand(string command)
    {
        var tcs = new TaskCompletionSource<int>();
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        var info = new ProcessStartInfo()
        {
            FileName = isWindows ? "cmd.exe" : "/bin/bash",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        if (isWindows)
            info.Arguments = "/c \"" + command + "\"";
        else
            info.Arguments = "-c \"" + EscapeQuotes(command) + "\"";
        
        Console.WriteLine(info.Arguments);

        using var process = new Process();
        process.StartInfo = info;
        process.EnableRaisingEvents = true;
        
        process.Exited += (sender, e) =>
        {
            if (sender is Process proc)
            {
                Console.WriteLine($"\n[Событие Exited] Процесс завершился с кодом выхода: {proc.ExitCode}");
                tcs.SetResult(proc.ExitCode);
            }
        };

        string output = "";
        string error = "";

        process.OutputDataReceived += (sender, e) =>
        {
            output += e.Data + Environment.NewLine;
        };
        
        process.ErrorDataReceived += (sender, e) =>
        {
            error += e.Data + Environment.NewLine;
        };
        
        if (!process.Start())
            throw new Exception("Failed to start process");

        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        await tcs.Task;
        
        if (tcs.Task.Result != 0)
            throw new Exception(
                $"Failed to get stream snapshot. ExitCode: {process.ExitCode}, message: {error}");
    }
}