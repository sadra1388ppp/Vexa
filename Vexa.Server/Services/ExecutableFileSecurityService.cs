using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace NovaChat.Server.Services;

public sealed class ExecutableFileSecurityService
{
    private const int DefaultScanTimeoutSeconds = 120;

    private readonly IConfiguration _configuration;
    private readonly ILogger<ExecutableFileSecurityService> _logger;
    private readonly HttpClient _httpClient;

    public ExecutableFileSecurityService(
        IConfiguration configuration,
        ILogger<ExecutableFileSecurityService> logger,
        HttpClient httpClient)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<ExecutableValidationResult> ValidateAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            return ExecutableValidationResult.Rejected("The uploaded file could not be found.");

        if (!await IsPortableExecutableAsync(filePath, cancellationToken))
        {
            return ExecutableValidationResult.Rejected(
                "The uploaded file is not a valid Windows PE executable.");
        }

        var sha256 = await ComputeSha256Async(filePath, cancellationToken);
        var cloudResult = await ScanWithCloudmersiveAsync(filePath, cancellationToken);

        switch (cloudResult.Status)
        {
            case CloudScanStatus.Clean:
                return ExecutableValidationResult.Accepted(
                    sha256,
                    "Cloudmersive");

            case CloudScanStatus.MalwareDetected:
                return ExecutableValidationResult.Rejected(cloudResult.Message);

            case CloudScanStatus.InvalidConfiguration:
            case CloudScanStatus.QuotaExceeded:
            case CloudScanStatus.FileTooLarge:
            case CloudScanStatus.ServiceUnavailable:
                _logger.LogWarning(
                    "Cloudmersive could not scan executable. Status: {Status}.",
                    cloudResult.Status);

                if (OperatingSystem.IsWindows())
                {
                    return await ValidateWithWindowsDefenderAsync(
                        filePath,
                        sha256,
                        cancellationToken);
                }

                return ExecutableValidationResult.Rejected(cloudResult.Message);

            case CloudScanStatus.Error:
            default:
                return ExecutableValidationResult.Rejected(cloudResult.Message);
        }
    }

    private async Task<CloudScanResult> ScanWithCloudmersiveAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        var apiKey = _configuration["FileSecurity:Cloudmersive:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return CloudScanResult.Failure(
                CloudScanStatus.InvalidConfiguration,
                "Cloudmersive API key is not configured. A Windows server may use Microsoft Defender as fallback.");
        }

        try
        {
            await using var fileStream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 128 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "virus/scan/file");

            request.Headers.TryAddWithoutValidation("Apikey", apiKey);

            using var multipart = new MultipartFormDataContent();
            using var fileContent = new StreamContent(fileStream);

            fileContent.Headers.ContentType =
                new MediaTypeHeaderValue("application/octet-stream");

            multipart.Add(
                fileContent,
                "inputFile",
                Path.GetFileName(filePath));

            request.Content = multipart;

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                CloudmersiveVirusScanResponse? result;

                try
                {
                    result = JsonSerializer.Deserialize<CloudmersiveVirusScanResponse>(
                        body,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                }
                catch (JsonException exception)
                {
                    _logger.LogError(
                        exception,
                        "Cloudmersive returned an invalid scan response.");

                    return CloudScanResult.Failure(
                        CloudScanStatus.ServiceUnavailable,
                        "Cloudmersive returned an invalid scan response.");
                }

                if (result?.CleanResult == true)
                    return CloudScanResult.Clean();

                var virusName = result?.FoundViruses?
                    .Select(v => v.VirusName)
                    .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));

                return CloudScanResult.Failure(
                    CloudScanStatus.MalwareDetected,
                    string.IsNullOrWhiteSpace(virusName)
                        ? "Cloudmersive detected a threat in the executable."
                        : $"Cloudmersive detected a threat: {virusName}");
            }

            return ClassifyCloudmersiveError(
                response.StatusCode,
                body);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return CloudScanResult.Failure(
                CloudScanStatus.ServiceUnavailable,
                "Cloudmersive scan timed out.");
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(
                exception,
                "Cloudmersive request failed.");

            return CloudScanResult.Failure(
                CloudScanStatus.ServiceUnavailable,
                "Cloudmersive is unavailable.");
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Cloudmersive scan failed.");

            return CloudScanResult.Failure(
                CloudScanStatus.Error,
                "Cloudmersive scan failed.");
        }
    }

    private static CloudScanResult ClassifyCloudmersiveError(
        HttpStatusCode statusCode,
        string responseBody)
    {
        var lowerBody = responseBody.ToLowerInvariant();

        if (statusCode == HttpStatusCode.TooManyRequests ||
            (statusCode is (HttpStatusCode.Forbidden or HttpStatusCode.PaymentRequired) &&
             (lowerBody.Contains("quota") ||
              lowerBody.Contains("limit") ||
              lowerBody.Contains("calls per month"))))
        {
            return CloudScanResult.Failure(
                CloudScanStatus.QuotaExceeded,
                "Cloudmersive scan quota is unavailable.");
        }

        if (statusCode == HttpStatusCode.RequestEntityTooLarge ||
            lowerBody.Contains("maximum file size") ||
            lowerBody.Contains("file size limit"))
        {
            return CloudScanResult.Failure(
                CloudScanStatus.FileTooLarge,
                "The file is larger than the Cloudmersive plan allows.");
        }

        if (statusCode == HttpStatusCode.Unauthorized)
        {
            return CloudScanResult.Failure(
                CloudScanStatus.InvalidConfiguration,
                "The Cloudmersive API key is invalid.");
        }

        if ((int)statusCode >= 500)
        {
            return CloudScanResult.Failure(
                CloudScanStatus.ServiceUnavailable,
                "Cloudmersive is temporarily unavailable.");
        }

        return CloudScanResult.Failure(
            CloudScanStatus.Error,
            $"Cloudmersive rejected the scan request ({(int)statusCode}).");
    }

    private async Task<ExecutableValidationResult> ValidateWithWindowsDefenderAsync(
        string filePath,
        string sha256,
        CancellationToken cancellationToken)
    {
        var defenderPath = FindMpCmdRun();

        if (defenderPath == null)
        {
            return ExecutableValidationResult.Rejected(
                "Cloudmersive could not scan the executable and Microsoft Defender was not found on this Windows server.");
        }

        var timeoutSeconds = _configuration.GetValue(
            "FileSecurity:Executable:ScanTimeoutSeconds",
            DefaultScanTimeoutSeconds);

        timeoutSeconds = Math.Clamp(timeoutSeconds, 10, 900);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = defenderPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.StartInfo.ArgumentList.Add("-Scan");
        process.StartInfo.ArgumentList.Add("-ScanType");
        process.StartInfo.ArgumentList.Add("3");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(filePath);

        try
        {
            if (!process.Start())
            {
                return ExecutableValidationResult.Rejected(
                    "Microsoft Defender could not start the executable scan.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var timeoutCts =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

            timeoutCts.CancelAfter(
                TimeSpan.FromSeconds(timeoutSeconds));

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested)
            {
                TryKill(process);

                return ExecutableValidationResult.Rejected(
                    $"Microsoft Defender scan timed out after {timeoutSeconds} seconds.");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode == 0)
            {
                return ExecutableValidationResult.Accepted(
                    sha256,
                    "Windows Defender");
            }

            var details = string.IsNullOrWhiteSpace(stderr)
                ? stdout
                : stderr;

            details = NormalizeScanDetails(details);

            return ExecutableValidationResult.Rejected(
                string.IsNullOrWhiteSpace(details)
                    ? $"Microsoft Defender rejected the executable scan (exit code {process.ExitCode})."
                    : $"Microsoft Defender rejected the executable scan (exit code {process.ExitCode}). {details}");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        catch (Exception exception)
        {
            TryKill(process);

            _logger.LogError(
                exception,
                "Windows Defender scan failed for executable {FilePath}.",
                filePath);

            return ExecutableValidationResult.Rejected(
                "Microsoft Defender scan failed.");
        }
    }

    private static string? FindMpCmdRun()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var platformRoot = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData),
            "Microsoft",
            "Windows Defender",
            "Platform");

        if (Directory.Exists(platformRoot))
        {
            var platformPath = Directory
                .GetDirectories(platformRoot)
                .OrderByDescending(
                    path => path,
                    StringComparer.OrdinalIgnoreCase)
                .Select(path =>
                    Path.Combine(path, "MpCmdRun.exe"))
                .FirstOrDefault(File.Exists);

            if (platformPath != null)
                return platformPath;
        }

        var programFilesPath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles),
            "Windows Defender",
            "MpCmdRun.exe");

        return File.Exists(programFilesPath)
            ? programFilesPath
            : null;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    private static string NormalizeScanDetails(string details)
    {
        var singleLine = details
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        return singleLine.Length > 500
            ? singleLine[..500] + "..."
            : singleLine;
    }

    private static async Task<string> ComputeSha256Async(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        var hash = await SHA256.HashDataAsync(
            stream,
            cancellationToken);

        return Convert.ToHexString(hash);
    }

    private static async Task<bool> IsPortableExecutableAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (stream.Length < 64)
            return false;

        var dosHeader = new byte[64];

        await ReadExactlyAsync(
            stream,
            dosHeader,
            cancellationToken);

        if (dosHeader[0] != (byte)'M' ||
            dosHeader[1] != (byte)'Z')
        {
            return false;
        }

        var peOffset =
            BinaryPrimitives.ReadInt32LittleEndian(
                dosHeader.AsSpan(0x3C, 4));

        if (peOffset < 64 ||
            peOffset > stream.Length - 4)
        {
            return false;
        }

        stream.Position = peOffset;

        var peHeader = new byte[4];

        await ReadExactlyAsync(
            stream,
            peHeader,
            cancellationToken);

        return peHeader[0] == (byte)'P' &&
               peHeader[1] == (byte)'E' &&
               peHeader[2] == 0 &&
               peHeader[3] == 0;
    }

    private static async Task ReadExactlyAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;

        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(
                    offset,
                    buffer.Length - offset),
                cancellationToken);

            if (read == 0)
                throw new EndOfStreamException();

            offset += read;
        }
    }

    private sealed record CloudmersiveVirusScanResponse(
        bool CleanResult,
        List<FoundVirus>? FoundViruses);

    private sealed record FoundVirus(
        string? FileName,
        string? VirusName);

    private enum CloudScanStatus
    {
        Clean,
        MalwareDetected,
        QuotaExceeded,
        FileTooLarge,
        ServiceUnavailable,
        InvalidConfiguration,
        Error
    }

    private sealed record CloudScanResult(
        CloudScanStatus Status,
        string Message)
    {
        public static CloudScanResult Clean() =>
            new(
                CloudScanStatus.Clean,
                "Cloudmersive found no threats.");

        public static CloudScanResult Failure(
            CloudScanStatus status,
            string message) =>
            new(status, message);
    }
}

public sealed record ExecutableValidationResult(
    bool IsAccepted,
    string Message,
    string? Sha256,
    string Scanner)
{
    public static ExecutableValidationResult Accepted(
        string sha256,
        string scanner) =>
        new(
            true,
            $"Executable accepted after {scanner} scanning.",
            sha256,
            scanner);

    public static ExecutableValidationResult Rejected(
        string message) =>
        new(
            false,
            message,
            null,
            string.Empty);
}
