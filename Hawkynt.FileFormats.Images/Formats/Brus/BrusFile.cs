using System;
using FileFormat.Core;

namespace FileFormat.Brus;

/// <summary>In-memory representation of a BRUS picture (.brus) for the Commodore 128's VDC.</summary>
/// <remarks>
/// A run-length coded bitmap, optionally followed by a colour chunk. Without the chunk the picture
/// is black on white; with it, every cell takes an ink and a paper from the VDC's sixteen — but the
/// colours are refreshed only every eight rows and indexed by row parity, so a pair of rows share
/// one entry and the picture alternates between two sets down its height.
/// <para/>
/// The run-length coding is the plain kind: a byte under 128 introduces that many literals, and one
/// above it repeats the next byte that many times less 128.
/// </remarks>
public readonly record struct BrusFile
  : IImageFormatReader<BrusFile>, IImageToRawImage<BrusFile> {

  /// <summary>The text a file carries at offset two.</summary>
  public const string Signature = "BRUS";

  /// <summary>Offset of the packed stream.</summary>
  public const int StreamOffset = 18;

  /// <summary>Most character columns a picture may be.</summary>
  public const int MaxColumns = 90;

  /// <summary>Most rows a picture may be.</summary>
  public const int MaxHeight = 700;

  /// <summary>The VDC's sixteen colours, as RGB triplets.</summary>
  public static ReadOnlySpan<byte> Palette => [
    0x00, 0x00, 0x00, 0x55, 0x55, 0x55, 0x00, 0x00, 0xAA, 0x55, 0x55, 0xFF,
    0x00, 0xAA, 0x00, 0x55, 0xFF, 0x55, 0x00, 0xAA, 0xAA, 0x55, 0xFF, 0xFF,
    0xAA, 0x00, 0x00, 0xFF, 0x55, 0x55, 0xAA, 0x00, 0xAA, 0xFF, 0x55, 0xFF,
    0xAA, 0xAA, 0x00, 0xFF, 0xFF, 0x55, 0xAA, 0xAA, 0xAA, 0xFF, 0xFF, 0xFF,
  ];

  static string IImageFormatMetadata<BrusFile>.PrimaryExtension => ".brus";
  static string[] IImageFormatMetadata<BrusFile>.FileExtensions => [".brus"];
  static BrusFile IImageFormatReader<BrusFile>.FromSpan(ReadOnlySpan<byte> data) => BrusReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<BrusFile>.VideoModes => [
    new("VDC", [(new IntegerRange(8, MaxColumns * 8), new IntegerRange(1, MaxHeight))], [16])
  ];

  /// <summary>Character columns across.</summary>
  public int Columns { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>The unpacked bitmap, one bit a pixel.</summary>
  public byte[] Bitmap { get; init; }

  /// <summary>Two colour bytes a column for every band of eight rows, or null when the file has none.</summary>
  public byte[]? Colors { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width => this.Columns * 8;

  public static RawImage ToRawImage(BrusFile file) {
    var width = file.Width;
    var bitmap = file.Bitmap ?? [];
    var rgb = new byte[width * file.Height * 3];

    for (var y = 0; y < file.Height; ++y)
    for (var x = 0; x < width; ++x) {
      var column = x >> 3;
      var at = y * file.Columns + column;
      var ink = at < bitmap.Length && ((bitmap[at] >> (~x & 7)) & 1) != 0;
      var target = (y * width + x) * 3;

      if (file.Colors is not { } colors) {
        // Without the colour chunk a set bit is black on a white ground.
        var level = (byte)(ink ? 0 : 255);
        rgb[target] = rgb[target + 1] = rgb[target + 2] = level;
        continue;
      }

      // One band of colours per eight rows, and within a band the row's parity picks which half.
      var band = (y >> 3) * (file.Columns << 1);
      var entry = band + (y & 1) * file.Columns + column;
      var value = entry < colors.Length ? colors[entry] : 0;

      // The high nibble is the paper and the low one the ink.
      var index = (ink ? value : value >> 4) & 15;
      Palette.Slice(index * 3, 3).CopyTo(rgb.AsSpan(target));
    }

    return new() { Width = width, Height = file.Height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }
}
