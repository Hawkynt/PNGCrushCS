using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Xbm;

/// <summary>In-memory representation of an XBM (X BitMap) image.</summary>
public sealed class XbmFile : IImageFileFormat<XbmFile> {

  static string IImageFileFormat<XbmFile>.PrimaryExtension => ".xbm";
  static string[] IImageFileFormat<XbmFile>.FileExtensions => [".xbm"];
  static FormatCapability IImageFileFormat<XbmFile>.Capabilities => FormatCapability.MonochromeOnly;
  static XbmFile IImageFileFormat<XbmFile>.FromFile(FileInfo file) => XbmReader.FromFile(file);
  static XbmFile IImageFileFormat<XbmFile>.FromBytes(byte[] data) => XbmReader.FromBytes(data);
  static XbmFile IImageFileFormat<XbmFile>.FromStream(Stream stream) => XbmReader.FromStream(stream);
  static RawImage IImageFileFormat<XbmFile>.ToRawImage(XbmFile file) => file.ToRawImage();
  static byte[] IImageFileFormat<XbmFile>.ToBytes(XbmFile file) => XbmWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public string Name { get; init; } = "image";
  public int? HotspotX { get; init; }
  public int? HotspotY { get; init; }

  /// <summary>1bpp packed pixel data, LSB-first within each byte, ceil(width/8) bytes per row.</summary>
  public byte[] PixelData { get; init; } = [];

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  private static byte _ReverseBits(byte b) {
    var result = 0;
    for (var i = 0; i < 8; ++i) {
      result = (result << 1) | (b & 1);
      b >>= 1;
    }
    return (byte)result;
  }

  public RawImage ToRawImage() {
    var msb = new byte[this.PixelData.Length];
    for (var i = 0; i < this.PixelData.Length; ++i)
      msb[i] = _ReverseBits(this.PixelData[i]);

    return new() {
      Width = this.Width,
      Height = this.Height,
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
