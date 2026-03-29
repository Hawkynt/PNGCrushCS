using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AutodeskCel;

/// <summary>In-memory representation of an Autodesk Animator CEL/PIC image.</summary>
[FormatMagicBytes([0x19, 0x91])]
public sealed class AutodeskCelFile : IImageFileFormat<AutodeskCelFile> {

  /// <summary>Header size in bytes.</summary>
  public const int HeaderSize = 16;

  /// <summary>Magic number: 0x9119 little-endian (bytes 0x19, 0x91).</summary>
  public const ushort Magic = 0x9119;

  /// <summary>Size of the optional VGA palette appended to the file (256 entries x 3 bytes).</summary>
  public const int PaletteSize = 768;

  /// <summary>Number of palette entries.</summary>
  public const int PaletteEntryCount = 256;

  static string IImageFileFormat<AutodeskCelFile>.PrimaryExtension => ".cel";
  static string[] IImageFileFormat<AutodeskCelFile>.FileExtensions => [".cel"];
  static FormatCapability IImageFileFormat<AutodeskCelFile>.Capabilities => FormatCapability.IndexedOnly;
  static AutodeskCelFile IImageFileFormat<AutodeskCelFile>.FromFile(FileInfo file) => AutodeskCelReader.FromFile(file);
  static AutodeskCelFile IImageFileFormat<AutodeskCelFile>.FromBytes(byte[] data) => AutodeskCelReader.FromBytes(data);
  static AutodeskCelFile IImageFileFormat<AutodeskCelFile>.FromStream(Stream stream) => AutodeskCelReader.FromStream(stream);
  static RawImage IImageFileFormat<AutodeskCelFile>.ToRawImage(AutodeskCelFile file) => ToRawImage(file);
  static AutodeskCelFile IImageFileFormat<AutodeskCelFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<AutodeskCelFile>.ToBytes(AutodeskCelFile file) => AutodeskCelWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Horizontal offset.</summary>
  public int XOffset { get; init; }

  /// <summary>Vertical offset.</summary>
  public int YOffset { get; init; }

  /// <summary>Bits per pixel (typically 8).</summary>
  public int BitsPerPixel { get; init; } = 8;

  /// <summary>Compression type (0 = none).</summary>
  public byte Compression { get; init; }

  /// <summary>Raw indexed pixel data (width * height bytes for 8bpp).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Palette as RGB triplets (3 bytes per entry, 8-bit values 0-255). 256 entries = 768 bytes.</summary>
  public byte[] Palette { get; init; } = _BuildDefaultGrayscalePalette();

  public static RawImage ToRawImage(AutodeskCelFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = file.Palette[..],
      PaletteCount = file.Palette.Length / 3,
    };
  }

  public static AutodeskCelFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Expected {PixelFormat.Indexed8} but got {image.Format}.", nameof(image));
    if (image.PaletteCount > PaletteEntryCount)
      throw new ArgumentException($"Palette has {image.PaletteCount} entries but maximum is {PaletteEntryCount}.", nameof(image));

    var palette = _BuildDefaultGrayscalePalette();
    if (image.Palette is { } p) {
      var copyLength = Math.Min(p.Length, PaletteSize);
      p.AsSpan(0, copyLength).CopyTo(palette);
    }

    return new() {
      Width = image.Width,
      Height = image.Height,
      BitsPerPixel = 8,
      PixelData = image.PixelData[..],
      Palette = palette,
    };
  }

  private static byte[] _BuildDefaultGrayscalePalette() {
    var palette = new byte[PaletteSize];
    for (var i = 0; i < PaletteEntryCount; ++i) {
      var value = (byte)i;
      palette[i * 3] = value;
      palette[i * 3 + 1] = value;
      palette[i * 3 + 2] = value;
    }
    return palette;
  }
}
