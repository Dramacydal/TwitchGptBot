namespace TwitchGpt.Gpt.Music;

public interface ITrackRecognizer
{
    Task<TrackInfo?> RecognizeAsync(string filePath, CancellationToken token = default);
}
