using System;
using FileFormat.Core;

namespace FileFormat.GraphLogo;

/// <summary>In-memory representation of a Graph picture (.all).</summary>
/// <remarks>
/// A full ANTIC mode 4 screen that switches character set every row. The file starts with
/// twenty-four bank numbers, one per character row, followed by however many one-kilobyte sets
/// those numbers refer to, then the screen's characters and its colours. Redefining the set between
/// rows is what lets a mode 4 screen carry more than 128 distinct shapes: each row gets its own
/// alphabet.
/// </remarks>
public readonly record struct GraphLogoFile
  : IImageFormatReader<GraphLogoFile>, IImageToRawImage<GraphLogoFile>,
    IImageFromRawImage<GraphLogoFile>, IImageFormatWriter<GraphLogoFile> {

  /// <summary>Screen pixels across.</summary>
  public const int Width = 320;

  /// <summary>Rows.</summary>
  public const int Height = 192;

  /// <summary>Characters across.</summary>
  public const int Columns = Width / 8;

  /// <summary>Character rows, each with its own set.</summary>
  public const int CharacterRows = Height / 8;

  /// <summary>Size of one character set.</summary>
  public const int FontSize = 1024;

  /// <summary>Offset of the first character set, after the per-row bank numbers.</summary>
  public const int FontOffset = CharacterRows;

  /// <summary>Bytes that follow the character sets: the screen and then five colour registers.</summary>
  public const int TrailerSize = Columns * CharacterRows + 5;

  /// <summary>What a file's length is congruent to, modulo the character set size.</summary>
  public const int LengthRemainder = FontOffset + TrailerSize;

  static string IImageFormatMetadata<GraphLogoFile>.PrimaryExtension => ".all";
  static string[] IImageFormatMetadata<GraphLogoFile>.FileExtensions => [".all"];
  static GraphLogoFile IImageFormatReader<GraphLogoFile>.FromSpan(ReadOnlySpan<byte> data)
    => GraphLogoReader.FromSpan(data);
  static byte[] IImageFormatWriter<GraphLogoFile>.ToBytes(GraphLogoFile file) => GraphLogoWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<GraphLogoFile>.VideoModes => [
    new("Graph", [(Width, Height)], [5])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(GraphLogoFile file) {
    var data = file.Data ?? [];
    var screenOffset = data.Length - TrailerSize;
    var registers = Atari8BitGraphics.ReadPf0123Bak(data, data.Length - 5);
    var frame = new byte[Width * Height];

    for (var row = 0; row < CharacterRows; ++row) {
      var bank = row < data.Length ? data[row] : 0;
      Atari8BitGraphics.DecodeGr12Line(
        data, screenOffset + row * Columns, data, FontOffset + bank * FontSize,
        registers, frame, row * 8 * Width, Width, false);
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.ApplyPalette(frame),
    };
  }

  /// <summary>
  /// Writes a picture as a mode 4 screen whose character set is redefined for every row.
  /// </summary>
  /// <remarks>
  /// Redefining the set between rows is what the format exists for, and it is also what makes
  /// encoding one easy: a row holds forty cells and a set holds 128 glyphs, so every cell in a row
  /// can be given a glyph of its own and nothing has to be shared or approximated. Twenty-four sets
  /// is what that costs, one per row.
  /// <para/>
  /// The colours are five registers for the whole screen, and a cell buys a sixth from its own high
  /// bit: that bit draws the cell's highest pattern from PF3 rather than PF2. So each cell chooses
  /// between two four-colour sets and is given whichever fits it better.
  /// <para/>
  /// A mode 4 pixel is two screen pixels wide, so the picture is stored at half the width it comes
  /// back out at and every pair of columns shares a colour.
  /// </remarks>
  public static GraphLogoFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var source = image.SampleTo(Width, Height);
    var gtia = Atari8BitGraphics.Palette;
    var registers = Atari8BitGraphics.ChooseGr15Registers(
      PixelConverter.Convert(source, PixelFormat.Bgra32).PixelData, Width * Height,
      Atari8BitGraphics.Gr12RegisterCount);

    var data = new byte[FontOffset + CharacterRows * FontSize + TrailerSize];
    var screenOffset = FontOffset + CharacterRows * FontSize;

    // PF0 to PF3 and then the background, which is the order the registers are poked in rather than
    // the order a pattern indexes them.
    for (var i = 0; i < Atari8BitGraphics.Gr12RegisterCount; ++i)
      data[data.Length - 5 + i] = registers[(i + 1) % Atari8BitGraphics.Gr12RegisterCount];

    for (var row = 0; row < CharacterRows; ++row) {
      data[row] = (byte)row;

      for (var column = 0; column < Columns; ++column) {
        var glyph = FontOffset + row * FontSize + (column << 3);
        var inverse = _ChooseInverse(source.PixelData, registers, gtia, row, column);
        data[screenOffset + row * Columns + column] = (byte)(column | (inverse ? 128 : 0));

        for (var y = 0; y < 8; ++y) {
          byte bits = 0;
          for (var pixel = 0; pixel < 4; ++pixel)
            bits |= (byte)(_ChoosePattern(source.PixelData, registers, gtia, row * 8 + y,
              column * 8 + pixel * 2, inverse) << (6 - (pixel << 1)));

          data[glyph + y] = bits;
        }
      }
    }

    return new() { Data = data };
  }

  /// <summary>Whether a cell's highest pattern is better drawn from PF3 than from PF2.</summary>
  private static bool _ChooseInverse(
    ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> registers, ReadOnlySpan<byte> gtia, int row, int column) {
    long plain = 0, inverted = 0;

    for (var y = row * 8; y < row * 8 + 8; ++y)
    for (var x = column * 8; x < column * 8 + 8; x += 2) {
      plain += _BestCost(rgb, registers, gtia, y, x, false);
      inverted += _BestCost(rgb, registers, gtia, y, x, true);
    }

    return inverted < plain;
  }

  /// <summary>The pattern whose register is nearest the pixel, of the four a cell can draw.</summary>
  private static int _ChoosePattern(
    ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> registers, ReadOnlySpan<byte> gtia, int y, int x, bool inverse) {
    var best = 0;
    var bestCost = long.MaxValue;

    for (var pattern = 0; pattern < 4; ++pattern) {
      var cost = _Cost(rgb, registers, gtia, y, x, pattern, inverse);
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = pattern;
    }

    return best;
  }

  private static long _BestCost(
    ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> registers, ReadOnlySpan<byte> gtia, int y, int x, bool inverse) {
    var best = long.MaxValue;
    for (var pattern = 0; pattern < 4; ++pattern)
      best = Math.Min(best, _Cost(rgb, registers, gtia, y, x, pattern, inverse));

    return best;
  }

  /// <summary>
  /// How far a pattern is from the two screen pixels it covers, a mode 4 pixel being two wide.
  /// </summary>
  private static long _Cost(
    ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> registers, ReadOnlySpan<byte> gtia, int y, int x, int pattern,
    bool inverse) {
    var register = pattern == 3 && inverse ? 4 : pattern;
    var entry = (registers[register] & 254) * 3;
    long cost = 0;

    for (var offset = 0; offset < 2; ++offset) {
      var at = (y * Width + x + offset) * 3;
      long dr = rgb[at] - gtia[entry], dg = rgb[at + 1] - gtia[entry + 1], db = rgb[at + 2] - gtia[entry + 2];
      cost += dr * dr + dg * dg + db * db;
    }

    return cost;
  }
}
