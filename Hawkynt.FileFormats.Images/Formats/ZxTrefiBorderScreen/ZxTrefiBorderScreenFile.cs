using System;
using FileFormat.Core;

namespace FileFormat.ZxTrefiBorderScreen;

/// <summary>In-memory representation of a Border Screen by Trefi (.bsp).</summary>
/// <remarks>
/// An ordinary Spectrum screen, optionally with the border drawn round it, and optionally twice for
/// a picture that alternates between two fields. A flag byte in the header says which of the four
/// it is; nothing else distinguishes them, and the two-field forms are the only ones whose length
/// is fixed.
/// <para/>
/// The border is not a bitmap. The machine can only change the border colour by writing a port as
/// the beam travels, so what is stored is a run of colours: a byte names a colour and how long it
/// lasts, and a length of zero means it lasts to the end of the line. The runs are one stream for
/// the whole picture but the position within a line is not, which is why a line that ends mid-run
/// abandons it rather than carrying it over.
/// </remarks>
public readonly record struct ZxTrefiBorderScreenFile
  : IImageFormatReader<ZxTrefiBorderScreenFile>, IImageToRawImage<ZxTrefiBorderScreenFile> {

  /// <summary>Bytes one field's bitmap and attributes occupy.</summary>
  public const int ScreenSize = 6912;

  /// <summary>Where the first field's bitmap starts in a single-field file.</summary>
  public const int FirstBitmapOffset = 70;

  /// <summary>Width of a bordered picture.</summary>
  public const int BorderedWidth = 384;

  /// <summary>Height of a bordered picture.</summary>
  public const int BorderedHeight = 304;

  /// <summary>Where the screen sits inside the border, horizontally.</summary>
  public const int BorderLeft = 64;

  /// <summary>Where the screen sits inside the border, vertically.</summary>
  public const int BorderTop = 64;

  static string IImageFormatMetadata<ZxTrefiBorderScreenFile>.PrimaryExtension => ".bsp";
  static string[] IImageFormatMetadata<ZxTrefiBorderScreenFile>.FileExtensions => [".bsp"];
  static ZxTrefiBorderScreenFile IImageFormatReader<ZxTrefiBorderScreenFile>.FromSpan(ReadOnlySpan<byte> data)
    => ZxTrefiBorderScreenReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<ZxTrefiBorderScreenFile>.VideoModes => [
    new("ZX Spectrum", [(IntegerRange.Any, IntegerRange.Any)], [15])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>Where each field's screen and border start; a border offset below zero means none.</summary>
  public (int Bitmap, int Border)[] Fields { get; init; }

  public static RawImage ToRawImage(ZxTrefiBorderScreenFile file) {
    var data = file.Data ?? [];
    var fields = file.Fields ?? [];
    var rendered = new byte[fields.Length][];

    for (var i = 0; i < fields.Length; ++i)
      rendered[i] = _DecodeField(data, file.Width, file.Height, fields[i].Bitmap, fields[i].Border);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = rendered.Length == 1 ? rendered[0] : FrameBlend.Average(rendered[0], rendered[1]),
    };
  }

  private static byte[] _DecodeField(ReadOnlySpan<byte> data, int width, int height, int bitmap, int border) {
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      int red = 0, green = 0, blue = 0, left = 1;

      for (var x = 0; x < width; ++x) {
        if (border < 0)
          _ReadScreen(data, bitmap, x, y, out red, out green, out blue);
        else if (x is >= BorderLeft and < BorderLeft + ZxSpectrumGraphics.ScreenWidth
                 && y is >= BorderTop and < BorderTop + ZxSpectrumGraphics.ScreenHeight) {
          _ReadScreen(data, bitmap, x - BorderLeft, y - BorderTop, out red, out green, out blue);

          // The run resumes on the far side of the screen rather than carrying on behind it.
          left = 1;
        } else if (left > 0 && --left == 0) {
          if (border >= data.Length)
            throw new System.IO.InvalidDataException("A border screen's colour runs end before its border does.");

          int value = data[border++];

          // The border can only show the eight colours at normal intensity.
          var entry = (value & 7) * 3;
          red = ZxSpectrumGraphics.Palette[entry];
          green = ZxSpectrumGraphics.Palette[entry + 1];
          blue = ZxSpectrumGraphics.Palette[entry + 2];

          left = value >> 3;
          switch (left) {
            // Nothing follows: this colour holds to the end of the line.
            case 0: break;

            // The run is too long to fit the five bits left over, so its length is the next byte.
            case 1:
              if (border >= data.Length)
                throw new System.IO.InvalidDataException("A border run has no length.");

              left = data[border++];
              break;

            // Two is not twelve for any reason but that the encoder found the gap worth having.
            case 2: left = 12; break;
            default: left += 13; break;
          }

          // Lengths are counted in pairs of pixels, which is as finely as the border can be timed.
          left <<= 1;
        }

        var target = (y * width + x) * 3;
        rgb[target] = (byte)red;
        rgb[target + 1] = (byte)green;
        rgb[target + 2] = (byte)blue;
      }
    }

    return rgb;
  }

  private static void _ReadScreen(
    ReadOnlySpan<byte> data, int bitmap, int x, int y, out int red, out int green, out int blue) {
    var column = x >> 3;
    var attribute = data[bitmap + ZxSpectrumGraphics.BitmapSize + ((y >> 3) << 5) + column];
    var inkSet = ((data[bitmap + ZxSpectrumGraphics.LineOffset(y) + column] >> (~x & 7)) & 1) != 0;

    var entry = ZxSpectrumGraphics.ColorIndex(attribute, inkSet) * 3;
    red = ZxSpectrumGraphics.Palette[entry];
    green = ZxSpectrumGraphics.Palette[entry + 1];
    blue = ZxSpectrumGraphics.Palette[entry + 2];
  }
}
