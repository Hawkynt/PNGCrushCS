using System;
using FileFormat.Core;

namespace FileFormat.BbcMicro;

/// <summary>In-memory representation of a BBC Micro screen memory dump.</summary>
public readonly record struct BbcMicroFile : IImageFormatReader<BbcMicroFile>, IImageToRawImage<BbcMicroFile>, IImageFromRawImage<BbcMicroFile>, IImageFormatWriter<BbcMicroFile> {

  static string IImageFormatMetadata<BbcMicroFile>.PrimaryExtension => ".bbc";
  static string[] IImageFormatMetadata<BbcMicroFile>.FileExtensions => [".bbc"];
  static BbcMicroFile IImageFormatReader<BbcMicroFile>.FromSpan(ReadOnlySpan<byte> data) => BbcMicroReader.FromSpan(data);
  static byte[] IImageFormatWriter<BbcMicroFile>.ToBytes(BbcMicroFile file) => BbcMicroWriter.ToBytes(file);
  /// <summary>Width in pixels (depends on mode: 640, 320, or 160).</summary>
  public int Width { get; init; }
  /// <summary>Height in pixels (always 256).</summary>
  public int Height { get; init; }
  /// <summary>Screen mode.</summary>
  public BbcMicroMode Mode { get; init; }
  /// <summary>Raw screen memory (linearized from character block layout).</summary>
  public byte[] PixelData { get; init; }
  /// <summary>Optional palette indices.</summary>
  public byte[]? Palette { get; init; }

  /// <summary>Expected screen memory size for modes 0-2 (20480 bytes).</summary>
  internal const int ScreenSizeModes012 = 20480;

  /// <summary>Expected screen memory size for modes 4-5 (10240 bytes).</summary>
  internal const int ScreenSizeModes45 = 10240;

  /// <summary>Fixed height for all BBC Micro screen modes.</summary>
  internal const int FixedHeight = 256;

  /// <summary>Number of pixel rows per character cell.</summary>
  internal const int CharacterRows = 8;

  /// <summary>Number of character rows on screen (256 / 8).</summary>
  internal const int CharacterRowCount = FixedHeight / CharacterRows;

  /// <summary>Bytes per character cell (always 8: one byte per pixel row).</summary>
  internal const int BytesPerCharacter = 8;

  /// <summary>Gets the expected screen memory size for a given mode.</summary>
  internal static int GetExpectedScreenSize(BbcMicroMode mode) => mode switch {
    BbcMicroMode.Mode0 => ScreenSizeModes012,
    BbcMicroMode.Mode1 => ScreenSizeModes012,
    BbcMicroMode.Mode2 => ScreenSizeModes012,
    BbcMicroMode.Mode4 => ScreenSizeModes45,
    BbcMicroMode.Mode5 => ScreenSizeModes45,
    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown BBC Micro mode.")
  };

  /// <summary>Gets the pixel width for a given mode.</summary>
  internal static int GetWidth(BbcMicroMode mode) => mode switch {
    BbcMicroMode.Mode0 => 640,
    BbcMicroMode.Mode1 => 320,
    BbcMicroMode.Mode2 => 160,
    BbcMicroMode.Mode4 => 320,
    BbcMicroMode.Mode5 => 160,
    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown BBC Micro mode.")
  };

  /// <summary>Gets the number of character columns for a given mode (= bytes per scanline).</summary>
  internal static int GetCharacterColumns(BbcMicroMode mode) => mode switch {
    BbcMicroMode.Mode0 => 80,
    BbcMicroMode.Mode1 => 80,
    BbcMicroMode.Mode2 => 80,
    BbcMicroMode.Mode4 => 40,
    BbcMicroMode.Mode5 => 40,
    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown BBC Micro mode.")
  };

  /// <summary>Gets the number of colors available in a given mode.</summary>
  private static int _GetColorCount(BbcMicroMode mode) => mode switch {
    BbcMicroMode.Mode0 or BbcMicroMode.Mode4 => 2,
    BbcMicroMode.Mode1 or BbcMicroMode.Mode5 => 4,
    BbcMicroMode.Mode2 => 16,
    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown BBC Micro mode.")
  };

  /// <summary>Converts this BBC Micro screen to a platform-independent <see cref="RawImage"/> in Indexed8 format.</summary>
  public static RawImage ToRawImage(BbcMicroFile file) {

    var width = file.Width;
    var height = file.Height;
    var charCols = GetCharacterColumns(file.Mode);
    var pixels = new byte[width * height];

    // PixelData is already linearized from character-block layout by the reader.
    // Access pattern: y * charCols + byteCol
    for (var y = 0; y < height; ++y)
      switch (file.Mode) {
        case BbcMicroMode.Mode0:
        case BbcMicroMode.Mode4:
          // 1bpp: 8 pixels per byte, MSB first
          for (var byteCol = 0; byteCol < charCols; ++byteCol) {
            var srcOffset = y * charCols + byteCol;
            if (srcOffset >= file.PixelData.Length)
              continue;

            var b = file.PixelData[srcOffset];
            var baseX = byteCol * 8;
            for (var bit = 0; bit < 8; ++bit) {
              var x = baseX + bit;
              if (x < width)
                pixels[y * width + x] = (byte)((b >> (7 - bit)) & 1);
            }
          }
          break;

        case BbcMicroMode.Mode1:
        case BbcMicroMode.Mode5:
          // 2bpp: 4 pixels per byte. BBC interleaved: pixel0=bits(7,3), pixel1=bits(6,2), pixel2=bits(5,1), pixel3=bits(4,0)
          for (var byteCol = 0; byteCol < charCols; ++byteCol) {
            var srcOffset = y * charCols + byteCol;
            if (srcOffset >= file.PixelData.Length)
              continue;

            var b = file.PixelData[srcOffset];
            var baseX = byteCol * 4;
            var p0 = ((b >> 6) & 2) | ((b >> 3) & 1);
            var p1 = ((b >> 5) & 2) | ((b >> 2) & 1);
            var p2 = ((b >> 4) & 2) | ((b >> 1) & 1);
            var p3 = ((b >> 3) & 2) | (b & 1);

            if (baseX < width) pixels[y * width + baseX] = (byte)p0;
            if (baseX + 1 < width) pixels[y * width + baseX + 1] = (byte)p1;
            if (baseX + 2 < width) pixels[y * width + baseX + 2] = (byte)p2;
            if (baseX + 3 < width) pixels[y * width + baseX + 3] = (byte)p3;
          }
          break;

        case BbcMicroMode.Mode2:
          // 4bpp: 2 pixels per byte. BBC interleaved: pixel0=bits(7,5,3,1), pixel1=bits(6,4,2,0)
          for (var byteCol = 0; byteCol < charCols; ++byteCol) {
            var srcOffset = y * charCols + byteCol;
            if (srcOffset >= file.PixelData.Length)
              continue;

            var b = file.PixelData[srcOffset];
            var baseX = byteCol * 2;
            var p0 = ((b >> 4) & 8) | ((b >> 3) & 4) | ((b >> 2) & 2) | ((b >> 1) & 1);
            var p1 = ((b >> 3) & 8) | ((b >> 2) & 4) | ((b >> 1) & 2) | (b & 1);

            if (baseX < width) pixels[y * width + baseX] = (byte)p0;
            if (baseX + 1 < width) pixels[y * width + baseX + 1] = (byte)p1;
          }
          break;
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      PaletteCount = _GetColorCount(file.Mode),
    };
  }

  /// <summary>Creates a Mode 0 (1bpp, 640x256) BBC Micro screen dump from a <see cref="RawImage"/>. Pixels are thresholded at 128.</summary>
  public static BbcMicroFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var srcWidth = bgra.Width;
    var srcHeight = bgra.Height;
    var src = bgra.PixelData;

    const BbcMicroMode mode = BbcMicroMode.Mode0;
    const int width = 640;
    const int height = FixedHeight; // 256
    var charCols = GetCharacterColumns(mode); // 80

    // Linearized pixel data: 1bpp, 8 pixels per byte, MSB first
    var pixelData = new byte[height * charCols];

    for (var y = 0; y < height; ++y)
      for (var byteCol = 0; byteCol < charCols; ++byteCol) {
        byte val = 0;
        for (var bit = 0; bit < 8; ++bit) {
          var x = byteCol * 8 + bit;
          byte lum = 0;
          if (x < srcWidth && y < srcHeight) {
            var si = (y * srcWidth + x) * 4;
            lum = (byte)((src[si + 2] * 77 + src[si + 1] * 150 + src[si] * 29) >> 8);
          }

          if (lum >= 128)
            val |= (byte)(1 << (7 - bit));
        }

        pixelData[y * charCols + byteCol] = val;
      }

    return new() {
      Width = width,
      Height = height,
      Mode = mode,
      PixelData = pixelData,
    };
  }
}
