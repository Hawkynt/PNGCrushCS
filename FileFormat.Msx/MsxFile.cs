using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Msx;

/// <summary>In-memory representation of an MSX2 screen dump.</summary>
public sealed class MsxFile : IImageFileFormat<MsxFile> {

  static string IImageFileFormat<MsxFile>.PrimaryExtension => ".sc5";
  static string[] IImageFileFormat<MsxFile>.FileExtensions => [".sc2", ".sc5", ".sc7", ".sc8"];
  static MsxFile IImageFileFormat<MsxFile>.FromFile(FileInfo file) => MsxReader.FromFile(file);
  static MsxFile IImageFileFormat<MsxFile>.FromBytes(byte[] data) => MsxReader.FromBytes(data);
  static MsxFile IImageFileFormat<MsxFile>.FromStream(Stream stream) => MsxReader.FromStream(stream);
  static byte[] IImageFileFormat<MsxFile>.ToBytes(MsxFile file) => MsxWriter.ToBytes(file);

  /// <summary>TMS9918 fixed 16-color palette as RGB triplets.</summary>
  private static readonly byte[] _Tms9918Palette = [
    0x00, 0x00, 0x00, // 0: transparent/black
    0x00, 0x00, 0x00, // 1: black
    0x21, 0xC8, 0x42, // 2: medium green
    0x5E, 0xDC, 0x78, // 3: light green
    0x54, 0x55, 0xED, // 4: dark blue
    0x7D, 0x76, 0xFC, // 5: light blue
    0xD4, 0x52, 0x4D, // 6: dark red
    0x42, 0xEB, 0xF5, // 7: cyan
    0xFC, 0x55, 0x54, // 8: medium red
    0xFF, 0x79, 0x78, // 9: light red
    0xD4, 0xC1, 0x54, // 10: dark yellow
    0xE6, 0xCE, 0x80, // 11: light yellow
    0x21, 0xB0, 0x3B, // 12: dark green
    0xC9, 0x5B, 0xBA, // 13: magenta
    0xCC, 0xCC, 0xCC, // 14: gray
    0xFF, 0xFF, 0xFF, // 15: white
  ];

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>The MSX2 screen mode.</summary>
  public MsxMode Mode { get; init; }

  /// <summary>Bits per pixel for this mode.</summary>
  public int BitsPerPixel { get; init; }

  /// <summary>Raw pixel/pattern data.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Optional palette data (32 bytes for SC5/SC7, null for SC2/SC8).</summary>
  public byte[]? Palette { get; init; }

  /// <summary>Whether the original data had a 7-byte BLOAD header.</summary>
  public bool HasBloadHeader { get; init; }

  /// <summary>Converts this MSX2 screen to a platform-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(MsxFile file) {
    ArgumentNullException.ThrowIfNull(file);

    return file.Mode switch {
      MsxMode.Screen2 => _Sc2ToRawImage(file),
      MsxMode.Screen5 => _Sc5ToRawImage(file),
      MsxMode.Screen7 => _Sc7ToRawImage(file),
      MsxMode.Screen8 => _Sc8ToRawImage(file),
      _ => throw new NotSupportedException($"Unsupported MSX mode: {file.Mode}.")
    };
  }

  /// <summary>Not supported. MSX screen modes have complex format-specific constraints.</summary>
  public static MsxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to MsxFile is not supported due to complex format-specific constraints.");
  }

  /// <summary>Screen2: pattern-based TMS9918. Output as Rgb24 using fixed palette.</summary>
  private static RawImage _Sc2ToRawImage(MsxFile file) {
    const int width = 256;
    const int height = 192;
    var rgb = new byte[width * height * 3];
    var data = file.PixelData;

    // SC2 layout:
    // 0x0000-0x17FF: Pattern generator (3 banks x 2048 bytes)
    // 0x1800-0x1AFF: Pattern name table (768 bytes)
    // 0x2000-0x37FF: Color table (3 banks x 2048 bytes)
    const int nameTableOffset = 0x1800;
    const int colorTableOffset = 0x2000;

    for (var charRow = 0; charRow < 24; ++charRow)
      for (var charCol = 0; charCol < 32; ++charCol) {
        var charIndex = data[nameTableOffset + charRow * 32 + charCol];
        var bank = charRow / 8;
        var patternOffset = bank * 2048 + charIndex * 8;
        var colorOffset = colorTableOffset + bank * 2048 + charIndex * 8;

        for (var pixelRow = 0; pixelRow < 8; ++pixelRow) {
          var patternByte = patternOffset + pixelRow < data.Length ? data[patternOffset + pixelRow] : (byte)0;
          var colorByte = colorOffset + pixelRow < data.Length ? data[colorOffset + pixelRow] : (byte)0;
          var foreground = (colorByte >> 4) & 0x0F;
          var background = colorByte & 0x0F;
          var y = charRow * 8 + pixelRow;

          for (var bit = 0; bit < 8; ++bit) {
            var x = charCol * 8 + bit;
            var isSet = ((patternByte >> (7 - bit)) & 1) != 0;
            var colorIndex = isSet ? foreground : background;
            var palOffset = colorIndex * 3;
            var dstOffset = (y * width + x) * 3;
            rgb[dstOffset] = _Tms9918Palette[palOffset];
            rgb[dstOffset + 1] = _Tms9918Palette[palOffset + 1];
            rgb[dstOffset + 2] = _Tms9918Palette[palOffset + 2];
          }
        }
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Screen5: 256x212, 4bpp indexed with MSX palette.</summary>
  private static RawImage _Sc5ToRawImage(MsxFile file) {
    const int width = 256;
    const int height = 212;
    var pixels = new byte[width * height];
    var data = file.PixelData;

    for (var y = 0; y < height; ++y)
      for (var byteX = 0; byteX < 128; ++byteX) {
        var srcOffset = y * 128 + byteX;
        if (srcOffset >= data.Length)
          break;

        var b = data[srcOffset];
        var x = byteX * 2;
        pixels[y * width + x] = (byte)((b >> 4) & 0x0F);
        if (x + 1 < width)
          pixels[y * width + x + 1] = (byte)(b & 0x0F);
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = _ConvertMsxPalette(file.Palette),
      PaletteCount = 16,
    };
  }

  /// <summary>Screen7: 512x212, 4bpp indexed with MSX palette.</summary>
  private static RawImage _Sc7ToRawImage(MsxFile file) {
    const int width = 512;
    const int height = 212;
    var pixels = new byte[width * height];
    var data = file.PixelData;

    for (var y = 0; y < height; ++y)
      for (var byteX = 0; byteX < 256; ++byteX) {
        var srcOffset = y * 256 + byteX;
        if (srcOffset >= data.Length)
          break;

        var b = data[srcOffset];
        var x = byteX * 2;
        pixels[y * width + x] = (byte)((b >> 4) & 0x0F);
        if (x + 1 < width)
          pixels[y * width + x + 1] = (byte)(b & 0x0F);
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = _ConvertMsxPalette(file.Palette),
      PaletteCount = 16,
    };
  }

  /// <summary>Screen8: 256x212, 8bpp direct color GGGRRRBB.</summary>
  private static RawImage _Sc8ToRawImage(MsxFile file) {
    const int width = 256;
    const int height = 212;
    var rgb = new byte[width * height * 3];
    var data = file.PixelData;

    for (var i = 0; i < width * height; ++i) {
      if (i >= data.Length)
        break;

      var b = data[i];
      var g = (b >> 5) & 0x07;
      var r = (b >> 2) & 0x07;
      var bl = b & 0x03;
      var dstOffset = i * 3;
      rgb[dstOffset] = (byte)(r * 255 / 7);
      rgb[dstOffset + 1] = (byte)(g * 255 / 7);
      rgb[dstOffset + 2] = (byte)(bl * 255 / 3);
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Converts MSX V9938 palette (16 entries x 2 bytes: 0RRR0BBB, 00000GGG) to RGB triplets.</summary>
  private static byte[]? _ConvertMsxPalette(byte[]? msxPalette) {
    if (msxPalette == null || msxPalette.Length < 32)
      return null;

    var rgb = new byte[16 * 3];
    for (var i = 0; i < 16; ++i) {
      var byte0 = msxPalette[i * 2];
      var byte1 = msxPalette[i * 2 + 1];
      var r = (byte0 >> 4) & 0x07;
      var b = byte0 & 0x07;
      var g = byte1 & 0x07;
      rgb[i * 3] = (byte)(r * 255 / 7);
      rgb[i * 3 + 1] = (byte)(g * 255 / 7);
      rgb[i * 3 + 2] = (byte)(b * 255 / 7);
    }

    return rgb;
  }
}
