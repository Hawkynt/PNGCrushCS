using System;
using FileFormat.Core;

namespace FileFormat.LaserData;

public enum LaserDataCompression : byte {
  Uncompressed = 0,
  Group3 = 2,
  Group4 = 5,
}

/// <summary>In-memory representation of a LaserData document image (.lda).</summary>
public readonly record struct LaserDataFile : IImageFormatReader<LaserDataFile>, IImageToRawImage<LaserDataFile>, IImageFromRawImage<LaserDataFile>, IImageFormatWriter<LaserDataFile> {

  static string IImageFormatMetadata<LaserDataFile>.PrimaryExtension => ".lda";
  static string[] IImageFormatMetadata<LaserDataFile>.FileExtensions => [".lda"];
  static LaserDataFile IImageFormatReader<LaserDataFile>.FromSpan(ReadOnlySpan<byte> data) => LaserDataReader.FromSpan(data);
  static byte[] IImageFormatWriter<LaserDataFile>.ToBytes(LaserDataFile file) => LaserDataWriter.ToBytes(file);

  internal const ushort Magic = 0xDCDC;
  internal const int HeaderSize = 512;
  public const int MinFileSize = HeaderSize;

  public int Width { get; init; }
  public int Height { get; init; }
  public LaserDataCompression Compression { get; init; }
  public bool IsMostSignificantBitFirst { get; init; }
  public ushort HorizontalResolution { get; init; }
  public ushort VerticalResolution { get; init; }

  /// <summary>1bpp pixel data, most significant bit leftmost, rows padded to a byte boundary, a set bit being black.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(LaserDataFile file) {
    var bytesPerRow = (file.Width + 7) / 8;
    var rgb = new byte[file.Width * file.Height * 3];
    for (var y = 0; y < file.Height; ++y)
      for (var x = 0; x < file.Width; ++x) {
        var bit = (file.PixelData[y * bytesPerRow + (x >> 3)] >> (7 - (x & 7))) & 1;
        var offset = (y * file.Width + x) * 3;
        var color = bit == 1 ? (byte)0 : (byte)255;
        rgb[offset] = color;
        rgb[offset + 1] = color;
        rgb[offset + 2] = color;
      }

    return new() { Width = file.Width, Height = file.Height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>Creates a Group-4-compressed LaserData page from any source image.</summary>
  public static LaserDataFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width is < 1 or > ushort.MaxValue || image.Height is < 1 or > ushort.MaxValue)
      throw new ArgumentException($"LaserData dimensions must fit 16-bit fields; got {image.Width}x{image.Height}.", nameof(image));

    var indices = BilevelRows.Threshold(image, setWhenDark: true);
    return new() {
      Width = image.Width,
      Height = image.Height,
      Compression = LaserDataCompression.Group4,
      IsMostSignificantBitFirst = true,
      HorizontalResolution = 300,
      VerticalResolution = 300,
      PixelData = BilevelRows.Pack(indices, image.Width, image.Height),
    };
  }
}
