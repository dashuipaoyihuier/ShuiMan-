using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShuiMan.Windows;

/// <summary>Draws in physical left/right order. ScrollViewer handles zoomed panning.</summary>
public sealed class PageSurface : FrameworkElement
{
    private readonly List<(BitmapSource Image, Rect Bounds)> _pages = [];
    public void SetPages(IReadOnlyList<BitmapSource> pages, double width, double height, string fit, double zoom, bool spread, double offset, double rightScale)
    {
        _pages.Clear(); width = Math.Max(100, width); height = Math.Max(100, height);
        if (pages.Count == 0) { Width = width; Height = height; InvalidateVisual(); return; }
        double h = pages.Max(p => (double)p.PixelHeight);
        double gap = pages.Count > 1 && !spread ? 16 : 0;
        var sizes = pages.Select((p, i) => new Size(p.PixelWidth * h / p.PixelHeight * (i == 1 ? rightScale : 1), h * (i == 1 ? rightScale : 1))).ToArray();
        double dy = pages.Count > 1 ? offset * h : 0;
        double minY = Math.Min(0, dy), maxY = Math.Max(h, pages.Count > 1 ? dy + sizes[1].Height : h);
        double nativeWidth = sizes.Sum(s => s.Width) + gap, nativeHeight = maxY - minY;
        double scale = fit == "actual" ? 1 : fit == "width" ? Math.Max(1, width - 24) / nativeWidth : Math.Min(Math.Max(1, width - 24) / nativeWidth, Math.Max(1, height - 24) / nativeHeight);
        scale *= zoom;
        Width = Math.Max(width, nativeWidth * scale + 24); Height = Math.Max(height, nativeHeight * scale + 24);
        double x = (Width - nativeWidth * scale) / 2, y = (Height - nativeHeight * scale) / 2 - minY * scale;
        for (int i = 0; i < pages.Count; i++)
        {
            _pages.Add((pages[i], new Rect(x, y + (i == 1 ? dy * scale : 0), sizes[i].Width * scale, sizes[i].Height * scale)));
            x += (sizes[i].Width + gap) * scale;
        }
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        foreach (var page in _pages) dc.DrawImage(page.Image, page.Bounds);
    }
}
