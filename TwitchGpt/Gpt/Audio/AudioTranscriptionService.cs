using NLog;

namespace TwitchGpt.Gpt.Audio;

/// <summary>
/// Consumes audio chunks from AudioChunkWriter, transcribes them via Whisper,
/// maintains a rolling context buffer, and detects trigger words.
/// When a trigger is found, enqueues the full context into AiMessagesProcessor and resets the buffer.
/// </summary>
public sealed class AudioTranscriptionService
{
    private readonly AudioChunkWriter _chunkWriter;
    private readonly OpenRouterAudioClient _audioClient;
    private readonly AiMessagesProcessor _messagesProcessor;
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

    private DateTime _lastTriggerAt = DateTime.MinValue;

    /// <param name="chunkWriter">Source of audio chunk file paths</param>
    /// <param name="audioClient">Whisper client for transcription</param>
    /// <param name="messagesProcessor">Destination for detected voice commands</param>
    /// <param name="triggerWords">Words that indicate the streamer is addressing the bot</param>
    /// <param name="maxBufferSize">How many transcriptions to keep in context (default 5 ≈ 25s)</param>
    /// <param name="cooldown">Minimum time between two consecutive triggers</param>
    public AudioTranscriptionService(
        AudioChunkWriter chunkWriter,
        OpenRouterAudioClient audioClient,
        AiMessagesProcessor messagesProcessor,
        string[] triggerWords,
        int maxBufferSize = 5,
        TimeSpan? cooldown = null)
    {
        _chunkWriter = chunkWriter;
        _audioClient = audioClient;
        _messagesProcessor = messagesProcessor;
        _triggerWords = triggerWords;
        _maxBufferSize = maxBufferSize;
        _cooldown = cooldown ?? TimeSpan.FromSeconds(15);
    }

    public async Task RunAsync(CancellationToken token)
    {
        await foreach (var chunkPath in _chunkWriter.Chunks.ReadAllAsync(token))
        {
            // Read file bytes immediately before any async work so the file can be
            // safely evicted/deleted by AudioChunkWriter while we wait for the API
            byte[]? audioData = null;
            try { audioData = await File.ReadAllBytesAsync(chunkPath, token); }
            catch (Exception ex) { Logger.Error($"Failed to read chunk {chunkPath}: {ex.Message}"); }

            if (audioData == null)
                continue;

            try
            {
                await ProcessChunkAsync(audioData, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Error($"Transcription error for {chunkPath}: {ex.Message}");
            }
        }
    }

    private async Task ProcessChunkAsync(byte[] audioData, CancellationToken token)
    {
        var text = await _audioClient.TranscribeAsync(audioData, format: "mp3", token: token);

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

        _messagesProcessor.EnqueueVoiceCommand(fullContext);
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

    private ILogger Logger => Logging.Logger.Instance(nameof(AudioTranscriptionService));
}
