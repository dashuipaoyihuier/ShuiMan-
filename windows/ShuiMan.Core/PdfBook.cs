using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace ShuiMan.Core;

internal sealed class PdfBook : IOpenBook
{
    private readonly PdfDocument document;
    private readonly SemaphoreSlim renderGate = new(1, 1);
    private bool disposed;
    public Publication Publication { get; }
    private PdfBook(string path, PdfDocument document)
    {
        this.document = document;
        Publication = DocumentEngine.Describe(path, "pdf");
        if (document.PageCount == 0 || document.PageCount > 100_000) throw new InvalidDataException("PDF 页数无效或超过限制。");
        for (uint i = 0; i < document.PageCount; i++)
        {
            using var page = document.GetPage(i);
            Publication.Units.Add(new ReadingUnit
            {
                Locator = new SourceLocator($"pdf:page:{i}", (int)i), Title = $"第 {i + 1} 页",
                Width = page.Size.Width, Height = page.Size.Height, IsCover = i == 0
            });
        }
    }
    public static async Task<IOpenBook> OpenAsync(string path, string? password, CancellationToken token)
    {
        var file = await StorageFile.GetFileFromPathAsync(path).AsTask(token);
        try
        {
            var doc = await (password == null ? PdfDocument.LoadFromFileAsync(file) : PdfDocument.LoadFromFileAsync(file, password)).AsTask(token);
            return new PdfBook(path, doc);
        }
        catch (Exception ex) when ((uint)ex.HResult == 0x8007052b || (uint)ex.HResult == 0x80070005)
        { throw new PasswordRequiredException(); }
    }
    public async Task<BitmapSource> RenderAsync(int index, int maxEdge = 2400, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (index < 0 || index >= Publication.Units.Count) throw new ArgumentOutOfRangeException(nameof(index));
        await renderGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            using var page = document.GetPage((uint)index);
            using var stream = new InMemoryRandomAccessStream();
            double scale = Math.Clamp(maxEdge, 64, 8192) / Math.Max(page.Size.Width, page.Size.Height);
            var options = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Max(1, Math.Round(page.Size.Width * scale)),
                DestinationHeight = (uint)Math.Max(1, Math.Round(page.Size.Height * scale)),
                BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255)
            };
            await page.RenderToStreamAsync(stream, options).AsTask(cancellationToken);
            if (stream.Size > BookArchive.ResourceLimit) throw new InvalidDataException("PDF 页面渲染结果超过限制。");
            stream.Seek(0);
            using var input = stream.AsStreamForRead();
            var bitmap = new BitmapImage();
            bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = input; bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        finally { renderGate.Release(); }
    }
    public byte[]? Resource(string path) => null;
    public void Dispose() => disposed = true;
}
