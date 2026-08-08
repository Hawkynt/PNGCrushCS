using System;
using FileFormat.Core;

namespace FileFormat.Ps2Txc;

/// <summary>In-memory representation of a PS2 TXC texture image.</summary>
public readonly record struct Ps2TxcFile : IImageFormatReader<Ps2TxcFile>, IImageToRawImage<Ps2TxcFile>, IImageFromRawImage<Ps2TxcFile>, IImageFormatWriter<Ps2TxcFile> {

  static string IImageFormatMetadata<Ps2TxcFile>.PrimaryExtension => ".txc";
  static string[] IImageFormatMetadata<Ps2TxcFile>.FileExtensions => [".txc"];
  static Ps2TxcFile IImageFormatReader<Ps2TxcFile>.FromSpan(ReadOnlySpan<byte> data) => Ps2TxcReader.FromSpan(data);
  static byte[] IImageFormatWriter<Ps2TxcFile>.ToBytes(Ps2TxcFile file) => Ps2TxcWriter.ToBytes(file);

  /// <summary>Header size: width(2) + height(2) + bpp(2) + flags(2) = 8 bytes.</summary>
  internal const int HeaderSize = 8;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bits per pixel (16, 24, or 32).</summary>
  public int BitsPerPixel { get; init; }

  /// <summary>Format flags.</summary>
  public ushort Flags { get; init; }

  /// <summary>Raw pixel data.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this TXC image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(Ps2TxcFile file) {

    var pixelCount = file.Width * file.Height;
    var rgb = new byte[pixelCount * 3];

    switch (file.BitsPerPixel) {
      case 32:
        for (var i = 0; i < pixelCount; ++i) {
          var srcOffset = i * 4;
          var dstOffset = i * 3;
          rgb[dstOffset] = file.PixelData[srcOffset];
          rgb[dstOffset + 1] = file.PixelData[srcOffset + 1];
          rgb[dstOffset + 2] = file.PixelData[srcOffset + 2];
        }

        break;
      case 24:
        file.PixelData.AsSpan(0, pixelCount * 3).CopyTo(rgb);
        break;
      case 16:
        for (var i = 0; i < pixelCount; ++i) {
          var srcOffset = i * 2;
          var value = (ushort)(file.PixelData[srcOffset] | (file.PixelData[srcOffset + 1] << 8));
          var dstOffset = i * 3;
          rgb[dstOffset] = (byte)(((value >> 11) & 0x1F) * 255 / 31);
          rgb[dstOffset + 1] = (byte)(((value >> 5) & 0x3F) * 255 / 63);
          rgb[dstOffset + 2] = (byte)((value & 0x1F) * 255 / 31);
        }

        break;
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Creates a TXC texture from a platform-independent <see cref="RawImage"/>.</summary>
  /// <remarks>
  /// The header carries the size and the depth, so any size fits. Twenty-four bits is the depth
  /// chosen because it is the one the decoder takes byte for byte — sixteen would quantise the
  /// channels to 5-6-5 and thirty-two would spend a byte a pixel on an alpha channel nothing here
  /// reads back.
  /// </remarks>
  public static Ps2TxcFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var source = image.EnsureFormat(PixelFormat.Rgb24);

    return new() {
      Width = source.Width,
      Height = source.Height,
      BitsPerPixel = 24,
      Flags = 0,
      PixelData = source.PixelData[..(source.Width * source.Height * 3)],
    };
  }

}
