using System;
using FileFormat.Core;

namespace FileFormat.LarkaObjectEditor;

/// <summary>In-memory representation of a Larka Edytor Obiektów picture (.leo).</summary>
/// <remarks>
/// An ANTIC mode 4 object thirty-two characters wide and eight rows deep, drawn from two character
/// sets at once: even rows take their shapes from the first, odd rows from the second. Doubling the
/// set is what lets an object of this size use more distinct shapes than the 128 a single mode 4
/// character set holds.
/// <para/>
/// The character codes are not stored in reading order. They are interleaved so that the two sets'
/// halves sit apart, which is how the editor kept each set's codes contiguous in memory.
/// </remarks>
public readonly record struct LarkaObjectEditorFile
  : IImageFormatReader<LarkaObjectEditorFile>, IImageToRawImage<LarkaObjectEditorFile>,
    IImageFromRawImage<LarkaObjectEditorFile>, IImageFormatWriter<LarkaObjectEditorFile> {

  /// <summary>Screen pixels across.</summary>
  public const int Width = 256;

  /// <summary>Rows.</summary>
  public const int Height = 64;

  /// <summary>Characters across.</summary>
  public const int Columns = Width / 8;

  /// <summary>Character rows.</summary>
  public const int CharacterRows = Height / 8;

  /// <summary>Size of one character set.</summary>
  public const int FontSize = 1024;

  /// <summary>Offset of the character codes, after the two character sets.</summary>
  public const int CharactersOffset = FontSize * 2;

  /// <summary>Offset of the five colour registers: PF0, PF1, PF2, PF3 and the background.</summary>
  public const int RegisterOffset = 2560;

  /// <summary>Total file size.</summary>
  public const int FileSize = 2580;

  static string IImageFormatMetadata<LarkaObjectEditorFile>.PrimaryExtension => ".leo";
  static string[] IImageFormatMetadata<LarkaObjectEditorFile>.FileExtensions => [".leo"];
  static LarkaObjectEditorFile IImageFormatReader<LarkaObjectEditorFile>.FromSpan(ReadOnlySpan<byte> data)
    => LarkaObjectEditorReader.FromSpan(data);
  static byte[] IImageFormatWriter<LarkaObjectEditorFile>.ToBytes(LarkaObjectEditorFile file)
    => LarkaObjectEditorWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<LarkaObjectEditorFile>.VideoModes => [
    new("Object", [(Width, Height)], [5])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(LarkaObjectEditorFile file) {
    var data = file.Data ?? [];
    var registers = Atari8BitGraphics.ReadPf0123Bak(data, RegisterOffset);
    var frame = new byte[Width * Height];
    var characters = new byte[Columns];

    for (var row = 0; row < CharacterRows; ++row) {
      for (var column = 0; column < Columns; ++column) {
        var at = CharactersOffset + ((column & 1) << 7) + ((row & 1) << 6) + ((row & 6) << 3) + (column >> 1);
        characters[column] = at < data.Length ? data[at] : (byte)0;
      }

      // Even rows read the first character set, odd rows the second.
      Atari8BitGraphics.DecodeGr12Line(
        characters, 0, data, (row & 1) * FontSize, registers, frame, row * 8 * Width, Width, false);
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.ApplyPalette(frame),
    };
  }

  /// <summary>Where a cell's character code is stored, which is not its reading order.</summary>
  /// <remarks>
  /// The codes are interleaved so that the two sets' halves sit apart, which is how the editor kept
  /// each set's codes contiguous in memory rather than anything the picture needs.
  /// </remarks>
  public static int CodeOffset(int row, int column)
    => CharactersOffset + ((column & 1) << 7) + ((row & 1) << 6) + ((row & 6) << 3) + (column >> 1);

  /// <summary>Builds an object, giving every cell a character of its own.</summary>
  /// <remarks>
  /// Two character sets of 128 against 256 cells is exactly one character each, so no cell has to
  /// share a shape with another — even rows draw from the first set and odd rows from the second,
  /// which is precisely how the cells divide.
  /// <para/>
  /// A cell's fourth colour is chosen by the top bit of its character code: set, and pattern 3
  /// takes PF3 instead of PF2. That is a per-cell decision, so it is settled by trying both across
  /// the whole cell before any pixel is assigned.
  /// </remarks>
  public static LarkaObjectEditorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height);
    var chosen = _ChooseRegisters(rgb.PixelData);
    var data = new byte[FileSize];

    // Stored as PF0 to PF3 and then the background, which is the order the hardware has them in.
    for (var i = 0; i < 4; ++i)
      data[RegisterOffset + i] = chosen[i + 1];

    data[RegisterOffset + 4] = chosen[0];

    Span<byte> pattern = stackalloc byte[8];
    Span<byte> best = stackalloc byte[8];

    for (var row = 0; row < CharacterRows; ++row)
    for (var column = 0; column < Columns; ++column) {
      var glyph = (row >> 1) * Columns + column;
      var bestHigh = 0;
      var bestCost = long.MaxValue;

      for (var high = 0; high < 2; ++high) {
        var fourth = chosen[high != 0 ? 4 : 3];
        var cost = 0L;

        for (var y = 0; y < 8; ++y) {
          var value = 0;
          for (var pixel = 0; pixel < 4; ++pixel) {
            var at = ((row * 8 + y) * Width + column * 8 + pixel * 2) * 3;
            var (choice, error) = _Nearest(rgb.PixelData, at, chosen[0], chosen[1], chosen[2], fourth);

            value |= choice << (6 - pixel * 2);
            cost += error;
          }

          pattern[y] = (byte)value;
        }

        if (cost >= bestCost)
          continue;

        bestCost = cost;
        bestHigh = high;
        pattern.CopyTo(best);
      }

      var fontOffset = (row & 1) * FontSize + (glyph & 127) * 8;
      for (var y = 0; y < 8; ++y)
        data[fontOffset + y] = best[y];

      data[CodeOffset(row, column)] = (byte)((glyph & 127) | (bestHigh << 7));
    }

    return new() { Data = data };
  }

  /// <summary>The five colours the object is drawn in: the commonest the machine can show.</summary>
  private static byte[] _ChooseRegisters(ReadOnlySpan<byte> rgb) {
    var gtia = Atari8BitGraphics.Palette;
    var totals = new int[128];

    for (var i = 0; i + 2 < rgb.Length; i += 3) {
      var best = 0;
      var bestCost = long.MaxValue;

      // Only even entries: the low bit of a register is not a colour in this mode.
      for (var entry = 0; entry < 128; ++entry) {
        long dr = rgb[i] - gtia[entry * 6], dg = rgb[i + 1] - gtia[entry * 6 + 1], db = rgb[i + 2] - gtia[entry * 6 + 2];
        var cost = dr * dr * 77 + dg * dg * 150 + db * db * 29;
        if (cost >= bestCost)
          continue;

        bestCost = cost;
        best = entry;
      }

      ++totals[best];
    }

    var registers = new byte[5];
    for (var slot = 0; slot < registers.Length; ++slot) {
      var best = 0;
      for (var i = 1; i < totals.Length; ++i)
        if (totals[i] > totals[best])
          best = i;

      registers[slot] = (byte)(best * 2);
      totals[best] = -1;
    }

    return registers;
  }

  /// <summary>Which of a cell's four colours a pixel is closest to, and by how much.</summary>
  private static (int Choice, long Error) _Nearest(
    ReadOnlySpan<byte> rgb, int pixel, byte background, byte first, byte second, byte third) {
    var gtia = Atari8BitGraphics.Palette;
    Span<byte> candidates = [background, first, second, third];

    var best = 0;
    var bestCost = long.MaxValue;

    for (var i = 0; i < candidates.Length; ++i) {
      var entry = (candidates[i] & 254) * 3;
      long dr = rgb[pixel] - gtia[entry], dg = rgb[pixel + 1] - gtia[entry + 1], db = rgb[pixel + 2] - gtia[entry + 2];
      var cost = dr * dr * 77 + dg * dg * 150 + db * db * 29;

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = i;
    }

    return (best, bestCost);
  }
}
