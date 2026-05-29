using System;
using FileFormat.Core;

namespace FileFormat.NesChr;

/// <summary>In-memory representation of NES CHR tile data (2bpp planar, 8x8 tiles, 16 tiles per row).</summary>
public readonly record struct NesChrFile : IImageFormatReader<NesChrFile>, IImageToRawImage<NesChrFile>, IImageFromRawImage<NesChrFile>, IImageFormatWriter<NesChrFile> {

  /// <summary>Number of pixels per tile row/column.</summary>
  internal const int TileSize = 8;

  /// <summary>Number of bytes per tile (two planes of 8 bytes each).</summary>
  internal const int BytesPerTile = 16;

  /// <summary>Number of tiles arranged horizontally in the output image.</summary>
  internal const int TilesPerRow = 16;

  /// <summary>Fixed image width: 16 tiles x 8 pixels = 128.</summary>
  internal const int FixedWidth = TilesPerRow * TileSize;

  /// <summary>Default 4-entry grayscale palette (RGB triplets): black, dark gray, light gray, white.</summary>
  private static readonly byte[] _DefaultPalette = [0, 0, 0, 85, 85, 85, 170, 170, 170, 255, 255, 255];

  static string IImageFormatMetadata<NesChrFile>.PrimaryExtension => ".chr";
  static string[] IImageFormatMetadata<NesChrFile>.FileExtensions => [".chr"];
  static NesChrFile IImageFormatReader<NesChrFile>.FromSpan(ReadOnlySpan<byte> data) => NesChrReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<NesChrFile>.Capabilities => FormatCapability.IndexedOnly | FormatCapability.FixedResolution;
  static IntegerRange[] IImageFormatMetadata<NesChrFile>.AllowedPaletteRanges => [new IntegerRange(2, 4)];
  static (IntegerRange Width, IntegerRange Height)[] IImageFormatMetadata<NesChrFile>.AllowedDimensions =>
    [(128, new IntegerRange(8, 8192, step: 8))];
  // The 64-entry NES master palette is the POOL from which the user picks 4 colours (AllowedPaletteRanges = [2..4]).
  // The Save-As dialog's fixed-palette picker auto-selects the 4 best-matching master colours for the image and
  // lets the user manually adjust the selection via toggle swatches.
  static FixedPalette[] IImageFormatMetadata<NesChrFile>.FixedPalettes => [
    new FixedPalette("NES NTSC (Nestopia/FCEUX)",
      0x666666, 0x002A88, 0x1412A7, 0x3B00A4, 0x5C007E, 0x6E0040, 0x6C0600, 0x561D00,
      0x333500, 0x0B4800, 0x005200, 0x004F08, 0x00404D, 0x000000, 0x000000, 0x000000,
      0xADADAD, 0x155FD9, 0x4240FF, 0x7527FE, 0xA01ACC, 0xB71E7B, 0xB53120, 0x994E00,
      0x6B6D00, 0x388700, 0x0C9300, 0x008F32, 0x007C8D, 0x000000, 0x000000, 0x000000,
      0xFFFEFF, 0x64B0FF, 0x9290FF, 0xC676FF, 0xF36AFF, 0xFE6ECC, 0xFE8170, 0xEA9E22,
      0xBCBE00, 0x88D800, 0x5CE430, 0x45E082, 0x48CDDE, 0x4F4F4F, 0x000000, 0x000000,
      0xFFFEFF, 0xC0DFFF, 0xD3D2FF, 0xE8C8FF, 0xFBC2FF, 0xFEC4EA, 0xFECCC5, 0xF7D8A5,
      0xE4E594, 0xCFEF96, 0xBDF4AB, 0xB3F3CC, 0xB5EBF2, 0xB8B8B8, 0x000000, 0x000000),
  ];
  static byte[] IImageFormatWriter<NesChrFile>.ToBytes(NesChrFile file) => NesChrWriter.ToBytes(file);

  /// <summary>Image width in pixels (always 128).</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels (multiple of 8).</summary>
  public int Height { get; init; }

  /// <summary>Indexed pixel data (values 0-3, one byte per pixel, row-major).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>4-entry RGB palette (12 bytes: 4 colors x 3 bytes each).</summary>
  public byte[] Palette { get; init; }

  /// <summary>Converts this NES CHR file to a platform-independent <see cref="RawImage"/> in Indexed8 format.
  /// The NES CHR format doesn't store a palette, so reads always fall back to a 4-entry grayscale ramp.</summary>
  public static RawImage ToRawImage(NesChrFile file) {

    // The .chr file is raw tile data with no embedded palette. If the file struct happens to carry one
    // (e.g. from a fresh FromRawImage call), use it; otherwise fall back to a 4-entry grayscale ramp so
    // PixelConverter has valid 4-colour lookups for the 2bpp pixel values (0..3).
    var palette = file.Palette is { Length: >= 12 } p ? p[..12] : _DefaultPalette[..];

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = (file.PixelData ?? Array.Empty<byte>())[..],
      Palette = palette,
      PaletteCount = 4,
    };
  }

  /// <summary>Creates a NES CHR file from a platform-independent <see cref="RawImage"/>. Must be Indexed8 with at most 4 palette entries and width 128.</summary>
  public static NesChrFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"NES CHR requires Indexed8 pixel format, got {image.Format}.", nameof(image));
    if (image.Width != FixedWidth)
      throw new ArgumentException($"NES CHR requires width {FixedWidth}, got {image.Width}.", nameof(image));
    if (image.PaletteCount > 4)
      throw new ArgumentException($"NES CHR supports at most 4 palette entries, got {image.PaletteCount}.", nameof(image));

    var palette = image.Palette != null ? image.Palette[..] : _DefaultPalette[..];

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
      Palette = palette,
    };
  }
}
