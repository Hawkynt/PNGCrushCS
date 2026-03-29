using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.YJKImage;

/// <summary>In-memory representation of a YJK image (MSX2+ Screen 10/12, 256x212, YJK color encoding).</summary>
public sealed class YJKImageFile : IImageFileFormat<YJKImageFile> {

  static string IImageFileFormat<YJKImageFile>.PrimaryExtension => ".yjk";
  static string[] IImageFileFormat<YJKImageFile>.FileExtensions => [".yjk"];
  static YJKImageFile IImageFileFormat<YJKImageFile>.FromFile(FileInfo file) => YJKImageReader.FromFile(file);
  static YJKImageFile IImageFileFormat<YJKImageFile>.FromBytes(byte[] data) => YJKImageReader.FromBytes(data);
  static YJKImageFile IImageFileFormat<YJKImageFile>.FromStream(Stream stream) => YJKImageReader.FromStream(stream);
  static byte[] IImageFileFormat<YJKImageFile>.ToBytes(YJKImageFile file) => YJKImageWriter.ToBytes(file);

  /// <summary>Fixed image width.</summary>
  public const int FixedWidth = 256;

  /// <summary>Fixed image height.</summary>
  public const int FixedHeight = 212;

  /// <summary>Expected file size in bytes.</summary>
  public const int ExpectedFileSize = FixedWidth * FixedHeight;

  /// <summary>Image width, always 256.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 212.</summary>
  public int Height => FixedHeight;

  /// <summary>Raw pixel data (54272 bytes, one byte per pixel in YJK encoding).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Clamps a value to the range [min, max].</summary>
  private static int _Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

  /// <summary>Sign-extends a 5-bit value to a signed integer.</summary>
  private static int _SignExtend5(int value) => value >= 16 ? value - 32 : value;

  /// <summary>Converts this YJK image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(YJKImageFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var rgb = new byte[FixedWidth * FixedHeight * 3];

    for (var y = 0; y < FixedHeight; ++y)
      for (var x = 0; x < FixedWidth; x += 4) {
        var baseOffset = y * FixedWidth + x;
        var k5 = _SignExtend5((file.PixelData[baseOffset] & 7) | ((file.PixelData[baseOffset + 1] & 3) << 3));
        var j5 = _SignExtend5((file.PixelData[baseOffset + 2] & 7) | ((file.PixelData[baseOffset + 3] & 3) << 3));

        for (var i = 0; i < 4; ++i) {
          var luma = file.PixelData[baseOffset + i] >> 3;
          var r = (byte)(_Clamp(luma + j5, 0, 31) * 8);
          var g = (byte)(_Clamp(luma + k5, 0, 31) * 8);
          var b = (byte)(_Clamp((5 * luma - 2 * j5 - k5) / 4, 0, 31) * 8);

          var offset = (baseOffset + i) * 3;
          rgb[offset] = r;
          rgb[offset + 1] = g;
          rgb[offset + 2] = b;
        }
      }

    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Not supported. YJK encoding is complex and lossy.</summary>
  public static YJKImageFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to YJKImageFile is not supported due to complex YJK encoding constraints.");
  }
}
