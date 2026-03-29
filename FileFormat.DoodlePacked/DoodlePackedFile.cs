using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.DoodlePacked;

/// <summary>In-memory representation of a Doodle Packed (RLE-compressed C64 hires) image.</summary>
public sealed class DoodlePackedFile : IImageFileFormat<DoodlePackedFile> {

  static string IImageFileFormat<DoodlePackedFile>.PrimaryExtension => ".dpk";
  static string[] IImageFileFormat<DoodlePackedFile>.FileExtensions => [".dpk"];
  static DoodlePackedFile IImageFileFormat<DoodlePackedFile>.FromFile(FileInfo file) => DoodlePackedReader.FromFile(file);
  static DoodlePackedFile IImageFileFormat<DoodlePackedFile>.FromBytes(byte[] data) => DoodlePackedReader.FromBytes(data);
  static DoodlePackedFile IImageFileFormat<DoodlePackedFile>.FromStream(Stream stream) => DoodlePackedReader.FromStream(stream);
  static byte[] IImageFileFormat<DoodlePackedFile>.ToBytes(DoodlePackedFile file) => DoodlePackedWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public const int ImageWidth = 320;

  /// <summary>Image height in pixels.</summary>
  public const int ImageHeight = 200;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the screen RAM section in bytes.</summary>
  internal const int ScreenDataSize = 1000;

  /// <summary>Total decompressed payload size (bitmap + screen).</summary>
  internal const int DecompressedSize = BitmapDataSize + ScreenDataSize;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>RLE escape byte prefix.</summary>
  internal const byte RleEscapeByte = 0xC0;

  /// <summary>Mask to extract the run count from an escape byte.</summary>
  internal const byte RleCountMask = 0x3F;

  /// <summary>The fixed C64 16-color palette as 0xRRGGBB values.</summary>
  private static readonly int[] _C64Palette = [
    0x000000, 0xFFFFFF, 0x880000, 0xAAFFEE, 0xCC44CC, 0x00CC55,
    0x0000AA, 0xEEEE77, 0xDD8855, 0x664400, 0xFF7777, 0x333333,
    0x777777, 0xAAFF66, 0x0088FF, 0xBBBBBB
  ];

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Bitmap data (8000 bytes).</summary>
  public byte[] BitmapData { get; init; } = [];

  /// <summary>Screen RAM / video matrix (1000 bytes).</summary>
  public byte[] ScreenData { get; init; } = [];

  /// <summary>Converts this Doodle Packed image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(DoodlePackedFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var rgb = new byte[ImageWidth * ImageHeight * 3];

    for (var y = 0; y < ImageHeight; ++y)
      for (var x = 0; x < ImageWidth; ++x) {
        var cellX = x / 8;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapByte = file.BitmapData[cellIndex * 8 + byteInCell];
        var bitPosition = 7 - (x % 8);
        var bitValue = (bitmapByte >> bitPosition) & 1;

        var screenByte = file.ScreenData[cellIndex];
        var colorIndex = bitValue == 1
          ? (screenByte >> 4) & 0x0F
          : screenByte & 0x0F;

        var color = _C64Palette[colorIndex];
        var offset = (y * ImageWidth + x) * 3;
        rgb[offset] = (byte)((color >> 16) & 0xFF);
        rgb[offset + 1] = (byte)((color >> 8) & 0xFF);
        rgb[offset + 2] = (byte)(color & 0xFF);
      }

    return new() {
      Width = ImageWidth,
      Height = ImageHeight,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Not supported. Doodle Packed is a read-only format.</summary>
  public static DoodlePackedFile FromRawImage(RawImage image)
    => throw new NotSupportedException("Creating Doodle Packed files from raw images is not supported.");

  /// <summary>Decompresses RLE-encoded data using the Doodle Packed scheme.</summary>
  internal static byte[] RleDecode(byte[] compressed) {
    ArgumentNullException.ThrowIfNull(compressed);

    using var output = new MemoryStream();
    var i = 0;
    while (i < compressed.Length) {
      var b = compressed[i++];
      if (b >= RleEscapeByte) {
        var count = b & RleCountMask;
        if (i >= compressed.Length)
          break;

        var value = compressed[i++];
        for (var j = 0; j < count; ++j)
          output.WriteByte(value);
      } else
        output.WriteByte(b);
    }

    return output.ToArray();
  }

  /// <summary>Compresses data using the Doodle Packed RLE scheme.</summary>
  internal static byte[] RleEncode(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    using var output = new MemoryStream();
    var i = 0;
    while (i < data.Length) {
      var b = data[i];

      // Count consecutive identical bytes
      var count = 1;
      while (i + count < data.Length && data[i + count] == b && count < RleCountMask)
        ++count;

      if (count >= 3 || b >= RleEscapeByte) {
        // Encode as RLE run
        output.WriteByte((byte)(RleEscapeByte | count));
        output.WriteByte(b);
        i += count;
      } else {
        // Literal byte
        output.WriteByte(b);
        ++i;
      }
    }

    return output.ToArray();
  }
}
