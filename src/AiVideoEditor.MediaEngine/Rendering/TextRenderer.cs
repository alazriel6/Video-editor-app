using AiVideoEditor.Core.Models;
using SkiaSharp;

namespace AiVideoEditor.MediaEngine.Rendering;

/// <summary>
/// Studio Typography &amp; Glyph Spacing Renderer (BUG FIX 1).
/// Renders floating text on a 100% transparent surface by default (BackgroundBoxEnabled = false)
/// and performs character-by-character glyph positioning for real-time Letter Spacing (-20px to +100px)
/// and Line Height (0.5x to 3.0x).
/// </summary>
public static class TextRenderer
{
    public static void DrawTextWithSpacing(
        SKCanvas canvas,
        string text,
        float x,
        float y,
        SKPaint fillPaint,
        SKPaint? strokePaint,
        float letterSpacing,
        float lineSpacingMultiplier,
        SKTextAlign alignment = SKTextAlign.Center)
    {
        if (string.IsNullOrEmpty(text)) return;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        float fontMetricsLeading = fillPaint.FontMetrics.Descent - fillPaint.FontMetrics.Ascent;
        float baseLineHeight = (fontMetricsLeading > 0 ? fontMetricsLeading : fillPaint.TextSize * 1.2f)
            * Math.Clamp(lineSpacingMultiplier <= 0 ? 1.2f : lineSpacingMultiplier, 0.5f, 3.0f);

        for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
        {
            string line = lines[lineIdx];
            if (line.Length == 0) continue;

            float currentY = y + (lineIdx * baseLineHeight);
            float totalWidth = MeasureLineWithSpacing(line, fillPaint, letterSpacing);

            float startX = alignment switch
            {
                SKTextAlign.Left => x,
                SKTextAlign.Right => x - totalWidth,
                _ => x - (totalWidth * 0.5f)
            };

            float cursorX = startX;
            for (int i = 0; i < line.Length; i++)
            {
                string ch = line[i].ToString();
                if (strokePaint != null)
                {
                    canvas.DrawText(ch, cursorX, currentY, strokePaint);
                }
                canvas.DrawText(ch, cursorX, currentY, fillPaint);
                cursorX += fillPaint.MeasureText(ch) + letterSpacing;
            }
        }
    }

    public static float MeasureLineWithSpacing(string line, SKPaint paint, float letterSpacing)
    {
        if (string.IsNullOrEmpty(line)) return 0f;
        float totalWidth = 0f;
        for (int i = 0; i < line.Length; i++)
        {
            totalWidth += paint.MeasureText(line[i].ToString());
            if (i < line.Length - 1)
                totalWidth += letterSpacing;
        }
        return Math.Max(0f, totalWidth);
    }

    public static SKRect MeasureTextBlockBounds(
        string text,
        SKPaint paint,
        float letterSpacing,
        float lineSpacingMultiplier,
        SKTextAlign alignment,
        out float maxLineWidth,
        out float lineStep,
        out string[] lines)
    {
        lines = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        maxLineWidth = 0f;
        foreach (var line in lines)
        {
            float w = MeasureLineWithSpacing(line, paint, letterSpacing);
            if (w > maxLineWidth) maxLineWidth = w;
        }

        float fontMetricsLeading = paint.FontMetrics.Descent - paint.FontMetrics.Ascent;
        float baseLineHeight = (fontMetricsLeading > 0 ? fontMetricsLeading : paint.TextSize * 1.2f)
            * Math.Clamp(lineSpacingMultiplier <= 0 ? 1.2f : lineSpacingMultiplier, 0.5f, 3.0f);
        lineStep = baseLineHeight;

        float totalHeight = Math.Max(baseLineHeight, lines.Length * baseLineHeight);
        float top = -paint.TextSize;
        float bottom = top + totalHeight;

        float left = alignment switch
        {
            SKTextAlign.Left => 0f,
            SKTextAlign.Right => -maxLineWidth,
            _ => -maxLineWidth * 0.5f
        };
        float right = left + maxLineWidth;

        return new SKRect(left, top, right, bottom);
    }

    public static SKRect MeasureTextBlockBounds(
        string text,
        float anchorX,
        float anchorY,
        SKPaint paint,
        float letterSpacing,
        float lineSpacingMultiplier,
        SKTextAlign alignment,
        float padX,
        float padY)
    {
        var bounds = MeasureTextBlockBounds(
            text,
            paint,
            letterSpacing,
            lineSpacingMultiplier,
            alignment,
            out _,
            out _,
            out _);

        return new SKRect(
            anchorX + bounds.Left - padX,
            anchorY + bounds.Top - padY,
            anchorX + bounds.Right + padX,
            anchorY + bounds.Bottom + padY);
    }
}
