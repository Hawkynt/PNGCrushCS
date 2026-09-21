using System;
using FileFormat.Core;
using Hawkynt.NativeForms;

namespace Crush.Viewer;

/// <summary>Asks for the rectangle to keep, in pixels.</summary>
/// <remarks>
/// Numeric rather than drawn on the canvas: the toolkit's canvas publishes no mouse events a crop
/// marquee could be built on, and typing four numbers is what the viewer this replaces offered
/// anyway. A rectangle that only partly overlaps the picture is accepted and clipped, which is what
/// <see cref="Optimizer.Image.ImageTransformer.Crop"/> does with it; one that misses the picture
/// entirely is refused here, because there it would throw.
/// </remarks>
internal sealed class CropOptionsDialog : DialogForm {

  private readonly NumericUpDown _x;
  private readonly NumericUpDown _y;
  private readonly NumericUpDown _width;
  private readonly NumericUpDown _height;
  private readonly int _sourceWidth;
  private readonly int _sourceHeight;

  internal CropOptionsDialog(int sourceWidth, int sourceHeight) : base("Crop picture") {
    this._sourceWidth = sourceWidth;
    this._sourceHeight = sourceHeight;
    this.AddNote($"The picture is {sourceWidth} x {sourceHeight} pixels. A rectangle that hangs over an edge is clipped.", 40);

    this._x = this.AddRow("Left", _Spinner(0, 0));
    this._y = this.AddRow("Top", _Spinner(0, 0));
    this._width = this.AddRow("Width", _Spinner(sourceWidth, 1));
    this._height = this.AddRow("Height", _Spinner(sourceHeight, 1));

    this.AddButtons("Crop");
  }

  /// <summary>The rectangle the user asked to keep, before clipping.</summary>
  internal PixelRect Region => new((int)this._x.Value, (int)this._y.Value, (int)this._width.Value, (int)this._height.Value);

  protected override bool Validate(out string error) {
    if (this.Region.ClampTo(this._sourceWidth, this._sourceHeight).IsEmpty) {
      error = "That rectangle lies entirely outside the picture.";
      return false;
    }

    error = "";
    return true;
  }

  private static NumericUpDown _Spinner(int value, int minimum) => new() {
    Minimum = minimum,
    Maximum = 1 << 16,
    Increment = 1,
    Value = Math.Max(minimum, value),
  };
}
