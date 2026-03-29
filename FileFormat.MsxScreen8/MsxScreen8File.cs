using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MsxScreen8;

/// <summary>In-memory representation of an MSX2 Screen 8 image.
/// Layout: optional 7-byte BSAVE header (0xFE magic) + pixel data (54272 bytes, 8bpp GGGRRRBB).
/// 256x212, 256 colors direct color, each byte encodes G(3)-R(3)-B(2).
/// </summary>
[FormatMagicBytes([0xFE])]
public sealed class MsxScreen8File : IImageFileFormat<MsxScreen8File> {

  static string IImageFileFormat<MsxScreen8File>.PrimaryExtension => ".sc8";
  static string[] IImageFileFormat<MsxScreen8File>.FileExtensions => [".sc8"];
  static MsxScreen8File IImageFileFormat<MsxScreen8File>.FromFile(FileInfo file) => MsxScreen8Reader.FromFile(file);
  static MsxScreen8File IImageFileFormat<MsxScreen8File>.FromBytes(byte[] data) => MsxScreen8Reader.FromBytes(data);
  static MsxScreen8File IImageFileFormat<MsxScreen8File>.FromStream(Stream stream) => MsxScreen8Reader.FromStream(stream);
  static byte[] IImageFileFormat<MsxScreen8File>.ToBytes(MsxScreen8File file) => MsxScreen8Writer.ToBytes(file);

  /// <summary>Fixed width of an MSX Screen 8 image.</summary>
  public const int FixedWidth = 256;

  /// <summary>Fixed height of an MSX Screen 8 image.</summary>
  public const int FixedHeight = 212;

  /// <summary>BSAVE header magic byte.</summary>
  public const byte BsaveMagic = 0xFE;

  /// <summary>BSAVE header size in bytes.</summary>
  public const int BsaveHeaderSize = 7;

  /// <summary>Pixel data size in bytes (256x212).</summary>
  public const int PixelDataSize = 54272;

  /// <summary>Total file size with BSAVE header.</summary>
  public const int FileWithHeaderSize = BsaveHeaderSize + PixelDataSize;

  /// <summary>Image width, always 256.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 212.</summary>
  public int Height => FixedHeight;

  /// <summary>Raw pixel data (54272 bytes, each byte = GGGRRRBB).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Whether the original data had a 7-byte BSAVE header.</summary>
  public bool HasBsaveHeader { get; init; }

  /// <summary>Converts this MSX Screen 8 image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(MsxScreen8File file) {
    ArgumentNullException.ThrowIfNull(file);

    var rgb = new byte[FixedWidth * FixedHeight * 3];

    for (var i = 0; i < FixedWidth * FixedHeight; ++i) {
      if (i >= file.PixelData.Length)
        break;

      var b = file.PixelData[i];
      var g = (b >> 5) & 0x07;
      var r = (b >> 2) & 0x07;
      var bl = b & 0x03;
      var dstOffset = i * 3;
      rgb[dstOffset] = (byte)(r * 255 / 7);
      rgb[dstOffset + 1] = (byte)(g * 255 / 7);
      rgb[dstOffset + 2] = (byte)(bl * 255 / 3);
    }

    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Creates an MSX Screen 8 image from a platform-independent <see cref="RawImage"/>.</summary>
  public static MsxScreen8File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException($"RawImage must use Rgb24 format, got {image.Format}.", nameof(image));
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"RawImage must be {FixedWidth}x{FixedHeight}, got {image.Width}x{image.Height}.", nameof(image));

    var pixelData = new byte[PixelDataSize];
    for (var i = 0; i < FixedWidth * FixedHeight; ++i) {
      var srcOffset = i * 3;
      var r = image.PixelData[srcOffset];
      var g = image.PixelData[srcOffset + 1];
      var b = image.PixelData[srcOffset + 2];
      var r3 = (r * 7 + 127) / 255;
      var g3 = (g * 7 + 127) / 255;
      var b2 = (b * 3 + 127) / 255;
      pixelData[i] = (byte)((g3 << 5) | (r3 << 2) | b2);
    }

    return new() {
      PixelData = pixelData,
      HasBsaveHeader = false,
    };
  }
}
