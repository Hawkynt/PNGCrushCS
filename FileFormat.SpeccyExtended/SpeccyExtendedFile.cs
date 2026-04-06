using System;
using FileFormat.Core;

namespace FileFormat.SpeccyExtended;

/// <summary>In-memory representation of a Speccy eXtended Graphics (SXG) image.
/// Extends the standard ZX Spectrum screen with per-character-line extended attributes (768 bytes per attribute plane).
/// File structure: 4-byte header ("SXG" + version) + 6144 bitmap + standard attributes (768) + extended attributes (768) = 7684 bytes.
/// </summary>
[FormatDetectionPriority(100)]
[FormatMagicBytes(new byte[] { 0x53, 0x58, 0x47 })]
public sealed class SpeccyExtendedFile : IImageFormatReader<SpeccyExtendedFile>, IImageToRawImage<SpeccyExtendedFile>, IImageFormatWriter<SpeccyExtendedFile> {

  static string IImageFormatMetadata<SpeccyExtendedFile>.PrimaryExtension => ".sxg";
  static string[] IImageFormatMetadata<SpeccyExtendedFile>.FileExtensions => [".sxg"];
  static SpeccyExtendedFile IImageFormatReader<SpeccyExtendedFile>.FromSpan(ReadOnlySpan<byte> data) => SpeccyExtendedReader.FromSpan(data);

  static byte[] IImageFormatWriter<SpeccyExtendedFile>.ToBytes(SpeccyExtendedFile file) => SpeccyExtendedWriter.ToBytes(file);

  static bool? IImageFormatMetadata<SpeccyExtendedFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 3 && header[0] == 0x53 && header[1] == 0x58 && header[2] == 0x47;

  /// <summary>ZX Spectrum normal palette (bright=0): Black, Blue, Red, Magenta, Green, Cyan, Yellow, White.</summary>
  private static readonly int[] _NormalPalette = [
    0x000000, 0x0000CD, 0xCD0000, 0xCD00CD, 0x00CD00, 0x00CDCD, 0xCDCD00, 0xCDCDCD
  ];

  /// <summary>ZX Spectrum bright palette (bright=1).</summary>
  private static readonly int[] _BrightPalette = [
    0x000000, 0x0000FF, 0xFF0000, 0xFF00FF, 0x00FF00, 0x00FFFF, 0xFFFF00, 0xFFFFFF
  ];

  /// <summary>Always 256.</summary>
  public int Width => 256;

  /// <summary>Always 192.</summary>
  public int Height => 192;

  /// <summary>Format version byte (currently 1).</summary>
  public byte Version { get; init; } = 1;

  /// <summary>6144 bytes of 1bpp bitmap data in linear row order (deinterleaved).</summary>
  public byte[] BitmapData { get; init; } = [];

  /// <summary>768 bytes of standard attribute data, one per 8x8 cell (bit 7=flash, bit 6=bright, bits 5-3=paper, bits 2-0=ink).</summary>
  public byte[] AttributeData { get; init; } = [];

  /// <summary>768 bytes of extended attribute data, one per 8x8 cell (provides additional color information).</summary>
  public byte[] ExtendedAttributeData { get; init; } = [];

  /// <summary>Converts this SXG screen to a platform-independent <see cref="RawImage"/> in Rgb24 format.
  /// Uses extended attributes when the bright bit is set in the extended attribute (blends with standard attributes).</summary>
  public static RawImage ToRawImage(SpeccyExtendedFile file) {
    ArgumentNullException.ThrowIfNull(file);

    const int width = 256;
    const int height = 192;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var byteIndex = y * 32 + x / 8;
        var bitPosition = 7 - (x % 8);
        var bitValue = (file.BitmapData[byteIndex] >> bitPosition) & 1;

        var cellX = x / 8;
        var cellY = y / 8;
        var cellIndex = cellY * 32 + cellX;
        var attribute = file.AttributeData[cellIndex];
        var extAttr = file.ExtendedAttributeData[cellIndex];

        // If extended attribute has the bright bit set, use extended attribute for colors
        var useExtended = ((extAttr >> 6) & 1) == 1;
        var effectiveAttr = useExtended ? extAttr : attribute;

        var bright = (effectiveAttr >> 6) & 1;
        var paper = (effectiveAttr >> 3) & 0x07;
        var ink = effectiveAttr & 0x07;

        var palette = bright == 1 ? _BrightPalette : _NormalPalette;
        var color = palette[bitValue == 1 ? ink : paper];

        var offset = (y * width + x) * 3;
        rgb[offset] = (byte)((color >> 16) & 0xFF);
        rgb[offset + 1] = (byte)((color >> 8) & 0xFF);
        rgb[offset + 2] = (byte)(color & 0xFF);
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

}
