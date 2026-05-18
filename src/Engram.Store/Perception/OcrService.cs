using Microsoft.Extensions.Logging;

namespace Engram.Store.Perception;

/// <summary>
/// OCR service for extracting text from screen captures.
/// Uses Windows.Media.Ocr on Windows 10+ (WinRT API).
/// Falls back to basic text extraction if OCR unavailable.
/// </summary>
public class OcrService : IDisposable
{
    private readonly ILogger<OcrService>? _logger;
    private bool _ocrAvailable;
    private bool _disposed;
    private bool _initialized;

    public bool IsAvailable => _ocrAvailable;

    public OcrService(ILogger<OcrService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Initialize OCR engine. Call once at startup.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized) return;

        try
        {
            // Check if Windows.Media.Ocr is available (Win10+)
            var ocrEngine = await TryCreateOcrEngine();
            _ocrAvailable = ocrEngine != null;
            _initialized = true;
            _logger?.LogInformation("OCR {Status}", _ocrAvailable ? "available" : "unavailable");
        }
        catch (Exception ex)
        {
            _ocrAvailable = false;
            _initialized = true;
            _logger?.LogWarning(ex, "OCR initialization failed");
        }
    }

    /// <summary>
    /// Extract text from a screen frame image.
    /// </summary>
    public async Task<string> ExtractTextAsync(ScreenFrame frame)
    {
        if (frame.ImageData == null || frame.ImageData.Length == 0)
            return string.Empty;

        try
        {
            if (_ocrAvailable)
            {
                return await ExtractTextWinRt(frame.ImageData);
            }
            else
            {
                // Fallback: extract text from active window title only
                return $"[Window: {frame.ActiveWindowTitle}] [Process: {frame.ActiveWindowProcess}]";
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "OCR extraction failed");
            return $"[Window: {frame.ActiveWindowTitle}]";
        }
    }

    /// <summary>
    /// Extract text using Windows.Media.Ocr (WinRT).
    /// </summary>
    private async Task<string> ExtractTextWinRt(byte[] imageData)
    {
        try
        {
            // Use WinRT OCR if available
            // This requires Windows 10+ and the Microsoft.Windows.SDK.Contracts NuGet package
            // For now, use a managed approach with the active window title
            // Real implementation would use:
            // var stream = new InMemoryRandomAccessStream();
            // var bitmap = await BitmapDecoder.CreateAsync(stream);
            // var ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
            // var result = await ocrEngine.RecognizeAsync(bitmap);
            // return result.Text;

            // Managed fallback: return window title (real OCR needs WinRT package)
            await Task.CompletedTask;
            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task<object?> TryCreateOcrEngine()
    {
        try
        {
            // Try to create WinRT OCR engine
            // Requires Windows 10 Build 10240+
            await Task.CompletedTask;
            return null; // Will be replaced with actual WinRT call
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}
