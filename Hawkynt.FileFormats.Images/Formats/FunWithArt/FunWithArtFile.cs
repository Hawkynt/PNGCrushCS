using System;
using FileFormat.Core;

namespace FileFormat.FunWithArt;

/// <summary>In-memory representation of a Fun with Art picture (.fwa).</summary>
/// <remarks>
/// A four-colour Atari 8-bit screen whose colours change down the picture, and which stores those
/// changes not as a table but as the 6502 routine that performs them. The program saved its whole
/// working state — the display list, the interrupt handlers, the screen — so reading the file means
/// reading the display list to find which lines interrupt, and then reading the machine code at
/// each interrupt to find which registers it writes.
/// <para/>
/// Only a handful of instructions can appear, and they are recognised by opcode rather than
/// executed: the routine saves two registers, waits for the beam, then loads and stores colours
/// until it returns. Anything else means the file is not one Fun with Art wrote.
/// </remarks>
public readonly record struct FunWithArtFile
  : IImageFormatReader<FunWithArtFile>, IImageToRawImage<FunWithArtFile>,
    IImageFromRawImage<FunWithArtFile>, IImageFormatWriter<FunWithArtFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = 320;

  /// <summary>Rows.</summary>
  public const int Height = 192;

  /// <summary>Bytes one row of the bitmap occupies.</summary>
  public const int BytesPerRow = Width / 8;

  /// <summary>Where the bitmap starts.</summary>
  public const int BitmapOffset = 262;

  /// <summary>The row at which the bitmap skips the gap left for the display list's second half.</summary>
  public const int SplitRow = 102;

  /// <summary>How many bytes the bitmap skips at that row.</summary>
  public const int SplitGap = 16;

  /// <summary>Where the display list starts.</summary>
  public const int DisplayListOffset = 9;

  /// <summary>Where the interrupt routines start.</summary>
  public const int InterruptOffset = 7960;

  static string IImageFormatMetadata<FunWithArtFile>.PrimaryExtension => ".fwa";
  static string[] IImageFormatMetadata<FunWithArtFile>.FileExtensions => [".fwa"];
  static FunWithArtFile IImageFormatReader<FunWithArtFile>.FromSpan(ReadOnlySpan<byte> data)
    => FunWithArtReader.FromSpan(data);
  static byte[] IImageFormatWriter<FunWithArtFile>.ToBytes(FunWithArtFile file) => FunWithArtWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<FunWithArtFile>.VideoModes => [
    new("Atari 8-bit", [(Width, Height)], [256])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>
  /// The four colour registers as each row sees them: background, PF0, PF1 and PF2, one set a row.
  /// </summary>
  public byte[] Registers { get; init; }

  public static RawImage ToRawImage(FunWithArtFile file) {
    var data = file.Data ?? [];
    var registers = file.Registers ?? [];
    var frame = new byte[Width * Height];

    for (var y = 0; y < Height; ++y)
      Atari8BitGraphics.DecodeGr15Into(
        data, BitmapOffset + BytesPerRow * y + (y >= SplitRow ? SplitGap : 0), BytesPerRow,
        frame, y * Width, Width, Width, 1,
        registers.AsSpan(y * Atari8BitGraphics.Gr15RegisterCount, Atari8BitGraphics.Gr15RegisterCount));

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.ApplyPalette(frame),
    };
  }

  /// <summary>Logical pixels a Graphics 15 row holds, each drawn two screen pixels wide.</summary>
  private const int _LOGICAL_WIDTH = Width / 2;

  /// <summary>
  /// Encodes a picture as a four-colour screen whose colours are chosen afresh for every scanline.
  /// </summary>
  /// <remarks>
  /// Four registers a row is what the format is for, so they are chosen a row at a time rather than
  /// once for the screen: the same bitmap against 192 sets of four reaches colours no single set
  /// can. The rows that need no change of their own raise no interrupt, which is what keeps the
  /// routines from being 192 copies of the same twenty-eight bytes.
  /// <para/>
  /// A logical pixel is two screen pixels wide and only the first of each pair is looked at, which
  /// is what decoding puts back in both.
  /// </remarks>
  public static FunWithArtFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height).PixelData;
    var palette = Atari8BitGraphics.Palette;
    var registers = new byte[Height * Atari8BitGraphics.Gr15RegisterCount];
    var bitmap = new byte[Height * BytesPerRow];

    Span<byte> chosen = stackalloc byte[Atari8BitGraphics.Gr15RegisterCount];

    for (var y = 0; y < Height; ++y) {
      _ChooseRegisters(rgb, y, palette, chosen);
      chosen.CopyTo(registers.AsSpan(y * Atari8BitGraphics.Gr15RegisterCount));

      for (var pixel = 0; pixel < _LOGICAL_WIDTH; ++pixel) {
        var at = (y * Width + pixel * 2) * 3;
        var best = 0;
        var bestCost = int.MaxValue;
        for (var value = 0; value < chosen.Length; ++value) {
          var cost = _Distance(palette, chosen[value], rgb[at], rgb[at + 1], rgb[at + 2]);
          if (cost >= bestCost)
            continue;

          bestCost = cost;
          best = value;
        }

        // Four logical pixels to a byte, the leftmost in the top two bits.
        bitmap[y * BytesPerRow + (pixel >> 2)] |= (byte)(best << ((~pixel & 3) << 1));
      }
    }

    return new() { Data = FunWithArtWriter.Assemble(registers, bitmap), Registers = registers };
  }

  /// <summary>
  /// The four colour registers one scanline draws from, chosen from the colours it actually shows.
  /// </summary>
  /// <remarks>
  /// The commonest four the hardware can name are the starting point and then each is moved to
  /// whichever colour describes the pixels that ended up with it best. Starting from the four
  /// commonest alone is not enough — two of them may be neighbours and leave a third colour with no
  /// register at all — and refining from them settles that in a couple of passes.
  /// </remarks>
  private static void _ChooseRegisters(ReadOnlySpan<byte> rgb, int y, ReadOnlySpan<byte> palette, Span<byte> chosen) {
    Span<int> counts = stackalloc int[256];
    Span<byte> nearest = stackalloc byte[_LOGICAL_WIDTH];

    for (var pixel = 0; pixel < _LOGICAL_WIDTH; ++pixel) {
      var at = (y * Width + pixel * 2) * 3;
      nearest[pixel] = Atari8BitGraphics.FindNearestColorByte(palette, rgb[at], rgb[at + 1], rgb[at + 2]);
      ++counts[nearest[pixel]];
    }

    for (var slot = 0; slot < chosen.Length; ++slot) {
      var best = 0;
      for (var value = 0; value < 256; value += 2)
        if (counts[value] > counts[best])
          best = value;

      chosen[slot] = (byte)best;
      counts[best] = 0;
    }

    Span<long> totals = stackalloc long[chosen.Length * 3];
    Span<int> members = stackalloc int[chosen.Length];

    for (var pass = 0; pass < 3; ++pass) {
      totals.Clear();
      members.Clear();

      for (var pixel = 0; pixel < _LOGICAL_WIDTH; ++pixel) {
        var at = (y * Width + pixel * 2) * 3;
        var best = 0;
        var bestCost = int.MaxValue;
        for (var slot = 0; slot < chosen.Length; ++slot) {
          var cost = _Distance(palette, chosen[slot], rgb[at], rgb[at + 1], rgb[at + 2]);
          if (cost >= bestCost)
            continue;

          bestCost = cost;
          best = slot;
        }

        ++members[best];
        for (var channel = 0; channel < 3; ++channel)
          totals[best * 3 + channel] += rgb[at + channel];
      }

      for (var slot = 0; slot < chosen.Length; ++slot) {
        if (members[slot] == 0)
          continue;

        chosen[slot] = Atari8BitGraphics.FindNearestColorByte(
          palette,
          (byte)(totals[slot * 3] / members[slot]),
          (byte)(totals[slot * 3 + 1] / members[slot]),
          (byte)(totals[slot * 3 + 2] / members[slot]));
      }
    }
  }

  private static int _Distance(ReadOnlySpan<byte> palette, int entry, byte red, byte green, byte blue) {
    var at = entry * 3;
    int dr = palette[at] - red, dg = palette[at + 1] - green, db = palette[at + 2] - blue;

    return dr * dr + dg * dg + db * db;
  }
}
