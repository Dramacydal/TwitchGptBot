using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwitchGpt.Gpt.Music;

public class ShazamClient : ITrackRecognizer
{
    private readonly HttpClient _httpClient;
    private const string RecognizeUrl = "https://shazam-api6.p.rapidapi.com/shazam/recognize/";

    public ShazamClient(string apiKey, IWebProxy? proxy = null)
    {
        _httpClient = new HttpClient(new HttpClientHandler
        {
            UseProxy = proxy != null,
            Proxy = proxy
        });
        _httpClient.DefaultRequestHeaders.Add("x-rapidapi-host", "shazam-api6.p.rapidapi.com");
        _httpClient.DefaultRequestHeaders.Add("x-rapidapi-key", apiKey);
    }

    /// <summary>
    /// Recognizes the track in the given audio file.
    /// Returns (Artist, Title), or null if no match was found.
    /// </summary>
    public async Task<TrackInfo?> RecognizeAsync(string filePath, CancellationToken token = default)
    {
        await using var fileStream = File.OpenRead(filePath);
        var fileName = Path.GetFileName(filePath);

        using var form = new MultipartFormDataContent();
        using var fileContent = new StreamContent(fileStream);
        form.Add(fileContent, "upload_file", fileName);

        var response = await _httpClient.PostAsync(RecognizeUrl, form, token);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(token);
        var result = JsonSerializer.Deserialize<RecognizeResponse>(json);

        var track = result?.Result?.Track;
        if (track == null)
            return null;

        return new TrackInfo(track.Title ?? "", track.Subtitle ?? "");
    }

    // ── JSON model ──────────────────────────────────────────────────────────────

    private class RecognizeResponse
    {
        [JsonPropertyName("status")]
        public bool Status { get; set; }

        [JsonPropertyName("result")]
        public RecognizeResult? Result { get; set; }
    }

    private class RecognizeResult
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

