namespace Everywhere.Web;

public interface IWebBrowserHost
{
    /// <summary>
    /// Opens the browser in non-headless mode for debugging purposes. This will keep the browser open until manually closed, and will prevent automatic disposal.
    /// </summary>
    /// <param name="cancellationToken"></param>
    Task OpenBrowserAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts the main content of the given URL as <see cref="WebPageExtractionResult"/> using the browser extraction pipeline.
    /// </summary>
    /// <param name="url"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<WebPageExtractionResult> ExtractPageAsync(string url, CancellationToken cancellationToken = default);
}