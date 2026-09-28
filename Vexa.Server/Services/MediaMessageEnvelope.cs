using System.Text.Json;
using System.Text.Json.Serialization;

namespace NovaChat.Server.Services;

public sealed class MediaMessageEnvelope
{
    public const string Prefix = "__NOVACHAT_MEDIA__";

    public string Type { get; set; } = "file";
    public string StorageName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long Size { get; set; }
    public double? DurationSeconds { get; set; }

    public string Serialize() => Prefix + JsonSerializer.Serialize(this);

    public static bool TryParse(string? content, out MediaMessageEnvelope? media)
    {
        media = null;
        if (string.IsNullOrWhiteSpace(content) || !content.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        try
        {
            media = JsonSerializer.Deserialize<MediaMessageEnvelope>(content[Prefix.Length..]);
            return media != null && !string.IsNullOrWhiteSpace(media.StorageName) && !string.IsNullOrWhiteSpace(media.FileName);
        }
        catch { return false; }
    }
}

public sealed class E2eeMediaMessageEnvelope
{
    public const string Prefix = "__NOVACHAT_E2EE_MEDIA__";
    public const string Algorithm = "AES-256-GCM+RSA-OAEP-SHA256-MEDIA";

    [JsonPropertyName("v")] public int Version { get; set; } = 1;
    [JsonPropertyName("alg")] public string EncryptionAlgorithm { get; set; } = Algorithm;
    [JsonPropertyName("blobId")] public string BlobId { get; set; } = string.Empty;
    [JsonPropertyName("fileNonce")] public string FileNonce { get; set; } = string.Empty;
    [JsonPropertyName("fileTag")] public string FileTag { get; set; } = string.Empty;
    [JsonPropertyName("metadataNonce")] public string MetadataNonce { get; set; } = string.Empty;
    [JsonPropertyName("metadataTag")] public string MetadataTag { get; set; } = string.Empty;
    [JsonPropertyName("metadataCiphertext")] public string MetadataCiphertext { get; set; } = string.Empty;
    [JsonPropertyName("keys")] public Dictionary<string, string> Keys { get; set; } = new(StringComparer.Ordinal);

    public string Serialize() => Prefix + JsonSerializer.Serialize(this);

    public static bool TryParse(string? content, out E2eeMediaMessageEnvelope? envelope)
    {
        envelope = null;
        if (string.IsNullOrWhiteSpace(content) || !content.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        try
        {
            var value = JsonSerializer.Deserialize<E2eeMediaMessageEnvelope>(content[Prefix.Length..]);
            if (value == null || value.Version != 1 || value.EncryptionAlgorithm != Algorithm ||
                string.IsNullOrWhiteSpace(value.BlobId) || !Guid.TryParseExact(value.BlobId, "N", out _) ||
                string.IsNullOrWhiteSpace(value.FileNonce) || string.IsNullOrWhiteSpace(value.FileTag) ||
                string.IsNullOrWhiteSpace(value.MetadataNonce) || string.IsNullOrWhiteSpace(value.MetadataTag) ||
                string.IsNullOrWhiteSpace(value.MetadataCiphertext) || value.Keys.Count == 0)
                return false;
            envelope = value;
            return true;
        }
        catch (JsonException) { return false; }
    }
}
