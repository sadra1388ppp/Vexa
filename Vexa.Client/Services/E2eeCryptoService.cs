using NovaChat.Client.Models;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NovaChat.Client.Services;

public sealed class E2eeCryptoService
{
    private const int RsaKeySize = 3072;
    private const int AesKeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int MaxPlaintextLength = 100_000;
    private const long MaxImageBytes = 10 * 1024 * 1024;
    private const long MaxFileBytes = 25 * 1024 * 1024;
    private const long MaxVoiceBytes = 10 * 1024 * 1024;
    private const string TextAlgorithm = "AES-256-GCM+RSA-OAEP-SHA256";
    private const string MediaAlgorithm = "AES-256-GCM+RSA-OAEP-SHA256-MEDIA";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private RSA? _privateKey;
    private string _deviceId = string.Empty;
    private string _initializedUserId = string.Empty;
    private readonly ConcurrentDictionary<int, string> _recoveredMessageKeys = new();

    private string KeyFilePath
    {
        get
        {
            var userId = string.IsNullOrWhiteSpace(AuthState.UserId) ? "unknown" : AuthState.UserId.Trim();
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NovaChat",
                "e2ee",
                $"device-{userId}.json");
        }
    }

    public async Task InitializeAsync(ApiService api, CancellationToken cancellationToken = default)
    {
        var currentUserId = AuthState.UserId.Trim();
        if (string.IsNullOrWhiteSpace(currentUserId))
            throw new InvalidOperationException("E2EE requires an authenticated user.");

        await _initializeLock.WaitAsync(cancellationToken);
        try
        {
            if (_privateKey == null || string.IsNullOrWhiteSpace(_deviceId) || _initializedUserId != currentUserId)
            {
                _privateKey?.Dispose();
            _privateKey = null;
            _deviceId = string.Empty;
            _initializedUserId = currentUserId;

            var directory = System.IO.Path.GetDirectoryName(KeyFilePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            if (File.Exists(KeyFilePath))
            {
                try
                {
                    var protectedBytes = await File.ReadAllBytesAsync(KeyFilePath, cancellationToken);
                    var plainBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                    var stored = JsonSerializer.Deserialize<StoredDevice>(plainBytes, JsonOptions);
                    if (stored != null && !string.IsNullOrWhiteSpace(stored.DeviceId) && !string.IsNullOrWhiteSpace(stored.PrivateKeyPem))
                    {
                        _deviceId = stored.DeviceId;
                        _privateKey = RSA.Create();
                        _privateKey.ImportFromPem(stored.PrivateKeyPem);
                    }
                }
                catch
                {
                    _privateKey?.Dispose();
                    _privateKey = null;
                    _deviceId = string.Empty;
                }
            }

                if (_privateKey == null)
                {
                    _privateKey = RSA.Create(RsaKeySize);
                    _deviceId = Guid.NewGuid().ToString("N");
                    var stored = new StoredDevice { DeviceId = _deviceId, PrivateKeyPem = _privateKey.ExportPkcs8PrivateKeyPem() };
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(stored, JsonOptions);
                    var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                    await File.WriteAllBytesAsync(KeyFilePath, protectedBytes, cancellationToken);
                }
            }
        }
        finally
        {
            _initializeLock.Release();
        }

        var publicKeyPem = _privateKey!.ExportSubjectPublicKeyInfoPem();
        await api.PostAsync<RegisterDeviceRequest, RegisterDeviceResponse>("api/e2ee/devices", new RegisterDeviceRequest { DeviceId = _deviceId, PublicKeyPem = publicKeyPem });
    }

    public async Task<string> EncryptForChatAsync(int chatId, string plaintext, ApiService api, CancellationToken cancellationToken = default)
    {
        // Refresh the current device registration before encryption so a missing/revoked
        // server-side device row cannot cause the message to be encrypted without this device.
        await InitializeAsync(api, cancellationToken);
        if (string.IsNullOrWhiteSpace(plaintext))
            throw new ArgumentException("Message cannot be empty.", nameof(plaintext));
        if (plaintext.Length > MaxPlaintextLength)
            throw new InvalidOperationException("Message is too large.");

        var devices = await api.GetAsync<List<E2eeDeviceDto>>($"api/e2ee/chats/{chatId}/devices", cancellationToken);
        if (devices == null || devices.Count == 0)
            throw new InvalidOperationException("No trusted encryption devices are registered for this conversation. Ask every participant to open NovaChat once.");

        var aesKey = RandomNumberGenerator.GetBytes(AesKeySize);
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var plain = Encoding.UTF8.GetBytes(plaintext);
            var cipher = new byte[plain.Length];
            var tag = new byte[TagSize];
            using (var aes = new AesGcm(aesKey, TagSize))
                aes.Encrypt(nonce, plain, cipher, tag);

            var keys = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var device in devices.DistinctBy(x => x.DeviceId))
            {
                if (string.IsNullOrWhiteSpace(device.DeviceId) || string.IsNullOrWhiteSpace(device.PublicKeyPem))
                    continue;

                using var rsa = RSA.Create();
                rsa.ImportFromPem(device.PublicKeyPem);
                keys[device.DeviceId] = Convert.ToBase64String(rsa.Encrypt(aesKey, RSAEncryptionPadding.OaepSHA256));
            }

            if (keys.Count == 0)
                throw new InvalidOperationException("No valid trusted encryption devices are registered for this conversation.");

            var envelope = new E2eeEnvelope
            {
                Version = 1,
                Algorithm = TextAlgorithm,
                Nonce = Convert.ToBase64String(nonce),
                Tag = Convert.ToBase64String(tag),
                Ciphertext = Convert.ToBase64String(cipher),
                Keys = keys
            };
            return JsonSerializer.Serialize(envelope, JsonOptions);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(aesKey);
        }
    }

    public string DeviceId
        => _deviceId;

    public string PublicKeyPem
        => _privateKey?.ExportSubjectPublicKeyInfoPem() ?? string.Empty;

    public bool TryRewrapMessageKeyForDevice(string envelopeJson, string targetPublicKeyPem, out string wrappedKey)
    {
        wrappedKey = string.Empty;
        if (_privateKey == null || string.IsNullOrWhiteSpace(_deviceId) ||
            string.IsNullOrWhiteSpace(targetPublicKeyPem) ||
            !E2eeEnvelope.TryParse(envelopeJson, out var envelope) || envelope == null ||
            !envelope.Keys.TryGetValue(_deviceId, out var localWrappedKey) ||
            string.IsNullOrWhiteSpace(localWrappedKey))
            return false;

        try
        {
            var aesKey = _privateKey.Decrypt(
                Convert.FromBase64String(localWrappedKey),
                RSAEncryptionPadding.OaepSHA256);

            try
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(targetPublicKeyPem);
                wrappedKey = Convert.ToBase64String(
                    rsa.Encrypt(aesKey, RSAEncryptionPadding.OaepSHA256));
                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(aesKey);
            }
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public bool HasLocalMessageKey(string envelopeJson)
        => _privateKey != null &&
           !string.IsNullOrWhiteSpace(_deviceId) &&
           E2eeEnvelope.TryParse(envelopeJson, out var envelope) &&
           envelope != null &&
           envelope.Keys.ContainsKey(_deviceId);

    public bool AcceptRecoveredMessageKey(int messageId, string envelopeJson, string wrappedKey)
    {
        if (messageId <= 0 ||
            _privateKey == null ||
            string.IsNullOrWhiteSpace(_deviceId) ||
            string.IsNullOrWhiteSpace(wrappedKey) ||
            !E2eeEnvelope.TryParse(envelopeJson, out var envelope) ||
            envelope == null)
            return false;

        try
        {
            var key = _privateKey.Decrypt(
                Convert.FromBase64String(wrappedKey),
                RSAEncryptionPadding.OaepSHA256);
            CryptographicOperations.ZeroMemory(key);
            _recoveredMessageKeys[messageId] = wrappedKey;
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public async Task<MessageModel> DecryptMessageAsync(MessageModel message, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (E2eeMediaEnvelope.TryParse(message.Content, out var mediaEnvelope) && mediaEnvelope != null)
            return await DecryptMediaMessageAsync(message, mediaEnvelope, cancellationToken);

        if (!E2eeEnvelope.TryParse(message.Content, out var envelope) || envelope == null)
        {
            if (message.MessageType is "image" or "file" or "voice") return message;
            message.Content = "[Legacy message — not E2EE]";
            return message;
        }

        if (!envelope.Keys.TryGetValue(_deviceId, out var wrappedKey))
        {
            if (!_recoveredMessageKeys.TryGetValue(message.Id, out wrappedKey))
            {
                message.Content = "[Encrypted message — this device has no key]";
                return message;
            }
        }

        try
        {
            var key = _privateKey!.Decrypt(Convert.FromBase64String(wrappedKey), RSAEncryptionPadding.OaepSHA256);
            try
            {
                var nonce = Convert.FromBase64String(envelope.Nonce);
                var tag = Convert.FromBase64String(envelope.Tag);
                var cipher = Convert.FromBase64String(envelope.Ciphertext);
                var plain = new byte[cipher.Length];
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(nonce, cipher, tag, plain);
                message.Content = Encoding.UTF8.GetString(plain);
                return message;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
        catch (CryptographicException)
        {
            message.Content = "[Encrypted message — unable to decrypt]";
            return message;
        }
        catch (FormatException)
        {
            message.Content = "[Encrypted message — unable to decrypt]";
            return message;
        }
        catch (ArgumentException)
        {
            message.Content = "[Encrypted message — unable to decrypt]";
            return message;
        }
    }

    private async Task<MessageModel> DecryptMediaMessageAsync(MessageModel message, E2eeMediaEnvelope envelope, CancellationToken cancellationToken)
    {
        if (!envelope.Keys.TryGetValue(_deviceId, out var wrappedKey))
        {
            message.Content = "[Encrypted media — this device has no key]";
            return message;
        }

        try
        {
            var key = _privateKey!.Decrypt(Convert.FromBase64String(wrappedKey), RSAEncryptionPadding.OaepSHA256);
            try
            {
                var nonce = Convert.FromBase64String(envelope.MetadataNonce);
                var tag = Convert.FromBase64String(envelope.MetadataTag);
                var cipher = Convert.FromBase64String(envelope.MetadataCiphertext);
                var plain = new byte[cipher.Length];
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(nonce, cipher, tag, plain);
                var metadata = JsonSerializer.Deserialize<MediaMetadata>(plain, JsonOptions);
                if (metadata == null || metadata.Size < 0 || metadata.Size > MaxFileBytes ||
                    metadata.Type is not ("image" or "file" or "voice") || string.IsNullOrWhiteSpace(metadata.FileName))
                {
                    message.Content = "[Encrypted media — invalid metadata]";
                    return message;
                }

                message.MessageType = metadata.Type;
                message.FileName = metadata.FileName;
                message.ContentType = metadata.ContentType;
                message.FileSize = metadata.Size;
                message.DurationSeconds = metadata.DurationSeconds;
                message.AttachmentUrl = $"/api/ChatMedia/{message.Id}";
                var icon = metadata.Type switch { "image" => "📷", "voice" => "🎙", _ => "📎" };
                message.Content = $"{icon} {metadata.FileName}\u200B{message.Id}";
                return message;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
        catch (CryptographicException)
        {
            message.Content = "[Encrypted media — unable to decrypt]";
            return message;
        }
        catch (FormatException)
        {
            message.Content = "[Encrypted media — unable to decrypt]";
            return message;
        }
        catch (ArgumentException)
        {
            message.Content = "[Encrypted media — unable to decrypt]";
            return message;
        }
    }

    public async Task<byte[]> DecryptMediaBytesAsync(string envelopeJson, byte[] encryptedBytes, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        if (encryptedBytes.Length == 0) return encryptedBytes;
        if (!E2eeMediaEnvelope.TryParse(envelopeJson, out var envelope) || envelope == null)
            throw new InvalidOperationException("The media encryption envelope is invalid.");
        if (!envelope.Keys.TryGetValue(_deviceId, out var wrappedKey))
            throw new CryptographicException("This device does not have the media key.");

        var key = _privateKey!.Decrypt(Convert.FromBase64String(wrappedKey), RSAEncryptionPadding.OaepSHA256);
        try
        {
            var nonce = Convert.FromBase64String(envelope.FileNonce);
            var tag = Convert.FromBase64String(envelope.FileTag);
            var plain = new byte[encryptedBytes.Length];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, encryptedBytes, tag, plain);
            return plain;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public async Task<E2eeMediaEncryptionResult> EncryptMediaFileAsync(
        int chatId,
        string filePath,
        string type,
        double? durationSeconds,
        ApiService api,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        if (!File.Exists(filePath)) throw new FileNotFoundException("Media file was not found.", filePath);

        type = type.Trim().ToLowerInvariant();
        var info = new FileInfo(filePath);
        var maxBytes = type switch { "image" => MaxImageBytes, "voice" => MaxVoiceBytes, _ => MaxFileBytes };
        if (type is not ("image" or "file" or "voice")) throw new InvalidOperationException("Invalid media type.");
        if (info.Length <= 0 || info.Length > maxBytes) throw new InvalidOperationException($"This {type} is too large or empty.");
        if (type == "voice" && !string.Equals(info.Extension, ".wav", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Voice messages must be WAV audio.");

        var devices = await api.GetAsync<List<E2eeDeviceDto>>($"api/e2ee/chats/{chatId}/devices", cancellationToken);
        if (devices == null || devices.Count == 0) throw new InvalidOperationException("No trusted encryption devices are registered for this conversation. Ask every participant to open NovaChat once.");

        var fileBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var aesKey = RandomNumberGenerator.GetBytes(AesKeySize);
        try
        {
            var fileNonce = RandomNumberGenerator.GetBytes(NonceSize);
            var fileCipher = new byte[fileBytes.Length];
            var fileTag = new byte[TagSize];
            using (var aes = new AesGcm(aesKey, TagSize))
                aes.Encrypt(fileNonce, fileBytes, fileCipher, fileTag);

            var metadata = new MediaMetadata
            {
                Type = type,
                FileName = System.IO.Path.GetFileName(filePath),
                ContentType = GetContentType(filePath),
                Size = fileBytes.LongLength,
                DurationSeconds = durationSeconds
            };
            var metadataPlain = JsonSerializer.SerializeToUtf8Bytes(metadata, JsonOptions);
            var metadataNonce = RandomNumberGenerator.GetBytes(NonceSize);
            var metadataCipher = new byte[metadataPlain.Length];
            var metadataTag = new byte[TagSize];
            using (var aes = new AesGcm(aesKey, TagSize))
                aes.Encrypt(metadataNonce, metadataPlain, metadataCipher, metadataTag);

            var keys = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var device in devices.DistinctBy(x => x.DeviceId))
            {
                if (string.IsNullOrWhiteSpace(device.DeviceId) || string.IsNullOrWhiteSpace(device.PublicKeyPem))
                    continue;

                using var rsa = RSA.Create();
                rsa.ImportFromPem(device.PublicKeyPem);
                keys[device.DeviceId] = Convert.ToBase64String(rsa.Encrypt(aesKey, RSAEncryptionPadding.OaepSHA256));
            }

            if (keys.Count == 0)
                throw new InvalidOperationException("No valid trusted encryption devices are registered for this conversation.");

            var blobId = Guid.NewGuid().ToString("N");
            var envelope = new E2eeMediaEnvelope
            {
                Version = 1,
                Algorithm = MediaAlgorithm,
                BlobId = blobId,
                FileNonce = Convert.ToBase64String(fileNonce),
                FileTag = Convert.ToBase64String(fileTag),
                MetadataNonce = Convert.ToBase64String(metadataNonce),
                MetadataTag = Convert.ToBase64String(metadataTag),
                MetadataCiphertext = Convert.ToBase64String(metadataCipher),
                Keys = keys
            };

            return new E2eeMediaEncryptionResult(blobId, envelope.Serialize(), fileCipher);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(aesKey);
            CryptographicOperations.ZeroMemory(fileBytes);
        }
    }

    private static string GetContentType(string filePath) => System.IO.Path.GetExtension(filePath).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".wav" => "audio/wav",
        ".mp4" => "video/mp4",
        ".mov" => "video/quicktime",
        ".webm" => "video/webm",
        ".mkv" => "video/x-matroska",
        ".pdf" => "application/pdf",
        ".txt" => "text/plain",
        ".csv" => "text/csv",
        ".json" => "application/json",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xls" => "application/vnd.ms-excel",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".ppt" => "application/vnd.ms-powerpoint",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ".zip" => "application/zip",
        ".rar" => "application/vnd.rar",
        ".7z" => "application/x-7z-compressed",
        _ => "application/octet-stream"
    };

    private Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var currentUserId = AuthState.UserId.Trim();
        if (_privateKey == null || string.IsNullOrWhiteSpace(_deviceId) || _initializedUserId != currentUserId)
            throw new InvalidOperationException("E2EE is not initialized for the current user.");
        return Task.CompletedTask;
    }

    private sealed class StoredDevice
    {
        public string DeviceId { get; set; } = string.Empty;
        public string PrivateKeyPem { get; set; } = string.Empty;
    }

    private sealed class RegisterDeviceRequest
    {
        public string DeviceId { get; set; } = string.Empty;
        public string PublicKeyPem { get; set; } = string.Empty;
    }

    private sealed class RegisterDeviceResponse
    {
        public string DeviceId { get; set; } = string.Empty;
    }

    private sealed class E2eeDeviceDto
    {
        public string DeviceId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string PublicKeyPem { get; set; } = string.Empty;
    }

    private sealed class MediaMetadata
    {
        public string Type { get; set; } = "file";
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = "application/octet-stream";
        public long Size { get; set; }
        public double? DurationSeconds { get; set; }
    }

    private sealed class E2eeEnvelope
    {
        [JsonPropertyName("v")] public int Version { get; set; }
        [JsonPropertyName("alg")] public string Algorithm { get; set; } = string.Empty;
        [JsonPropertyName("nonce")] public string Nonce { get; set; } = string.Empty;
        [JsonPropertyName("tag")] public string Tag { get; set; } = string.Empty;
        [JsonPropertyName("ciphertext")] public string Ciphertext { get; set; } = string.Empty;
        [JsonPropertyName("keys")] public Dictionary<string, string> Keys { get; set; } = new(StringComparer.Ordinal);

        public static bool TryParse(string? content, out E2eeEnvelope? envelope)
        {
            envelope = null;
            if (string.IsNullOrWhiteSpace(content)) return false;
            try
            {
                var candidate = JsonSerializer.Deserialize<E2eeEnvelope>(content, JsonOptions);
                if (candidate == null || candidate.Version != 1 || candidate.Algorithm != TextAlgorithm || candidate.Keys.Count == 0) return false;
                if (string.IsNullOrWhiteSpace(candidate.Nonce) || string.IsNullOrWhiteSpace(candidate.Tag) || string.IsNullOrWhiteSpace(candidate.Ciphertext)) return false;
                envelope = candidate;
                return true;
            }
            catch (JsonException) { return false; }
        }
    }

    private sealed class E2eeMediaEnvelope
    {
        [JsonPropertyName("v")] public int Version { get; set; }
        [JsonPropertyName("alg")] public string Algorithm { get; set; } = string.Empty;
        [JsonPropertyName("blobId")] public string BlobId { get; set; } = string.Empty;
        [JsonPropertyName("fileNonce")] public string FileNonce { get; set; } = string.Empty;
        [JsonPropertyName("fileTag")] public string FileTag { get; set; } = string.Empty;
        [JsonPropertyName("metadataNonce")] public string MetadataNonce { get; set; } = string.Empty;
        [JsonPropertyName("metadataTag")] public string MetadataTag { get; set; } = string.Empty;
        [JsonPropertyName("metadataCiphertext")] public string MetadataCiphertext { get; set; } = string.Empty;
        [JsonPropertyName("keys")] public Dictionary<string, string> Keys { get; set; } = new(StringComparer.Ordinal);

        public string Serialize() => "__NOVACHAT_E2EE_MEDIA__" + JsonSerializer.Serialize(this, JsonOptions);

        public static bool TryParse(string? content, out E2eeMediaEnvelope? envelope)
        {
            envelope = null;
            const string prefix = "__NOVACHAT_E2EE_MEDIA__";
            if (string.IsNullOrWhiteSpace(content) || !content.StartsWith(prefix, StringComparison.Ordinal)) return false;
            try
            {
                var value = JsonSerializer.Deserialize<E2eeMediaEnvelope>(content[prefix.Length..], JsonOptions);
                if (value == null || value.Version != 1 || value.Algorithm != MediaAlgorithm ||
                    string.IsNullOrWhiteSpace(value.BlobId) || !Guid.TryParseExact(value.BlobId, "N", out _) ||
                    string.IsNullOrWhiteSpace(value.FileNonce) || string.IsNullOrWhiteSpace(value.FileTag) ||
                    string.IsNullOrWhiteSpace(value.MetadataNonce) || string.IsNullOrWhiteSpace(value.MetadataTag) ||
                    string.IsNullOrWhiteSpace(value.MetadataCiphertext) || value.Keys.Count == 0) return false;
                envelope = value;
                return true;
            }
            catch (JsonException) { return false; }
        }
    }

    public sealed record E2eeMediaEncryptionResult(string BlobId, string EnvelopeJson, byte[] EncryptedBytes);
}
