using System;
using System.Threading;
using System.Threading.Tasks;

namespace Engram.Store.Automation;

/// <summary>
/// Defines web browser driver operations for automation steps.
/// </summary>
public interface IBrowserDriver : IAsyncDisposable
{
    Task NavigateAsync(string url, CancellationToken ct = default);
    Task ClickAsync(string selector, CancellationToken ct = default);
    Task TypeAsync(string selector, string text, CancellationToken ct = default);
    Task<string> GetTextContentAsync(string selector, CancellationToken ct = default);
    Task<byte[]> TakeScreenshotAsync(CancellationToken ct = default);
    Task<string> GetUrlAsync(CancellationToken ct = default);
}
