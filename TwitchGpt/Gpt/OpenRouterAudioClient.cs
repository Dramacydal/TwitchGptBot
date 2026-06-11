using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwitchGpt.Gpt;

public class OpenRouterAudioClient
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "https://openrouter.ai/api/v1/audio/transcriptions";


    // public string Model { get; set; } = "openai/whisper-1";
    public string Model { get; set; } = "openai/whisper-large-v3-turbo";

    public OpenRouterAudioClient(string apiKey, IWebProxy? proxy = null)
    {
        _httpClient = new HttpClient(new HttpClientHandler
        {
            UseProxy = proxy != null,
            Proxy = proxy
        });
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    public async Task<string?> TranscribeAsync(byte[] audioData, string format = "mp3", CancellationToken token = default)
    {
        var payload = new TranscriptionRequest
        {
            Model = Model,
            InputAudio = new AudioInput
            {
                Data = Convert.ToBase64String(audioData),
                Format = format
            }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(BaseUrl, content, token);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(token);
        var result = JsonSerializer.Deserialize<TranscriptionResponse>(responseJson);

        return result?.Text;
    }

    public async Task<string?> TranscribeAsync(string filePath, string? format = null, CancellationToken token = default)
    {
        var audioData = await File.ReadAllBytesAsync(filePath, token);
        format ??= Path.GetExtension(filePath).TrimStart('.').ToLower();
        return await TranscribeAsync(audioData, format, token);
    }

    private class TranscriptionRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("input_audio")]
        public AudioInput InputAudio { get; set; } = new();

    }

    private class AudioInput
    {
        [JsonPropertyName("data")]
        public string Data { get; set; } = "";

        [JsonPropertyName("format")]
        public string Format { get; set; } = "mp3";
    }

    private class TranscriptionResponse
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
