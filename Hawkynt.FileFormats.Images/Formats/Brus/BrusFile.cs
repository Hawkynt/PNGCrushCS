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
  : IImageFormatReader<BrusFile>, IImageToRawImage<BrusFile>,
    IImageFromRawImage<BrusFile>, IImageFormatWriter<BrusFile> {

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
  static byte[] IImageFormatWriter<BrusFile>.ToBytes(BrusFile file) => BrusWriter.ToBytes(file);
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

  /// <summary>Fits a picture into one ink and one paper per cell, per band of eight rows.</summary>
  /// <remarks>
  /// The colours refresh only every eight rows and are indexed by row parity, so a cell's two rows
  /// of each parity share an entry. Both halves of a band are given the same pair here: a picture
  /// that came from elsewhere has no reason to alternate, and writing two different pairs would
  /// make every other row wrong rather than every other row better.
  /// </remarks>
  public static BrusFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var columns = Math.Clamp((image.Width + 7) / 8, 1, MaxColumns);
    var height = Math.Clamp(image.Height, 1, MaxHeight);
    var width = columns * 8;

    var indexed = image.SampleTo(width, height).EnsureIndexed(PixelFormat.Indexed8, Palette.ToArray());
    var bitmap = new byte[columns * height];
    var bands = (height + 7) >> 3;
    var colors = new byte[bands * (columns << 1)];

    Span<int> frequency = stackalloc int[16];
    for (var band = 0; band < bands; ++band)
    for (var column = 0; column < columns; ++column) {
      frequency.Clear();
      var top = band * 8;
      var bottom = Math.Min(top + 8, height);

      for (var y = top; y < bottom; ++y)
      for (var x = column * 8; x < column * 8 + 8; ++x)
        ++frequency[indexed.PixelData[y * width + x] & 15];

      int paper = 0, ink = 0;
      for (var i = 0; i < 16; ++i) {
        if (frequency[i] > frequency[paper]) {
          ink = paper;
          paper = i;
        } else if (i != paper && frequency[i] > frequency[ink])
          ink = i;
      }

      var entry = (byte)((paper << 4) | ink);
      colors[band * (columns << 1) + column] = entry;
      colors[band * (columns << 1) + columns + column] = entry;

      for (var y = top; y < bottom; ++y) {
        byte bits = 0;
        for (var x = 0; x < 8; ++x)
          if (_Nearer(indexed.PixelData[y * width + column * 8 + x] & 15, ink, paper))
            bits |= (byte)(1 << (7 - x));

        bitmap[y * columns + column] = bits;
      }
    }

    return new() { Columns = columns, Height = height, Bitmap = bitmap, Colors = colors };
  }

  private static bool _Nearer(int index, int ink, int paper) {
    if (index == ink)
      return true;
    if (index == paper)
      return false;

    return _Distance(index, ink) < _Distance(index, paper);
  }

  private static int _Distance(int a, int b) {
    int dr = Palette[a * 3] - Palette[b * 3];
    int dg = Palette[a * 3 + 1] - Palette[b * 3 + 1];
    int db = Palette[a * 3 + 2] - Palette[b * 3 + 2];
    return dr * dr + dg * dg + db * db;
  }
}
