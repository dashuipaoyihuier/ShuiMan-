using System.IO;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using ShuiMan.Core;

namespace ShuiMan.Windows;

/// <summary>Offline publication origin. Book markup cannot access files, networks, scripts, or host objects.</summary>
internal sealed class RestrictedBookView(WebView2 view, string userDataDirectory, SemaphoreSlim io) : IDisposable
{
    private IOpenBook? _book;
    private string _host = "empty.invalid";
    private bool _initialized;
    private string? _resource;
    public event Action<string>? NavigateRequested;
    public void SetZoom(double zoom) { if (_initialized) view.ZoomFactor = Math.Clamp(zoom, .25, 5); }
    public async Task ShowAsync(IOpenBook book, string resource, CancellationToken token)
    {
        if (!_initialized)
        {
            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(null, userDataDirectory);
                await view.EnsureCoreWebView2Async(environment);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                throw new InvalidOperationException("此 EPUB 页面需要 Microsoft Edge WebView2 Runtime。请运行安装包中的 install.ps1 安装运行环境，再重新打开。ZIP、图片和 PDF 阅读不受影响。");
            }
            var core = view.CoreWebView2;
            core.Settings.IsScriptEnabled = false; core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false; core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false; core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.AreDevToolsEnabled = false; core.Settings.AreDefaultContextMenusEnabled = false;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.NavigationStarting += (_, e) =>
            {
                if (e.Uri == "about:blank") return;
                if (!IsLocal(e.Uri)) { e.Cancel = true; return; }
                var target = Uri.UnescapeDataString(new Uri(e.Uri).AbsolutePath.TrimStart('/'));
                if (target != _resource)
                {
                    e.Cancel = true;
                    view.Dispatcher.BeginInvoke(() => NavigateRequested?.Invoke(target));
                }
            };
            core.FrameNavigationStarting += (_, e) => e.Cancel = true;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += ResourceRequested;
            _initialized = true;
        }
        token.ThrowIfCancellationRequested();
        if (_book == book && _resource == resource) return;
        _resource = resource;
        _book = book; _host = "book-" + book.Publication.Identity.ToLowerInvariant() + ".invalid";
        view.CoreWebView2.Navigate("https://" + _host + "/" + string.Join("/", resource.Split('/').Select(Uri.EscapeDataString)));
    }
    private bool IsLocal(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == _host && uri.IsDefaultPort;
    private async void ResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        const string policy = "default-src 'none'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; font-src 'self' data:; script-src 'none'; connect-src 'none'; frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'";
        using var deferral = e.GetDeferral();
        try
        {
            if (_book == null || !IsLocal(e.Request.Uri)) { e.Response = view.CoreWebView2.Environment.CreateWebResourceResponse(null, 403, "Offline publication", ""); return; }
            var path = Uri.UnescapeDataString(new Uri(e.Request.Uri).AbsolutePath.TrimStart('/'));
            bool convert = Path.GetExtension(path).ToLowerInvariant() is ".tif" or ".tiff" or ".heic" or ".heif" or ".jxl";
            var book = _book;
            byte[]? bytes = null;
            await io.WaitAsync();
            try { if (book == _book) bytes = await Task.Run(() => { var raw = book.Resource(path); return raw != null && convert ? DocumentEngine.RasterToPng(raw) : raw; }); }
            finally { io.Release(); }
            if (book != _book) return;
            if (bytes == null) { e.Response = view.CoreWebView2.Environment.CreateWebResourceResponse(null, 404, "Not found", ""); return; }
            string mime = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".xhtml" or ".html" or ".htm" => "text/html", ".css" => "text/css", ".svg" => "image/svg+xml",
                ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".webp" => "image/webp", ".bmp" => "image/bmp",
                ".avif" => "image/avif", ".woff" => "font/woff", ".woff2" => "font/woff2", ".ttf" => "font/ttf", ".otf" => "font/otf", _ => "application/octet-stream"
            };
            if (convert) mime = "image/png";
            e.Response = view.CoreWebView2.Environment.CreateWebResourceResponse(new MemoryStream(bytes, false), 200, "OK", $"Content-Type: {mime}\r\nContent-Security-Policy: {policy}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n");
        }
        catch
        {
            try { e.Response = view.CoreWebView2.Environment.CreateWebResourceResponse(null, 404, "Resource unavailable", ""); }
            catch (InvalidOperationException) { /* A disposed view cancels its deferred requests. */ }
        }
    }
    public void Clear() { if (_book == null) return; _resource = null; _book = null; _host = "empty.invalid"; if (_initialized) view.CoreWebView2.Navigate("about:blank"); }
    public void Dispose() { _book = null; view.Dispose(); }
}
