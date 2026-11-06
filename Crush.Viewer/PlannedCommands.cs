namespace Crush.Viewer;

/// <summary>A command whose place in the ribbon is settled and which the libraries cannot serve.</summary>
/// <remarks>
/// The button is built, sized, iconed and disabled, and its tooltip says what it would do and why it
/// does not. That is the same treatment "Assemble pages…" gets, and for the same reason: where a
/// command belongs is a decision about the ribbon, and it is worth making once rather than every time
/// an operation lands. A greyed button that explains itself is also the only honest way to show a
/// gap — a button that looks live and does nothing is a bug report waiting to be filed.
/// </remarks>
/// <param name="Text">What the button says.</param>
/// <param name="Icon">
/// The artwork, or <c>null</c> where none of the vendored sets draws the command as itself.
/// </param>
/// <param name="Does">What the command would do, in a sentence.</param>
/// <param name="Why">Why it is off, in a sentence fit for the end of a tooltip.</param>
internal readonly record struct PlannedCommand(string Text, string? Icon, string Does, string Why) {

  /// <summary>What the button says when somebody hovers over it.</summary>
  internal string ToolTipText => $"{this.Does} {this.Why}";
}

/// <summary>The commands the ribbon has a place for and neither image library has an operation for.</summary>
/// <remarks>
/// Every reason below names what is missing rather than saying "not yet": a gap that is a decision
/// reads differently from one that is waiting on work, and the user deserves to know which.
/// </remarks>
internal static class PlannedCommands {

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

  /// <summary>Why the two retouching commands that remove marks are off.</summary>
  private const string _MEDIAN_ONLY =
    "Left out on purpose: the only tool the image libraries have for it is a median filter, and a "
    + "median smears a mark into its surroundings rather than finding it and filling it in.";

  /// <summary>Colour commands the libraries cannot serve.</summary>
  internal static PlannedCommand[] Colour => [
    new("White balance…", "temperature_normal",
      "Neutralises a cast from whatever in the picture is meant to be neutral.",
      "Neither image library can find a neutral in a picture: the colour library's white-point "
        + "transforms convert between fixed illuminants and never look at the picture, so the "
        + "button stays off."),
  ];

  /// <summary>Retouching that takes something out of the picture that does not belong in it.</summary>
  internal static PlannedCommand[] Repair => [
    new("Scratch removal…", "bandaid",
      "Finds the thin bright or dark lines a scanned negative carries and fills them from their surroundings.",
      _MEDIAN_ONLY),
    new("Dust and speckles…", "broom",
      "Finds specks that differ from every neighbour and fills them from their surroundings.",
      _MEDIAN_ONLY),
  ];

  /// <summary>The one automatic correction that needs more than the histogram's ends.</summary>
  internal static PlannedCommand[] Automatic => [
    new("Auto correct…", "wand",
      "Reads the black point, white point and neutral out of the histogram and applies all three.",
      "The engine finds the black and white points (that is Auto levels, beside this) but has "
        + "nothing that finds a neutral, so the combined correction stays off."),
  ];
}
