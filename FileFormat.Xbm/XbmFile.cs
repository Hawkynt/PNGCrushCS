using System;
using FileFormat.Core;

namespace FileFormat.Xbm;

/// <summary>In-memory representation of an XBM (X BitMap) image.</summary>
public readonly record struct XbmFile : IImageFormatReader<XbmFile>, IImageToRawImage<XbmFile>, IImageFromRawImage<XbmFile>, IImageFormatWriter<XbmFile> {

  static string IImageFormatMetadata<XbmFile>.PrimaryExtension => ".xbm";
  static string[] IImageFormatMetadata<XbmFile>.FileExtensions => [".xbm"];
  static XbmFile IImageFormatReader<XbmFile>.FromSpan(ReadOnlySpan<byte> data) => XbmReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<XbmFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<XbmFile>.ToBytes(XbmFile file) => XbmWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public string Name { get; init; }
  public int? HotspotX { get; init; }
  public int? HotspotY { get; init; }

  /// <summary>1bpp packed pixel data, LSB-first within each byte, ceil(width/8) bytes per row.</summary>
  public byte[] PixelData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  private static byte _ReverseBits(byte b) {
    var result = 0;
    for (var i = 0; i < 8; ++i) {
      result = (result << 1) | (b & 1);
      b >>= 1;
    }
    return (byte)result;
  }

  public static RawImage ToRawImage(XbmFile file) {
    var msb = new byte[file.PixelData.Length];
    for (var i = 0; i < file.PixelData.Length; ++i)
      msb[i] = _ReverseBits(file.PixelData[i]);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed1,
      PixelData = msb,
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  public static XbmFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed1.", nameof(image));

    var lsb = new byte[image.PixelData.Length];
    for (var i = 0; i < image.PixelData.Length; ++i)
      lsb[i] = _ReverseBits(image.PixelData[i]);

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = lsb,
    };
  }
}
