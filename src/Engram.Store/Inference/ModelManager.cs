using System.Net.Http;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace Engram.Store.Inference;

/// <summary>
/// Manages local SLM models. Downloads Phi-4-mini GGUF on first run.
/// Models cached at %LOCALAPPDATA%/Engram/models/.
/// </summary>
public class ModelManager : IDisposable
{
    private readonly ILogger<ModelManager>? _logger;
    private readonly HttpClient _http;
    private bool _disposed;

    /// <summary>
    /// Default model configuration for Phi-4-mini GGUF Q4_K_M.
    /// </summary>
    public static readonly ModelConfig Phi4Mini = new()
    {
        Name = "Phi-4-mini",
        FileName = "phi-4-mini-q4_k_m.gguf",
        DownloadUrl = "https://huggingface.co/unsloth/Phi-4-mini-instruct-GGUF/resolve/main/Phi-4-mini-instruct-Q4_K_M.gguf",
        SizeBytes = 2_300_000_000L, // ~2.2GB
        ContextSize = 4096,
        Description = "Phi-4-mini 3.8B params, Q4_K_M quantized (~2.2GB)"
    };

    public ModelManager(ILogger<ModelManager>? logger = null)
    {
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromHours(2) };
    }

    /// <summary>
    /// Get the directory where models are stored.
    /// %LOCALAPPDATA%/Engram/models/
    /// </summary>
    public static string GetModelsDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Engram", "models");
    }

    /// <summary>
    /// Get the full path to a model file.
    /// </summary>
    public static string GetModelPath(ModelConfig config)
    {
        return Path.Combine(GetModelsDirectory(), config.FileName);
    }

    /// <summary>
    /// Check if a model is downloaded and ready.
    /// </summary>
    public bool IsModelReady(ModelConfig config)
    {
        var path = GetModelPath(config);
        if (!File.Exists(path))
            return false;

        var fileInfo = new FileInfo(path);
        // Accept if file is at least 90% of expected size (partial downloads)
        return fileInfo.Length >= config.SizeBytes * 0.9;
    }

    /// <summary>
    /// Get model status.
    /// </summary>
    public ModelStatus GetStatus(ModelConfig config)
    {
        var path = GetModelPath(config);
        if (!File.Exists(path))
            return new ModelStatus { State = ModelState.NotDownloaded, Path = path };

        var fileInfo = new FileInfo(path);
        var progress = (double)fileInfo.Length / config.SizeBytes;

        if (progress >= 0.9)
            return new ModelStatus { State = ModelState.Ready, Path = path, SizeBytes = fileInfo.Length, Progress = 1.0 };

        return new ModelStatus
        {
            State = ModelState.PartialDownload,
            Path = path,
            SizeBytes = fileInfo.Length,
            Progress = progress
        };
    }

    /// <summary>
    /// Download a model. Reports progress via callback.
    /// Supports resume (checks for partial download).
    /// </summary>
    public async Task DownloadModelAsync(
        ModelConfig config,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var modelPath = GetModelPath(config);
        var tempPath = modelPath + ".downloading";
        var modelsDir = GetModelsDirectory();

        Directory.CreateDirectory(modelsDir);

        _logger?.LogInformation("Starting download: {Name} ({SizeMB}MB)", config.Name, config.SizeBytes / (1024 * 1024));

        long existingBytes = 0;
        if (File.Exists(tempPath))
        {
            existingBytes = new FileInfo(tempPath).Length;
            _logger?.LogInformation("Resuming download from {Bytes}MB", existingBytes / (1024 * 1024));
        }

        var request = new HttpRequestMessage(HttpMethod.Get, config.DownloadUrl);
        if (existingBytes > 0)
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingBytes, null);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = (response.Content.Headers.ContentLength ?? 0) + existingBytes;
        var downloadedBytes = existingBytes;

        using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var fileStream = new FileStream(tempPath, existingBytes > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920);

        var buffer = new byte[81920];
        int bytesRead;
        var lastReport = DateTime.UtcNow;

        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            downloadedBytes += bytesRead;

            // Report progress every 500ms
            if (DateTime.UtcNow - lastReport > TimeSpan.FromMilliseconds(500))
            {
                progress?.Report(new ModelDownloadProgress
                {
                    BytesDownloaded = downloadedBytes,
                    TotalBytes = totalBytes,
                    Progress = totalBytes > 0 ? (double)downloadedBytes / totalBytes : 0
                });
                lastReport = DateTime.UtcNow;
            }
        }

        // Rename temp to final
        fileStream.Close();
        if (File.Exists(modelPath))
            File.Delete(modelPath);
        File.Move(tempPath, modelPath);

        _logger?.LogInformation("Model download complete: {Path}", modelPath);
        progress?.Report(new ModelDownloadProgress
        {
            BytesDownloaded = downloadedBytes,
            TotalBytes = totalBytes,
            Progress = 1.0
        });
    }

    /// <summary>
    /// Delete a downloaded model.
    /// </summary>
    public bool DeleteModel(ModelConfig config)
    {
        var path = GetModelPath(config);
        if (File.Exists(path))
        {
            File.Delete(path);
            _logger?.LogInformation("Deleted model: {Path}", path);
            return true;
        }
        return false;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _http.Dispose();
            _disposed = true;
        }
    }
}

public class ModelConfig
{
    public string Name { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public int ContextSize { get; init; } = 4096;
    public string Description { get; init; } = string.Empty;
}

public class ModelStatus
{
    public ModelState State { get; init; }
    public string Path { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public double Progress { get; init; }
}

public enum ModelState
{
    NotDownloaded,
    PartialDownload,
    Ready
}

public class ModelDownloadProgress
{
    public long BytesDownloaded { get; init; }
    public long TotalBytes { get; init; }
    public double Progress { get; init; }
}
