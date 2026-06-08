using System.Threading.Channels;
using NLog;

namespace TwitchGpt.Gpt.Audio;

/// <summary>
/// Consumes audio chunks from AudioChunkWriter, transcribes them via Whisper,
/// maintains a rolling context buffer, and detects trigger words.
/// When a trigger is found, publishes the full context to the Commands channel
/// and resets the buffer.
/// </summary>
public sealed class AudioTranscriptionService
{
    private readonly AudioChunkWriter _chunkWriter;
    private readonly OpenRouterAudioClient _audioClient;
    private readonly string[] _triggerWords;
    private readonly int _maxBufferSize;
    private readonly TimeSpan _cooldown;

    // Known Whisper hallucination markers — transcriptions containing these are discarded
    private static readonly string[] HallucinationMarkers =
    [
        "Amara.org",
        "DimaTorzok",
        "DimaTorzhok",
    ];

    // Rolling buffer of recent transcriptions
    private readonly Queue<string> _contextBuffer = new();

    // Detected voice commands are published here for the bot to consume
    private readonly Channel<string> _commandChannel = Channel.CreateBounded<string>(
        new BoundedChannelOptions(16) { FullMode = BoundedChannelFullMode.DropOldest });

    private DateTime _lastTriggerAt = DateTime.MinValue;

    public ChannelReader<string> Commands => _commandChannel.Reader;

    /// <param name="chunkWriter">Source of audio chunk file paths</param>
    /// <param name="audioClient">Whisper client for transcription</param>
    /// <param name="triggerWords">Words that indicate the streamer is addressing the bot</param>
    /// <param name="maxBufferSize">How many chunks to keep in context (default 5 ≈ 25s)</param>
    /// <param name="cooldown">Minimum time between two consecutive triggers</param>
    public AudioTranscriptionService(
        AudioChunkWriter chunkWriter,
        OpenRouterAudioClient audioClient,
        string[] triggerWords,
        int maxBufferSize = 5,
        TimeSpan? cooldown = null)
    {
        _chunkWriter = chunkWriter;
        _audioClient = audioClient;
        _triggerWords = triggerWords;
        _maxBufferSize = maxBufferSize;
        _cooldown = cooldown ?? TimeSpan.FromSeconds(15);
    }

    public async Task RunAsync(CancellationToken token)
    {
        await foreach (var chunkPath in _chunkWriter.Chunks.ReadAllAsync(token))
        {
            try
            {
                await ProcessChunkAsync(chunkPath, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Error($"Transcription error for {chunkPath}: {ex.Message}");

                // Delete the chunk even on error to avoid accumulating stale files
                TryDeleteFile(chunkPath);
            }
        }

        _commandChannel.Writer.TryComplete();
    }

    private async Task ProcessChunkAsync(string chunkPath, CancellationToken token)
    {
        var text = await _audioClient.TranscribeAsync(chunkPath, format: "wav", token: token);

        TryDeleteFile(chunkPath);

        if (string.IsNullOrWhiteSpace(text))
            return;

        // Filter out transcriptions that contain no actual words — e.g. " ." or "..."
        if (!text.Any(char.IsLetter))
        {
            Logger.Debug($"Transcription has no words, skipping: '{text.Trim()}'");
            return;
        }

        if (HallucinationMarkers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase)))
        {
            Logger.Debug($"Hallucination detected, skipping: {text}");
            return;
        }

        Logger.Debug($"Transcribed: {text}");

        AddToBuffer(text);

        // Skip if still within cooldown window after the last trigger
        if (DateTime.Now - _lastTriggerAt < _cooldown)
            return;

        var fullContext = GetContextText();

        if (!ContainsTrigger(fullContext))
            return;

        Logger.Info($"Voice trigger detected. Context: {fullContext}");

        _lastTriggerAt = DateTime.Now;
        _contextBuffer.Clear();

        _commandChannel.Writer.TryWrite(fullContext);
    }

    private void AddToBuffer(string text)
    {
        _contextBuffer.Enqueue(text);

        // Drop the oldest chunk when the buffer exceeds the limit
        if (_contextBuffer.Count > _maxBufferSize)
            _contextBuffer.Dequeue();
    }

    private string GetContextText() => string.Join(" ", _contextBuffer);

    private bool ContainsTrigger(string text) =>
        _triggerWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch { /* ignore — file may already be deleted */ }
    }

    private ILogger Logger => Logging.Logger.Instance(nameof(AudioTranscriptionService));
}
