using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.RunPaint;

/// <summary>In-memory representation of a Run Paint multicolor image (.rpm).</summary>
public sealed class RunPaintFile : IImageFileFormat<RunPaintFile> {

  static string IImageFileFormat<RunPaintFile>.PrimaryExtension => ".rpm";
  static string[] IImageFileFormat<RunPaintFile>.FileExtensions => [".rpm"];
  static RunPaintFile IImageFileFormat<RunPaintFile>.FromFile(FileInfo file) => RunPaintReader.FromFile(file);
  static RunPaintFile IImageFileFormat<RunPaintFile>.FromBytes(byte[] data) => RunPaintReader.FromBytes(data);
  static RunPaintFile IImageFileFormat<RunPaintFile>.FromStream(Stream stream) => RunPaintReader.FromStream(stream);
  static byte[] IImageFileFormat<RunPaintFile>.ToBytes(RunPaintFile file) => RunPaintWriter.ToBytes(file);

  /// <summary>The fixed width of the image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of the image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>Total uncompressed payload size (8000 + 1000 + 1000 + 1).</summary>
  internal const int UncompressedPayloadSize = 10001;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the screen RAM section in bytes.</summary>
  internal const int ScreenRamSize = 1000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>The fixed C64 16-color palette as 0xRRGGBB values.</summary>
  private static readonly int[] _C64Palette = [
    0x000000, 0xFFFFFF, 0x880000, 0xAAFFEE, 0xCC44CC, 0x00CC55,
    0x0000AA, 0xEEEE77, 0xDD8855, 0x664400, 0xFF7777, 0x333333,
    0x777777, 0xAAFF66, 0x0088FF, 0xBBBBBB
  ];

  /// <summary>Image width, always 160.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Multicolor bitmap data (8000 bytes).</summary>
  public byte[] BitmapData { get; init; } = [];

  /// <summary>Screen RAM (1000 bytes).</summary>
  public byte[] ScreenRam { get; init; } = [];

  /// <summary>Color RAM (1000 bytes).</summary>
  public byte[] ColorRam { get; init; } = [];

  /// <summary>Background color index (0-15).</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>Converts this image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(RunPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    const int width = FixedWidth;
    const int height = FixedHeight;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var cellX = x / 4;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapByte = file.BitmapData[cellIndex * 8 + byteInCell];
        var pixelInByte = x % 4;
        var bitValue = (bitmapByte >> ((3 - pixelInByte) * 2)) & 0x03;

        var colorIndex = bitValue switch {
          0 => file.BackgroundColor & 0x0F,
          1 => (file.ScreenRam[cellIndex] >> 4) & 0x0F,
          2 => file.ScreenRam[cellIndex] & 0x0F,
          3 => file.ColorRam[cellIndex] & 0x0F,
          _ => 0
        };

        var color = _C64Palette[colorIndex];
        var offset = (y * width + x) * 3;
        rgb[offset] = (byte)((color >> 16) & 0xFF);
        rgb[offset + 1] = (byte)((color >> 8) & 0xFF);
        rgb[offset + 2] = (byte)(color & 0xFF);
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Not supported. Run Paint images have complex cell-based color constraints.</summary>
  public static RunPaintFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to RunPaintFile is not supported due to complex cell-based color constraints.");
  }

  /// <summary>RLE-decompresses data using 0xC0 mask encoding.</summary>
  internal static byte[] RleDecode(byte[] compressed) {
    using var output = new MemoryStream();
    var i = 0;
    while (i < compressed.Length) {
      var b = compressed[i];
      if ((b & 0xC0) == 0xC0) {
        var count = b & 0x3F;
        ++i;
        if (i >= compressed.Length)
          break;

        var value = compressed[i];
        for (var j = 0; j < count; ++j)
          output.WriteByte(value);
      } else
        output.WriteByte(b);

      ++i;
    }

    return output.ToArray();
  }

  /// <summary>RLE-compresses data using 0xC0 mask encoding.</summary>
  internal static byte[] RleEncode(byte[] data) {
    using var output = new MemoryStream();
    var i = 0;
    while (i < data.Length) {
      var value = data[i];
      var count = 1;
      while (i + count < data.Length && data[i + count] == value && count < 63)
        ++count;

      if (count > 1 || (value & 0xC0) == 0xC0) {
        output.WriteByte((byte)(0xC0 | count));
        output.WriteByte(value);
      } else
        output.WriteByte(value);

      i += count;
    }

    return output.ToArray();
  }
}
