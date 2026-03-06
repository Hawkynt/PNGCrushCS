using System.Collections.Generic;

namespace FileFormat.Png;

/// <summary>Data model representing a PNG file</summary>
public sealed class PngFile {
  /// <summary>Image width in pixels</summary>
  public required int Width { get; init; }

  /// <summary>Image height in pixels</summary>
  public required int Height { get; init; }

  /// <summary>Bit depth per channel (1, 2, 4, 8, or 16)</summary>
  public required int BitDepth { get; init; }

  /// <summary>PNG color type</summary>
  public required PngColorType ColorType { get; init; }

  /// <summary>Interlace method</summary>
  public PngInterlaceMethod InterlaceMethod { get; init; } = PngInterlaceMethod.None;

  /// <summary>Raw pixel data as scanlines (one byte array per row, without filter bytes)</summary>
  public byte[][]? PixelData { get; init; }

  /// <summary>Palette data (RGB triplets, 3 bytes per entry)</summary>
  public byte[]? Palette { get; init; }

  /// <summary>Number of actual palette entries used</summary>
  public int PaletteCount { get; init; }

  /// <summary>Transparency chunk data (tRNS)</summary>
  public byte[]? Transparency { get; init; }

  /// <summary>Ancillary chunks to preserve before PLTE</summary>
  public IReadOnlyList<PngChunk>? ChunksBeforePlte { get; init; }

  /// <summary>Ancillary chunks to preserve between PLTE and IDAT</summary>
  public IReadOnlyList<PngChunk>? ChunksBetweenPlteAndIdat { get; init; }

  /// <summary>Ancillary chunks to preserve after IDAT</summary>
  public IReadOnlyList<PngChunk>? ChunksAfterIdat { get; init; }
}
