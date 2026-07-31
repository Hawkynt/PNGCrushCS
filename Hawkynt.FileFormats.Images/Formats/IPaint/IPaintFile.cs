using System;
using FileFormat.Core;

namespace FileFormat.IPaint;

/// <summary>In-memory representation of an I Paint picture (.ip).</summary>
/// <remarks>
/// A Commodore 128 picture in the 80-column display's own terms: one bit a pixel, with colour — if
/// there is any — held separately and at a coarser grain. The colour is stored two rows to every
/// eight of the picture, and the two alternate down the block, so a cell shows one pair of colours
/// on its even rows and another on its odd ones. That is not a compression trick but what the
/// display chip could actually be made to do.
/// <para/>
/// The colour is optional: a file may simply end after its bitmap, in which case the picture is
/// black on white.
/// </remarks>
public readonly record struct IPaintFile
  : IImageFormatReader<IPaintFile>, IImageToRawImage<IPaintFile> {

  /// <summary>The sixteen colours the 80-column chip can show.</summary>
  /// <remarks>
  /// Not the VIC-II's sixteen. The 80-column chip is a different design with the same palette a PC
  /// of the time had: two levels of each channel, a pair of greys — and, in place of the dark
  /// yellow an even ramp would give, brown. That one entry is the giveaway, and reading it as
  /// 0xAAAA00 rather than 0xAA5500 is the mistake the table invites.
  /// </remarks>
  public static ReadOnlySpan<int> Palette => [
    0x000000, 0x555555, 0x0000AA, 0x5555FF, 0x00AA00, 0x55FF55, 0x00AAAA, 0x55FFFF,
    0xAA0000, 0xFF5555, 0xAA00AA, 0xFF55FF, 0xAA5500, 0xFFFF55, 0xAAAAAA, 0xFFFFFF,
  ];

  static string IImageFormatMetadata<IPaintFile>.PrimaryExtension => ".ip";
  static string[] IImageFormatMetadata<IPaintFile>.FileExtensions => [".ip"];
  static IPaintFile IImageFormatReader<IPaintFile>.FromSpan(ReadOnlySpan<byte> data)
    => IPaintReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<IPaintFile>.VideoModes => [
    new("Commodore 128", [(new(8, 720), new(1, 700))], [2, 16])
  ];

  /// <summary>Character cells across.</summary>
  public int Columns { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>The unpacked bitmap, one bit a pixel.</summary>
  public byte[] Bitmap { get; init; }

  /// <summary>
  /// The unpacked colours, two rows of cells for every eight rows of picture, or empty if the file
  /// carried none.
  /// </summary>
  public byte[] Colors { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width => this.Columns * 8;

  public static RawImage ToRawImage(IPaintFile file) {
    var bitmap = file.Bitmap ?? [];
    var colors = file.Colors ?? [];
    var width = file.Width;
    var rgb = new byte[width * file.Height * 3];

    for (var y = 0; y < file.Height; ++y)
    for (var x = 0; x < width; ++x) {
      var column = x >> 3;
      var lit = ((bitmap[y * file.Columns + column] >> (~x & 7)) & 1) != 0;

      int color;
      if (colors.Length == 0)
        color = lit ? 0 : 0xFFFFFF;
      else {
        // Two stored rows serve eight picture rows, taken alternately.
        var attribute = colors[(y >> 3) * file.Columns * 2 + (y & 1) * file.Columns + column];
        color = Palette[(lit ? attribute : attribute >> 4) & 15];
      }

      var target = (y * width + x) * 3;
      rgb[target] = (byte)(color >> 16);
      rgb[target + 1] = (byte)(color >> 8);
      rgb[target + 2] = (byte)color;
    }

    return new() { Width = width, Height = file.Height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }
}
