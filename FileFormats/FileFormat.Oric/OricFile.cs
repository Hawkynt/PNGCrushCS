using System;
using FileFormat.Core;

namespace FileFormat.Oric;

/// <summary>In-memory representation of an Oric hi-res graphics screen dump.</summary>
public readonly record struct OricFile : IImageFormatReader<OricFile>, IImageToRawImage<OricFile>, IImageFormatWriter<OricFile> {

  static string IImageFormatMetadata<OricFile>.PrimaryExtension => ".oric";
  static string[] IImageFormatMetadata<OricFile>.FileExtensions => [".oric", ".tap"];
  static OricFile IImageFormatReader<OricFile>.FromSpan(ReadOnlySpan<byte> data) => OricReader.FromSpan(data);
  static byte[] IImageFormatWriter<OricFile>.ToBytes(OricFile file) => OricWriter.ToBytes(file);
  /// <summary>Always 240.</summary>
  public int Width => 240;

  /// <summary>Always 200.</summary>
  public int Height => 200;

  /// <summary>Raw screen data (40 bytes per row x 200 rows = 8000 bytes). Each byte is either pixel data (bit 6=0: bits 0-5 = 6 pixels MSB-first) or an attribute byte (bit 6=1).</summary>
  public byte[] ScreenData { get; init; }

  private static readonly byte[][] _OricPalette = [
    [0, 0, 0],       // 0 = Black
    [255, 0, 0],     // 1 = Red
    [0, 255, 0],     // 2 = Green
    [255, 255, 0],   // 3 = Yellow
    [0, 0, 255],     // 4 = Blue
    [255, 0, 255],   // 5 = Magenta
    [0, 255, 255],   // 6 = Cyan
    [255, 255, 255], // 7 = White
  ];

  public static RawImage ToRawImage(OricFile file) {
    const int rowBytes = 40;
    const int pixelsPerByte = 6;
    const int width = 240;
    const int height = 200;
    var pixels = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      var ink = 7;
      var paper = 0;
      var pixelX = 0;

      for (var col = 0; col < rowBytes; ++col) {
        var b = y * rowBytes + col < file.ScreenData.Length ? file.ScreenData[y * rowBytes + col] : (byte)0;

        if ((b & 0x40) != 0) {
          // Attribute byte
          var colorIndex = b & 0x07;
          if ((b & 0x80) != 0)
            paper = colorIndex;
          else
            ink = colorIndex;

          // Attribute byte produces 6 paper-colored pixels
          for (var p = 0; p < pixelsPerByte && pixelX < width; ++p, ++pixelX) {
            var offset = (y * width + pixelX) * 3;
            var c = _OricPalette[paper];
            pixels[offset] = c[0];
            pixels[offset + 1] = c[1];
            pixels[offset + 2] = c[2];
          }
        } else {
          // Pixel byte: bits 5..0 are 6 pixels, MSB (bit 5) is leftmost
          for (var p = 5; p >= 0 && pixelX < width; --p, ++pixelX) {
            var set = ((b >> p) & 1) != 0;
            var offset = (y * width + pixelX) * 3;
            var c = _OricPalette[set ? ink : paper];
            pixels[offset] = c[0];
            pixels[offset + 1] = c[1];
            pixels[offset + 2] = c[2];
          }
        }
      }
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }

}
