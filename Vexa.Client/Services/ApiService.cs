using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using NovaChat.Client.Models;

namespace NovaChat.Client.Services;

public class ApiService
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public ApiService()
    {
        _httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:5256/") };
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new FlexibleStringConverter());
        return options;
    }

    public string BuildAbsoluteUrl(string? relativeUrl)
    {
        if (string.IsNullOrWhiteSpace(relativeUrl)) return string.Empty;
        return Uri.TryCreate(relativeUrl, UriKind.Absolute, out var absolute)
            ? absolute.ToString()
            : new Uri(_httpClient.BaseAddress!, relativeUrl).ToString();
    }

    private void AddAuthorization()
    {
        _httpClient.DefaultRequestHeaders.Authorization = null;
        if (!string.IsNullOrWhiteSpace(AuthState.Token))
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthState.Token);
    }

    public async Task<TResponse?> PostAsync<TResponse>(string endpoint, object data)
    {
        return await PostAsync<object, TResponse>(endpoint, data);
    }

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(string endpoint, TRequest data)
    {
        AddAuthorization();
        using var response = await _httpClient.PostAsJsonAsync(endpoint, data);
        await EnsureSuccessAsync(response, endpoint);
        return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions);
    }

    public async Task<TResponse?> PutAsync<TRequest, TResponse>(string endpoint, TRequest data)
    {
        AddAuthorization();
        using var response = await _httpClient.PutAsJsonAsync(endpoint, data);
        await EnsureSuccessAsync(response, endpoint);
        return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions);
    }

    public Task<TResponse?> GetAsync<TResponse>(string endpoint) => GetAsync<TResponse>(endpoint, CancellationToken.None);

    public async Task<TResponse?> GetAsync<TResponse>(string endpoint, CancellationToken cancellationToken)
    {
        AddAuthorization();
        using var response = await _httpClient.GetAsync(endpoint, cancellationToken);
        await EnsureSuccessAsync(response, endpoint);
        return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken);
    }

    public async Task<byte[]?> GetBytesAsync(string endpoint)
    {
        AddAuthorization();

        if (TryGetChatMediaMessageId(endpoint, out var messageId))
        {
            using var envelopeResponse = await _httpClient.GetAsync($"api/ChatMedia/{messageId}/envelope");
            if (envelopeResponse.IsSuccessStatusCode)
            {
                var envelopePayload = await envelopeResponse.Content.ReadFromJsonAsync<EncryptedMediaEnvelopeResponse>(JsonOptions);
                if (envelopePayload == null || string.IsNullOrWhiteSpace(envelopePayload.Envelope))
                    throw new HttpRequestException("The server did not return an encrypted media envelope.");

                using var mediaResponse = await _httpClient.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead);
                await EnsureSuccessAsync(mediaResponse, endpoint);
                var encryptedBytes = await mediaResponse.Content.ReadAsByteArrayAsync();
                var e2ee = new E2eeCryptoService();
                await e2ee.InitializeAsync(this);
                return await e2ee.DecryptMediaBytesAsync(envelopePayload.Envelope, encryptedBytes);
            }

            if (envelopeResponse.StatusCode != System.Net.HttpStatusCode.NotFound)
                await EnsureSuccessAsync(envelopeResponse, $"api/ChatMedia/{messageId}/envelope");
        }

        using var response = await _httpClient.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead);
        await EnsureSuccessAsync(response, endpoint);
        return await response.Content.ReadAsByteArrayAsync();
    }

    public async Task<byte[]> GetE2eeMediaBytesAsync(
        int messageId,
        E2eeCryptoService e2ee,
        CancellationToken cancellationToken = default)
    {
        if (messageId <= 0) throw new ArgumentOutOfRangeException(nameof(messageId));
        ArgumentNullException.ThrowIfNull(e2ee);

        AddAuthorization();

        var envelopeEndpoint = $"api/ChatMedia/{messageId}/envelope";
        using var envelopeResponse = await _httpClient.GetAsync(envelopeEndpoint, cancellationToken);
        await EnsureSuccessAsync(envelopeResponse, envelopeEndpoint);

        var envelopePayload = await envelopeResponse.Content.ReadFromJsonAsync<EncryptedMediaEnvelopeResponse>(JsonOptions, cancellationToken);
        if (envelopePayload == null || string.IsNullOrWhiteSpace(envelopePayload.Envelope))
            throw new HttpRequestException("The server returned an empty E2EE media envelope.");

        var mediaEndpoint = $"api/ChatMedia/{messageId}";
        using var mediaResponse = await _httpClient.GetAsync(mediaEndpoint, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(mediaResponse, mediaEndpoint);

        var encryptedBytes = await mediaResponse.Content.ReadAsByteArrayAsync(cancellationToken);
        if (encryptedBytes.Length == 0)
            throw new InvalidDataException("The server returned an empty encrypted media blob.");

        return await e2ee.DecryptMediaBytesAsync(envelopePayload.Envelope, encryptedBytes, cancellationToken);
    }

    public async Task<TResponse?> UploadFileAsync<TResponse>(string endpoint, string filePath, string fieldName = "file")
    {
        AddAuthorization();

        // Chat media intentionally stays outside E2EE.
        // Only text messages are end-to-end encrypted. Images, voice messages,
        // and regular files use the normal authenticated multipart upload flow.
        using var form = new MultipartFormDataContent();
        await using var stream = File.OpenRead(filePath);
        using var fileContent = new StreamContent(stream);
        var mediaType = System.IO.Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".wav" => "audio/wav",
            ".mp3" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            ".webm" => "video/webm",
            _ => "application/octet-stream"
        };
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        form.Add(fileContent, fieldName, System.IO.Path.GetFileName(filePath));

        using var response = await _httpClient.PostAsync(endpoint, form);
        await EnsureSuccessAsync(response, endpoint);
        return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions);
    }

    public async Task<bool> DeleteAsync(string endpoint)
    {
        AddAuthorization();
        using var response = await _httpClient.DeleteAsync(endpoint);
        await EnsureSuccessAsync(response, endpoint);
        return true;
    }

    private static bool TryGetChatMediaMessageId(string endpoint, out int messageId)
    {
        messageId = 0;
        var clean = endpoint.Split('?', 2)[0].Trim('/');
        var segments = clean.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 3 &&
               segments[0].Equals("api", StringComparison.OrdinalIgnoreCase) &&
               segments[1].Equals("ChatMedia", StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(segments[2], out messageId) && messageId > 0;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string endpoint)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        var message = ExtractErrorMessage(body);
        throw new HttpRequestException($"API {endpoint} failed ({(int)response.StatusCode} {response.ReasonPhrase}): {message}");
    }

    private static string ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "The server returned no error details.";
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("message", out var message))
                return message.ValueKind == JsonValueKind.String ? message.GetString() ?? body : message.ToString();
            if (document.RootElement.TryGetProperty("title", out var title))
                return title.ValueKind == JsonValueKind.String ? title.GetString() ?? body : title.ToString();
        }
        catch (JsonException) { }
        return body.Length > 1000 ? body[..1000] : body;
    }

    private sealed class EncryptedMediaEnvelopeResponse
    {
        public string Envelope { get; set; } = string.Empty;
    }

    private sealed class FlexibleStringConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String: return reader.GetString();
                case JsonTokenType.Number:
                    if (reader.TryGetInt64(out var integer)) return integer.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (reader.TryGetDecimal(out var decimalValue)) return decimalValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    return reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
                case JsonTokenType.True: return "true";
                case JsonTokenType.False: return "false";
                case JsonTokenType.Null: return null;
                default: throw new JsonException($"Cannot convert JSON token {reader.TokenType} to string.");
            }
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
    }
}
