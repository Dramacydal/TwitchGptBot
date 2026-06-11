using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using NLog;

namespace TwitchGpt.Gpt.Music;

public class ShazamClientV3 : ITrackRecognizer
{
    private readonly HttpClient _httpClient;
    private const string RecognizeUrl = "https://shazam-core.p.rapidapi.com/v1/tracks/recognize";

    public ShazamClientV3(string apiKey, IWebProxy? proxy = null)
    {
        _httpClient = new HttpClient(new HttpClientHandler
        {
            UseProxy = proxy != null,
            Proxy = proxy,
        });
        _httpClient.DefaultRequestHeaders.Add("x-rapidapi-host", "shazam-core.p.rapidapi.com");
        _httpClient.DefaultRequestHeaders.Add("x-rapidapi-key", apiKey);
    }

    /// <summary>
    /// Recognizes the track in the given audio file.
    /// Returns (Artist, Title), or null if no match was found.
    /// </summary>
    public async Task<TrackInfo?> RecognizeAsync(string filePath, CancellationToken token = default)
    {
        Logger.Debug($"Recognizing {filePath}...");
        
        await using var fileStream = File.OpenRead(filePath);
        var fileName = Path.GetFileName(filePath);

        using var form = new MultipartFormDataContent();
        using var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/mpeg");
        form.Add(fileContent, "file", fileName);

        var response = await _httpClient.PostAsync(RecognizeUrl, form, token);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(token);
        Logger.Debug($"Shazam response: {json}");
        var result = JsonSerializer.Deserialize<RecognizeResponse>(json);

        var track = result?.Track;
        if (track == null)
            return null;

        return new TrackInfo(track.Title ?? "", track.Subtitle ?? "");
    }

    private ILogger Logger => Logging.Logger.Instance(nameof(ShazamClientV3));

    // ── JSON model ──────────────────────────────────────────────────────────────

    private class RecognizeResponse
    {
        [JsonPropertyName("track")]
        public TrackData? Track { get; set; }
    }

    private class TrackData
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        // Artist name lives in "subtitle" in the Shazam response
        [JsonPropertyName("subtitle")]
        public string? Subtitle { get; set; }
    }
}

