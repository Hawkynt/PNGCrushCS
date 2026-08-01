using System;
using FileFormat.Core;

namespace FileFormat.Xbm;

/// <summary>In-memory representation of an XBM (X BitMap) image.</summary>
[FormatMimeType("image/x-xbitmap", "image/x-xbm")]
public readonly record struct XbmFile : IImageFormatReader<XbmFile>, IImageToRawImage<XbmFile>, IImageFromRawImage<XbmFile>, IImageFormatWriter<XbmFile> {

  static string IImageFormatMetadata<XbmFile>.PrimaryExtension => ".xbm";
  static string[] IImageFormatMetadata<XbmFile>.FileExtensions => [".xbm"];
  static XbmFile IImageFormatReader<XbmFile>.FromSpan(ReadOnlySpan<byte> data) => XbmReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<XbmFile>.VideoModes => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])];
  static byte[] IImageFormatWriter<XbmFile>.ToBytes(XbmFile file) => XbmWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public string Name { get; init; }
  public int? HotspotX { get; init; }
  public int? HotspotY { get; init; }

  /// <summary>1bpp packed pixel data, LSB-first within each byte, ceil(width/8) bytes per row.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>
  /// Index 0 is the background and index 1 the ink, so a set bit draws black.
  /// </summary>
  /// <remarks>
  /// The two were the other way round, which turned every image of this format into its own negative:
  /// the bits a writer sets to mark ink were being painted white and the blank background black.
  /// Nothing that only checked an image's size would notice, since a negative is exactly as big as
  /// the picture it inverts.
  /// </remarks>
  private static readonly byte[] _BlackWhitePalette = [255, 255, 255, 0, 0, 0];

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
    image = image.EnsureFormat(PixelFormat.Indexed1);

    var lsb = new byte[image.PixelData.Length];
    for (var i = 0; i < image.PixelData.Length; ++i)
      lsb[i] = _ReverseBits(image.PixelData[i]);

    return new() {
      Width = image.Width,
      Height = image.Height,
      Name = "image",
      PixelData = lsb,
    };
  }
}
