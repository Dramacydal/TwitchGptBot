using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using NLog;
using TwitchGpt.Helpers;

namespace TwitchGpt.Gpt.Audio;

/// <summary>
/// Runs streamlink | ffmpeg and splits the audio stream into fixed-length chunks.
/// Automatically restarts when the process exits (stream offline or interrupted).
/// Publishes ready file paths to a Channel.
/// </summary>
public sealed class AudioChunkWriter : IAsyncDisposable
{
    private readonly string _channelName;
    private readonly int _chunkSeconds;
    private readonly string _outputDir;
    private readonly Channel<string> _channel;
    private readonly FileSystemWatcher _watcher;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    private Process? _process;
    private ChildProcessGuard? _processGuard;
    private int _lastChunkIndex = -1;

    private readonly Queue<string> _recentChunks = new();
    private const int MaxRecentChunks = 3;

    public ChannelReader<string> Chunks => _channel.Reader;

    public IReadOnlyList<string> RecentChunkPaths
    {
        get { lock (_recentChunks) return _recentChunks.ToArray(); }
    }

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

        _watcher = new FileSystemWatcher(_outputDir, "chunk_*.mp3")
        {
            NotifyFilter = NotifyFilters.FileName,
            EnableRaisingEvents = false
        };
        _watcher.Created += OnFileCreated;
    }

    /// <summary>
    /// Long-running loop: starts the capture pipeline and restarts it whenever the
    /// stream goes offline or the process exits unexpectedly.
    /// Completes only when <paramref name="token"/> is cancelled.
    /// </summary>
    public async Task RunAsync(CancellationToken token)
    {
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        try
        {
            while (!token.IsCancellationRequested)
            {
                CleanOutputDir();
                _lastChunkIndex = -1;

                var command = BuildCommand();
                Logger.Info($"AudioChunkWriter starting: {command}");

                _process = CreateProcess();
                _processGuard = new ChildProcessGuard();

                _process.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Logger.Debug($"[ffmpeg] {e.Data}");
                };

                if (!_process.Start())
                {
                    Logger.Error("AudioChunkWriter: failed to start process, retrying...");
                    await WaitBeforeRetry(token);
                    continue;
                }

                _processGuard.AddProcess(_process);
                _process.BeginErrorReadLine();
                _watcher.EnableRaisingEvents = true;

                await _process.WaitForExitAsync(token).ConfigureAwait(false);

                _watcher.EnableRaisingEvents = false;

                if (token.IsCancellationRequested)
                    break;

                Logger.Info($"AudioChunkWriter: process exited (stream offline?), retrying in {RetryDelay.TotalSeconds}s");
                await WaitBeforeRetry(token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            KillProcess();
            _channel.Writer.TryComplete();
        }
    }

    private static async Task WaitBeforeRetry(CancellationToken token)
    {
        try { await Task.Delay(RetryDelay, token); }
        catch (OperationCanceledException) { }
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

                lock (_recentChunks)
                {
                    _recentChunks.Enqueue(readyPath);
                    if (_recentChunks.Count > MaxRecentChunks)
                        TryDeleteFile(_recentChunks.Dequeue());
                }

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

    private void OnProcessExit(object? sender, EventArgs e) => KillProcess();

    private void KillProcess()
    {
        _watcher.EnableRaisingEvents = false;
        try
        {
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }
        catch { /* already exited */ }
    }

    private string BuildCommand()
    {
        var outputPattern = Path.Combine(_outputDir, "chunk_%05d.mp3");

        // -vn             — audio only, drop video
        // -ac 2           — stereo
        // -ar 44100       — CD sample rate, preserves full frequency range for music recognition
        // -acodec mp3     — encode to mp3
        // -q:a 2          — VBR ~190 kbps, high quality
        // -f segment      — split into fixed-length segments
        var ffmpegArgs = $"-loglevel error -y -i pipe:0 -vn -ac 2 -ar 44100 -acodec libmp3lame -q:a 2 -f segment -segment_time {_chunkSeconds} -reset_timestamps 1 \"{outputPattern}\"";
        var streamlinkArgs = $"--twitch-disable-ads https://www.twitch.tv/{_channelName} audio_only -O";

        return $"streamlink {streamlinkArgs} | ffmpeg {ffmpegArgs}";
    }

    private Process CreateProcess()
    {
        var command = BuildCommand();
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var info = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd.exe" : "/bin/bash",
            Arguments = isWindows ? $"/c \"{command}\"" : $"-c \"{command.Replace("\"", "\\\"")}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };

        return new Process { StartInfo = info, EnableRaisingEvents = true };
    }

    private void CleanOutputDir()
    {
        foreach (var file in Directory.GetFiles(_outputDir, "chunk_*.mp3"))
        {
            try { File.Delete(file); }
            catch { /* ignore */ }
        }
    }

    private string ChunkPath(int index) =>
        Path.Combine(_outputDir, $"chunk_{index:D5}.mp3");

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
        KillProcess();
        _watcher.Dispose();
        _process?.Dispose();
        _processGuard?.Dispose();
        await Task.CompletedTask;
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch { /* ignore — file may already be deleted */ }
    }

    private ILogger Logger => Logging.Logger.Instance(nameof(AudioChunkWriter));
}
