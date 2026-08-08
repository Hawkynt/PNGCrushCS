using System;
using FileFormat.Core;

namespace FileFormat.Graph2FontMch;

/// <summary>In-memory representation of a Graph2Font MCH picture (.mch).</summary>
/// <remarks>
/// A character screen in which every cell carries its own nine bytes — one of flags and eight of
/// shape — rather than pointing at a shared character set. That is the whole idea: a normal
/// character screen is cheap because cells repeat, and this format gives that up to let every cell
/// on screen be different, which is what a picture rather than a page of text needs.
/// <para/>
/// The colours are then rewritten every scanline from tables, and the sprites with them when the
/// file is long enough to carry them. A flag bit per cell can change the character's inverse
/// halfway down its own height, which doubles the colours a single cell can show.
/// </remarks>
public readonly record struct Graph2FontMchFile
  : IImageFormatReader<Graph2FontMchFile>, IImageToRawImage<Graph2FontMchFile>,
    IImageFromRawImage<Graph2FontMchFile>, IImageFormatWriter<Graph2FontMchFile> {

  /// <summary>Pixels across, including the borders the sprites can reach.</summary>
  public const int Width = 336;

  /// <summary>Rows.</summary>
  public const int Height = 240;

  /// <summary>Bytes a cell occupies: its flags and its eight rows.</summary>
  public const int BytesPerCell = 9;

  /// <summary>Cell rows a screen holds, which is more than it displays.</summary>
  public const int CellRows = 30;

  /// <summary>Characters written per scanline, which is the widest of the three the format allows.</summary>
  public const int WrittenColumns = 48;

  /// <summary>Colour registers a scanline holds: the background, then PF0 to PF3.</summary>
  public const int RegisterCount = 5;

  /// <summary>The flags byte's mode bits for the five-colour character mode without a raster block.</summary>
  public const byte WrittenMode = 5;

  /// <summary>
  /// The first cell the chip actually displays, the widest screen being wider than the picture.
  /// </summary>
  /// <remarks>
  /// ANTIC centres what it fetches on the screen, so at 48 characters three fall off each side and
  /// the 42 between them fill the frame exactly. That is why this width and not one of the narrower
  /// two: the others leave a band of background the picture cannot reach.
  /// </remarks>
  public const int FirstDisplayedColumn = 3;

  static string IImageFormatMetadata<Graph2FontMchFile>.PrimaryExtension => ".mch";
  static string[] IImageFormatMetadata<Graph2FontMchFile>.FileExtensions => [".mch"];
  static Graph2FontMchFile IImageFormatReader<Graph2FontMchFile>.FromSpan(ReadOnlySpan<byte> data)
    => Graph2FontMchReader.FromSpan(data);
  static byte[] IImageFormatWriter<Graph2FontMchFile>.ToBytes(Graph2FontMchFile file)
    => Graph2FontMchWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<Graph2FontMchFile>.VideoModes => [
    new("Graph2Font MCH", [(Width, Height)], [256])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Characters ANTIC fetches per scanline.</summary>
  public int Columns { get; init; }

  /// <summary>What ANTIC is fetching, which the flags byte chooses.</summary>
  public AnticMode Mode { get; init; }

  /// <summary>The GTIA mode bits, which the same byte chooses.</summary>
  public int GtiaMode { get; init; }

  /// <summary>Whether the file carries sprites and their per-scanline tables.</summary>
  public bool HasSprites { get; init; }

  public static RawImage ToRawImage(Graph2FontMchFile file) {
    var data = file.Data ?? [];
    var bitmapLength = file.Columns * BytesPerCell * CellRows;

    // One cell anywhere with the flag set switches the whole screen to the split-inverse reading.
    var split = false;
    for (var at = 0; at < bitmapLength && at < data.Length; at += BytesPerCell)
      if ((data[at] & 64) != 0) {
        split = true;
        break;
      }

    var gtia = new _Renderer(data, split) {
      PlayfieldColumns = file.Columns,
      Priority = file.GtiaMode,
    };

    var frame = new byte[Width * Height];

    for (var y = 0; y < Height; ++y) {
      var colors = bitmapLength + y;
      gtia.SetTabulatedColors(data, colors, Height, file.HasSprites ? 9 : 5, file.GtiaMode);

      if (file.HasSprites) {
        for (var i = 0; i < GtiaRenderer.SpriteCount; ++i) {
          gtia.SetPlayerHpos(i, data[colors + (9 + i) * Height]);
          gtia.SetMissileHpos(i, data[colors + (13 + i) * Height]);
        }

        gtia.SetPlayerSizes(data[colors + 4080]);
        gtia.SetMissileSizes(data[colors + 4320]);
        gtia.Priority = file.GtiaMode | data[colors + 4560];
        gtia.ProcessSpriteDma(data, colors + 4800);
      }

      gtia.StartLine(44);
      gtia.DrawSpan(y, 44, 212, file.Mode, frame, Width, 0);
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.ApplyPalette(frame),
    };
  }

  /// <summary>Writes a picture as a screen of cells with their own shapes and per-scanline colours.</summary>
  /// <remarks>
  /// Giving up the shared character set is the whole idea of the format, and it is what makes
  /// encoding one exact: every cell carries its own eight bytes, so nothing has to be fitted to a
  /// set or shared with a neighbour. The widest of the three screen widths is written because at 48
  /// characters the picture reaches both borders and no part of the frame is left to the background.
  /// <para/>
  /// The colours are rewritten every scanline, five registers apiece, and a scanline holding five or
  /// fewer of the machine's colours is given exactly those rather than a quantiser's idea of them.
  /// A cell then chooses, with its own high bit, whether its highest pattern draws from PF2 or PF3 —
  /// so four of the five reach any one cell.
  /// <para/>
  /// The sprites and the raster block are not written. A picture that needs them is an animation
  /// rather than a picture, which is what the reader refuses for the same reason.
  /// </remarks>
  public static Graph2FontMchFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var source = image.SampleTo(Width, Height);
    var rgb = source.PixelData;
    var bitmapLength = WrittenColumns * BytesPerCell * CellRows;
    var data = new byte[bitmapLength + RegisterCount * Height];
    data[0] = WrittenMode;

    var registers = new byte[Height * RegisterCount];
    for (var y = 0; y < Height; ++y) {
      _ChooseRegisters(rgb, y, registers.AsSpan(y * RegisterCount, RegisterCount));
      for (var i = 0; i < RegisterCount; ++i)
        data[bitmapLength + i * Height + y] = registers[y * RegisterCount + i];
    }

    var gtia = Atari8BitGraphics.Palette;
    for (var row = 0; row < CellRows; ++row)
    for (var column = FirstDisplayedColumn; column < WrittenColumns - FirstDisplayedColumn; ++column) {
      var cell = (row * WrittenColumns + column) * BytesPerCell;
      var left = (column - FirstDisplayedColumn) * 8;
      var inverse = _ChooseInverse(rgb, registers, gtia, row, left);
      data[cell] = (byte)(inverse ? 128 : 0);

      for (var y = row * 8; y < row * 8 + 8; ++y) {
        byte bits = 0;
        for (var pixel = 0; pixel < 4; ++pixel)
          bits |= (byte)(_ChoosePattern(rgb, registers, gtia, y, left + pixel * 2, inverse)
                         << (6 - (pixel << 1)));

        data[cell + 1 + (y & 7)] = bits;
      }
    }

    return new() {
      Data = data,
      Columns = WrittenColumns,
      Mode = AnticMode.FiveColor,
      GtiaMode = 0,
      HasSprites = false,
    };
  }

  /// <summary>
  /// A scanline's five registers: the colours it actually holds where there are few enough of them,
  /// and a reduction of it where there are not.
  /// </summary>
  private static void _ChooseRegisters(ReadOnlySpan<byte> rgb, int y, Span<byte> registers) {
    var found = 0;

    for (var x = 0; x < Width && found <= RegisterCount; x += 2) {
      var at = (y * Width + x) * 3;
      var color = Atari8BitGraphics.NearestRegister(rgb[at], rgb[at + 1], rgb[at + 2]);

      var seen = false;
      for (var i = 0; i < found; ++i)
        seen |= registers[i] == color;

      if (seen)
        continue;

      if (found < RegisterCount)
        registers[found] = color;

      ++found;
    }

    if (found <= RegisterCount)
      return;

    // More colours than registers, so the line is reduced rather than the first five kept.
    var bgra = new byte[Width * 4];
    for (var x = 0; x < Width; ++x) {
      var at = (y * Width + x) * 3;
      bgra[x * 4] = rgb[at + 2];
      bgra[x * 4 + 1] = rgb[at + 1];
      bgra[x * 4 + 2] = rgb[at];
      bgra[x * 4 + 3] = 255;
    }

    Atari8BitGraphics.ChooseGr15Registers(bgra, Width, RegisterCount).CopyTo(registers);
  }

  /// <summary>Whether a cell's highest pattern is better drawn from PF3 than from PF2.</summary>
  private static bool _ChooseInverse(
    ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> registers, ReadOnlySpan<byte> gtia, int row, int left) {
    long plain = 0, inverted = 0;

    for (var y = row * 8; y < row * 8 + 8; ++y)
    for (var x = left; x < left + 8; x += 2) {
      plain += _BestCost(rgb, registers, gtia, y, x, false);
      inverted += _BestCost(rgb, registers, gtia, y, x, true);
    }

    return inverted < plain;
  }

  private static long _BestCost(
    ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> registers, ReadOnlySpan<byte> gtia, int y, int x, bool inverse) {
    var best = long.MaxValue;
    for (var pattern = 0; pattern < 4; ++pattern)
      best = Math.Min(best, _Cost(rgb, registers, gtia, y, x, pattern, inverse));

    return best;
  }

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

  /// <summary>
  /// How far a pattern is from the two screen pixels it covers, a character pixel being two wide.
  /// </summary>
  private static long _Cost(
    ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> registers, ReadOnlySpan<byte> gtia, int y, int x, int pattern,
    bool inverse) {
    var register = pattern == 3 && inverse ? 4 : pattern;
    var entry = registers[y * RegisterCount + register] * 3;
    long cost = 0;

    for (var offset = 0; offset < 2; ++offset) {
      var at = (y * Width + x + offset) * 3;
      long dr = rgb[at] - gtia[entry], dg = rgb[at + 1] - gtia[entry + 1], db = rgb[at + 2] - gtia[entry + 2];
      cost += dr * dr + dg * dg + db * db;
    }

    return cost;
  }

  /// <summary>Every cell carries its own shape, so there is no character set to look anything up in.</summary>
  private sealed class _Renderer(byte[] data, bool split) : GtiaRenderer {

    protected override int GetPlayfieldByte(int y, int column) {
      var cell = ((y >> 3) * this.PlayfieldColumns + column) * BytesPerCell;
      if (cell + 8 >= data.Length)
        return 0;

      // A different bit of the flags byte supplies the inverse for the cell's lower half.
      var shift = split && (y & 4) != 0 ? 2 : 1;

      return ((data[cell] << shift) & 256) | data[cell + 1 + (y & 7)];
    }
  }
}
