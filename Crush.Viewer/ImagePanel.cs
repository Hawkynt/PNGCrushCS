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

  internal ImagePanel() {
    DoubleBuffered = true;
    SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    BackColor = Color.FromArgb(48, 48, 48);
  }

  [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
  internal Bitmap? Image {
    get => _image;
    set {
      _image = value;
      _autoFit = true;
      FitToWindow();
    }
  }

  internal float Zoom => _zoom;

  internal void FitToWindow() {
    if (_image == null || ClientSize.Width <= 0 || ClientSize.Height <= 0)
      return;

    var scaleX = (float)ClientSize.Width / _image.Width;
    var scaleY = (float)ClientSize.Height / _image.Height;
    _zoom = Math.Min(scaleX, scaleY);
    _offset = new(
      (ClientSize.Width - _image.Width * _zoom) / 2f,
      (ClientSize.Height - _image.Height * _zoom) / 2f
    );
    _autoFit = true;
    Invalidate();
  }

  internal void ActualSize() {
    if (_image == null)
      return;

    _zoom = 1f;
    _offset = new(
      (ClientSize.Width - _image.Width) / 2f,
      (ClientSize.Height - _image.Height) / 2f
    );
    _autoFit = false;
    Invalidate();
  }

  internal void ZoomIn() { _autoFit = false; _SetZoom(_zoom * 1.25f); }
  internal void ZoomOut() { _autoFit = false; _SetZoom(_zoom / 1.25f); }

  protected override void OnResize(EventArgs e) {
    base.OnResize(e);
    if (_autoFit)
      FitToWindow();
  }

  private void _SetZoom(float newZoom) {
    if (_image == null)
      return;

    newZoom = Math.Clamp(newZoom, 0.01f, 100f);
    var center = new PointF(ClientSize.Width / 2f, ClientSize.Height / 2f);
    var imageX = (center.X - _offset.X) / _zoom;
    var imageY = (center.Y - _offset.Y) / _zoom;
    _zoom = newZoom;
    _offset = new(center.X - imageX * _zoom, center.Y - imageY * _zoom);
    Invalidate();
  }

  protected override void OnMouseWheel(MouseEventArgs e) {
    base.OnMouseWheel(e);
    if (_image == null)
      return;

    _autoFit = false;
    var factor = e.Delta > 0 ? 1.15f : 1f / 1.15f;
    var newZoom = Math.Clamp(_zoom * factor, 0.01f, 100f);
    var imageX = (e.X - _offset.X) / _zoom;
    var imageY = (e.Y - _offset.Y) / _zoom;
    _zoom = newZoom;
    _offset = new(e.X - imageX * _zoom, e.Y - imageY * _zoom);
    Invalidate();
  }

  protected override void OnMouseDown(MouseEventArgs e) {
    base.OnMouseDown(e);
    if (e.Button == MouseButtons.Middle || e.Button == MouseButtons.Left && ModifierKeys.HasFlag(Keys.Space)) {
      _panning = true;
      _lastMouse = e.Location;
      Cursor = Cursors.SizeAll;
    }
  }

  protected override void OnMouseMove(MouseEventArgs e) {
    base.OnMouseMove(e);
    if (!_panning || _lastMouse == null)
      return;

    _autoFit = false;
    _offset = new(_offset.X + e.X - _lastMouse.Value.X, _offset.Y + e.Y - _lastMouse.Value.Y);
    _lastMouse = e.Location;
    Invalidate();
  }

  protected override void OnMouseUp(MouseEventArgs e) {
    base.OnMouseUp(e);
    if (_panning) {
      _panning = false;
      _lastMouse = null;
      Cursor = Cursors.Default;
    }
  }

  protected override void OnMouseDoubleClick(MouseEventArgs e) {
    base.OnMouseDoubleClick(e);
    if (e.Button == MouseButtons.Left)
      FitToWindow();
  }

  protected override void OnPaint(PaintEventArgs e) {
    base.OnPaint(e);
    if (_image == null)
      return;

    var g = e.Graphics;
    var destRect = new RectangleF(_offset.X, _offset.Y, _image.Width * _zoom, _image.Height * _zoom);

    _DrawCheckerboard(g, destRect);

    // Interpolation mode:
    // - NearestNeighbor at zoom >= 1.0 (pixel-perfect, no artifacts when viewing at actual size or zoomed in)
    // - Bilinear at zoom < 1.0 (smooth downscale without ringing artifacts that bicubic introduces)
    if (_zoom >= 1f) {
      g.InterpolationMode = InterpolationMode.NearestNeighbor;
      g.PixelOffsetMode = PixelOffsetMode.Half;
    } else {
      g.InterpolationMode = InterpolationMode.HighQualityBilinear;
      g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    g.DrawImage(_image, destRect);
  }

  private static void _DrawCheckerboard(Graphics g, RectangleF rect) {
    var clip = g.ClipBounds;
    var left = Math.Max(rect.Left, clip.Left);
    var top = Math.Max(rect.Top, clip.Top);
    var right = Math.Min(rect.Right, clip.Right);
    var bottom = Math.Min(rect.Bottom, clip.Bottom);

    var startCol = (int)Math.Floor((left - rect.Left) / _CHECKER_SIZE);
    var startRow = (int)Math.Floor((top - rect.Top) / _CHECKER_SIZE);
    var endCol = (int)Math.Ceiling((right - rect.Left) / _CHECKER_SIZE);
    var endRow = (int)Math.Ceiling((bottom - rect.Top) / _CHECKER_SIZE);

    for (var row = startRow; row < endRow; ++row)
      for (var col = startCol; col < endCol; ++col) {
        var brush = (row + col) % 2 == 0 ? _checkerLight : _checkerDark;
        var x = rect.Left + col * _CHECKER_SIZE;
        var y = rect.Top + row * _CHECKER_SIZE;
        var w = Math.Min(_CHECKER_SIZE, rect.Right - x);
        var h = Math.Min(_CHECKER_SIZE, rect.Bottom - y);
        if (w > 0 && h > 0)
          g.FillRectangle(brush, x, y, w, h);
      }
  }
}
