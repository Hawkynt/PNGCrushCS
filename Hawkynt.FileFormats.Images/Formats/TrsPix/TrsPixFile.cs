using System;
using FileFormat.Core;

namespace FileFormat.TrsPix;

/// <summary>
/// TRS-80 Color Computer PIX image: a 5-byte header ("PIX\0" magic + 1-byte HSCREEN mode) wrapping
/// a raw bitplane dump. The canonical modes correspond to the CoCo's HSCREEN command:
/// <list type="bullet">
///   <item><description>0 = 320x192 monochrome (1bpp)</description></item>
///   <item><description>1 = 320x192 4-colour (2bpp)</description></item>
///   <item><description>2 = 640x192 monochrome (1bpp)</description></item>
///   <item><description>3 = 640x192 4-colour (2bpp)</description></item>
/// </list>
/// </summary>
[FormatMagicBytes([0x50, 0x49, 0x58, 0x00])]
public readonly record struct TrsPixFile : IImageFormatReader<TrsPixFile>, IImageFormatWriter<TrsPixFile>, IImageToRawImage<TrsPixFile>, IImageFromRawImage<TrsPixFile> {

  static string IImageFormatMetadata<TrsPixFile>.PrimaryExtension => ".pix";
  static string[] IImageFormatMetadata<TrsPixFile>.FileExtensions => [".pix"];
  static TrsPixFile IImageFormatReader<TrsPixFile>.FromSpan(ReadOnlySpan<byte> data) => TrsPixReader.FromSpan(data);
  static byte[] IImageFormatWriter<TrsPixFile>.ToBytes(TrsPixFile file) => TrsPixWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<TrsPixFile>.VideoModes => [
    new("HSCREEN 0 mono",   [(320, 192)], [2]),
    new("HSCREEN 1 4-col",  [(320, 192)], [4]),
    new("HSCREEN 2 mono",   [(640, 192)], [2]),
    new("HSCREEN 3 4-col",  [(640, 192)], [4]),
  ];

  public byte Mode { get; init; }
  public byte[] PixelData { get; init; }

  public int Width => Mode is 0 or 1 ? 320 : 640;
  public int Height => 192;
  public int BitsPerPixel => Mode is 0 or 2 ? 1 : 2;

  // CoCo HSCREEN palette: monochrome black/white for mono modes, 4 colours (black/green/yellow/blue,
  // a deliberately representative quartet from the Semigraphics palette) for 4-colour modes.
  private static readonly byte[] _MonoPalette = [0, 0, 0, 255, 255, 255];
  private static readonly byte[] _FourColorPalette = [
    0,   0,   0,
    0, 255,   0,
    255, 255, 0,
    0,   0, 255,
  ];

  public static RawImage ToRawImage(TrsPixFile file) {
    ArgumentNullException.ThrowIfNull(file.PixelData);
    var bpp = file.BitsPerPixel;
    var rowBytes = (file.Width * bpp + 7) >> 3;
    var indices = new byte[file.Width * file.Height];
    var mask = (1 << bpp) - 1;

    for (var y = 0; y < file.Height; ++y) {
      var rowOff = y * rowBytes;
      for (var x = 0; x < file.Width; ++x) {
        var bitIx = x * bpp;
        var byteIx = rowOff + (bitIx >> 3);
        var shift = 8 - bpp - (bitIx & 7);
        indices[y * file.Width + x] = (byte)((file.PixelData[byteIx] >> shift) & mask);
      }
    }

    var palette = bpp == 1 ? _MonoPalette : _FourColorPalette;
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = bpp == 1 ? PixelFormat.Indexed1 : PixelFormat.Indexed8,
      PixelData = bpp == 1 ? file.PixelData : indices,
      Palette = (byte[])palette.Clone(),
      PaletteCount = 1 << bpp,
    };
  }

  public static TrsPixFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var mode = image switch {
      { Width: 320, Height: 192, Format: PixelFormat.Indexed1 } => (byte)0,
      { Width: 320, Height: 192, Format: PixelFormat.Indexed8 } => (byte)1,
      { Width: 640, Height: 192, Format: PixelFormat.Indexed1 } => (byte)2,
      { Width: 640, Height: 192, Format: PixelFormat.Indexed8 } => (byte)3,
      _ => throw new ArgumentException($"Unsupported TRS-80 PIX geometry {image.Width}x{image.Height} {image.Format}.", nameof(image)),
    };

    var bpp = mode is 0 or 2 ? 1 : 2;
    var rowBytes = (image.Width * bpp + 7) >> 3;
    if (bpp == 1) {
      return new() { Mode = mode, PixelData = (byte[])image.PixelData.Clone() };
    }
    var packed = new byte[rowBytes * image.Height];
    for (var y = 0; y < image.Height; ++y) {
      var rowOff = y * rowBytes;
      for (var x = 0; x < image.Width; ++x) {
        var bitIx = x * bpp;
        var byteIx = rowOff + (bitIx >> 3);
        var shift = 8 - bpp - (bitIx & 7);
        packed[byteIx] |= (byte)((image.PixelData[y * image.Width + x] & 0x03) << shift);
      }
    }
    return new() { Mode = mode, PixelData = packed };
  }
}
