using System;
using FileFormat.Core;

namespace FileFormat.SunIcon;

/// <summary>In-memory representation of a Sun Icon (.icon) image.</summary>
/// <remarks>
/// The signature is the whole of "/* Format_version=" rather than the "/* " it used to be. Three
/// bytes of comment opener is not a signature at all — it matched every C-style comment, and so this
/// format claimed XPM files (which open "/* XPM */"), PICON and UIL, then failed to read them because
/// none of them carries the Width field it wants.
/// </remarks>
[FormatMagicBytes([
  0x2F, 0x2A, 0x20, 0x46, 0x6F, 0x72, 0x6D, 0x61, 0x74, 0x5F, 0x76, 0x65, 0x72, 0x73, 0x69, 0x6F,
  0x6E, 0x3D,
])]
[FormatMimeType("image/x-sun-icon")]
public readonly record struct SunIconFile : IImageFormatReader<SunIconFile>, IImageToRawImage<SunIconFile>, IImageFromRawImage<SunIconFile>, IImageFormatWriter<SunIconFile> {

  static string IImageFormatMetadata<SunIconFile>.PrimaryExtension => ".icon";
  static string[] IImageFormatMetadata<SunIconFile>.FileExtensions => [".icon"];
  static SunIconFile IImageFormatReader<SunIconFile>.FromSpan(ReadOnlySpan<byte> data) => SunIconReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<SunIconFile>.VideoModes => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])];
  static byte[] IImageFormatWriter<SunIconFile>.ToBytes(SunIconFile file) => SunIconWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>1bpp packed pixel data, MSB-first within each byte, ceil(width/8) bytes per row.</summary>
  public byte[] PixelData { get; init; }

  // 1 = foreground (black), 0 = background (white)
  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  public static RawImage ToRawImage(SunIconFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed1,
    PixelData = file.PixelData[..],
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  public static SunIconFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Indexed1);

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
