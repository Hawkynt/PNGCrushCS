using System;
using FileFormat.Core;
using Hawkynt.ColorProcessing.Adapter;
using Hawkynt.ImageProcessing;

namespace Crush.Viewer;

/// <summary>One value a pixel command asks for: a number in a range, or one of a few named choices.</summary>
/// <param name="Label">What the row is called in the dialog.</param>
/// <param name="Minimum">The smallest value the row accepts.</param>
/// <param name="Maximum">The largest value the row accepts.</param>
/// <param name="Initial">What the dialog opens on.</param>
/// <param name="Decimals">How many places after the point the row shows and accepts.</param>
/// <param name="Choices">
/// The names of the choices, when the row is a choice rather than a number; the value handed to the
/// command is then the index of the one picked.
/// </param>
internal sealed record PixelSetting(
  string Label,
  double Minimum,
  double Maximum,
  double Initial,
  int Decimals = 0,
  string[]? Choices = null) {

  /// <summary>A row that picks one of several named things.</summary>
  internal static PixelSetting Choice(string label, int initial, params string[] choices)
    => new(label, 0, choices.Length - 1, initial, 0, choices);
}

/// <summary>How much of the picture a command's preview should show.</summary>
internal enum PreviewExtent {

  /// <summary>
  /// The whole picture, shrunk to fit. Right for a correction that treats every pixel on its own,
  /// where what matters is how the picture as a whole comes out.
  /// </summary>
  Whole,

  /// <summary>
  /// The middle of the picture at one picture pixel to one screen pixel. Right for anything that
  /// looks at neighbours: a blur radius of three on a shrunk preview is a blur of three preview
  /// pixels, which is not what the picture will get.
  /// </summary>
  Detail,
}

/// <summary>A ribbon command that runs one pixel operation over the whole picture.</summary>
/// <param name="Text">What the button says.</param>
/// <param name="Icon">The artwork, or <c>null</c> where no vendored set draws the command as itself.</param>
/// <param name="Does">What the command does, in a sentence; the button's tooltip.</param>
/// <param name="Settings">
/// What the command asks for. None means the command applies as soon as it is clicked.
/// </param>
/// <param name="Run">The operation, given the picture and one value per setting, in order.</param>
/// <param name="Preview">How much of the picture the dialog's preview should show.</param>
internal sealed record PixelCommand(
  string Text,
  string? Icon,
  string Does,
  PixelSetting[] Settings,
  Func<RawImage, double[], RawImage> Run,
  PreviewExtent Preview = PreviewExtent.Whole) {

  /// <summary>Whether the command opens a dialog before it runs.</summary>
  internal bool AsksFirst => this.Settings.Length > 0;

  /// <summary>The values the dialog opens on.</summary>
  internal double[] InitialValues => Array.ConvertAll(this.Settings, s => s.Initial);

  /// <summary>Runs the command with the values the dialog opens on.</summary>
  internal RawImage RunWithInitialValues(RawImage image) => this.Run(image, this.InitialValues);
}

/// <summary>
/// The pixel commands the ribbon offers, grouped the way the ribbon groups them, each bound to the
/// library operation that does the work.
/// </summary>
/// <remarks>
/// <para>
/// No pixel is touched in here. Every command is one call into <see cref="ColorCorrection"/>,
/// <see cref="ImageEffects"/>, <see cref="ImageScaler"/> or, for the effects the pixel-operations
/// engine does not have and the colour library does, <see cref="RawImageEffects"/>. What this file
/// owns is the part that is the viewer's business: which commands there are, what each is called,
/// what it asks for and in what units, and what a dialog should open on.
/// </para>
/// <para>
/// The opening values are deliberately not the identity. A dialog that opens on a brightness of zero
/// shows a preview identical to the picture, which tells the user nothing about what the control
/// does; opening on a modest amount shows the effect at once, and zero is one click away.
/// </para>
/// <para>
/// Percentages are what the dialogs show wherever the library takes a fraction, because that is the
/// unit every image application's sliders use; the conversion happens here, next to the call.
/// </para>
/// </remarks>
internal static class PixelCommands {

  /// <summary>The engine's six kernels, in the order a chooser lists them.</summary>
  internal static readonly ResampleKernel[] Kernels = [
    ResampleKernel.Nearest,
    ResampleKernel.Box,
    ResampleKernel.Bilinear,
    ResampleKernel.Bicubic,
    ResampleKernel.Mitchell,
    ResampleKernel.Lanczos,
  ];

  /// <summary>What a chooser calls each kernel.</summary>
  internal static readonly string[] KernelNames = [
    "Nearest neighbour",
    "Box (area average)",
    "Bilinear",
    "Bicubic (Catmull-Rom)",
    "Mitchell-Netravali",
    "Lanczos (three lobes)",
  ];

  /// <summary>Tone: the shape of the transfer curve.</summary>
  internal static PixelCommand[] Tone => [
    new("Brightness…", "weather_sun", "Lifts or lowers every channel by a fixed amount.",
      [new("Brightness (%)", -100, 100, 15)],
      (i, v) => ColorCorrection.Brightness(i, v[0])),
    new("Contrast…", "contrast_high", "Pushes tones away from or towards mid grey.",
      [new("Contrast (%)", -100, 95, 20)],
      (i, v) => ColorCorrection.Contrast(i, v[0])),
    new("Gamma…", "color_gradient", "Reshapes the transfer curve by a power; above one lightens the midtones.",
      [new("Gamma", 0.1, 5, 1.3, 2)],
      (i, v) => ColorCorrection.Gamma(i, v[0])),
    new("Levels…", "view-object-histogram-linear", "Maps a chosen black point, white point and midtone onto the full range.",
      [new("Black point", 0, 254, 16), new("White point", 1, 255, 235), new("Midtone gamma", 0.1, 10, 1, 2)],
      (i, v) => {
        var black = v[0] / 255.0;
        var white = Math.Max(v[1], v[0] + 1) / 255.0;
        return ColorCorrection.Levels(i, black, white, v[2]);
      }),
    new("Curves…", "chart_curve", "Reshapes the transfer curve through five points, over all three channels at once.",
      [
        new("Output at 0%", 0, 100, 0),
        new("Output at 25%", 0, 100, 20),
        new("Output at 50%", 0, 100, 50),
        new("Output at 75%", 0, 100, 80),
        new("Output at 100%", 0, 100, 100),
      ],
      (i, v) => ColorCorrection.ApplyCurve(i, ToneCurves.Through([0, 0.25, 0.5, 0.75, 1], [v[0] / 100, v[1] / 100, v[2] / 100, v[3] / 100, v[4] / 100]))),
  ];

  /// <summary>Colour: where a pixel sits around the wheel rather than how bright it is.</summary>
  internal static PixelCommand[] Colour => [
    new("Saturation…", "image-saturation", "Moves colours towards or away from grey without turning their hue.",
      [new("Saturation (%)", 0, 300, 130)],
      (i, v) => ColorCorrection.Saturation(i, v[0])),
    new("Hue…", "color_wheel", "Turns every colour around the wheel, and scales saturation and lightness in the same pass.",
      [new("Hue (degrees)", -180, 180, 30), new("Saturation (%)", 0, 300, 100), new("Lightness (%)", 0, 200, 100)],
      (i, v) => ColorCorrection.Modulate(i, v[2], v[1], v[0])),
    new("Colour balance…", "color-adjustment", "Shifts the three channels against one another, per tonal range.",
      [
        new("Shadows: cyan - red", -100, 100, 0),
        new("Shadows: magenta - green", -100, 100, 0),
        new("Shadows: yellow - blue", -100, 100, 0),
        new("Midtones: cyan - red", -100, 100, 20),
        new("Midtones: magenta - green", -100, 100, 0),
        new("Midtones: yellow - blue", -100, 100, -10),
        new("Highlights: cyan - red", -100, 100, 0),
        new("Highlights: magenta - green", -100, 100, 0),
        new("Highlights: yellow - blue", -100, 100, 0),
      ],
      (i, v) => RawImageEffects.ColorBalance(
        i,
        (v[0] / 100, v[1] / 100, v[2] / 100),
        (v[3] / 100, v[4] / 100, v[5] / 100),
        (v[6] / 100, v[7] / 100, v[8] / 100))),
  ];

  /// <summary>Regenerate: the engine's resampler run at the size the picture already is.</summary>
  internal static PixelCommand Regenerate => new(
    "Regenerate…", null,
    "Runs a resampling kernel over the picture at its own size. Only Mitchell changes anything: it softens slightly, as it would on any resample.",
    [PixelSetting.Choice("Kernel", Array.IndexOf(Kernels, ResampleKernel.Mitchell), KernelNames)],
    (i, v) => ImageScaler.Regenerate(i, Kernels[(int)v[0]]),
    PreviewExtent.Detail);

  /// <summary>Pixel art: doubling rules that read the picture's edges rather than its grid.</summary>
  internal static PixelCommand[] PixelArt => [
    new("Scale2x", null, "Doubles the picture by the Scale2x rule, which keeps pixel art's hard edges square.",
      [], (i, _) => RawImageEffects.Scale2x(i)),
    new("Eagle", null, "Doubles the picture by the Eagle rule, rounding a corner from its two diagonal neighbours.",
      [], (i, _) => RawImageEffects.Eagle(i)),
    new("xBR", null, "Doubles the picture by xBR, which follows the direction of an edge rather than the pixel grid.",
      [], (i, _) => RawImageEffects.Xbr(i)),
  ];

  /// <summary>Sharpness and noise.</summary>
  internal static PixelCommand[] Sharpness => [
    new("Sharpen…", "image-sharpen", "Raises local contrast by subtracting a Gaussian from the picture.",
      [new("Radius (0 = from sigma)", 0, 20, 0), new("Sigma", 0.1, 10, 1, 1)],
      (i, v) => ImageEffects.Sharpen(i, v[0], v[1]),
      PreviewExtent.Detail),
    new("Unsharp mask…", null, "Adds back the difference between the picture and a blur of it, by amount, radius and threshold.",
      [new("Radius (0 = from sigma)", 0, 20, 0), new("Sigma", 0.1, 10, 1.5, 1), new("Amount (%)", 0, 500, 100), new("Threshold (%)", 0, 100, 5)],
      (i, v) => ImageEffects.UnsharpMask(i, v[0], v[1], v[2] / 100, v[3] / 100),
      PreviewExtent.Detail),
    new("Denoise…", null, "Averages each pixel with the neighbours close to it in colour, which smooths noise and keeps edges.",
      [new("Strength (%)", 1, 100, 10), new("Radius", 1, 5, 2)],
      (i, v) => RawImageEffects.ReduceNoise(i, v[0] / 100, (int)v[1]),
      PreviewExtent.Detail),
  ];

  /// <summary>Corrections that read the picture and decide the amount for themselves.</summary>
  internal static PixelCommand[] Automatic => [
    new("Auto levels", null, "Stretches the darkest and lightest tones the picture has to black and white, all channels together.",
      [], (i, _) => ColorCorrection.AutoLevels(i)),
    new("Equalize", null, "Spreads the tones so that every level is used about equally often.",
      [], (i, _) => ColorCorrection.Equalize(i)),
  ];

  /// <summary>Blurs, which differ in the shape of the kernel.</summary>
  internal static PixelCommand[] Blur => [
    new("Blur…", "image-blur", "Averages each pixel with its neighbours over a chosen radius, all weighted equally.",
      [new("Radius", 1, 50, 2)],
      (i, v) => ImageEffects.BoxBlur(i, (int)v[0]),
      PreviewExtent.Detail),
    new("Gaussian blur…", "draw_convolve", "Blurs through a Gaussian kernel, which stays smooth at any radius.",
      [new("Sigma", 0.1, 50, 2, 1)],
      (i, v) => ImageEffects.GaussianBlur(i, 0, v[0]),
      PreviewExtent.Detail),
    new("Motion blur…", "draw_smudge", "Smears the picture along a chosen angle for a chosen distance.",
      [new("Distance", 2, 200, 15), new("Angle (degrees)", -180, 180, 0)],
      (i, v) => RawImageEffects.MotionBlur(i, (int)v[0], v[1]),
      PreviewExtent.Detail),
  ];

  /// <summary>Effects that leave a picture looking like something other than a photograph.</summary>
  internal static PixelCommand[] Stylise => [
    new("Emboss…", null, "Turns edges into light and shade, as though the picture were pressed into metal.",
      [new("Radius (0 = from sigma)", 0, 20, 0), new("Sigma", 0.1, 10, 1, 1)],
      (i, v) => ImageEffects.Emboss(i, v[0], v[1]),
      PreviewExtent.Detail),
    new("Edge detect…", null, "Keeps the edges and throws the flat areas away.",
      [new("Radius", 0.5, 10, 1, 1)],
      (i, v) => ImageEffects.DetectEdges(i, v[0]),
      PreviewExtent.Detail),
    new("Oil painting…", "draw-brush", "Replaces each pixel with the mean of the commonest brightness around it, which leaves brush-like patches.",
      [new("Radius", 1, 10, 3), new("Brightness levels", 2, 64, 20)],
      (i, v) => RawImageEffects.OilPainting(i, (int)v[0], (int)v[1]),
      PreviewExtent.Detail),
    new("Pixelate…", "large_tiles", "Averages the picture into square blocks.",
      [new("Block size", 2, 128, 8)],
      (i, v) => RawImageEffects.Pixelate(i, (int)v[0]),
      PreviewExtent.Detail),
    new("Posterise…", "color_swatches", "Rounds each channel to a small number of levels.",
      [new("Levels per channel", 2, 32, 4)],
      (i, v) => RawImageEffects.Posterize(i, (int)v[0])),
    new("Solarise…", null, "Inverts only the tones above a threshold, which is what over-exposing a print does.",
      [new("Threshold (%)", 0, 100, 50)],
      (i, v) => RawImageEffects.Solarize(i, v[0] / 100)),
  ];

  /// <summary>Effects that map the whole tonal range onto a colour or two.</summary>
  internal static PixelCommand[] Tint => [
    new("Sepia…", null, "Tints the picture towards brown through the usual sepia matrix.",
      [new("Intensity (%)", 0, 100, 100)],
      (i, v) => RawImageEffects.Sepia(i, v[0] / 100)),
    new("Duotone…", null, "Maps the tonal range between two chosen colours.",
      [
        new("Shadow red", 0, 255, 0),
        new("Shadow green", 0, 255, 0),
        new("Shadow blue", 0, 255, 51),
        new("Highlight red", 0, 255, 255),
        new("Highlight green", 0, 255, 230),
        new("Highlight blue", 0, 255, 153),
      ],
      (i, v) => RawImageEffects.Duotone(i, v[0] / 255, v[1] / 255, v[2] / 255, v[3] / 255, v[4] / 255, v[5] / 255)),
  ];

  /// <summary>Every command above, for whoever needs to look one up by its text.</summary>
  internal static PixelCommand[] All => [
    .. Tone, .. Colour, Regenerate, .. PixelArt, .. Sharpness, .. Automatic, .. Blur, .. Stylise, .. Tint,
  ];

  /// <summary>Finds a command by its button text, with or without the trailing ellipsis.</summary>
  internal static PixelCommand? Find(string text) {
    var wanted = text.TrimEnd('…', '.').Trim();
    foreach (var command in All)
      if (string.Equals(command.Text.TrimEnd('…'), wanted, StringComparison.OrdinalIgnoreCase))
        return command;

    return null;
  }
}

/// <summary>An area marked on the canvas, carried over onto the pixels of the picture behind it.</summary>
internal static class MarkedArea {

  /// <summary>
  /// The picture's pixels under an area marked on the shown image, which may be wider than the
  /// picture when its pixels are not square.
  /// </summary>
  /// <param name="left">The area's left edge in the shown image's pixels.</param>
  /// <param name="top">The area's top edge in the shown image's pixels.</param>
  /// <param name="width">The area's width in the shown image's pixels.</param>
  /// <param name="height">The area's height in the shown image's pixels.</param>
  /// <param name="shownWidth">How wide the shown image is.</param>
  /// <param name="shownHeight">How tall the shown image is.</param>
  /// <param name="pictureWidth">How wide the picture is.</param>
  /// <param name="pictureHeight">How tall the picture is.</param>
  /// <returns>The rectangle, clipped to the picture; empty when the area misses it.</returns>
  /// <remarks>
  /// Rounded outwards, so that an area drawn tight around an eye still covers the eye's edge pixels.
  /// </remarks>
  internal static PixelRect ToPicture(
    double left, double top, double width, double height,
    int shownWidth, int shownHeight, int pictureWidth, int pictureHeight) {
    var scaleX = pictureWidth / (double)Math.Max(1, shownWidth);
    var scaleY = pictureHeight / (double)Math.Max(1, shownHeight);
    var x0 = (int)Math.Floor(left * scaleX);
    var y0 = (int)Math.Floor(top * scaleY);
    var x1 = (int)Math.Ceiling((left + width) * scaleX);
    var y1 = (int)Math.Ceiling((top + height) * scaleY);
    return new PixelRect(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0)).ClampTo(pictureWidth, pictureHeight);
  }

  /// <summary>The area with half its size again on every side, for a preview that shows it in context.</summary>
  internal static PixelRect WithSurroundings(PixelRect area, int pictureWidth, int pictureHeight) {
    var marginX = Math.Max(4, area.Width / 2);
    var marginY = Math.Max(4, area.Height / 2);
    return new PixelRect(area.X - marginX, area.Y - marginY, area.Width + marginX * 2, area.Height + marginY * 2)
      .ClampTo(pictureWidth, pictureHeight);
  }

  /// <summary>Red-eye reduction inside the ellipse an area bounds, on a picture or on a cut-out of it.</summary>
  /// <param name="image">The picture, or the part of it that starts at <paramref name="origin"/>.</param>
  /// <param name="area">The marked area, in the whole picture's pixels.</param>
  /// <param name="origin">Where <paramref name="image"/> sits in the whole picture.</param>
  /// <param name="threshold">From 0 to 0.8; see <see cref="RedEyeReduction.Apply"/>.</param>
  internal static RawImage ReduceRedEye(RawImage image, PixelRect area, (int X, int Y) origin, double threshold)
    => RedEyeReduction.Apply(image, area.X - origin.X, area.Y - origin.Y, area.Width, area.Height, threshold);
}

/// <summary>Tone curves built from a few points the user sets.</summary>
internal static class ToneCurves {

  /// <summary>
  /// A smooth curve through the given points that never overshoots between two of them.
  /// </summary>
  /// <param name="inputs">The points' positions along the input axis, rising, from 0 to 1.</param>
  /// <param name="outputs">What each point maps to.</param>
  /// <remarks>
  /// <para>
  /// A monotone cubic Hermite spline, with the tangents chosen by Fritsch and Carlson's rule
  /// ("Monotone Piecewise Cubic Interpolation", SIAM J. Numer. Anal. 17(2), 1980): start from the
  /// mean of the neighbouring secants, set a tangent to zero wherever the data turns, and scale any
  /// pair of tangents whose squares sum past nine back onto that circle.
  /// </para>
  /// <para>
  /// That property is the reason for the choice over a natural spline. Five points set to rise
  /// gently would, through a natural spline, dip below the first or climb past the last somewhere in
  /// between — a curve that darkens a tone it was told to brighten. Between two points this one
  /// stays between their outputs, which is what a user dragging points expects.
  /// </para>
  /// </remarks>
  internal static ToneCurve Through(double[] inputs, double[] outputs) {
    ArgumentNullException.ThrowIfNull(inputs);
    ArgumentNullException.ThrowIfNull(outputs);
    if (inputs.Length != outputs.Length || inputs.Length < 2)
      throw new ArgumentException("A curve needs at least two points, each with an input and an output.");

    var count = inputs.Length;
    var secants = new double[count - 1];
    for (var k = 0; k < count - 1; ++k)
      secants[k] = (outputs[k + 1] - outputs[k]) / (inputs[k + 1] - inputs[k]);

    var tangents = new double[count];
    tangents[0] = secants[0];
    tangents[count - 1] = secants[count - 2];
    for (var k = 1; k < count - 1; ++k)
      tangents[k] = secants[k - 1] * secants[k] <= 0 ? 0 : (secants[k - 1] + secants[k]) / 2;

    for (var k = 0; k < count - 1; ++k) {
      if (secants[k] == 0) {
        tangents[k] = 0;
        tangents[k + 1] = 0;
        continue;
      }

      var alpha = tangents[k] / secants[k];
      var beta = tangents[k + 1] / secants[k];
      var length = alpha * alpha + beta * beta;
      if (length <= 9)
        continue;

      var tau = 3 / Math.Sqrt(length);
      tangents[k] = tau * alpha * secants[k];
      tangents[k + 1] = tau * beta * secants[k];
    }

    return value => {
      if (value <= inputs[0])
        return outputs[0];
      if (value >= inputs[count - 1])
        return outputs[count - 1];

      var k = 0;
      while (value > inputs[k + 1])
        ++k;

      var h = inputs[k + 1] - inputs[k];
      var t = (value - inputs[k]) / h;
      var t2 = t * t;
      var t3 = t2 * t;
      return (2 * t3 - 3 * t2 + 1) * outputs[k]
        + (t3 - 2 * t2 + t) * h * tangents[k]
        + (-2 * t3 + 3 * t2) * outputs[k + 1]
        + (t3 - t2) * h * tangents[k + 1];
    };
  }
}
