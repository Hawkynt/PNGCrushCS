using System;
using FileFormat.Core;

namespace FileFormat.PcEngineTile;

/// <summary>In-memory representation of PC Engine/TurboGrafx-16 4BPP planar tile data (SNES-style interleave, 8x8 tiles, 16 tiles per row).</summary>
public readonly record struct PcEngineTileFile : IImageFormatReader<PcEngineTileFile>, IImageToRawImage<PcEngineTileFile>, IImageFromRawImage<PcEngineTileFile>, IImageFormatWriter<PcEngineTileFile> {

  /// <summary>Number of pixels per tile row/column.</summary>
  internal const int TileSize = 8;

  /// <summary>Number of bytes per tile (four planes: planes 0+1 interleaved 16 bytes, planes 2+3 interleaved 16 bytes).</summary>
  internal const int BytesPerTile = 32;

  /// <summary>Number of tiles arranged horizontally in the output image.</summary>
  internal const int TilesPerRow = 16;

  /// <summary>Fixed image width: 16 tiles x 8 pixels = 128.</summary>
  internal const int FixedWidth = TilesPerRow * TileSize;

  /// <summary>Maximum palette index value (4 bits = 0..15).</summary>
  internal const int MaxPaletteIndex = 15;

  /// <summary>Number of palette entries.</summary>
  internal const int PaletteEntryCount = 16;

  /// <summary>Default 16-entry grayscale palette (RGB triplets): evenly spaced from black to white.</summary>
  private static readonly byte[] _DefaultPalette = _BuildDefaultPalette();

  private static byte[] _BuildDefaultPalette() {
    var palette = new byte[PaletteEntryCount * 3];
    for (var i = 0; i < PaletteEntryCount; ++i) {
      var gray = (byte)(i * 255 / (PaletteEntryCount - 1));
      palette[i * 3] = gray;
      palette[i * 3 + 1] = gray;
      palette[i * 3 + 2] = gray;
    }

    return palette;
  }

  static string IImageFormatMetadata<PcEngineTileFile>.PrimaryExtension => ".pce";
  static string[] IImageFormatMetadata<PcEngineTileFile>.FileExtensions => [".pce"];
  static PcEngineTileFile IImageFormatReader<PcEngineTileFile>.FromSpan(ReadOnlySpan<byte> data) => PcEngineTileReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<PcEngineTileFile>.Capabilities => FormatCapability.IndexedOnly;
  static byte[] IImageFormatWriter<PcEngineTileFile>.ToBytes(PcEngineTileFile file) => PcEngineTileWriter.ToBytes(file);

  /// <summary>Image width in pixels (always 128).</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels (multiple of 8).</summary>
  public int Height { get; init; }

  /// <summary>Indexed pixel data (values 0-15, one byte per pixel, row-major).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>16-entry RGB palette (48 bytes: 16 colors x 3 bytes each).</summary>
  public byte[] Palette { get; init; }

  /// <summary>Converts this PC Engine tile file to a platform-independent <see cref="RawImage"/> in Indexed8 format.</summary>
  public static RawImage ToRawImage(PcEngineTileFile file) {

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = file.Palette[..],
      PaletteCount = PaletteEntryCount,
    };
  }

  /// <summary>Creates a PC Engine tile file from a platform-independent <see cref="RawImage"/>. Must be Indexed8 with at most 16 palette entries and width 128.</summary>
  public static PcEngineTileFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"PC Engine tile data requires Indexed8 pixel format, got {image.Format}.", nameof(image));
    if (image.Width != FixedWidth)
      throw new ArgumentException($"PC Engine tile data requires width {FixedWidth}, got {image.Width}.", nameof(image));
    if (image.PaletteCount > PaletteEntryCount)
      throw new ArgumentException($"PC Engine tile data supports at most {PaletteEntryCount} palette entries, got {image.PaletteCount}.", nameof(image));

    var palette = image.Palette != null ? image.Palette[..] : _DefaultPalette[..];

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
      Palette = palette,
    };
  }
}
