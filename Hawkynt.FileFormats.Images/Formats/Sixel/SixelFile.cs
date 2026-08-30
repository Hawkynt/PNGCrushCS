using System;
using FileFormat.Core;

namespace FileFormat.Sixel;

/// <summary>In-memory representation of a SIXEL (DEC terminal graphics) image.</summary>
[FormatMimeType("image/x-sixel")]
public readonly record struct SixelFile : IImageFormatReader<SixelFile>, IImageToRawImage<SixelFile>, IImageFromRawImage<SixelFile>, IImageFormatWriter<SixelFile> {

  static string IImageFormatMetadata<SixelFile>.PrimaryExtension => ".six";
  static string[] IImageFormatMetadata<SixelFile>.FileExtensions => [".six", ".sixel"];
  static SixelFile IImageFormatReader<SixelFile>.FromSpan(ReadOnlySpan<byte> data) => SixelReader.FromSpan(data);
  static byte[] IImageFormatWriter<SixelFile>.ToBytes(SixelFile file) => SixelWriter.ToBytes(file);

  static bool? IImageFormatMetadata<SixelFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    var offset = header.Length > 0 && header[0] == 0x90
      ? 1
      : header.Length >= 2 && header[0] == 0x1B && header[1] == (byte)'P'
        ? 2
        : -1;

    if (offset < 0) {
      if (header.Length == 1 && header[0] == 0x1B)
        return null;
      return false;
    }

    // ESC P identifies any DCS. Only the final byte 'q', after decimal parameters and separators,
    // says that this particular DCS carries SIXEL graphics.
    for (var i = offset; i < header.Length; ++i) {
      var b = header[i];
      if (b == (byte)'q')
        return true;
      if (b != (byte)';' && b is < (byte)'0' or > (byte)'9')
        return false;
    }

    return null;
  }

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }
  public byte[]? Palette { get; init; }
  public int PaletteColorCount { get; init; }
  public int AspectRatio { get; init; }
  public int BackgroundMode { get; init; }

  public static RawImage ToRawImage(SixelFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = file.Palette is { } p ? p[..] : null,
      PaletteCount = file.PaletteColorCount,
    };
  }

  public static SixelFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Indexed8);

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
      Palette = image.Palette is { } p ? p[..] : null,
      PaletteColorCount = image.PaletteCount,
      AspectRatio = 0,
      // SIXEL colours are emitted as separate overprinted planes. P2=1 is what makes zero bits in a
      // later plane leave colours painted by an earlier plane alone.
      BackgroundMode = 1,
    };
  }
}
