using System;
using FileFormat.Core;

namespace FileFormat.NokiaOperatorLogo;

/// <summary>In-memory representation of a Nokia Operator Logo (NOL) image.</summary>
public readonly record struct NokiaOperatorLogoFile : IImageFormatReader<NokiaOperatorLogoFile>, IImageToRawImage<NokiaOperatorLogoFile>, IImageFromRawImage<NokiaOperatorLogoFile>, IImageFormatWriter<NokiaOperatorLogoFile> {

  /// <summary>Magic bytes: "NOL" (0x4E 0x4F 0x4C).</summary>
  internal static readonly byte[] Magic = [0x4E, 0x4F, 0x4C];

  /// <summary>Header size in bytes: magic(3) + null(1) + unknown(2) + MCC(2) + MNC(1) + pad(1) + width(1) + pad(1) + height(1) + pad(1) + unknown(6) = 20.</summary>
  internal const int HeaderSize = 20;

  /// <summary>Minimum valid file size (header only, zero-dimension images are rejected anyway).</summary>
  public const int MinFileSize = HeaderSize;

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  static string IImageFormatMetadata<NokiaOperatorLogoFile>.PrimaryExtension => ".nol";
  static string[] IImageFormatMetadata<NokiaOperatorLogoFile>.FileExtensions => [".nol"];
  static NokiaOperatorLogoFile IImageFormatReader<NokiaOperatorLogoFile>.FromSpan(ReadOnlySpan<byte> data) => NokiaOperatorLogoReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<NokiaOperatorLogoFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<NokiaOperatorLogoFile>.ToBytes(NokiaOperatorLogoFile file) => NokiaOperatorLogoWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Mobile Country Code stored in the header.</summary>
  public int Mcc { get; init; }

  /// <summary>Mobile Network Code stored in the header.</summary>
  public int Mnc { get; init; }

  /// <summary>1bpp pixel data, MSB-first, rows padded to byte boundary.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this NOL image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(NokiaOperatorLogoFile file) {

    var bytesPerRow = (file.Width + 7) / 8;
    var rgb = new byte[file.Width * file.Height * 3];

    for (var y = 0; y < file.Height; ++y)
      for (var x = 0; x < file.Width; ++x) {
        var byteIndex = y * bytesPerRow + x / 8;
        var bitIndex = 7 - (x % 8);
        var bit = (file.PixelData[byteIndex] >> bitIndex) & 1;
        var offset = (y * file.Width + x) * 3;
        var color = bit == 1 ? (byte)0 : (byte)255;
        rgb[offset] = color;
        rgb[offset + 1] = color;
        rgb[offset + 2] = color;
      }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Creates a <see cref="NokiaOperatorLogoFile"/> from a <see cref="RawImage"/>.</summary>
  public static NokiaOperatorLogoFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException($"Expected Rgb24 but got {image.Format}.", nameof(image));
    if (image.Width is < 1 or > 255 || image.Height is < 1 or > 255)
      throw new ArgumentException($"Dimensions must be 1-255, got {image.Width}x{image.Height}.", nameof(image));

    var w = image.Width;
    var h = image.Height;
    var bytesPerRow = (w + 7) / 8;
    var pixelData = new byte[bytesPerRow * h];

    for (var y = 0; y < h; ++y)
      for (var x = 0; x < w; ++x) {
        var srcOffset = (y * w + x) * 3;
        var luma = (image.PixelData[srcOffset] + image.PixelData[srcOffset + 1] + image.PixelData[srcOffset + 2]) / 3;
        if (luma < 128) {
          var byteIndex = y * bytesPerRow + x / 8;
          var bitIndex = 7 - (x % 8);
          pixelData[byteIndex] |= (byte)(1 << bitIndex);
        }
      }

    return new() {
      Width = w,
      Height = h,
      PixelData = pixelData,
    };
  }
}
