using System;

namespace FileFormat.Core;

/// <summary>Declares format identity: extensions, capabilities, and signature matching.</summary>
public interface IImageFormatMetadata<TSelf> where TSelf : IImageFormatMetadata<TSelf> {

  /// <summary>The canonical file extension for this format (e.g. ".png").</summary>
  static abstract string PrimaryExtension { get; }

  /// <summary>All recognized file extensions for this format (e.g. [".png"]).</summary>
  static abstract string[] FileExtensions { get; }

  /// <summary>Capability flags for this format (e.g. MonochromeOnly, IndexedOnly). Default: <see cref="FormatCapability.VariableResolution"/>.</summary>
  static virtual FormatCapability Capabilities => FormatCapability.VariableResolution;

  /// <summary>
  /// The disjoint, inclusive ranges of allowed palette sizes for this format. Authoritative — callers must trust this rather than guess.
  /// Mix <see cref="IntegerRange"/> for spans and bare ints (implicitly <see cref="FixedValue"/>) for discrete points:
  /// <list type="bullet">
  /// <item>Empty array (default) — no palette-size constraint (full-colour RGB/RGBA/grayscale).</item>
  /// <item><c>[2]</c> — exactly 2 (monochrome). Equivalent to <c>[new FixedValue(2)]</c>.</item>
  /// <item><c>[new IntegerRange(2, 256)]</c> — any size from 2 to 256 (8-bit indexed).</item>
  /// <item><c>[2, 16, 256]</c> — discrete allowed sizes only.</item>
  /// <item><c>[new IntegerRange(16, 32), new IntegerRange(64, 96)]</c> — multiple disjoint intervals.</item>
  /// <item><c>[new IntegerRange(2, 4), 16, 256]</c> — mix of ranges and fixed points.</item>
  /// </list>
  /// Entries must be sorted ascending and non-overlapping.
  /// </summary>
  static virtual IntegerRange[] AllowedPaletteRanges => System.Array.Empty<IntegerRange>();

  /// <summary>
  /// Pre-defined palettes this format supports. Non-empty means the format does not allow arbitrary palette generation —
  /// callers must choose one of these palettes. The ditherer remains user-selectable.
  /// Examples: CGA (4-colour palettes 0/1 in low/high intensity), DOOM (hardcoded 256-colour palette), NES hardware palette.
  /// </summary>
  static virtual FixedPalette[] FixedPalettes => System.Array.Empty<FixedPalette>();

  /// <summary>
  /// The allowed (Width, Height) combinations the format's <c>FromRawImage</c> accepts.
  /// Each entry is an independent option — within an entry, Width and Height are coupled (e.g. Apple II HGR pairs 280 with 192).
  /// Empty array (default) = no dimensional constraint (any positive width/height accepted).
  /// </summary>
  /// <remarks>
  /// Combine with the <see cref="FormatCapability.FixedResolution"/> flag — the cap flag drives the Save-As
  /// resize prompt; this property tells the resize dialog exactly which dimensions are valid.
  /// Examples:
  /// <code>
  /// // Apple II HGR + DHGR — two coupled options:
  /// [(280, 192), (560, 192)]
  ///
  /// // NES CHR — width fixed at 128, height any multiple of 8 from 8..4096:
  /// [(128, new IntegerRange(8, 4096, step: 8))]
  ///
  /// // OTB — bounded but arbitrary on both axes:
  /// [(new IntegerRange(1, 255), new IntegerRange(1, 255))]
  ///
  /// // Several fixed sizes:
  /// [(640, 480), (800, 600), (1024, 768)]
  /// </code>
  /// </remarks>
  static virtual (IntegerRange Width, IntegerRange Height)[] AllowedDimensions =>
    System.Array.Empty<(IntegerRange, IntegerRange)>();

  /// <summary>Tests whether the given file header matches this format's signature. Returns <c>true</c> (match), <c>false</c> (explicitly not this format), or <c>null</c> (no opinion — fall back to attribute-based matching).</summary>
  static virtual bool? MatchesSignature(ReadOnlySpan<byte> header) => null;
}
