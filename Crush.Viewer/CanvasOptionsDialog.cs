using System;
using System.Drawing;
using FileFormat.Core;
using Hawkynt.NativeForms;
using Optimizer.Image;

namespace Crush.Viewer;

/// <summary>Asks how wide a border to put round the picture and what colour to fill it with.</summary>
/// <remarks>
/// One number for all four sides, because that is what a margin is and it is what
/// <see cref="ImageTransformer.ExtendCanvas"/> can be asked for with the picture anchored in the
/// middle. Four independent edges would be a different operation — an anchored canvas of a stated
/// size — and inventing it here rather than in the transform library would put the arithmetic in the
/// wrong place.
/// </remarks>
internal sealed class MarginOptionsDialog : DialogForm {

  private readonly NumericUpDown _margin;
  private readonly ColorPicker _colour;

  internal MarginOptionsDialog(int sourceWidth, int sourceHeight) : base("Add a margin") {
    this.AddNote(
      $"The picture is {sourceWidth} x {sourceHeight} pixels and stays that size; the margin is added "
      + "round it on all four sides.",
      40);

    this._margin = this.AddRow("Margin", new NumericUpDown { Minimum = 1, Maximum = 4096, Increment = 1, Value = 16 });
    this._colour = this.AddRow("Fill", new ColorPicker { SelectedColor = Color.White, AlphaEnabled = true });

    this.AddButtons("Add");
  }

  /// <summary>How many pixels go on each side.</summary>
  /// <remarks>
  /// Not <c>Margin</c>: a control has one of those already, and shadowing it would mean a caller who
  /// asked a <see cref="Control"/>-typed reference for the margin got the layout's answer.
  /// </remarks>
  internal int MarginWidth => (int)this._margin.Value;

  /// <summary>What the margin is filled with.</summary>
  internal Rgba32 Fill => CanvasColour.ToRgba(this._colour.SelectedColor);
}

/// <summary>Asks what shape to letterbox the picture into and what colour the bars should be.</summary>
/// <remarks>
/// The picture is scaled to fit inside the stated rectangle with its aspect ratio kept and centred
/// on a filled field, which is <see cref="ResizeMode.Fit"/>. A target the picture already fits
/// exactly produces bars of zero width, and a target smaller than the picture shrinks it, so this is
/// the one resize on the ribbon where the numbers are the frame rather than the picture.
/// </remarks>
internal sealed class LetterboxOptionsDialog : DialogForm {

  private readonly NumericUpDown _width;
  private readonly NumericUpDown _height;
  private readonly ComboBox _interpolation;
  private readonly ColorPicker _colour;

  internal LetterboxOptionsDialog(int sourceWidth, int sourceHeight, InterpolationHint hint) : base("Letterbox picture") {
    this.AddNote(
      $"The picture is {sourceWidth} x {sourceHeight} pixels. It is scaled to fit inside the frame "
      + "below, keeping its shape, and the rest of the frame is filled.",
      40);

    this._width = this.AddRow("Frame width", _Spinner(sourceWidth));
    this._height = this.AddRow("Frame height", _Spinner(sourceHeight));
    this._interpolation = this.AddRow("Interpolation", _Interpolations(hint));
    this._colour = this.AddRow("Bars", new ColorPicker { SelectedColor = Color.Black, AlphaEnabled = true });

    this.AddButtons("Letterbox");
  }

  /// <summary>How wide the framed picture ends up.</summary>
  internal int FrameWidth => (int)this._width.Value;

  /// <summary>How tall the framed picture ends up.</summary>
  internal int FrameHeight => (int)this._height.Value;

  /// <summary>Which filter the resampler should use on the way in.</summary>
  internal InterpolationHint Interpolation => (InterpolationHint)this._interpolation.SelectedItem!;

  /// <summary>What the bars are filled with.</summary>
  internal Rgba32 Bars => CanvasColour.ToRgba(this._colour.SelectedColor);

  private static NumericUpDown _Spinner(int value) => new() {
    Minimum = 1,
    Maximum = 16384,
    Increment = 1,
    Value = Math.Clamp(value, 1, 16384),
  };

  private static ComboBox _Interpolations(InterpolationHint hint) {
    var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, DisplaySelector = o => o?.ToString() ?? "" };
    box.Items.Add(InterpolationHint.NearestNeighbor);
    box.Items.Add(InterpolationHint.Bilinear);
    box.Items.Add(InterpolationHint.Bicubic);
    box.SelectedIndex = hint switch { InterpolationHint.NearestNeighbor => 0, InterpolationHint.Bilinear => 1, _ => 2 };
    return box;
  }
}

/// <summary>Carries a colour from the toolkit's picker to the one the transform library takes.</summary>
internal static class CanvasColour {

  /// <inheritdoc cref="CanvasColour"/>
  internal static Rgba32 ToRgba(Color colour) => new(colour.R, colour.G, colour.B, colour.A);
}
