using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Crush.Viewer;

/// <summary>Double-buffered panel for zoom/pan image rendering with checkerboard alpha background.</summary>
internal sealed class ImagePanel : Panel {

  private Bitmap? _image;
  private float _zoom = 1f;
  private PointF _offset;
  private Point? _lastMouse;
  private bool _panning;
  private bool _autoFit = true;

  private static readonly Brush _checkerLight = new SolidBrush(Color.FromArgb(204, 204, 204));
  private static readonly Brush _checkerDark = new SolidBrush(Color.FromArgb(170, 170, 170));
  private const int _CHECKER_SIZE = 12;

  // --- Crop overlay state ---
  private RectangleF _cropRect;      // in IMAGE coordinates
  private bool _cropVisible;
  private float _cropAspect;         // 0 = free, >0 = locked W/H ratio
  private CropDragMode _drag = CropDragMode.None;
  private PointF _dragStart;         // screen coords at drag start
  private RectangleF _dragStartRect; // image-coord rect at drag start
  private const int _HANDLE_SIZE = 8;
  private const int _HANDLE_HIT = 10; // slightly larger hit area than visual

  private enum CropDragMode { None, Move, ResizeNW, ResizeN, ResizeNE, ResizeE, ResizeSE, ResizeS, ResizeSW, ResizeW }

  /// <summary>Raised when the user double-clicks inside the crop rect or presses Enter to confirm the crop.</summary>
  internal event Action? CropConfirmed;

  /// <summary>Raised when the user presses Escape to cancel the crop.</summary>
  internal event Action? CropCancelled;

  /// <summary>Raised whenever the zoom factor changes.</summary>
  internal event Action<float>? ZoomChanged;

  // ===== VideoMode-driven display hints =====
  // PixelAspectRatio drives horizontal stretch (Atari ST 4:3 display from 320 logical px, NES 8:7, etc.).
  // DisplayFilter applies a post-decode transform (NTSC composite, PAL, etc.) — cached in _filteredImage.
  private FileFormat.Core.PixelAspectRatio? _pixelAspectRatio;
  private FileFormat.Core.DisplayFilter _displayFilter = FileFormat.Core.DisplayFilter.None;
  private bool _displayFilterEnabled = true;
  private Bitmap? _filteredImage; // cached filter output; disposed when source changes
  private float _xStretch = 1f;   // computed from PAR; applied to draw-rect width

  /// <summary>Sets the format-declared display hints for the currently loaded image. Pass <c>null</c> to clear.</summary>
  internal void SetVideoModeHints(FileFormat.Core.PixelAspectRatio? par, FileFormat.Core.DisplayFilter filter) {
    this._pixelAspectRatio = par;
    this._displayFilter = filter;
    this._xStretch = par.HasValue ? (float)par.Value.Ratio : 1f;
    this._InvalidateFilterCache();
    this.Invalidate();
  }

  /// <summary>Toggles whether the <see cref="FileFormat.Core.DisplayFilter"/> is applied during paint.
  /// When off, the raw pixel data is shown. Default: on.</summary>
  [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
  internal bool DisplayFilterEnabled {
    get => this._displayFilterEnabled;
    set {
      if (this._displayFilterEnabled == value) return;
      this._displayFilterEnabled = value;
      this._InvalidateFilterCache();
      this.Invalidate();
    }
  }

  private void _InvalidateFilterCache() {
    if (this._filteredImage != null && !ReferenceEquals(this._filteredImage, this._image)) {
      this._filteredImage.Dispose();
    }
    this._filteredImage = null;
  }

  private Bitmap? _CurrentDisplayBitmap() {
    if (this._image == null) return null;
    if (!this._displayFilterEnabled || this._displayFilter == FileFormat.Core.DisplayFilter.None)
      return this._image;
    if (this._filteredImage == null)
      this._filteredImage = DisplayFilterPipeline.Apply(this._image, this._displayFilter);
    return this._filteredImage;
  }

  internal ImagePanel() {
    this.DoubleBuffered = true;
    this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
    this.BackColor = Color.FromArgb(48, 48, 48);
  }

  [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
  internal Bitmap? Image {
    get => this._image;
    set {
      this._image = value;
      this._InvalidateFilterCache();
      this._autoFit = true;
      this.HideCropRect();
      this.FitToWindow();
    }
  }

  internal float Zoom => this._zoom;
  internal bool IsCropVisible => this._cropVisible;

  /// <summary>Lower zoom limit: image is compressed to a single pixel along its longest dimension.</summary>
  internal float MinAllowedZoom => this._image == null ? 0.0001f : 1f / Math.Max(this._image.Width, this._image.Height);

  /// <summary>Upper zoom limit: a single image pixel fills the entire window's longest dimension.</summary>
  internal float MaxAllowedZoom => Math.Max(this.ClientSize.Width, this.ClientSize.Height) is var s && s > 0 ? s : 8192f;

  internal void FitToWindow() {
    if (this._image == null || this.ClientSize.Width <= 0 || this.ClientSize.Height <= 0)
      return;

    var scaleX = (float)this.ClientSize.Width / this._image.Width;
    var scaleY = (float)this.ClientSize.Height / this._image.Height;
    this._zoom = Math.Min(scaleX, scaleY);
    this._offset = new(
      (this.ClientSize.Width - this._image.Width * this._zoom) / 2f,
      (this.ClientSize.Height - this._image.Height * this._zoom) / 2f
    );
    this._autoFit = true;
    this.Invalidate();
    this.ZoomChanged?.Invoke(this._zoom);
  }

  internal void ActualSize() {
    if (this._image == null)
      return;

    this._zoom = 1f;
    this._offset = new(
      (this.ClientSize.Width - this._image.Width) / 2f,
      (this.ClientSize.Height - this._image.Height) / 2f
    );
    this._autoFit = false;
    this.Invalidate();
    this.ZoomChanged?.Invoke(this._zoom);
  }

  internal void ZoomIn() {
    this._autoFit = false;
    this._SetZoom(this._zoom * 1.25f); }
  internal void ZoomOut() {
    this._autoFit = false;
    this._SetZoom(this._zoom / 1.25f); }

  /// <summary>Sets the zoom factor and re-centers the image in the viewport so the user can find it.
  /// Clamped to <see cref="MinAllowedZoom"/>..<see cref="MaxAllowedZoom"/>.</summary>
  internal void SetZoom(float newZoom) {
    if (this._image == null) return;

    this._autoFit = false;
    newZoom = Math.Clamp(newZoom, this.MinAllowedZoom, this.MaxAllowedZoom);
    this._zoom = newZoom;
    this._offset = new(
      (this.ClientSize.Width - this._image.Width * this._zoom) / 2f,
      (this.ClientSize.Height - this._image.Height * this._zoom) / 2f
    );
    this.Invalidate();
    this.ZoomChanged?.Invoke(this._zoom);
  }

  // --- Crop overlay public API ---

  /// <summary>
  /// Shows the crop selection overlay. <paramref name="aspectRatio"/> > 0 locks the aspect ratio (W/H).
  /// The initial rect defaults to a centered rectangle covering 75% of the image.
  /// </summary>
  internal void ShowCropRect(float aspectRatio, RectangleF? initial = null) {
    if (this._image == null) return;

    this._cropAspect = aspectRatio;
    this._cropVisible = true;

    if (initial.HasValue) {
      this._cropRect = initial.Value;
    } else {
      // Default: centered 75% of image, respecting aspect ratio
      var imgW = (float)this._image.Width;
      var imgH = (float)this._image.Height;

      if (aspectRatio > 0) {
        // Fit the aspect ratio inside 75% of the image
        var maxW = imgW * 0.75f;
        var maxH = imgH * 0.75f;
        var fitW = maxW;
        var fitH = fitW / aspectRatio;
        if (fitH > maxH) {
          fitH = maxH;
          fitW = fitH * aspectRatio;
        }

        this._cropRect = new((imgW - fitW) / 2f, (imgH - fitH) / 2f, fitW, fitH);
      } else {
        var w = imgW * 0.75f;
        var h = imgH * 0.75f;
        this._cropRect = new((imgW - w) / 2f, (imgH - h) / 2f, w, h);
      }
    }

    this._ClampCropToImage();
    this.Focus();
    this.Invalidate();
  }

  /// <summary>Removes the crop overlay.</summary>
  internal void HideCropRect() {
    this._cropVisible = false;
    this._drag = CropDragMode.None;
    this.Cursor = Cursors.Default;
    this.Invalidate();
  }

  /// <summary>Returns the current crop selection in image coordinates.</summary>
  internal RectangleF GetCropRect() => this._cropRect;

  // --- Coordinate transforms ---

  private PointF _ScreenToImage(PointF screen) => new((screen.X - this._offset.X) / this._zoom, (screen.Y - this._offset.Y) / this._zoom);
  private PointF _ImageToScreen(PointF image) => new(this._offset.X + image.X * this._zoom, this._offset.Y + image.Y * this._zoom);

  private RectangleF _ImageRectToScreen(RectangleF r) {
    var tl = this._ImageToScreen(new(r.X, r.Y));
    return new(tl.X, tl.Y, r.Width * this._zoom, r.Height * this._zoom);
  }

  // --- Overrides ---

  protected override void OnResize(EventArgs e) {
    base.OnResize(e);
    if (this._autoFit)
      this.FitToWindow();
  }

  private void _SetZoom(float newZoom) {
    if (this._image == null)
      return;

    newZoom = Math.Clamp(newZoom, this.MinAllowedZoom, this.MaxAllowedZoom);
    var center = new PointF(this.ClientSize.Width / 2f, this.ClientSize.Height / 2f);
    var imageX = (center.X - this._offset.X) / this._zoom;
    var imageY = (center.Y - this._offset.Y) / this._zoom;
    this._zoom = newZoom;
    this._offset = new(center.X - imageX * this._zoom, center.Y - imageY * this._zoom);
    this.Invalidate();
    this.ZoomChanged?.Invoke(this._zoom);
  }

  protected override void OnMouseWheel(MouseEventArgs e) {
    base.OnMouseWheel(e);
    if (this._image == null)
      return;

    this._autoFit = false;
    var factor = e.Delta > 0 ? 1.15f : 1f / 1.15f;
    var newZoom = Math.Clamp(this._zoom * factor, this.MinAllowedZoom, this.MaxAllowedZoom);
    var imageX = (e.X - this._offset.X) / this._zoom;
    var imageY = (e.Y - this._offset.Y) / this._zoom;
    this._zoom = newZoom;
    this._offset = new(e.X - imageX * this._zoom, e.Y - imageY * this._zoom);
    this.Invalidate();
    this.ZoomChanged?.Invoke(this._zoom);
  }

  protected override void OnMouseDown(MouseEventArgs e) {
    base.OnMouseDown(e);

    // Pan: right-click drag only.
    if (e.Button == MouseButtons.Right) {
      this._panning = true;
      this._lastMouse = e.Location;
      this.Cursor = Cursors.SizeAll;
      return;
    }

    // Left-click with crop visible: start crop interaction
    if (e.Button == MouseButtons.Left && this._cropVisible && this._image != null) {
      var screenPt = new PointF(e.X, e.Y);
      var screenRect = this._ImageRectToScreen(this._cropRect);

      // Check handles first (corners, then edges)
      var mode = this._HitTestHandle(screenPt, screenRect);
      if (mode != CropDragMode.None) {
        this._drag = mode;
        this._dragStart = screenPt;
        this._dragStartRect = this._cropRect;
        return;
      }

      // Inside rect: move
      if (screenRect.Contains(screenPt)) {
        this._drag = CropDragMode.Move;
        this._dragStart = screenPt;
        this._dragStartRect = this._cropRect;
        return;
      }

      // Outside: start new rect from click position (free aspect only, or create at aspect)
      var imgPt = this._ScreenToImage(screenPt);
      imgPt = new(
        Math.Clamp(imgPt.X, 0, this._image.Width),
        Math.Clamp(imgPt.Y, 0, this._image.Height)
      );
      this._cropRect = new(imgPt.X, imgPt.Y, 0, 0);
      this._drag = CropDragMode.ResizeSE;
      this._dragStart = screenPt;
      this._dragStartRect = this._cropRect;
      this.Invalidate();
    }
  }

  protected override void OnMouseMove(MouseEventArgs e) {
    base.OnMouseMove(e);

    // Pan mode
    if (this._panning && this._lastMouse != null) {
      this._autoFit = false;
      this._offset = new(this._offset.X + e.X - this._lastMouse.Value.X, this._offset.Y + e.Y - this._lastMouse.Value.Y);
      this._lastMouse = e.Location;
      this.Invalidate();
      return;
    }

    // Crop dragging
    if (this._drag != CropDragMode.None && this._cropVisible && this._image != null) {
      var dx = (e.X - this._dragStart.X) / this._zoom;
      var dy = (e.Y - this._dragStart.Y) / this._zoom;
      this._ApplyDrag(dx, dy);
      this.Invalidate();
      return;
    }

    // Hover cursor feedback for crop
    if (this._cropVisible && this._image != null && !this._panning) {
      var screenPt = new PointF(e.X, e.Y);
      var screenRect = this._ImageRectToScreen(this._cropRect);
      var mode = this._HitTestHandle(screenPt, screenRect);
      this.Cursor = mode switch {
        CropDragMode.Move => Cursors.SizeAll,
        CropDragMode.ResizeNW or CropDragMode.ResizeSE => Cursors.SizeNWSE,
        CropDragMode.ResizeNE or CropDragMode.ResizeSW => Cursors.SizeNESW,
        CropDragMode.ResizeN or CropDragMode.ResizeS => Cursors.SizeNS,
        CropDragMode.ResizeW or CropDragMode.ResizeE => Cursors.SizeWE,
        _ => screenRect.Contains(screenPt) ? Cursors.SizeAll : Cursors.Cross,
      };
    }
  }

  protected override void OnMouseUp(MouseEventArgs e) {
    base.OnMouseUp(e);
    if (this._panning) {
      this._panning = false;
      this._lastMouse = null;
      // Restore cursor based on crop state
      if (this._cropVisible)
        this.Cursor = Cursors.Default; // will be updated on next mouse move
      else
        this.Cursor = Cursors.Default;
    }
    if (this._drag != CropDragMode.None) {
      this._drag = CropDragMode.None;
      // Normalize rect (ensure positive width/height)
      this._NormalizeCropRect();
      this._ClampCropToImage();
      this.Invalidate();
    }
  }

  protected override void OnMouseDoubleClick(MouseEventArgs e) {
    base.OnMouseDoubleClick(e);
    if (e.Button == MouseButtons.Left) {
      if (this._cropVisible && this._image != null) {
        var screenRect = this._ImageRectToScreen(this._cropRect);
        if (screenRect.Contains(e.X, e.Y)) {
          // Double-click inside crop rect confirms the crop
          this.CropConfirmed?.Invoke();
          return;
        }
      }

      this.FitToWindow();
    }
  }

  protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
    if (this._cropVisible) {
      if (keyData == Keys.Return) {
        this.CropConfirmed?.Invoke();
        return true;
      }
      if (keyData == Keys.Escape) {
        this.CropCancelled?.Invoke();
        return true;
      }
    }
    return base.ProcessCmdKey(ref msg, keyData);
  }

  protected override void OnPaint(PaintEventArgs e) {
    base.OnPaint(e);
    if (this._image == null)
      return;

    var g = e.Graphics;
    var displayBitmap = this._CurrentDisplayBitmap()!;
    // Apply PixelAspectRatio: stretch the destination's X axis.
    var destRect = new RectangleF(
      this._offset.X,
      this._offset.Y,
      this._image.Width * this._zoom * this._xStretch,
      this._image.Height * this._zoom);

    _DrawCheckerboard(g, destRect);

    // Interpolation mode:
    // - NearestNeighbor at zoom >= 1.0 (pixel-perfect, no artifacts when viewing at actual size or zoomed in)
    // - Bilinear at zoom < 1.0 OR when PAR-stretching (avoids jaggy nearest-neighbor stretch)
    if (this._zoom >= 1f && Math.Abs(this._xStretch - 1f) < 0.001f) {
      g.InterpolationMode = InterpolationMode.NearestNeighbor;
      g.PixelOffsetMode = PixelOffsetMode.Half;
    } else {
      g.InterpolationMode = InterpolationMode.HighQualityBilinear;
      g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    g.DrawImage(displayBitmap, destRect);

    // Draw crop overlay on top of the image
    if (this._cropVisible)
      this._DrawCropOverlay(g, destRect);
  }

  // --- Crop overlay painting ---

  private void _DrawCropOverlay(Graphics g, RectangleF imageDestRect) {
    var cropScreen = this._ImageRectToScreen(this._cropRect);

    // 1. Semi-transparent dark overlay outside the crop rect
    using var dimBrush = new SolidBrush(Color.FromArgb(128, 0, 0, 0));

    // Top strip
    if (cropScreen.Top > imageDestRect.Top)
      g.FillRectangle(dimBrush, imageDestRect.Left, imageDestRect.Top, imageDestRect.Width, cropScreen.Top - imageDestRect.Top);
    // Bottom strip
    if (cropScreen.Bottom < imageDestRect.Bottom)
      g.FillRectangle(dimBrush, imageDestRect.Left, cropScreen.Bottom, imageDestRect.Width, imageDestRect.Bottom - cropScreen.Bottom);
    // Left strip (between top and bottom strips)
    var midTop = Math.Max(cropScreen.Top, imageDestRect.Top);
    var midBottom = Math.Min(cropScreen.Bottom, imageDestRect.Bottom);
    if (cropScreen.Left > imageDestRect.Left && midBottom > midTop)
      g.FillRectangle(dimBrush, imageDestRect.Left, midTop, cropScreen.Left - imageDestRect.Left, midBottom - midTop);
    // Right strip
    if (cropScreen.Right < imageDestRect.Right && midBottom > midTop)
      g.FillRectangle(dimBrush, cropScreen.Right, midTop, imageDestRect.Right - cropScreen.Right, midBottom - midTop);

    // 2. Crop rect border
    using var whitePen = new Pen(Color.White, 2f);
    using var dashPen = new Pen(Color.FromArgb(160, 0, 0, 0), 1f) { DashStyle = DashStyle.Dash };
    g.DrawRectangle(whitePen, cropScreen.X, cropScreen.Y, cropScreen.Width, cropScreen.Height);
    g.DrawRectangle(dashPen, cropScreen.X, cropScreen.Y, cropScreen.Width, cropScreen.Height);

    // 3. Rule of thirds guides (subtle)
    using var guidePen = new Pen(Color.FromArgb(80, 255, 255, 255), 1f);
    var thirdW = cropScreen.Width / 3f;
    var thirdH = cropScreen.Height / 3f;
    g.DrawLine(guidePen, cropScreen.X + thirdW, cropScreen.Y, cropScreen.X + thirdW, cropScreen.Bottom);
    g.DrawLine(guidePen, cropScreen.X + 2 * thirdW, cropScreen.Y, cropScreen.X + 2 * thirdW, cropScreen.Bottom);
    g.DrawLine(guidePen, cropScreen.X, cropScreen.Y + thirdH, cropScreen.Right, cropScreen.Y + thirdH);
    g.DrawLine(guidePen, cropScreen.X, cropScreen.Y + 2 * thirdH, cropScreen.Right, cropScreen.Y + 2 * thirdH);

    // 4. Resize handles (8 points: corners + edge midpoints)
    _DrawHandle(g, cropScreen.X, cropScreen.Y);                                             // NW
    _DrawHandle(g, cropScreen.X + cropScreen.Width / 2f, cropScreen.Y);                     // N
    _DrawHandle(g, cropScreen.Right, cropScreen.Y);                                          // NE
    _DrawHandle(g, cropScreen.Right, cropScreen.Y + cropScreen.Height / 2f);                 // E
    _DrawHandle(g, cropScreen.Right, cropScreen.Bottom);                                     // SE
    _DrawHandle(g, cropScreen.X + cropScreen.Width / 2f, cropScreen.Bottom);                 // S
    _DrawHandle(g, cropScreen.X, cropScreen.Bottom);                                         // SW
    _DrawHandle(g, cropScreen.X, cropScreen.Y + cropScreen.Height / 2f);                     // W

    // 5. Dimension label
    var cw = (int)Math.Round(this._cropRect.Width);
    var ch = (int)Math.Round(this._cropRect.Height);
    var label = $"{cw} x {ch}";
    using var font = new Font("Segoe UI", 9f, FontStyle.Bold);
    var labelSize = g.MeasureString(label, font);
    var labelX = cropScreen.X + (cropScreen.Width - labelSize.Width) / 2f;
    var labelY = cropScreen.Bottom + 4f;
    // If label would go below visible area, put it above
    if (labelY + labelSize.Height > this.ClientSize.Height)
      labelY = cropScreen.Y - labelSize.Height - 4f;

    using var labelBg = new SolidBrush(Color.FromArgb(180, 0, 0, 0));
    g.FillRectangle(labelBg, labelX - 3, labelY - 1, labelSize.Width + 6, labelSize.Height + 2);
    g.DrawString(label, font, Brushes.White, labelX, labelY);
  }

  private static void _DrawHandle(Graphics g, float cx, float cy) {
    var half = ImagePanel._HANDLE_SIZE / 2f;
    var rect = new RectangleF(cx - half, cy - half, ImagePanel._HANDLE_SIZE, ImagePanel._HANDLE_SIZE);
    g.FillRectangle(Brushes.White, rect);
    using var pen = new Pen(Color.FromArgb(80, 0, 0, 0), 1f);
    g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
  }

  // --- Hit testing ---

  private CropDragMode _HitTestHandle(PointF screenPt, RectangleF screenRect) {
    var half = ImagePanel._HANDLE_HIT / 2f;

    // Corners (check first, they take priority)
    if (_IsNear(screenPt, screenRect.X, screenRect.Y, half)) return CropDragMode.ResizeNW;
    if (_IsNear(screenPt, screenRect.Right, screenRect.Y, half)) return CropDragMode.ResizeNE;
    if (_IsNear(screenPt, screenRect.X, screenRect.Bottom, half)) return CropDragMode.ResizeSW;
    if (_IsNear(screenPt, screenRect.Right, screenRect.Bottom, half)) return CropDragMode.ResizeSE;

    // Edge midpoints
    if (_IsNear(screenPt, screenRect.X + screenRect.Width / 2f, screenRect.Y, half)) return CropDragMode.ResizeN;
    if (_IsNear(screenPt, screenRect.X + screenRect.Width / 2f, screenRect.Bottom, half)) return CropDragMode.ResizeS;
    if (_IsNear(screenPt, screenRect.X, screenRect.Y + screenRect.Height / 2f, half)) return CropDragMode.ResizeW;
    if (_IsNear(screenPt, screenRect.Right, screenRect.Y + screenRect.Height / 2f, half)) return CropDragMode.ResizeE;

    // Edge proximity (within a few pixels of any edge)
    const float edgeTol = 6f;
    var inVertRange = screenPt.Y >= screenRect.Y - edgeTol && screenPt.Y <= screenRect.Bottom + edgeTol;
    var inHorzRange = screenPt.X >= screenRect.X - edgeTol && screenPt.X <= screenRect.Right + edgeTol;

    if (inVertRange && Math.Abs(screenPt.X - screenRect.X) < edgeTol) return CropDragMode.ResizeW;
    if (inVertRange && Math.Abs(screenPt.X - screenRect.Right) < edgeTol) return CropDragMode.ResizeE;
    if (inHorzRange && Math.Abs(screenPt.Y - screenRect.Y) < edgeTol) return CropDragMode.ResizeN;
    if (inHorzRange && Math.Abs(screenPt.Y - screenRect.Bottom) < edgeTol) return CropDragMode.ResizeS;

    return CropDragMode.None;
  }

  private static bool _IsNear(PointF pt, float cx, float cy, float tolerance)
    => Math.Abs(pt.X - cx) <= tolerance && Math.Abs(pt.Y - cy) <= tolerance;

  // --- Drag logic ---

  private void _ApplyDrag(float dx, float dy) {
    if (this._image == null) return;
    var imgW = (float)this._image.Width;
    var imgH = (float)this._image.Height;
    var r = this._dragStartRect;

    switch (this._drag) {
      case CropDragMode.Move: {
        var newX = Math.Clamp(r.X + dx, 0, imgW - r.Width);
        var newY = Math.Clamp(r.Y + dy, 0, imgH - r.Height);
        this._cropRect = new(newX, newY, r.Width, r.Height);
        break;
      }

      case CropDragMode.ResizeSE:
        this._ResizeFromCorner(r.X, r.Y, r.Right + dx, r.Bottom + dy, anchorLeft: true, anchorTop: true, imgW, imgH);
        break;
      case CropDragMode.ResizeNW:
        this._ResizeFromCorner(r.X + dx, r.Y + dy, r.Right, r.Bottom, anchorLeft: false, anchorTop: false, imgW, imgH);
        break;
      case CropDragMode.ResizeNE:
        this._ResizeFromCorner(r.X, r.Y + dy, r.Right + dx, r.Bottom, anchorLeft: true, anchorTop: false, imgW, imgH);
        break;
      case CropDragMode.ResizeSW:
        this._ResizeFromCorner(r.X + dx, r.Y, r.Right, r.Bottom + dy, anchorLeft: false, anchorTop: true, imgW, imgH);
        break;

      case CropDragMode.ResizeN:
        this._ResizeFromEdge(r, 0, dy, 0, 0, imgW, imgH);
        break;
      case CropDragMode.ResizeS:
        this._ResizeFromEdge(r, 0, 0, 0, dy, imgW, imgH);
        break;
      case CropDragMode.ResizeW:
        this._ResizeFromEdge(r, dx, 0, 0, 0, imgW, imgH);
        break;
      case CropDragMode.ResizeE:
        this._ResizeFromEdge(r, 0, 0, dx, 0, imgW, imgH);
        break;
    }
  }

  private void _ResizeFromCorner(float newLeft, float newTop, float newRight, float newBottom, bool anchorLeft, bool anchorTop, float imgW, float imgH) {
    // Clamp to image bounds
    newLeft = Math.Clamp(newLeft, 0, imgW);
    newTop = Math.Clamp(newTop, 0, imgH);
    newRight = Math.Clamp(newRight, 0, imgW);
    newBottom = Math.Clamp(newBottom, 0, imgH);

    var w = newRight - newLeft;
    var h = newBottom - newTop;

    // Enforce minimum size
    const float minSize = 4f;
    if (w < minSize) w = minSize;
    if (h < minSize) h = minSize;

    // Apply aspect ratio constraint
    if (this._cropAspect > 0) {
      var desiredH = w / this._cropAspect;
      if (desiredH > h) {
        // Width is driving; reduce width to match height
        w = h * this._cropAspect;
      } else {
        h = desiredH;
      }

      // Re-clamp after AR adjustment
      if (anchorLeft) {
        if (newLeft + w > imgW) w = imgW - newLeft;
        h = w / this._cropAspect;
      } else {
        if (newRight - w < 0) w = newRight;
        h = w / this._cropAspect;
      }
      if (anchorTop) {
        if (newTop + h > imgH) { h = imgH - newTop; w = h * this._cropAspect; }
      } else {
        if (newBottom - h < 0) { h = newBottom; w = h * this._cropAspect; }
      }
    }

    var x = anchorLeft ? newLeft : newRight - w;
    var y = anchorTop ? newTop : newBottom - h;

    this._cropRect = new(x, y, w, h);
  }

  private void _ResizeFromEdge(RectangleF r, float dLeft, float dTop, float dRight, float dBottom, float imgW, float imgH) {
    var newLeft = Math.Clamp(r.X + dLeft, 0, imgW);
    var newTop = Math.Clamp(r.Y + dTop, 0, imgH);
    var newRight = Math.Clamp(r.Right + dRight, 0, imgW);
    var newBottom = Math.Clamp(r.Bottom + dBottom, 0, imgH);

    var w = Math.Max(4f, newRight - newLeft);
    var h = Math.Max(4f, newBottom - newTop);

    if (this._cropAspect > 0) {
      // Edge resize with locked AR: adjust the perpendicular dimension symmetrically
      var isHorizontal = dLeft != 0 || dRight != 0;
      if (isHorizontal) {
        var desiredH = w / this._cropAspect;
        var centerY = (newTop + newBottom) / 2f;
        newTop = centerY - desiredH / 2f;
        newBottom = centerY + desiredH / 2f;
        // Clamp and re-adjust
        if (newTop < 0) { newTop = 0; newBottom = desiredH; }
        if (newBottom > imgH) { newBottom = imgH; newTop = imgH - desiredH; }
        if (newTop < 0) { newTop = 0; w = (newBottom - newTop) * this._cropAspect; }
        h = newBottom - newTop;
      } else {
        var desiredW = h * this._cropAspect;
        var centerX = (newLeft + newRight) / 2f;
        newLeft = centerX - desiredW / 2f;
        newRight = centerX + desiredW / 2f;
        if (newLeft < 0) { newLeft = 0; newRight = desiredW; }
        if (newRight > imgW) { newRight = imgW; newLeft = imgW - desiredW; }
        if (newLeft < 0) { newLeft = 0; h = (newRight - newLeft) / this._cropAspect; }
        w = newRight - newLeft;
      }
    }

    this._cropRect = new(
      dLeft != 0 ? newRight - w : newLeft,
      dTop != 0 ? newBottom - h : newTop,
      w, h
    );
  }

  private void _NormalizeCropRect() {
    var x = this._cropRect.X;
    var y = this._cropRect.Y;
    var w = this._cropRect.Width;
    var h = this._cropRect.Height;
    if (w < 0) { x += w; w = -w; }
    if (h < 0) { y += h; h = -h; }

    this._cropRect = new(x, y, w, h);
  }

  private void _ClampCropToImage() {
    if (this._image == null) return;
    var x = Math.Clamp(this._cropRect.X, 0, this._image.Width);
    var y = Math.Clamp(this._cropRect.Y, 0, this._image.Height);
    var w = Math.Min(this._cropRect.Width, this._image.Width - x);
    var h = Math.Min(this._cropRect.Height, this._image.Height - y);
    w = Math.Max(w, 1);
    h = Math.Max(h, 1);
    this._cropRect = new(x, y, w, h);
  }

  // --- Checkerboard ---

  private static void _DrawCheckerboard(Graphics g, RectangleF rect) {
    var clip = g.ClipBounds;
    var left = Math.Max(rect.Left, clip.Left);
    var top = Math.Max(rect.Top, clip.Top);
    var right = Math.Min(rect.Right, clip.Right);
    var bottom = Math.Min(rect.Bottom, clip.Bottom);

    var startCol = (int)Math.Floor((left - rect.Left) / ImagePanel._CHECKER_SIZE);
    var startRow = (int)Math.Floor((top - rect.Top) / ImagePanel._CHECKER_SIZE);
    var endCol = (int)Math.Ceiling((right - rect.Left) / ImagePanel._CHECKER_SIZE);
    var endRow = (int)Math.Ceiling((bottom - rect.Top) / ImagePanel._CHECKER_SIZE);

    for (var row = startRow; row < endRow; ++row)
      for (var col = startCol; col < endCol; ++col) {
        var brush = (row + col) % 2 == 0 ? ImagePanel._checkerLight : ImagePanel._checkerDark;
        var x = rect.Left + col * ImagePanel._CHECKER_SIZE;
        var y = rect.Top + row * ImagePanel._CHECKER_SIZE;
        var w = Math.Min(ImagePanel._CHECKER_SIZE, rect.Right - x);
        var h = Math.Min(ImagePanel._CHECKER_SIZE, rect.Bottom - y);
        if (w > 0 && h > 0)
          g.FillRectangle(brush, x, y, w, h);
      }
  }
}
