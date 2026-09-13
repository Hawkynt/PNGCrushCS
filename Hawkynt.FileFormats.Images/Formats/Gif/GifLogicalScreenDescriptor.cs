namespace FileFormat.Gif;

/// <summary>The 7-byte Logical Screen Descriptor that follows the GIF signature.</summary>
/// <param name="Width">Canvas width in pixels.</param>
/// <param name="Height">Canvas height in pixels.</param>
/// <param name="HasGlobalColorTable">True if a global colour table immediately follows the LSD.</param>
/// <param name="ColorResolution">Bits-per-primary-colour of the source palette (1..8). For modern GIFs this is
/// typically <c>8</c> regardless of the GCT size — it's informational, not a real decoding constraint.</param>
/// <param name="GlobalColorTableSorted">True if the global colour table is sorted by frequency.</param>
/// <param name="GlobalColorTableSize">The exponent <c>e</c> such that the GCT contains 2^(e+1) entries. 0..7.</param>
/// <param name="BackgroundColorIndex">Index into the global colour table for the canvas background.</param>
/// <param name="PixelAspectRatio">Raw pixel-aspect-ratio byte. 0 = square pixels; otherwise <c>(value + 15) / 64</c>
/// is the ratio. Almost always 0 in real-world GIFs.</param>
public readonly record struct GifLogicalScreenDescriptor(
  ushort Width,
  ushort Height,
  bool HasGlobalColorTable,
  byte ColorResolution,
  bool GlobalColorTableSorted,
  byte GlobalColorTableSize,
  byte BackgroundColorIndex,
  byte PixelAspectRatio
) {

  /// <summary>The number of entries the GCT carries: <c>2^(GlobalColorTableSize + 1)</c>.</summary>
  public int GlobalColorTableEntryCount => this.HasGlobalColorTable ? 1 << (this.GlobalColorTableSize + 1) : 0;

  /// <summary><see cref="ColorResolution"/> as the raw 3-bit on-disk value rather than the bit count
  /// <see cref="ColorResolution"/> stores.</summary>
  public global::FileFormat.Gif.ColorResolution ColorResolutionValue
    => (global::FileFormat.Gif.ColorResolution)((this.ColorResolution - 1) & 0x07);

  /// <summary>This descriptor with its colour resolution set from the raw 3-bit enumeration.</summary>
  public GifLogicalScreenDescriptor WithColorResolution(global::FileFormat.Gif.ColorResolution resolution)
    => this with { ColorResolution = (byte)((byte)resolution + 1) };
}
