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
  : IImageFormatReader<CocoP11File>, IImageToRawImage<CocoP11File> {

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
}
