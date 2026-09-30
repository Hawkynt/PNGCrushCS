namespace Crush.Viewer;

/// <summary>A command whose place in the ribbon is settled and whose pixel operation is not written.</summary>
/// <remarks>
/// The button is built, sized, iconed and disabled, and its tooltip says what it will do. That is the
/// same treatment "Assemble pages…" gets, and for the same reason: where a command belongs is a
/// decision about the ribbon, and it is worth making once rather than every time an operation lands.
/// A greyed button that explains itself is also the only honest way to show a gap — a button that
/// looks live and does nothing is a bug report waiting to be filed.
/// </remarks>
/// <param name="Text">What the button says.</param>
/// <param name="Icon">
/// The artwork, or <c>null</c> where none of the vendored sets draws the command as itself. A resampling
/// kernel or a pixel-art doubling rule has no picture anybody would recognise, and a generic resize
/// glyph shared across all of them would say nothing the group's name does not.
/// </param>
/// <param name="Does">What the command will do, in a sentence.</param>
internal readonly record struct PlannedCommand(string Text, string? Icon, string Does) {

  /// <summary>What the button says when somebody hovers over it.</summary>
  internal string ToolTipText => $"{this.Does} {PlannedCommands.NotYet}";
}

/// <summary>The commands the ribbon has a place for and the image library has no operation for.</summary>
internal static class PlannedCommands {

  /// <summary>Why every command in here is off, in a sentence fit for the end of a tooltip.</summary>
  internal const string NotYet =
    "The operation behind it is not in the image library yet, so the button stays off until it lands.";

  /// <summary>Why scanning is not offered, in a sentence fit for a tooltip.</summary>
  /// <remarks>
  /// Named rather than planned, because this one is not waiting on an image operation. Scanning is a
  /// platform service — SANE on Linux, WIA or TWAIN on Windows, ImageCaptureCore on macOS — and the
  /// toolkit this viewer is built on exposes none of the three. Reaching any of them means a native
  /// integration per platform, which is a larger piece of work than the viewer it would serve.
  /// </remarks>
  internal const string AcquireUnavailable =
    "Scanning needs a platform service the toolkit does not expose: SANE on Linux, WIA or TWAIN on "
    + "Windows. That is a native integration per platform rather than anything this viewer can do on "
    + "its own, so the command is named here and nothing pretends to scan.";

  /// <summary>Tone adjustments: the shape of the transfer curve, one channel at a time or all of them.</summary>
  internal static PlannedCommand[] Tone => [
    new("Brightness…", "weather_sun", "Lifts or lowers every channel by a fixed amount."),
    new("Contrast…", "contrast_high", "Pushes tones away from or towards mid grey."),
    new("Gamma…", "color_gradient", "Reshapes the transfer curve by a power, which is the curve a display applies."),
    new("Levels…", "view-object-histogram-linear", "Maps a chosen black point, white point and mid tone onto the full range."),
    new("Curves…", "chart_curve", "Reshapes the transfer curve freehand, per channel or over all three."),
  ];

  /// <summary>Colour adjustments: where a pixel sits around the wheel rather than how bright it is.</summary>
  internal static PlannedCommand[] Colour => [
    new("Saturation…", "image-saturation", "Moves colours towards or away from grey without turning their hue."),
    new("Hue…", "color_wheel", "Rotates every colour around the wheel by one angle."),
    new("Colour balance…", "color-adjustment", "Shifts the three channels against one another, per tonal range."),
    new("White balance…", "temperature_normal", "Neutralises a cast from whatever in the picture is meant to be neutral."),
  ];

  /// <summary>Resampling kernels beyond the three the resizer already carries.</summary>
  internal static PlannedCommand[] Resample => [
    new("Lanczos…", null, "Resamples through a windowed sinc, which holds the most detail and can ring."),
    new("Mitchell…", null, "Resamples through the Mitchell-Netravali cubic, which trades blur against ringing."),
    new("Catmull-Rom…", null, "Resamples through the Catmull-Rom cubic, sharper than Mitchell and readier to ring."),
    new("Area average…", null, "Averages every source pixel a target pixel covers, which is what a heavy reduction wants."),
  ];

  /// <summary>Doubling rules that read the picture's edges rather than its grid.</summary>
  internal static PlannedCommand[] Regenerate => [
    new("Scale2x", null, "Doubles a picture by the Scale2x rule, which keeps pixel art's hard edges square."),
    new("Eagle", null, "Doubles a picture by the Eagle rule, rounding a corner from its two diagonal neighbours."),
    new("xBR", null, "Doubles a picture by xBR, which follows the direction of an edge rather than the pixel grid."),
  ];

  /// <summary>Retouching that takes something out of the picture that does not belong in it.</summary>
  internal static PlannedCommand[] Repair => [
    new("Scratch removal…", "bandaid", "Finds the thin bright or dark lines a scanned negative carries and fills them from their surroundings."),
    new("Red-eye removal…", "eye_red", "Drains the red out of a flash reflection inside a named region, leaving the pupil dark."),
    new("Dust and speckles…", "broom", "Takes out specks that differ from every neighbour, the way a median filter does."),
  ];

  /// <summary>Sharpness and noise, which are the same knob turned in opposite directions.</summary>
  internal static PlannedCommand[] Sharpness => [
    new("Sharpen…", "image-sharpen", "Raises local contrast through a small convolution."),
    new("Unsharp mask…", null, "Subtracts a blurred copy, by amount, radius and threshold."),
    new("Denoise…", null, "Smooths sensor noise while leaving edges where they are."),
  ];

  /// <summary>One command that reads the histogram and decides the corrections for itself.</summary>
  internal static PlannedCommand[] Automatic => [
    new("Auto correct…", "wand", "Reads the black point, white point and neutral out of the histogram and applies all three."),
  ];

  /// <summary>Blurs, which differ only in the shape of the kernel.</summary>
  internal static PlannedCommand[] Blur => [
    new("Blur…", "image-blur", "Averages each pixel with its neighbours over a chosen radius."),
    new("Gaussian blur…", "draw_convolve", "Blurs through a Gaussian kernel, which stays smooth at any radius."),
    new("Motion blur…", "draw_smudge", "Smears the picture along a chosen angle for a chosen distance."),
  ];

  /// <summary>Effects that leave a picture looking like something other than a photograph.</summary>
  internal static PlannedCommand[] Stylise => [
    new("Emboss…", null, "Turns edges into light and shade, as though the picture were pressed into metal."),
    new("Edge detect…", null, "Keeps the edges and throws the flat areas away."),
    new("Oil painting…", "draw-brush", "Replaces each pixel with the commonest colour around it, which leaves brush-like patches."),
    new("Pixelate…", "large_tiles", "Averages the picture into square blocks."),
    new("Posterise…", "color_swatches", "Rounds each channel to a small number of levels."),
    new("Solarise…", null, "Inverts only the tones above a threshold, which is what over-exposing a print does."),
  ];

  /// <summary>Effects that map the whole tonal range onto a colour or two.</summary>
  internal static PlannedCommand[] Tint => [
    new("Sepia…", null, "Tints a greyscale copy towards brown."),
    new("Duotone…", null, "Maps the tonal range between two chosen colours."),
  ];
}
