using System;
using FileFormat.Core;

namespace FileFormat.Hireslace;

/// <summary>In-memory representation of a C64 Hireslace Editor (.hle) image.
/// Two interlaced hires frames (320x200) blended together.
/// Payload: LoadAddress(2) + bitmap1(8000) + screen1(1000) + bitmap2(8000) + screen2(1000) = 18002 bytes.
/// </summary>
public readonly record struct HireslaceFile : IImageFormatReader<HireslaceFile>, IImageToRawImage<HireslaceFile>, IImageFromRawImage<HireslaceFile>, IImageFormatWriter<HireslaceFile> {

  static string IImageFormatMetadata<HireslaceFile>.PrimaryExtension => ".hle";
  static string[] IImageFormatMetadata<HireslaceFile>.FileExtensions => [".hle"];
  static HireslaceFile IImageFormatReader<HireslaceFile>.FromSpan(ReadOnlySpan<byte> data) => HireslaceReader.FromSpan(data);
  static byte[] IImageFormatWriter<HireslaceFile>.ToBytes(HireslaceFile file) => HireslaceWriter.ToBytes(file);

  /// <summary>Bitmap data size per frame in bytes (320x200 / 8 * 8 cell-ordered).</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Screen RAM size per frame in bytes (40x25 cells).</summary>
  internal const int ScreenDataSize = 1000;

  /// <summary>Load address size in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Total payload size: 2 + 8000 + 1000 + 8000 + 1000 = 18002.</summary>
  internal const int ExpectedFileSize = LoadAddressSize + BitmapDataSize + ScreenDataSize + BitmapDataSize + ScreenDataSize;

  /// <summary>The fixed C64 16-color palette as 0xRRGGBB values.</summary>
  internal static readonly int[] C64Palette = [
    0x000000, 0xFFFFFF, 0x880000, 0xAAFFEE, 0xCC44CC, 0x00CC55,
    0x0000AA, 0xEEEE77, 0xDD8855, 0x664400, 0xFF7777, 0x333333,
    0x777777, 0xAAFF66, 0x0088FF, 0xBBBBBB
  ];

  /// <summary>Always 320.</summary>
  public int Width => 320;

  /// <summary>Always 200.</summary>
  public int Height => 200;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Bitmap data for frame 1 (8000 bytes, cell-ordered).</summary>
  public byte[] Bitmap1 { get; init; }

  /// <summary>Screen RAM for frame 1 (1000 bytes).</summary>
  public byte[] Screen1 { get; init; }

  /// <summary>Bitmap data for frame 2 (8000 bytes, cell-ordered).</summary>
  public byte[] Bitmap2 { get; init; }

  /// <summary>Screen RAM for frame 2 (1000 bytes).</summary>
  public byte[] Screen2 { get; init; }

  /// <summary>Converts this Hireslace image to a platform-independent <see cref="RawImage"/> in Rgb24 format (320x200, interlace-blended).</summary>
  public static RawImage ToRawImage(HireslaceFile file) {

    const int width = 320;
    const int height = 200;
    var rgb1 = _DecodeHiresFrame(file.Bitmap1, file.Screen1, width, height);
    var rgb2 = _DecodeHiresFrame(file.Bitmap2, file.Screen2, width, height);

    // Average the two frames for interlace blend
    var result = new byte[width * height * 3];
    for (var i = 0; i < result.Length; ++i)
      result[i] = (byte)((rgb1[i] + rgb2[i]) / 2);

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = result,
    };
  }

  /// <summary>Creates a Hireslace image from a platform-independent <see cref="RawImage"/>. Both frames use the same data (no true interlace difference).</summary>
  public static HireslaceFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var srcWidth = bgra.Width;
    var srcHeight = bgra.Height;
    var src = bgra.PixelData;

    const int cellsX = 40;
    const int cellsY = 25;
    var bitmapData = new byte[BitmapDataSize];
    var screenData = new byte[ScreenDataSize];
    Span<int> freq = stackalloc int[16];

    for (var cy = 0; cy < cellsY; ++cy)
      for (var cx = 0; cx < cellsX; ++cx) {
        freq.Clear();

        for (var py = 0; py < 8; ++py)
          for (var px = 0; px < 8; ++px) {
            var x = cx * 8 + px;
            var y = cy * 8 + py;
            byte r = 0, g = 0, b = 0;
            if (x < srcWidth && y < srcHeight) {
              var si = (y * srcWidth + x) * 4;
              b = src[si]; g = src[si + 1]; r = src[si + 2];
            }
            ++freq[_FindNearestC64Color(r, g, b)];
          }

        var fg = 0;
        var bg = 0;
        var maxFreq = -1;
        var secondFreq = -1;
        for (var i = 0; i < 16; ++i)
          if (freq[i] > maxFreq) {
            secondFreq = maxFreq;
            bg = fg;
            maxFreq = freq[i];
            fg = i;
          } else if (freq[i] > secondFreq) {
            secondFreq = freq[i];
            bg = i;
          }

        var cellIndex = cy * cellsX + cx;
        screenData[cellIndex] = (byte)((fg << 4) | (bg & 0x0F));

        for (var py = 0; py < 8; ++py) {
          byte bitmapByte = 0;
          for (var px = 0; px < 8; ++px) {
            var x = cx * 8 + px;
            var y = cy * 8 + py;
            byte r = 0, g = 0, b2 = 0;
            if (x < srcWidth && y < srcHeight) {
              var si = (y * srcWidth + x) * 4;
              b2 = src[si]; g = src[si + 1]; r = src[si + 2];
            }
            if (_FindNearestC64Color(r, g, b2) == fg)
              bitmapByte |= (byte)(1 << (7 - px));
          }
          bitmapData[cellIndex * 8 + py] = bitmapByte;
        }
      }

    return new() {
      LoadAddress = 0x2000,
      Bitmap1 = bitmapData[..],
      Screen1 = screenData[..],
      Bitmap2 = bitmapData[..],
      Screen2 = screenData[..],
    };
  }

  private static byte[] _DecodeHiresFrame(byte[] bitmap, byte[] screen, int width, int height) {
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var cellX = x / 8;
        var cellY = y / 8;
        var cellIndex = cellY * 40 + cellX;
        var byteInCell = y % 8;
        var bitmapByte = bitmap[cellIndex * 8 + byteInCell];
        var bitPosition = 7 - (x % 8);
        var bitValue = (bitmapByte >> bitPosition) & 1;

        var screenByte = screen[cellIndex];
        var colorIndex = bitValue == 1
          ? (screenByte >> 4) & 0x0F
          : screenByte & 0x0F;

        var color = C64Palette[colorIndex];
        var offset = (y * width + x) * 3;
        rgb[offset] = (byte)((color >> 16) & 0xFF);
        rgb[offset + 1] = (byte)((color >> 8) & 0xFF);
        rgb[offset + 2] = (byte)(color & 0xFF);
      }

    return rgb;
  }

  private static int _FindNearestC64Color(byte r, byte g, byte b) {
    var bestDist = int.MaxValue;
    var bestIdx = 0;
    for (var i = 0; i < 16; ++i) {
      var c = C64Palette[i];
      var cr = (c >> 16) & 0xFF;
      var cg = (c >> 8) & 0xFF;
      var cb = c & 0xFF;
      var dr = r - cr;
      var dg = g - cg;
      var db = b - cb;
      var dist = dr * dr + dg * dg + db * db;
      if (dist < bestDist) {
        bestDist = dist;
        bestIdx = i;
      }
    }
    return bestIdx;
  }
}
