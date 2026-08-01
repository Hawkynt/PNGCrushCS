using System;
using FileFormat.Core;

namespace FileFormat.CocoP11;

/// <summary>In-memory representation of a Color Computer P11 picture (.p11).</summary>
/// <remarks>
/// A Tandy Color Computer screen at 128x96, drawn as 256x192 because every pixel occupies two
/// across and two down. Two bits a pixel against four colours the hardware fixes — the machine has
/// no palette, only a choice between two sets, and this is the one with green in it.
/// <para/>
/// Because a pixel is two scanlines tall, consecutive rows share a line of storage: the row index
/// is rounded down to an even number before it reaches the bitmap.
/// </remarks>
public readonly record struct CocoP11File
  : IImageFormatReader<CocoP11File>, IImageToRawImage<CocoP11File>,
    IImageFromRawImage<CocoP11File>, IImageFormatWriter<CocoP11File> {

  /// <summary>Screen pixels across.</summary>
  public const int Width = 256;

  /// <summary>Screen rows.</summary>
  public const int Height = 192;

  /// <summary>Bytes one stored row occupies: four pixels to a byte.</summary>
  public const int Stride = Width / 8;

  /// <summary>Offset of the bitmap, after the header.</summary>
  public const int BitmapOffset = 5;

  /// <summary>Size of a file holding nothing but the picture.</summary>
  public const int FileSize = 3083;

  /// <summary>Size of a file with a trailer the picture does not use.</summary>
  public const int LongFileSize = 3243;

  /// <summary>The four colours the hardware fixes, as RGB triplets.</summary>
  public static ReadOnlySpan<byte> Palette => [
    0x07, 0xFF, 0x00, 0xFF, 0xFF, 0x00, 0x3B, 0x08, 0xFF, 0xCC, 0x00, 0x3B,
  ];

  static string IImageFormatMetadata<CocoP11File>.PrimaryExtension => ".p11";
  static string[] IImageFormatMetadata<CocoP11File>.FileExtensions => [".p11"];
  static CocoP11File IImageFormatReader<CocoP11File>.FromSpan(ReadOnlySpan<byte> data)
    => CocoP11Reader.FromSpan(data);
  static byte[] IImageFormatWriter<CocoP11File>.ToBytes(CocoP11File file)
    => CocoP11Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<CocoP11File>.VideoModes => [
    new("P11", [(Width, Height)], [4])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(CocoP11File file) {
    var data = file.Data ?? [];
    var pixels = new byte[Width * Height];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      // A pixel is two scanlines tall, so a pair of rows reads the same stored line.
      var at = BitmapOffset + ((y & ~1) >> 1) * Stride + (x >> 3);
      var b = at < data.Length ? data[at] : 0;
      pixels[y * Width + x] = (byte)((b >> (~x & 6)) & 3);
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = Palette.ToArray(),
      PaletteCount = 4,
    };
  }

  /// <summary>Writes the five bytes a reader identifies the format by.</summary>
  public static void WriteHeader(Span<byte> data) {
    data[0] = 0;
    data[1] = 12;
    data[3] = 14;
    data[4] = 0;
  }

  /// <summary>Builds a picture in the four colours the hardware fixes.</summary>
  /// <remarks>
  /// There is no palette to choose: the four are in the chip. A pixel is two screen pixels wide and
  /// two scanlines tall, so each is read at its top-left corner rather than averaged over the four
  /// it covers — averaging would mix colours the hardware has no way to show between.
  /// </remarks>
  public static CocoP11File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height);
    var data = new byte[FileSize];
    WriteHeader(data);

    for (var row = 0; row < Height / 2; ++row)
    for (var column = 0; column < Stride; ++column) {
      var value = 0;
      for (var pixel = 0; pixel < 4; ++pixel) {
        var at = (row * 2 * Width + column * 8 + pixel * 2) * 3;
        value |= _Nearest(rgb.PixelData, at) << (6 - pixel * 2);
      }

      data[BitmapOffset + row * Stride + column] = (byte)value;
    }

    return new() { Data = data };
  }

  /// <summary>Which of the four fixed colours a pixel is closest to.</summary>
  private static int _Nearest(ReadOnlySpan<byte> rgb, int pixel) {
    var best = 0;
    var bestCost = long.MaxValue;

    for (var entry = 0; entry < 4; ++entry) {
      long dr = rgb[pixel] - Palette[entry * 3];
      long dg = rgb[pixel + 1] - Palette[entry * 3 + 1];
      long db = rgb[pixel + 2] - Palette[entry * 3 + 2];
      var cost = dr * dr * 77 + dg * dg * 150 + db * db * 29;

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = entry;
    }

    return best;
  }
}
