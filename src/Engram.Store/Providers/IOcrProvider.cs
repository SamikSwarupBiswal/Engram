namespace Engram.Store.Providers;

/// <summary>
/// Provider interface for OCR (Optical Character Recognition).
/// Production: Windows Copilot Runtime. Dev: mock/fallback.
/// </summary>
public interface IOcrProvider
{
    /// <summary>Extract text from an image.</summary>
    Task<OcrResult> ExtractTextAsync(byte[] imageData, CancellationToken cancellationToken = default);

    /// <summary>Whether this provider is available on the current platform.</summary>
    bool IsAvailable { get; }
}

public class OcrResult
{
    public string Text { get; init; } = string.Empty;
    public float Confidence { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}
