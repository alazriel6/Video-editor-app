using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AiVideoEditor.Core.Models;
using AiVideoEditor.MediaEngine.Rendering;
using SkiaSharp;

namespace AiVideoEditor.App.Controls;

public enum MonitorDisplayMode
{
    ProgramSequence,
    SourceAsset
}

public sealed class VideoPreviewCanvas : Control
{
    private WriteableBitmap? _bitmapBuffer;
    private CompositorEngine? _compositor;
    private ProjectDocument? _document;
    private MediaAsset? _sourceAsset;
    private TimelineClip? _selectedClip;
    private MonitorDisplayMode _mode = MonitorDisplayMode.ProgramSequence;
    private double _currentTime;
    private double _sourceTime;
    private double _previewQualityScale = 0.5; // Default 1/2 (540p) for buttery 60fps

    private enum CanvasDragHandle
    {
        None,
        MoveInside,
        ScaleCornerOrEdge,
        RotateTopHandle
    }

    private CanvasDragHandle _activeHandle = CanvasDragHandle.None;
    private Point _dragStartMouse;
    private float _dragStartPosX;
    private float _dragStartPosY;
    private float _dragStartScale;
    private float _dragStartRotation;
    private Point _currentBoxCenter;
    private double _dragStartDistFromCenter;
    private bool _snapCenterX;
    private bool _snapCenterY;

    public event Action<float, float>? ClipPositionDragged;
    public event Action<float>? ClipScaleDragged;
    public event Action<float>? ClipRotationDragged;
    public event Action<TimelineClip>? ClipClickedOnCanvas;
    public event Action<TimelineClip>? TextLayerDoubleClicked;
    public event Action? FrameRendered;

    public MonitorDisplayMode Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            InvalidateVisual();
        }
    }

    public double PreviewQualityScale
    {
        get => _previewQualityScale;
        set
        {
            _previewQualityScale = Math.Clamp(value, 0.25, 1.0);
            InvalidateVisual();
        }
    }

    public void AttachEngine(CompositorEngine compositor, ProjectDocument document)
    {
        _compositor = compositor;
        _document = document;
        InvalidateVisual();
    }

    public void UpdateDocument(ProjectDocument document, TimelineClip? selectedClip = null)
    {
        _document = document;
        _selectedClip = selectedClip;
        InvalidateVisual();
    }

    public void SetSourceAsset(MediaAsset? asset, double sourceTime = 0.0)
    {
        _sourceAsset = asset;
        _sourceTime = sourceTime;
        InvalidateVisual();
    }

    public void SetSourceTime(double sourceTime)
    {
        _sourceTime = Math.Max(0, sourceTime);
        if (_mode == MonitorDisplayMode.SourceAsset)
            InvalidateVisual();
    }

    public void SetTime(double timeSeconds)
    {
        _currentTime = timeSeconds;
        InvalidateVisual();
    }

    private Rect GetDestRect(Size bounds)
    {
        double targetAspect = _document != null && _document.Height > 0
            ? (double)_document.Width / _document.Height
            : 16.0 / 9.0;

        double pad = 14.0;
        double availW = Math.Max(16, bounds.Width - pad * 2);
        double availH = Math.Max(16, bounds.Height - pad * 2);

        double viewW = availW;
        double viewH = availW / targetAspect;
        if (viewH > availH)
        {
            viewH = availH;
            viewW = availH * targetAspect;
        }

        return new Rect(
            (bounds.Width - viewW) * 0.5,
            (bounds.Height - viewH) * 0.5,
            viewW,
            viewH);
    }

    private Rect GetSelectedClipBoxRect(Rect destRect)
    {
        if (_selectedClip == null) return default;
        double clipOffset = Math.Clamp(_currentTime - _selectedClip.TimelineStart, 0, Math.Max(0.01, _selectedClip.Duration));
        float scale = _selectedClip.EvaluateScale(clipOffset);
        float posX = _selectedClip.EvaluatePosX(clipOffset);
        float posY = _selectedClip.EvaluatePosY(clipOffset);

        double baseW = _selectedClip.TextData != null ? destRect.Width * 0.62 : destRect.Width;
        double baseH = _selectedClip.TextData != null ? destRect.Height * 0.24 : destRect.Height;

        double boxW = Math.Max(28, baseW * scale);
        double boxH = Math.Max(24, baseH * scale);
        double cx = destRect.Center.X + (posX / 960.0) * destRect.Width;
        double cy = destRect.Center.Y + (posY / 540.0) * destRect.Height;

        if (_selectedClip.TextData != null && Math.Abs(posY) < 0.5f && !_selectedClip.PosYTrack.IsAnimated)
        {
            cy = _selectedClip.TextData.VerticalPositionRatio * destRect.Height + destRect.Y + (posY / 540.0) * destRect.Height;
        }

        return new Rect(cx - boxW * 0.5, cy - boxH * 0.5, boxW, boxH);
    }

    private Point[] GetScaleHandleCenters(Rect boxRect)
    {
        return new[]
        {
            new Point(boxRect.Left, boxRect.Top),
            new Point(boxRect.Center.X, boxRect.Top),
            new Point(boxRect.Right, boxRect.Top),
            new Point(boxRect.Right, boxRect.Center.Y),
            new Point(boxRect.Right, boxRect.Bottom),
            new Point(boxRect.Center.X, boxRect.Bottom),
            new Point(boxRect.Left, boxRect.Bottom),
            new Point(boxRect.Left, boxRect.Center.Y)
        };
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        if (bounds.Width <= 4 || bounds.Height <= 4)
            return;

        var destRect = GetDestRect(bounds.Size);

        int docW = _document?.Width ?? 1920;
        int docH = _document?.Height ?? 1080;
        int renderW = Math.Max(240, (int)Math.Round(docW * _previewQualityScale));
        int renderH = Math.Max(135, (int)Math.Round(docH * _previewQualityScale));

        if (_bitmapBuffer == null || _bitmapBuffer.PixelSize.Width != renderW || _bitmapBuffer.PixelSize.Height != renderH)
        {
            _bitmapBuffer?.Dispose();
            _bitmapBuffer = new WriteableBitmap(
                new PixelSize(renderW, renderH),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);
        }

        if (_compositor != null && _document != null)
        {
            using var locked = _bitmapBuffer.Lock();
            var skInfo = new SKImageInfo(renderW, renderH, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(skInfo, locked.Address, locked.RowBytes);
            if (surface != null)
            {
                if (_mode == MonitorDisplayMode.SourceAsset)
                {
                    _compositor.RenderSourceAssetFrame(surface.Canvas, _sourceAsset, _sourceTime, renderW, renderH);
                }
                else
                {
                    _compositor.RenderCompositeFrame(surface.Canvas, _document, _currentTime, renderW, renderH);
                    FrameRendered?.Invoke();
                }
            }
        }

        // #111215 Primary Workspace Canvas
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(17, 18, 21)), new Rect(0, 0, bounds.Width, bounds.Height));
        context.DrawImage(_bitmapBuffer, new Rect(0, 0, renderW, renderH), destRect);
        context.DrawRectangle(new Pen(new SolidColorBrush(Color.FromRgb(45, 48, 59)), 1), destRect);

        // Center-Snap Yellow Crosshair Guidelines (#F59E0B) while dragging
        if (_activeHandle == CanvasDragHandle.MoveInside)
        {
            var snapPen = new Pen(new SolidColorBrush(Color.FromRgb(245, 158, 11)), 1.2, DashStyle.Dash);
            if (_snapCenterX)
            {
                context.DrawLine(snapPen, new Point(destRect.Center.X, destRect.Top), new Point(destRect.Center.X, destRect.Bottom));
            }
            if (_snapCenterY)
            {
                context.DrawLine(snapPen, new Point(destRect.Left, destRect.Center.Y), new Point(destRect.Right, destRect.Center.Y));
            }
        }

        // Interactive WYSIWYG Bounding Box + 8 Scale Handles + Top Rotation Handle
        if (_mode == MonitorDisplayMode.ProgramSequence && _selectedClip != null &&
            _currentTime >= _selectedClip.TimelineStart && _currentTime <= _selectedClip.TimelineStart + _selectedClip.Duration)
        {
            var boxRect = GetSelectedClipBoxRect(destRect);
            var accentBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246)); // #3B82F6
            var boxPen = new Pen(accentBrush, 1.5);
            context.DrawRectangle(boxPen, boxRect);

            // Top rotation stem & circular handle
            var topCenter = new Point(boxRect.Center.X, boxRect.Top);
            var rotCenter = new Point(boxRect.Center.X, boxRect.Top - 18);
            context.DrawLine(boxPen, topCenter, rotCenter);
            context.DrawEllipse(Brushes.White, boxPen, rotCenter, 4.5, 4.5);

            // 8 White Square Corner/Edge Handles
            foreach (var hc in GetScaleHandleCenters(boxRect))
            {
                var hRect = new Rect(hc.X - 3.5, hc.Y - 3.5, 7, 7);
                context.FillRectangle(Brushes.White, hRect);
                context.DrawRectangle(boxPen, hRect);
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_mode != MonitorDisplayMode.ProgramSequence || _document == null) return;

        var pt = e.GetPosition(this);
        var destRect = GetDestRect(Bounds.Size);

        // Double-click on a text clip focuses the Inspector Text Box
        if (e.ClickCount >= 2 && _selectedClip?.TextData != null)
        {
            TextLayerDoubleClicked?.Invoke(_selectedClip);
            return;
        }

        if (_selectedClip != null &&
            _currentTime >= _selectedClip.TimelineStart &&
            _currentTime <= _selectedClip.TimelineStart + _selectedClip.Duration)
        {
            var boxRect = GetSelectedClipBoxRect(destRect);
            double clipOffset = Math.Clamp(_currentTime - _selectedClip.TimelineStart, 0, _selectedClip.Duration);
            _dragStartMouse = pt;
            _dragStartPosX = _selectedClip.EvaluatePosX(clipOffset);
            _dragStartPosY = _selectedClip.EvaluatePosY(clipOffset);
            _dragStartScale = _selectedClip.EvaluateScale(clipOffset);
            _dragStartRotation = _selectedClip.EvaluateRotation(clipOffset);
            _currentBoxCenter = boxRect.Center;

            // 1. Check Top Rotation Handle
            var rotCenter = new Point(boxRect.Center.X, boxRect.Top - 18);
            if (Vector.Distance(pt, rotCenter) <= 8.0)
            {
                _activeHandle = CanvasDragHandle.RotateTopHandle;
                e.Pointer.Capture(this);
                return;
            }

            // 2. Check 8 Corner/Edge Scale Handles
            foreach (var hc in GetScaleHandleCenters(boxRect))
            {
                if (Math.Abs(pt.X - hc.X) <= 6.5 && Math.Abs(pt.Y - hc.Y) <= 6.5)
                {
                    _activeHandle = CanvasDragHandle.ScaleCornerOrEdge;
                    _dragStartDistFromCenter = Math.Max(12.0, Vector.Distance(pt, _currentBoxCenter));
                    e.Pointer.Capture(this);
                    return;
                }
            }

            // 3. Check Inside Bounding Box -> Move Position X/Y
            if (boxRect.Inflate(6).Contains(pt))
            {
                _activeHandle = CanvasDragHandle.MoveInside;
                e.Pointer.Capture(this);
                return;
            }
        }

        // 4. Otherwise hit-test topmost visible clip at playhead
        foreach (var track in _document.Tracks.Where(t => !t.IsMuted && (t.Type == TrackType.Text || t.Type == TrackType.Video)))
        {
            var hit = track.Clips.LastOrDefault(c => _currentTime >= c.TimelineStart && _currentTime <= c.TimelineStart + c.Duration);
            if (hit != null)
            {
                _selectedClip = hit;
                ClipClickedOnCanvas?.Invoke(hit);
                double clipOffset = Math.Clamp(_currentTime - hit.TimelineStart, 0, hit.Duration);
                _dragStartMouse = pt;
                _dragStartPosX = hit.EvaluatePosX(clipOffset);
                _dragStartPosY = hit.EvaluatePosY(clipOffset);
                _activeHandle = CanvasDragHandle.MoveInside;
                e.Pointer.Capture(this);
                InvalidateVisual();
                break;
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_activeHandle == CanvasDragHandle.None || _selectedClip == null) return;

        var pt = e.GetPosition(this);
        var destRect = GetDestRect(Bounds.Size);

        if (_activeHandle == CanvasDragHandle.MoveInside)
        {
            float dx = (float)((pt.X - _dragStartMouse.X) * (960.0 / Math.Max(60.0, destRect.Width)));
            float dy = (float)((pt.Y - _dragStartMouse.Y) * (540.0 / Math.Max(60.0, destRect.Height)));

            float newX = _dragStartPosX + dx;
            float newY = _dragStartPosY + dy;

            // Magnetic Center Snap within 6px
            _snapCenterX = Math.Abs(newX) <= 6.0f;
            _snapCenterY = Math.Abs(newY) <= 6.0f;
            if (_snapCenterX) newX = 0f;
            if (_snapCenterY) newY = 0f;

            ClipPositionDragged?.Invoke(newX, newY);
            InvalidateVisual();
        }
        else if (_activeHandle == CanvasDragHandle.ScaleCornerOrEdge)
        {
            double curDist = Math.Max(6.0, Vector.Distance(pt, _currentBoxCenter));
            float ratio = (float)(curDist / Math.Max(12.0, _dragStartDistFromCenter));
            float newScale = Math.Clamp(_dragStartScale * ratio, 0.1f, 4.0f);
            ClipScaleDragged?.Invoke((float)Math.Round(newScale, 3));
            InvalidateVisual();
        }
        else if (_activeHandle == CanvasDragHandle.RotateTopHandle)
        {
            double angleRad = Math.Atan2(pt.X - _currentBoxCenter.X, -1.0 * (pt.Y - _currentBoxCenter.Y));
            float angleDeg = (float)(angleRad * (180.0 / Math.PI));
            if (Math.Abs(angleDeg) < 3.0f) angleDeg = 0f;
            ClipRotationDragged?.Invoke((float)Math.Round(angleDeg, 1));
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_activeHandle != CanvasDragHandle.None)
        {
            _activeHandle = CanvasDragHandle.None;
            _snapCenterX = false;
            _snapCenterY = false;
            e.Pointer.Capture(null);
            InvalidateVisual();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _bitmapBuffer?.Dispose();
        _bitmapBuffer = null;
    }
}
