using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PDFiumCore;

namespace ShuiMan.Core;

internal sealed class PdfBook : IOpenBook
{
    // PDFium requires serialization across all documents, including disposal.
    private static readonly SemaphoreSlim nativeGate = new(1, 1);
    private static int liveDocuments;
    private FpdfDocumentT? document;
    private volatile bool disposed;
    public Publication Publication { get; }

    private PdfBook(string path, FpdfDocumentT document, CancellationToken token)
    {
        this.document = document;
        Publication = DocumentEngine.Describe(path, "pdf");
        int count = fpdfview.FPDF_GetPageCount(document);
        if (count <= 0 || count > 100_000) throw new InvalidDataException("PDF 页数无效或超过限制。");
        for (int i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            double width = 0, height = 0;
            if (fpdfview.FPDF_GetPageSizeByIndex(document, i, ref width, ref height) == 0 ||
                !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
                throw new InvalidDataException($"PDF 第 {i + 1} 页尺寸无效。");
            Publication.Units.Add(new ReadingUnit
            {
                Locator = new SourceLocator($"pdf:page:{i}", i), Title = $"第 {i + 1} 页",
                Width = width, Height = height, IsCover = i == 0
            });
        }
    }

    public static Task<IOpenBook> OpenAsync(string path, string? password, CancellationToken token) =>
        Task.Run<IOpenBook>(() =>
        {
            nativeGate.Wait(token);
            try
            {
                token.ThrowIfCancellationRequested();
                if (liveDocuments == 0) fpdfview.FPDF_InitLibrary();
                FpdfDocumentT? opened = null;
                try
                {
                    // PDFium accepts UTF-8 paths and owns its file handle until close.
                    opened = fpdfview.FPDF_LoadDocument(path, password);
                    if (opened is null) throw OpenError(fpdfview.FPDF_GetLastError());
                    var book = new PdfBook(path, opened, token);
                    token.ThrowIfCancellationRequested();
                    liveDocuments++;
                    opened = null;
                    return book;
                }
                finally
                {
                    if (opened is not null) fpdfview.FPDF_CloseDocument(opened);
                    if (liveDocuments == 0) fpdfview.FPDF_DestroyLibrary();
                }
            }
            finally { nativeGate.Release(); }
        }, token);

    private static Exception OpenError(ulong code) => code switch
    {
        4 => new PasswordRequiredException(),
        2 => new IOException("PDF 文件无法读取，请检查文件是否存在以及读取权限。"),
        3 => new InvalidDataException("PDF 文件格式无效或内容已损坏。"),
        5 => new NotSupportedException("此 PDF 使用了不支持的加密方式。"),
        _ => new InvalidDataException($"PDF 无法打开（错误 {code}）。请检查文件是否完整。")
    };

    public Task<BitmapSource> RenderAsync(int index, int maxEdge = 2400, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (index < 0 || index >= Publication.Units.Count) throw new ArgumentOutOfRangeException(nameof(index));
        return Task.Run(() =>
        {
            nativeGate.Wait(cancellationToken);
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                cancellationToken.ThrowIfCancellationRequested();
                var current = document ?? throw new ObjectDisposedException(nameof(PdfBook));
                var unit = Publication.Units[index];
                double scale = Math.Clamp(maxEdge, 64, 8192) / Math.Max(unit.Width, unit.Height);
                int width = Math.Max(1, (int)Math.Round(unit.Width * scale));
                int height = Math.Max(1, (int)Math.Round(unit.Height * scale));
                if ((long)width * height * 4 > BookArchive.ResourceLimit)
                    throw new InvalidDataException("PDF 页面渲染结果超过限制。");
                var page = fpdfview.FPDF_LoadPage(current, index);
                if (page is null) throw new InvalidDataException($"PDF 第 {index + 1} 页无法读取。");
                try
                {
                    var bitmap = fpdfview.FPDFBitmapCreateEx(width, height, 4, IntPtr.Zero, 0);
                    if (bitmap is null) throw new InvalidDataException("PDF 页面图像无法分配，请降低渲染尺寸。");
                    try
                    {
                        if (fpdfview.FPDFBitmapFillRect(bitmap, 0, 0, width, height, 0xffffffffUL) == 0)
                            throw new InvalidDataException("PDF 页面背景无法初始化。");
                        cancellationToken.ThrowIfCancellationRequested();
                        fpdfview.FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, 0,
                            (int)(RenderFlags.RenderAnnotations | RenderFlags.LimitImageCacheSize));
                        cancellationToken.ThrowIfCancellationRequested();
                        int stride = fpdfview.FPDFBitmapGetStride(bitmap);
                        long length = (long)stride * height;
                        var buffer = fpdfview.FPDFBitmapGetBuffer(bitmap);
                        if (stride < width * 4 || length > BookArchive.ResourceLimit || buffer == IntPtr.Zero)
                            throw new InvalidDataException("PDF 页面渲染结果无效或超过限制。");
                        // WPF copies the pixels, so the native bitmap can be closed immediately.
                        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32,
                            null, buffer, checked((int)length), stride);
                        result.Freeze();
                        cancellationToken.ThrowIfCancellationRequested();
                        return result;
                    }
                    finally { fpdfview.FPDFBitmapDestroy(bitmap); }
                }
                finally { fpdfview.FPDF_ClosePage(page); }
            }
            finally { nativeGate.Release(); }
        }, cancellationToken);
    }

    public byte[]? Resource(string path) => null;

    public void Dispose()
    {
        // Rendering uses worker threads and never waits for the WPF dispatcher.
        nativeGate.Wait();
        try
        {
            if (disposed) return;
            disposed = true;
            var current = document;
            document = null;
            try
            {
                if (current is not null) fpdfview.FPDF_CloseDocument(current);
            }
            finally
            {
                if (--liveDocuments == 0) fpdfview.FPDF_DestroyLibrary();
            }
        }
        finally { nativeGate.Release(); }
    }
}
