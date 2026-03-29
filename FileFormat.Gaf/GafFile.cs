using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Gaf;

/// <summary>In-memory representation of a GAF (Total Annihilation) texture archive image.</summary>
[FormatMagicBytes([0x00, 0x01, 0x01, 0x00])]
public sealed class GafFile : IImageFileFormat<GafFile> {

  static string IImageFileFormat<GafFile>.PrimaryExtension => ".gaf";
  static string[] IImageFileFormat<GafFile>.FileExtensions => [".gaf"];
  static FormatCapability IImageFileFormat<GafFile>.Capabilities => FormatCapability.IndexedOnly;
  static GafFile IImageFileFormat<GafFile>.FromFile(FileInfo file) => GafReader.FromFile(file);
  static GafFile IImageFileFormat<GafFile>.FromBytes(byte[] data) => GafReader.FromBytes(data);
  static GafFile IImageFileFormat<GafFile>.FromStream(Stream stream) => GafReader.FromStream(stream);
  static RawImage IImageFileFormat<GafFile>.ToRawImage(GafFile file) => ToRawImage(file);
  static byte[] IImageFileFormat<GafFile>.ToBytes(GafFile file) => GafWriter.ToBytes(file);

  /// <summary>Width of the first frame in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Height of the first frame in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Entry name (up to 32 ASCII characters, null-terminated).</summary>
  public string Name { get; init; } = string.Empty;

  /// <summary>Palette index treated as transparent (typically 9 in TA).</summary>
  public byte TransparencyIndex { get; init; } = 9;

  /// <summary>8-bit indexed pixel data (one byte per pixel, row-major).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Optional external palette as RGB triplets (3 bytes per entry, up to 256 entries). Null means use default grayscale.</summary>
  public byte[]? Palette { get; init; }

  /// <summary>X offset of the frame hotspot.</summary>
  public short XOffset { get; init; }

  /// <summary>Y offset of the frame hotspot.</summary>
  public short YOffset { get; init; }

  /// <summary>Converts a GAF file to a <see cref="RawImage"/>. Uses embedded palette or a default 256-entry grayscale palette.</summary>
  public static RawImage ToRawImage(GafFile file) {
    ArgumentNullException.ThrowIfNull(file);

    byte[] palette;
    int paletteCount;

    if (file.Palette is { Length: > 0 }) {
      palette = file.Palette[..];
      paletteCount = palette.Length / 3;
    } else {
      palette = new byte[256 * 3];
      for (var i = 0; i < 256; ++i) {
        palette[i * 3] = (byte)i;
        palette[i * 3 + 1] = (byte)i;
        palette[i * 3 + 2] = (byte)i;
      }
      paletteCount = 256;
    }

    return new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = palette,
      PaletteCount = paletteCount,
    };
  }

  /// <summary>Creates a GAF file from a <see cref="RawImage"/>. Must be Indexed8.</summary>
  public static GafFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"GAF requires Indexed8 pixel format, got {image.Format}.", nameof(image));

    return new GafFile {
      Width = image.Width,
      Height = image.Height,
      Name = "entry",
      PixelData = image.PixelData[..],
      Palette = image.Palette is { Length: > 0 } ? image.Palette[..] : null,
    };
  }
}
