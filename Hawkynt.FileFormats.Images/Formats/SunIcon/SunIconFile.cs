using System;
using FileFormat.Core;

namespace FileFormat.SunIcon;

/// <summary>In-memory representation of a Sun Icon (.icon) image.</summary>
/// <remarks>
/// The signature is the whole of <c>/* Format_version=</c> and not the <c>/*</c> it used to be.
/// Two characters of comment opener and a space is not a signature — it is how every C comment
/// begins, and three of the formats here are C source. The detector answered "Sun Icon" for any
/// XPM, PICON or UIL file put to it, and the reader behind that answer then refused the file for
/// want of the <c>Width</c> field it wanted.
/// </remarks>
[FormatMagicBytes([0x2F, 0x2A, 0x20, 0x46, 0x6F, 0x72, 0x6D, 0x61, 0x74, 0x5F, 0x76, 0x65, 0x72, 0x73, 0x69, 0x6F, 0x6E, 0x3D])]
[FormatMimeType("image/x-sun-icon")]
public readonly record struct SunIconFile : IImageFormatReader<SunIconFile>, IImageToRawImage<SunIconFile>, IImageFromRawImage<SunIconFile>, IImageFormatWriter<SunIconFile> {

  static string IImageFormatMetadata<SunIconFile>.PrimaryExtension => ".icon";
  /// <summary><c>.pr</c> is the name the SunView pixrect tools wrote them under.</summary>
  static string[] IImageFormatMetadata<SunIconFile>.FileExtensions => [".icon", ".pr"];
  static SunIconFile IImageFormatReader<SunIconFile>.FromSpan(ReadOnlySpan<byte> data) => SunIconReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<SunIconFile>.VideoModes => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])];
  static byte[] IImageFormatWriter<SunIconFile>.ToBytes(SunIconFile file) => SunIconWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>1bpp packed pixel data, MSB-first within each byte, ceil(width/8) bytes per row.</summary>
  public byte[] PixelData { get; init; }

  // 1 = foreground (black), 0 = background (white)
  // Paper first: a Sun icon is a stencil on the page, so a clear bit is the page.
  private static readonly byte[] _BlackWhitePalette = [255, 255, 255, 0, 0, 0];

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
