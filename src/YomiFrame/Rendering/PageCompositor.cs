using SkiaSharp;

namespace YomiFrame.Rendering;

/// <summary>
/// Composites pages for display — handles double-page layout, shadows, and split detection.
/// </summary>
public sealed class PageCompositor : IDisposable
{
    private readonly bool _doublePage;
    private readonly bool _coverMode;
    private readonly bool _showShadow;
    private readonly bool _splitWide;
    private readonly bool _isRtl;

    public PageCompositor(bool doublePage, bool coverMode, bool showShadow, bool splitWide, bool isRtl)
    {
        _doublePage = doublePage;
        _coverMode = coverMode;
        _showShadow = showShadow;
        _splitWide = splitWide;
        _isRtl = isRtl;
    }

    /// <summary>
    /// Determines if a page is "wide" (landscape orientation, likely a double-page spread).
    /// </summary>
    public static bool IsWidePage(SKBitmap bitmap)
    {
        return bitmap.Width > bitmap.Height * 1.2f;
    }

    /// <summary>
    /// Splits a wide bitmap into left and right halves.
    /// </summary>
    public static (SKBitmap left, SKBitmap right) SplitPage(SKBitmap source)
    {
        int halfW = source.Width / 2;
        int h = source.Height;

        var left = new SKBitmap(halfW, h, source.ColorType, source.AlphaType);
        var right = new SKBitmap(source.Width - halfW, h, source.ColorType, source.AlphaType);

        using (var leftCanvas = new SKCanvas(left))
        {
            leftCanvas.DrawBitmap(source, new SKRect(0, 0, halfW, h), new SKRect(0, 0, halfW, h));
        }

        using (var rightCanvas = new SKCanvas(right))
        {
            rightCanvas.DrawBitmap(source, new SKRect(halfW, 0, source.Width, h), new SKRect(0, 0, source.Width - halfW, h));
        }

        return (left, right);
    }

    /// <summary>
    /// Composites two pages side by side for double-page view.
    /// </summary>
    public SKBitmap CompositeDoublePages(SKBitmap page1, SKBitmap page2)
    {
        // Normalize heights
        int maxH = Math.Max(page1.Height, page2.Height);
        float scale1 = (float)maxH / page1.Height;
        float scale2 = (float)maxH / page2.Height;

        int w1 = (int)(page1.Width * scale1);
        int w2 = (int)(page2.Width * scale2);
        int shadowWidth = _showShadow ? 4 : 0;
        int totalW = w1 + w2 + shadowWidth;

        var result = new SKBitmap(totalW, maxH, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Black);

        using var paint = new SKPaint
        {
            FilterQuality = SKFilterQuality.High,
            IsAntialias = true
        };

        // RTL: page1 on right, page2 on left
        // LTR: page1 on left, page2 on right
        SKRect rect1, rect2;
        if (_isRtl)
        {
            rect2 = new SKRect(0, 0, w2, maxH);
            rect1 = new SKRect(w2 + shadowWidth, 0, totalW, maxH);
        }
        else
        {
            rect1 = new SKRect(0, 0, w1, maxH);
            rect2 = new SKRect(w1 + shadowWidth, 0, totalW, maxH);
        }

        canvas.DrawBitmap(page1, new SKRect(0, 0, page1.Width, page1.Height), rect1, paint);
        canvas.DrawBitmap(page2, new SKRect(0, 0, page2.Width, page2.Height), rect2, paint);

        // Draw shadow divider
        if (_showShadow)
        {
            float shadowX = _isRtl ? w2 : w1;
            using var shadowPaint = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(shadowX, 0),
                    new SKPoint(shadowX + shadowWidth, 0),
                    new[] { new SKColor(0, 0, 0, 80), new SKColor(0, 0, 0, 0) },
                    SKShaderTileMode.Clamp)
            };
            canvas.DrawRect(shadowX, 0, shadowWidth, maxH, shadowPaint);
        }

        return result;
    }

    public void Dispose()
    {
        // No unmanaged resources currently
    }
}
