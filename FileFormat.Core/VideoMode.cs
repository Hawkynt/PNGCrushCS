using System;
using System.Linq;

namespace FileFormat.Core;

/// <summary>
/// One coupled configuration the user can pick when saving to a file format —
/// bundles <see cref="Dimensions"/>, <see cref="AllowedPaletteRanges"/>, <see cref="AvailablePalettes"/>,
/// plus display hints (<see cref="PixelAspectRatio"/>, <see cref="DisplayFilter"/>).
/// </summary>
/// <remarks>
/// A format declares <see cref="IImageFormatMetadata{TSelf}.VideoModes"/> as an array of these.
/// Each mode is a single user choice — the Save-As flow asks the user to pick one before resize/colour-reduction.
/// <para/>
/// Within a single mode:
/// <list type="bullet">
/// <item><b>Multiple <c>(W, H)</c> pairs</b> sharing the same palette/colour profile go into <see cref="Dimensions"/>
/// (e.g. VGA's 256-colour modes 320×200, 320×240, 360×480).</item>
/// <item><b>Multiple palettes</b> that share the same dimensions and palette-size go into <see cref="AvailablePalettes"/>
/// (e.g. CGA's four 4-colour palette variants in one "4-colour" mode).</item>
/// </list>
/// Fan out into separate <see cref="VideoMode"/> entries only when the dimensions OR palette-size profile actually differ.
/// </remarks>
public sealed record VideoMode {

  /// <summary>Human-readable name shown in the mode picker (e.g. "Low resolution", "Mode 13h", "Tilesheet (2bpp)").</summary>
  public string Name { get; }

  /// <summary>Optional long-form description shown as a tooltip.</summary>
  public string? Description { get; init; }

  /// <summary>All coupled (Width, Height) options inside this mode. Always non-empty.</summary>
  public (IntegerRange Width, IntegerRange Height)[] Dimensions { get; }

  /// <summary>Palette-size options the user can pick within this mode. <c>null</c> means full-colour (no palette).</summary>
  public IntegerRange[]? AllowedPaletteRanges { get; init; }

  /// <summary>Pre-defined palettes available within this mode (e.g. CGA palette variants, NES master palette).
  /// <c>null</c> means the user constructs an arbitrary palette.</summary>
  public FixedPalette[]? AvailablePalettes { get; init; }

  /// <summary>Pixel aspect ratio for display. <c>null</c> = square (1:1). Drives the viewer's X-axis scale.</summary>
  public PixelAspectRatio? PixelAspectRatio { get; init; }

  /// <summary>Post-decode display filter (NTSC composite blending, PAL phase shift, etc.). Default <see cref="DisplayFilter.None"/>.</summary>
  public DisplayFilter DisplayFilter { get; init; } = DisplayFilter.None;

  public VideoMode(
      string name,
      (IntegerRange Width, IntegerRange Height)[] dimensions,
      IntegerRange[]? allowedPaletteRanges = null,
      FixedPalette[]? availablePalettes = null,
      PixelAspectRatio? pixelAspectRatio = null,
      DisplayFilter displayFilter = DisplayFilter.None,
      string? description = null) {
    if (string.IsNullOrEmpty(name)) throw new ArgumentException("VideoMode name is required.", nameof(name));
    if (dimensions == null || dimensions.Length == 0)
      throw new ArgumentException("VideoMode must declare at least one (Width, Height) pair.", nameof(dimensions));

    this.Name = name;
    this.Dimensions = dimensions;
    this.AllowedPaletteRanges = allowedPaletteRanges;
    this.AvailablePalettes = availablePalettes;
    this.PixelAspectRatio = pixelAspectRatio;
    this.DisplayFilter = displayFilter;
    this.Description = description;
  }

  /// <summary>True if any of this mode's <see cref="Dimensions"/> entries accepts the given (width, height).</summary>
  public bool MatchesDimensions(int width, int height) =>
    this.Dimensions.Any(d => d.Width.Contains(width) && d.Height.Contains(height));

  /// <summary>Maximum colour count this mode can encode. Returns <c>int.MaxValue</c> for full-colour modes.</summary>
  public int MaxColourCount =>
    this.AllowedPaletteRanges is { Length: > 0 } ranges
      ? ranges[ranges.Length - 1].Max
      : int.MaxValue;

  /// <summary>True if this mode has any palette-size constraint (i.e. needs colour reduction for full-colour source images).</summary>
  public bool IsIndexed => this.AllowedPaletteRanges is { Length: > 0 };
}

/// <summary>Pixel aspect ratio (Numerator / Denominator) for display correction.
/// Square pixels = (1, 1); Atari ST 4:3 stretch = (6, 5); Apple II HGR = (12, 7); NES = (8, 7); etc.</summary>
public readonly record struct PixelAspectRatio(int Numerator, int Denominator) {

  /// <summary>Implicit conversion from a tuple literal, e.g. <c>(6, 5)</c>.</summary>
  public static implicit operator PixelAspectRatio((int Numerator, int Denominator) t) =>
    new(t.Numerator, t.Denominator);

  /// <summary>The aspect ratio as a floating-point multiplier for horizontal stretch.</summary>
  public double Ratio => (double)this.Numerator / this.Denominator;

  /// <summary>Standard square pixels (1:1).</summary>
  public static readonly PixelAspectRatio Square = new(1, 1);
}

/// <summary>Post-decode display filter declared by a format's <see cref="VideoMode"/>.
/// The viewer applies the filter when painting; the on-disk pixel data is unaffected.</summary>
public enum DisplayFilter {

  /// <summary>No display filter — render pixels as-is.</summary>
  None,

  /// <summary>Classic NTSC composite-video colour bleeding / dot crawl. Suits NES, early consoles, Apple II.</summary>
  NtscComposite,

  /// <summary>Less aggressive than composite — closer to S-Video output.</summary>
  NtscSvideo,

  /// <summary>PAL phase shift / 50 Hz interlace artefacts. Suits European systems.</summary>
  Pal,
}
