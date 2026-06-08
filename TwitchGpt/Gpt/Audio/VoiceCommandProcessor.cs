using NLog;

namespace TwitchGpt.Gpt.Audio;

/// <summary>
/// Bridges AudioTranscriptionService and AiMessagesProcessor.
/// Reads detected voice commands and forwards them to the main bot for processing.
/// </summary>
public sealed class VoiceCommandProcessor
{
    private readonly AudioTranscriptionService _transcriptionService;
    private readonly AiMessagesProcessor _messagesProcessor;

    public VoiceCommandProcessor(
        AudioTranscriptionService transcriptionService,
        AiMessagesProcessor messagesProcessor)
    {
        _transcriptionService = transcriptionService;
        _messagesProcessor = messagesProcessor;
    }

    public async Task RunAsync(CancellationToken token)
    {
        Logger.Info($"{nameof(VoiceCommandProcessor)} started");

        await foreach (var context in _transcriptionService.Commands.ReadAllAsync(token))
        {
            Logger.Info($"Voice command received: {context}");
            _messagesProcessor.EnqueueVoiceCommand(context);
        }

        Logger.Info($"{nameof(VoiceCommandProcessor)} stopped");
    }

    private ILogger Logger => Logging.Logger.Instance(nameof(VoiceCommandProcessor));
}
