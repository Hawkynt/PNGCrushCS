using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Cel;

/// <summary>In-memory representation of a KiSS CEL (paper doll cell) image.</summary>
[FormatMagicBytes([0x4B, 0x69, 0x53, 0x53])]
public sealed class CelFile : IImageFileFormat<CelFile> {

  static string IImageFileFormat<CelFile>.PrimaryExtension => ".cel";
  static string[] IImageFileFormat<CelFile>.FileExtensions => [".cel"];
  static CelFile IImageFileFormat<CelFile>.FromFile(FileInfo file) => CelReader.FromFile(file);
  static CelFile IImageFileFormat<CelFile>.FromBytes(byte[] data) => CelReader.FromBytes(data);
  static CelFile IImageFileFormat<CelFile>.FromStream(Stream stream) => CelReader.FromStream(stream);
  static RawImage IImageFileFormat<CelFile>.ToRawImage(CelFile file) => ToRawImage(file);
  static CelFile IImageFileFormat<CelFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<CelFile>.ToBytes(CelFile file) => CelWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bits per pixel: 4 (indexed), 8 (indexed), or 32 (RGBA).</summary>
  public int BitsPerPixel { get; init; }

  /// <summary>Horizontal offset for paper doll positioning.</summary>
  public int XOffset { get; init; }

  /// <summary>Vertical offset for paper doll positioning.</summary>
  public int YOffset { get; init; }

  /// <summary>Raw pixel data. For bpp=4: packed nybbles (low-first). For bpp=8: one byte per pixel. For bpp=32: RGBA 4 bytes per pixel.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Optional palette as RGB triplets (3 bytes per entry). Required for indexed modes (bpp=4 or bpp=8).</summary>
  public byte[]? Palette { get; init; }

  public static RawImage ToRawImage(CelFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.BitsPerPixel switch {
      32 => new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Rgba32,
        PixelData = file.PixelData[..],
      },
      8 => new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Indexed8,
        PixelData = file.PixelData[..],
        Palette = file.Palette != null ? file.Palette[..] : null,
        PaletteCount = file.Palette != null ? file.Palette.Length / 3 : 0,
      },
      4 => new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Indexed4,
        PixelData = file.PixelData[..],
        Palette = file.Palette != null ? file.Palette[..] : null,
        PaletteCount = file.Palette != null ? file.Palette.Length / 3 : 0,
      },
      _ => throw new InvalidDataException($"Unsupported bits per pixel: {file.BitsPerPixel}.")
    };
  }

  public static CelFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    return image.Format switch {
      PixelFormat.Rgba32 => new() {
        Width = image.Width,
        Height = image.Height,
        BitsPerPixel = 32,
        PixelData = image.PixelData[..],
      },
      PixelFormat.Indexed8 => new() {
        Width = image.Width,
        Height = image.Height,
        BitsPerPixel = 8,
        PixelData = image.PixelData[..],
        Palette = image.Palette != null ? image.Palette[..] : null,
      },
      PixelFormat.Indexed4 => new() {
        Width = image.Width,
        Height = image.Height,
        BitsPerPixel = 4,
        PixelData = image.PixelData[..],
        Palette = image.Palette != null ? image.Palette[..] : null,
      },
      _ => throw new ArgumentException($"Unsupported pixel format {image.Format}. Expected Rgba32, Indexed8, or Indexed4.", nameof(image))
    };
  }
}
