using System;
using System.Collections.Generic;

namespace FileFormat.ShapeTable;

/// <summary>Assembles shape table (.shp) bytes.</summary>
/// <remarks>
/// Three of the four programs sharing this extension are read out whole and kept that way, so
/// writing them back is a copy. The two packed C64 screens are not: the reader unpacks them and
/// keeps the screen rather than the stream, so those are packed again here.
/// <para/>
/// The packing is the reader's own scheme read backwards — an escape byte introduces a count and a
/// value, a count of zero meaning 256 — with one restriction the reader does not impose. Its three
/// sections are one stream read three times under a different escape byte each time, so a run may
/// start in the bitmap and finish in the colour map. Runs written here always stop at a section
/// boundary, which costs at most two bytes a section and removes the whole question.
/// </remarks>
public static class ShapeTableWriter {

  /// <summary>Where the packed C64 forms were loaded, which the first two bytes name.</summary>
  private const int _LOAD_ADDRESS = 0x6000;

  /// <summary>A run of three is the shortest that a three-byte escape sequence does not lengthen.</summary>
  private const int _MINIMUM_RUN = 3;

  /// <summary>The longest run a single count byte can name.</summary>
  private const int _MAXIMUM_RUN = 256;

  public static byte[] ToBytes(ShapeTableFileType file) {
    var data = file.Data ?? [];

    return file.Kind switch {
      ShapeTableKind.C64Multicolor => _PackMulticolor(data),
      ShapeTableKind.C64Hires => _PackHires(data, file.Columns, file.Height),

      // The other three are kept as they were read, so there is nothing to reassemble.
      _ => data[..],
    };
  }

  /// <summary>
  /// Packs a multicolour screen into the form whose third byte is zero: bitmap, video matrix and
  /// colour map, with the shared background register in the header rather than the stream.
  /// </summary>
  private static byte[] _PackMulticolor(byte[] screen) {
    if (screen.Length < 10001)
      throw new ArgumentException("A packed multicolour shape table needs a 10001-byte screen.", nameof(screen));

    var bitmapEscape = _LeastUsedByte(screen.AsSpan(0, 8000));
    var output = new List<byte>(4096) {
      (byte)(_LOAD_ADDRESS & 0xFF),
      (byte)(_LOAD_ADDRESS >> 8),
      0,
      bitmapEscape,
      screen[10000],
    };

    _Pack(screen.AsSpan(0, 8000), bitmapEscape, output);
    _Pack(screen.AsSpan(8000, 1000), 0, output);
    _Pack(screen.AsSpan(9000, 1000), 255, output);

    return output.ToArray();
  }

  /// <summary>
  /// Packs a hi-res screen into whichever of the three forms describes its geometry — the widths
  /// and heights other than 320 by 200 each have their own third byte, and only that byte says so.
  /// </summary>
  private static byte[] _PackHires(byte[] screen, int columns, int height) {
    if (columns <= 0)
      columns = 40;
    if (height <= 0)
      height = 200;

    var bitmapLength = height * columns;
    var matrixLength = (bitmapLength >> 3) * 9 - bitmapLength;
    if (screen.Length < bitmapLength + matrixLength)
      throw new ArgumentException("A packed hi-res shape table needs its bitmap and video matrix.", nameof(screen));

    var bitmapEscape = _LeastUsedByte(screen.AsSpan(0, bitmapLength));
    var output = new List<byte>(4096) { (byte)(_LOAD_ADDRESS & 0xFF), (byte)(_LOAD_ADDRESS >> 8) };

    if (columns == 39)
      output.AddRange([167, 25, bitmapEscape]);
    else if (height != 200)
      output.AddRange([168, (byte)(height >> 3), bitmapEscape]);
    else
      output.AddRange([128, bitmapEscape]);

    _Pack(screen.AsSpan(0, bitmapLength), bitmapEscape, output);
    _Pack(screen.AsSpan(bitmapLength, matrixLength), 0, output);

    return output.ToArray();
  }

  /// <summary>Packs one section, whose runs never reach past its end.</summary>
  private static void _Pack(ReadOnlySpan<byte> source, byte escape, List<byte> output) {
    for (var at = 0; at < source.Length;) {
      var value = source[at];

      var run = 1;
      while (run < _MAXIMUM_RUN && at + run < source.Length && source[at + run] == value)
        ++run;

      // A byte equal to the escape cannot stand for itself however few of them there are.
      if (run >= _MINIMUM_RUN || value == escape) {
        output.Add(escape);
        output.Add((byte)(run == _MAXIMUM_RUN ? 0 : run));
        output.Add(value);
      } else
        for (var i = 0; i < run; ++i)
          output.Add(value);

      at += run;
    }
  }

  /// <summary>
  /// The byte value a section uses least, which costs least to spend as its escape.
  /// </summary>
  /// <remarks>
  /// Every occurrence of the escape as an ordinary value has to be written as a run of one, three
  /// bytes for one — so the cheapest escape is the rarest byte, and a byte the section does not
  /// contain at all is free.
  /// </remarks>
  private static byte _LeastUsedByte(ReadOnlySpan<byte> section) {
    Span<int> counts = stackalloc int[256];
    foreach (var value in section)
      ++counts[value];

    var best = 0;
    for (var value = 1; value < 256; ++value)
      if (counts[value] < counts[best])
        best = value;

    return (byte)best;
  }
}
