using System.IO;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace ShuiMan.Core;

internal sealed class PdfBook : IOpenBook
{
    private PdfDocument? document;
    private FileStream? sourceFile;
    private IRandomAccessStream? sourceStream;
    private readonly SemaphoreSlim renderGate = new(1, 1);
    private volatile bool disposed;
    public Publication Publication { get; }
    private PdfBook(string path, PdfDocument document, FileStream sourceFile, IRandomAccessStream sourceStream)
    {
        this.document = document;
        this.sourceFile = sourceFile;
        this.sourceStream = sourceStream;
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
        FileStream? sourceFile = null;
        IRandomAccessStream? sourceStream = null;
        try
        {
            token.ThrowIfCancellationRequested();
            // PdfDocument has no Close/Dispose API. Keep ownership of the input
            // stream so closing a book releases the original file immediately,
            // independently of when the WinRT document wrapper is collected.
            sourceFile = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.RandomAccess);
            sourceStream = sourceFile.AsRandomAccessStream();
            PdfDocument doc;
            try
            {
                doc = await (password == null ? PdfDocument.LoadFromStreamAsync(sourceStream) :
                    PdfDocument.LoadFromStreamAsync(sourceStream, password)).AsTask(token).ConfigureAwait(false);
            }
            catch (Exception ex) when ((uint)ex.HResult == 0x8007052b || (uint)ex.HResult == 0x80070005)
            { throw new PasswordRequiredException(); }
            catch (Exception ex) when (ex is not OperationCanceledException && string.IsNullOrWhiteSpace(ex.Message))
            { throw new InvalidDataException($"PDF 无法打开（错误 0x{ex.HResult:X8}）。请检查文件是否完整。", ex); }
            token.ThrowIfCancellationRequested();
            var book = new PdfBook(path, doc, sourceFile, sourceStream);
            sourceFile = null;
            sourceStream = null;
            return book;
        }
        finally
        {
            // Covers cancellation, password errors, malformed documents, and
            // constructor validation failures before ownership is transferred.
            try { sourceStream?.Dispose(); }
            finally { sourceFile?.Dispose(); }
        }
    }
    public async Task<BitmapSource> RenderAsync(int index, int maxEdge = 2400, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (index < 0 || index >= Publication.Units.Count) throw new ArgumentOutOfRangeException(nameof(index));
        await renderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var current = document ?? throw new ObjectDisposedException(nameof(PdfBook));
            using var page = current.GetPage((uint)index);
            using var stream = new InMemoryRandomAccessStream();
            double scale = Math.Clamp(maxEdge, 64, 8192) / Math.Max(page.Size.Width, page.Size.Height);
            var options = new PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Max(1, Math.Round(page.Size.Width * scale)),
                DestinationHeight = (uint)Math.Max(1, Math.Round(page.Size.Height * scale)),
                BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255)
            };
            await page.RenderToStreamAsync(stream, options).AsTask(cancellationToken).ConfigureAwait(false);
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
    public void Dispose()
    {
        // Render continuations do not capture a UI context, so this drain cannot
        // depend on the thread synchronously closing the book.
        renderGate.Wait();
        try
        {
            if (disposed) return;
            disposed = true;
            document = null;
            try { sourceStream?.Dispose(); }
            finally
            {
                sourceStream = null;
                sourceFile?.Dispose();
                sourceFile = null;
            }
        }
        finally { renderGate.Release(); }
    }
}
