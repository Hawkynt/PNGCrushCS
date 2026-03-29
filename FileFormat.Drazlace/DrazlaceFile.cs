using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Drazlace;

/// <summary>In-memory representation of a Drazlace interlace multicolor image (.dlp/.drl).</summary>
public sealed class DrazlaceFile : IImageFileFormat<DrazlaceFile> {

  static string IImageFileFormat<DrazlaceFile>.PrimaryExtension => ".dlp";
  static string[] IImageFileFormat<DrazlaceFile>.FileExtensions => [".dlp", ".drl"];
  static DrazlaceFile IImageFileFormat<DrazlaceFile>.FromFile(FileInfo file) => DrazlaceReader.FromFile(file);
  static DrazlaceFile IImageFileFormat<DrazlaceFile>.FromBytes(byte[] data) => DrazlaceReader.FromBytes(data);
  static DrazlaceFile IImageFileFormat<DrazlaceFile>.FromStream(Stream stream) => DrazlaceReader.FromStream(stream);
  static byte[] IImageFileFormat<DrazlaceFile>.ToBytes(DrazlaceFile file) => DrazlaceWriter.ToBytes(file);

  /// <summary>The fixed width of the image in pixels.</summary>
  public const int FixedWidth = 160;

  /// <summary>The fixed height of the image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>Total uncompressed payload size (bitmap1:8000 + screen1:1000 + color:1000 + bg:1 + bitmap2:8000 + screen2:1000 = 19001).</summary>
  internal const int UncompressedPayloadSize = 19001;

  /// <summary>Size of the bitmap data section in bytes.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Size of the screen RAM section in bytes.</summary>
  internal const int ScreenRamSize = 1000;

  /// <summary>Size of the color RAM section in bytes.</summary>
  internal const int ColorRamSize = 1000;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>The escape byte used by Drazlace RLE compression.</summary>
  internal const byte RleEscapeByte = 0x00;

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

  /// <summary>Multicolor bitmap data for frame 1 (8000 bytes).</summary>
  public byte[] BitmapData1 { get; init; } = [];

  /// <summary>Screen RAM for frame 1 (1000 bytes).</summary>
  public byte[] ScreenRam1 { get; init; } = [];

  /// <summary>Color RAM shared between both frames (1000 bytes).</summary>
  public byte[] ColorRam { get; init; } = [];

  /// <summary>Background color index (0-15).</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>Multicolor bitmap data for frame 2 (8000 bytes).</summary>
  public byte[] BitmapData2 { get; init; } = [];

  /// <summary>Screen RAM for frame 2 (1000 bytes).</summary>
  public byte[] ScreenRam2 { get; init; } = [];

  /// <summary>Converts this Drazlace image to a platform-independent <see cref="RawImage"/> in Rgb24 format by decoding both frames and blending.</summary>
  public static RawImage ToRawImage(DrazlaceFile file) {
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
        var pixelInByte = x % 4;

        var color1 = _DecodeMulticolorPixel(file.BitmapData1, file.ScreenRam1, file.ColorRam, file.BackgroundColor, cellIndex, byteInCell, pixelInByte);
        var color2 = _DecodeMulticolorPixel(file.BitmapData2, file.ScreenRam2, file.ColorRam, file.BackgroundColor, cellIndex, byteInCell, pixelInByte);

        var r = ((color1 >> 16) & 0xFF) + ((color2 >> 16) & 0xFF);
        var g = ((color1 >> 8) & 0xFF) + ((color2 >> 8) & 0xFF);
        var b = (color1 & 0xFF) + (color2 & 0xFF);

        var offset = (y * width + x) * 3;
        rgb[offset] = (byte)(r / 2);
        rgb[offset + 1] = (byte)(g / 2);
        rgb[offset + 2] = (byte)(b / 2);
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Decodes a single multicolor pixel from the given bitmap/screen/color data.</summary>
  private static int _DecodeMulticolorPixel(byte[] bitmapData, byte[] screenRam, byte[] colorRam, byte backgroundColor, int cellIndex, int byteInCell, int pixelInByte) {
    var bitmapByte = bitmapData[cellIndex * 8 + byteInCell];
    var bitValue = (bitmapByte >> ((3 - pixelInByte) * 2)) & 0x03;

    var colorIndex = bitValue switch {
      0 => backgroundColor & 0x0F,
      1 => (screenRam[cellIndex] >> 4) & 0x0F,
      2 => screenRam[cellIndex] & 0x0F,
      3 => colorRam[cellIndex] & 0x0F,
      _ => 0
    };

    return _C64Palette[colorIndex];
  }

  /// <summary>Not supported. Drazlace images have complex cell-based color constraints.</summary>
  public static DrazlaceFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to DrazlaceFile is not supported due to complex cell-based color constraints.");
  }

  /// <summary>RLE-decompresses data using 0x00 escape byte encoding: 0x00, count, value.</summary>
  internal static byte[] RleDecode(byte[] compressed) {
    using var output = new MemoryStream();
    var i = 0;
    while (i < compressed.Length) {
      if (compressed[i] == RleEscapeByte) {
        if (i + 2 >= compressed.Length)
          break;

        var count = compressed[i + 1];
        var value = compressed[i + 2];
        for (var j = 0; j < count; ++j)
          output.WriteByte(value);

        i += 3;
      } else {
        output.WriteByte(compressed[i]);
        ++i;
      }
    }

    return output.ToArray();
  }

  /// <summary>RLE-compresses data using 0x00 escape byte encoding: 0x00, count, value.</summary>
  internal static byte[] RleEncode(byte[] data) {
    using var output = new MemoryStream();
    var i = 0;
    while (i < data.Length) {
      var value = data[i];
      var count = 1;
      while (i + count < data.Length && data[i + count] == value && count < 255)
        ++count;

      if (count > 2 || value == RleEscapeByte) {
        output.WriteByte(RleEscapeByte);
        output.WriteByte((byte)count);
        output.WriteByte(value);
      } else {
        for (var j = 0; j < count; ++j)
          output.WriteByte(value);
      }

      i += count;
    }

    return output.ToArray();
  }
}
