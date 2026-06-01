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
  // Each entry is a 64-colour master palette from which the user picks up to 4 (palette range [2..4]).
  // Sources: Mesen libretro defaults; Firebrandx's Smooth V2 + NES Classic reconstructions; FCEUX r57shell PAL.
  // The Save-As dialog's fixed-palette picker auto-selects the 4 best-matching master colours per image and
  // lets the user adjust via toggle swatches.
  static VideoMode[] IImageFormatMetadata<NesChrFile>.VideoModes => [
    new("Default", [(128, new IntegerRange(8, 8192, step: 8))], [new IntegerRange(2, 4)], [
      new FixedPalette("NES NTSC (Nestopia/FCEUX)",
        0x666666, 0x002A88, 0x1412A7, 0x3B00A4, 0x5C007E, 0x6E0040, 0x6C0600, 0x561D00,
        0x333500, 0x0B4800, 0x005200, 0x004F08, 0x00404D, 0x000000, 0x000000, 0x000000,
        0xADADAD, 0x155FD9, 0x4240FF, 0x7527FE, 0xA01ACC, 0xB71E7B, 0xB53120, 0x994E00,
        0x6B6D00, 0x388700, 0x0C9300, 0x008F32, 0x007C8D, 0x000000, 0x000000, 0x000000,
        0xFFFEFF, 0x64B0FF, 0x9290FF, 0xC676FF, 0xF36AFF, 0xFE6ECC, 0xFE8170, 0xEA9E22,
        0xBCBE00, 0x88D800, 0x5CE430, 0x45E082, 0x48CDDE, 0x4F4F4F, 0x000000, 0x000000,
        0xFFFEFF, 0xC0DFFF, 0xD3D2FF, 0xE8C8FF, 0xFBC2FF, 0xFEC4EA, 0xFECCC5, 0xF7D8A5,
        0xE4E594, 0xCFEF96, 0xBDF4AB, 0xB3F3CC, 0xB5EBF2, 0xB8B8B8, 0x000000, 0x000000),
      new FixedPalette("NES Smooth (Firebrandx V2)",
        0x6A6D6A, 0x001380, 0x1E008A, 0x39007A, 0x550056, 0x5A0018, 0x4F1000, 0x3D1C00,
        0x253200, 0x003D00, 0x004000, 0x003924, 0x002E55, 0x000000, 0x000000, 0x000000,
        0xB9BCB9, 0x1850C7, 0x4B30E3, 0x7322D6, 0x951FA9, 0x9D285C, 0x983700, 0x7F4C00,
        0x5E6400, 0x227700, 0x027E02, 0x007645, 0x006E8A, 0x000000, 0x000000, 0x000000,
        0xFFFFFF, 0x68A6FF, 0x8C9CFF, 0xB586FF, 0xD975FD, 0xE377B9, 0xE58D68, 0xD49D29,
        0xB3AF0C, 0x7BC211, 0x55CA47, 0x46CB81, 0x47C1C5, 0x4A4D4A, 0x000000, 0x000000,
        0xFFFFFF, 0xCCEAFF, 0xDDDEFF, 0xECDAFF, 0xF8D7FE, 0xFCD6F5, 0xFDDBCF, 0xF9E7B5,
        0xF1F0AA, 0xDAFAA9, 0xC9FFBC, 0xC3FBD7, 0xC4F6F6, 0xBEC1BE, 0x000000, 0x000000),
      new FixedPalette("NES Classic Edition (Firebrandx)",
        0x60615F, 0x000083, 0x1D0195, 0x340875, 0x51055E, 0x56000F, 0x4C0700, 0x372308,
        0x203A0B, 0x0F4B0E, 0x194C16, 0x02421E, 0x023154, 0x000000, 0x000000, 0x000000,
        0xA9AAA8, 0x104BBF, 0x4712D8, 0x6300CA, 0x8800A9, 0x930B46, 0x8A2D04, 0x6F5206,
        0x5C7114, 0x1B8D12, 0x199509, 0x178448, 0x206B8E, 0x000000, 0x000000, 0x000000,
        0xFBFBFB, 0x6699F8, 0x8974F9, 0xAB58F8, 0xD557EF, 0xDE5FA9, 0xDC7F59, 0xC7A224,
        0xA7BE03, 0x79CA10, 0x3AD54A, 0x11D1A4, 0x06BFFE, 0x414240, 0x000000, 0x000000,
        0xFBFBFB, 0xBED4FA, 0xC9C7F9, 0xD7BEFA, 0xE8B8F9, 0xF5BAE5, 0xF3CAC2, 0xDFCDA7,
        0xD9E09C, 0xC9EB9E, 0xC0EDB8, 0xB5F4C7, 0xB9EAE9, 0xABABAB, 0x000000, 0x000000),
      new FixedPalette("NES PAL (FCEUX r57shell)",
        0x585858, 0x002094, 0x0104C4, 0x3000C4, 0x5D0095, 0x790042, 0x790000, 0x5E0A00,
        0x2F2901, 0x004402, 0x005103, 0x004F03, 0x003D42, 0x000000, 0x000000, 0x000000,
        0xA1A1A1, 0x0058DF, 0x2C32FE, 0x6A15FE, 0xA106DE, 0xC00881, 0xC01C15, 0xA03D02,
        0x686205, 0x238108, 0x00920B, 0x008F1B, 0x007981, 0x000000, 0x000000, 0x000000,
        0xFFFFFF, 0x41ADFE, 0x8086FE, 0xC067FE, 0xFA56FE, 0xFF59D7, 0xFF6E67, 0xF99117,
        0xBEB711, 0x7AD716, 0x34E922, 0x00E669, 0x04D0D8, 0x424242, 0x000000, 0x000000,
        0xFFFFFF, 0xC1DAF0, 0xCFD1F5, 0xE0C9F5, 0xEDC6EF, 0xF5C6E1, 0xF5CBCA, 0xEDD4B3,
        0xDFDDA6, 0xCEE5A6, 0xC1E9B3, 0xB9E8CA, 0xB9E3E2, 0xACACAC, 0x000000, 0x000000),
    ]),
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
