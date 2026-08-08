using System;
using FileFormat.Core;

namespace FileFormat.HayesJtfax;

/// <summary>In-memory representation of a Hayes JT Fax image.</summary>
public readonly record struct HayesJtfaxFile : IImageFormatReader<HayesJtfaxFile>, IImageToRawImage<HayesJtfaxFile>, IImageFromRawImage<HayesJtfaxFile>, IImageFormatWriter<HayesJtfaxFile> {

  static string IImageFormatMetadata<HayesJtfaxFile>.PrimaryExtension => ".jtf";
  static string[] IImageFormatMetadata<HayesJtfaxFile>.FileExtensions => [".jtf"];
  static HayesJtfaxFile IImageFormatReader<HayesJtfaxFile>.FromSpan(ReadOnlySpan<byte> data) => HayesJtfaxReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<HayesJtfaxFile>.VideoModes => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])];
  static byte[] IImageFormatWriter<HayesJtfaxFile>.ToBytes(HayesJtfaxFile file) => HayesJtfaxWriter.ToBytes(file);

  /// <summary>Magic bytes: "JT" (0x4A 0x54).</summary>
  internal static readonly byte[] Magic = [0x4A, 0x54];

  /// <summary>Header size: magic(2) + version(2) + width(2) + height(2) + reserved(2) = 10 bytes.</summary>
  internal const int HeaderSize = 10;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>The largest either dimension goes, which is what the header's words hold.</summary>
  public const int MaxDimension = 65535;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>File version number.</summary>
  public ushort Version { get; init; }

  /// <summary>1bpp pixel data, MSB first, rows padded to byte boundary.</summary>
  public byte[] PixelData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Converts this JTF image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(HayesJtfaxFile file) {

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

  /// <summary>Creates a JT Fax page from a platform-independent <see cref="RawImage"/>.</summary>
  /// <remarks>
  /// A fax page is ink on paper: the header carries the size, so any size fits, and everything
  /// darker than mid grey becomes a set bit — which is what the decoder here draws as black.
  /// </remarks>
  public static HayesJtfaxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // The header states the size as words; a bigger page would be written with its dimensions
    // wrapped and read back as a different one rather than as a broken one.
    if (image.Width is < 1 or > MaxDimension || image.Height is < 1 or > MaxDimension)
      throw new ArgumentException(
        $"A JT Fax page is at most {MaxDimension}x{MaxDimension}; got {image.Width}x{image.Height}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      Version = 1,
      PixelData = MonochromePage.Encode(image, image.Width, image.Height, inkIsWhite: false),
    };
  }

}
