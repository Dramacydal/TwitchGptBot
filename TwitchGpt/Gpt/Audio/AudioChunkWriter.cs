using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using NLog;
using TwitchGpt.Helpers;

namespace TwitchGpt.Gpt.Audio;

/// <summary>
/// Runs streamlink | ffmpeg and splits the audio stream into fixed-length chunks.
/// Publishes ready file paths to a Channel. Files are deleted by the consumer after processing.
/// </summary>
public sealed class AudioChunkWriter : IAsyncDisposable
{
    private readonly string _channelName;
    private readonly int _chunkSeconds;
    private readonly string _outputDir;
    private readonly Channel<string> _channel;
    private readonly FileSystemWatcher _watcher;

    private Process? _process;
    private ChildProcessGuard? _processGuard;
    private int _lastChunkIndex = -1;

    public ChannelReader<string> Chunks => _channel.Reader;

    public AudioChunkWriter(string channelName, string channelId, int chunkSeconds = 5, string? outputDir = null)
    {
        _channelName = channelName;
        _chunkSeconds = chunkSeconds;
        _outputDir = outputDir ?? Path.Combine(Path.GetTempPath(), $"twitchgpt_audio_{channelId}");
        _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = true
        });

        Directory.CreateDirectory(_outputDir);

        _watcher = new FileSystemWatcher(_outputDir, "chunk_*.wav")
        {
            NotifyFilter = NotifyFilters.FileName,
            EnableRaisingEvents = false
        };
        _watcher.Created += OnFileCreated;
    }

    public Task StartAsync(CancellationToken token)
    {
        CleanOutputDir();

        var command = BuildCommand();
        Logger.Info($"AudioChunkWriter starting: {command}");

        _process = CreateProcess(command);

        _process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                Logger.Debug($"[ffmpeg] {e.Data}");
        };

        if (!_process.Start())
            throw new Exception("Failed to start ffmpeg audio process");

        // Register with Job Object so the child is killed if our process dies (Windows only)
        _processGuard = new ChildProcessGuard();
        _processGuard.AddProcess(_process);

        _process.BeginErrorReadLine();
        _watcher.EnableRaisingEvents = true;

        token.Register(Stop);

        // Ensure the child process is killed if the parent exits unexpectedly
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        return Task.CompletedTask;
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        // ffmpeg creates chunk_001 when it finishes writing chunk_000
        // so we publish the previous file — it is already closed and safe to read
        var index = ParseChunkIndex(e.Name);
        if (index == null)
            return;

        if (_lastChunkIndex >= 0)
        {
            var readyPath = ChunkPath(_lastChunkIndex);
            if (File.Exists(readyPath))
            {
                Logger.Debug($"AudioChunkWriter: chunk ready → {readyPath}");

                const int maxAttempts = 5;
                for (var attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    if (_channel.Writer.TryWrite(readyPath))
                        break;

                    Logger.Warn($"AudioChunkWriter: enqueue attempt {attempt}/{maxAttempts} failed for {readyPath}");

                    if (attempt == maxAttempts)
                        Logger.Error($"AudioChunkWriter: giving up on chunk after {maxAttempts} attempts: {readyPath}");
                    else
                        Thread.Sleep(50);
                }
            }
        }

        _lastChunkIndex = index.Value;
    }

    private void OnProcessExit(object? sender, EventArgs e) => Stop();

    private void Stop()
    {
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        _watcher.EnableRaisingEvents = false;

        try
        {
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }
        catch { /* already exited */ }

        if (!_channel.Writer.TryComplete())
            Logger.Debug("AudioChunkWriter: channel was already completed");
    }

    private string BuildCommand()
    {
        var outputPattern = Path.Combine(_outputDir, "chunk_%05d.wav");

        // -vn          — audio only, drop video
        // -ac 1        — mono
        // -ar 16000    — 16 kHz sample rate (optimal for Whisper)
        // -f segment   — split into fixed-length segments
        var ffmpegArgs = $"-loglevel error -y -i pipe:0 -vn -ac 1 -ar 16000 -acodec pcm_s16le -f segment -segment_time {_chunkSeconds} -reset_timestamps 1 \"{outputPattern}\"";
        var streamlinkArgs = $"--twitch-disable-ads https://www.twitch.tv/{_channelName} audio_only -O";

        return $"streamlink {streamlinkArgs} | ffmpeg {ffmpegArgs}";
    }

    private Process CreateProcess(string command)
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var info = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd.exe" : "/bin/bash",
            Arguments = isWindows ? $"/c \"{command}\"" : $"-c \"{command.Replace("\"", "\\\"")}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.Exited += (_, _) =>
        {
            Logger.Info("AudioChunkWriter: ffmpeg process exited");
            if (!_channel.Writer.TryComplete())
                Logger.Debug("AudioChunkWriter: channel was already completed");
        };

        return process;
    }

    private void CleanOutputDir()
    {
        foreach (var file in Directory.GetFiles(_outputDir, "chunk_*.wav"))
        {
            try { File.Delete(file); }
            catch { /* ignore */ }
        }
    }

    private string ChunkPath(int index) =>
        Path.Combine(_outputDir, $"chunk_{index:D5}.wav");

    private static int? ParseChunkIndex(string? fileName)
    {
        if (fileName == null)
            return null;

        var name = Path.GetFileNameWithoutExtension(fileName);
        if (name.StartsWith("chunk_") && int.TryParse(name["chunk_".Length..], out var index))
            return index;

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        _watcher.Dispose();
        _process?.Dispose();
        _processGuard?.Dispose();
        await Task.CompletedTask;
    }

    private ILogger Logger => Logging.Logger.Instance(nameof(AudioChunkWriter));
}
