using System;
using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;

namespace Crush.Viewer;

/// <summary>How the next picture is scaled when it arrives.</summary>
internal enum ZoomMode {

  /// <summary>Scaled down until the whole picture is inside the canvas.</summary>
  Fit,

  /// <summary>Shown one picture pixel to one screen pixel.</summary>
  Actual,

  /// <summary>Kept at whatever scale the user zoomed to.</summary>
  Free,
}

/// <summary>The image canvas: the toolkit's zoom panel with a picture viewer's mouse wheel on it.</summary>
/// <remarks>
/// <para>
/// The keys belong to <see cref="ShortcutTable"/> rather than here, so that they answer with the
/// thumbnail list focused too. What is left is the wheel, which the panel below turns into a zoom on
/// its own: a picture viewer's wheel walks the folder, and zooming is what Control with the wheel is
/// for.
/// </para>
/// <para>
/// The canvas can also be asked for an area. That is the least a command needs that must not run
/// over the whole picture — red-eye reduction, whose colour rule cannot tell a pupil from a red
/// jumper — and it is no more than that: one rectangle, dragged with the left button, drawn with the
/// ellipse inside it that the retouch actually uses, and reported once when the button comes up.
/// While an area is being asked for, the left button drags the area instead of panning; the wheel
/// and Control with the wheel keep doing what they always do.
/// </para>
/// </remarks>
internal sealed class ViewportPanel : ZoomPanel {

  private const int _MINIMUM_DRAG = 3;

  private PointF _origin;
  private double _scale = 1;
  private bool _selecting;
  private bool _dragging;
  private PointF _anchor;
  private PointF _current;

  internal ViewportPanel() => this.PaintContent += this._OnPaintContent;

  /// <summary>Raised with -1 or 1 when the user asks for the neighbouring picture.</summary>
  internal event EventHandler<int>? WalkRequested;

  /// <summary>
  /// Raised when an area has been dragged out, with its rectangle in the canvas's content
  /// coordinates — the pixels of the image it is showing.
  /// </summary>
  internal event EventHandler<RectangleF>? AreaSelected;

  /// <summary>Whether the left button currently drags out an area rather than panning.</summary>
  internal bool SelectingArea {
    get => this._selecting;
    set {
      this._selecting = value;
      this._dragging = false;
      this.Invalidate();
    }
  }

  protected override void OnMouseWheel(MouseEventArgs e) {
    if ((e.Modifiers & KeyModifiers.Control) != 0) {
      base.OnMouseWheel(e);
      return;
    }

    if (e.Delta != 0)
      this.WalkRequested?.Invoke(this, e.Delta > 0 ? -1 : 1);
  }

  protected override void OnMouseDown(MouseEventArgs e) {
    if (!this._selecting || e.Button != MouseButtons.Left) {
      base.OnMouseDown(e);
      return;
    }

    this.Focus();
    this._dragging = true;
    this._anchor = this._current = this._ToContent(e.X, e.Y);
    this.Invalidate();
  }

  protected override void OnMouseMove(MouseEventArgs e) {
    if (!this._dragging) {
      base.OnMouseMove(e);
      return;
    }

    this._current = this._ToContent(e.X, e.Y);
    this.Invalidate();
  }

  protected override void OnMouseUp(MouseEventArgs e) {
    if (!this._dragging) {
      base.OnMouseUp(e);
      return;
    }

    this._dragging = false;
    this._current = this._ToContent(e.X, e.Y);
    this.Invalidate();

    var area = this._Area();
    if (area.Width * this._scale >= _MINIMUM_DRAG && area.Height * this._scale >= _MINIMUM_DRAG)
      this.AreaSelected?.Invoke(this, area);
  }

  /// <summary>Maps a point on the control to the content pixel under it, by the last paint's mapping.</summary>
  private PointF _ToContent(int x, int y)
    => new((float)((x - this._origin.X) / this._scale), (float)((y - this._origin.Y) / this._scale));

  private RectangleF _Area() {
    var left = Math.Min(this._anchor.X, this._current.X);
    var top = Math.Min(this._anchor.Y, this._current.Y);
    return new(left, top, Math.Abs(this._current.X - this._anchor.X), Math.Abs(this._current.Y - this._anchor.Y));
  }

  /// <summary>Remembers the mapping the panel painted with, and draws the area being dragged.</summary>
  /// <remarks>
  /// The panel keeps its pan to itself and only tells a host where content sits while it paints, so
  /// the mapping is taken from there. It is always current by the time a mouse event can use it: the
  /// panel repaints after every pan and zoom, and nothing can be clicked before the first paint.
  /// </remarks>
  private void _OnPaintContent(object? sender, ZoomPanelPaintEventArgs e) {
    this._origin = e.Origin;
    this._scale = e.Zoom <= 0 ? 1 : e.Zoom;
    if (!this._selecting || !this._dragging)
      return;

    var area = this._Area();
    var topLeft = e.ToView(new(area.X, area.Y));
    var bounds = new Rectangle(
      (int)Math.Round(topLeft.X), (int)Math.Round(topLeft.Y),
      Math.Max(1, (int)Math.Round(area.Width * e.Zoom)), Math.Max(1, (int)Math.Round(area.Height * e.Zoom)));

    // Drawn twice, dark under light, so the outline shows on any picture.
    e.Graphics.DrawRectangle(Color.FromArgb(160, 0, 0, 0), bounds, 3);
    e.Graphics.DrawRectangle(Color.FromArgb(220, 255, 255, 255), bounds, 1);
    e.Graphics.DrawEllipse(Color.FromArgb(160, 0, 0, 0), bounds, 3);
    e.Graphics.DrawEllipse(Color.FromArgb(255, 255, 230, 0), bounds, 1);
  }
}
