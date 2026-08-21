using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Codecs.QuickTimeRle;

/// <summary>
/// What one depth of a QuickTime Animation stream means: how wide a coded unit is, how many pixels
/// it covers, and what the pictures that come out of it are made of.
/// </summary>
/// <remarks>
/// The compressor does not work a pixel at a time at every depth. At eight bits and below it moves
/// four bytes at a time — four indices at eight bits, eight at four, sixteen at two — and at one bit
/// it moves two, which is again sixteen pixels. Every count in the bitstream is in those units and
/// not in pixels: a copy of five is five units, a run of minus three is one unit written three times,
/// and a skip of nine steps eight units forward. Measuring that against a sample of ffmpeg's own
/// encoder is what settled it — a line whose first thirty-two of sixty-four pixels were unchanged is
/// written with a skip of thirty-three at twenty-four bits and a skip of nine at eight.
/// <para/>
/// The depth also decides what the picture is. Sixteen, twenty-four and thirty-two bits carry colour
/// directly; one, two, four and eight carry indices into a table, and the depths above thirty-two are
/// the same indices into a grey ramp that runs the other way — index zero is white.
/// </remarks>
internal sealed class QuickTimeRleLayout {

  /// <summary>Depths at or above this one are indices into a grey ramp of <c>Depth - 32</c> bits.</summary>
  private const int _GREYSCALE_BASE = 32;

  private QuickTimeRleLayout(int depth, int bitsPerSample, int unitBytes, int unitPixels, bool isIndexed, int canvasBytesPerPixel) {
    this.Depth = depth;
    this.BitsPerSample = bitsPerSample;
    this.UnitBytes = unitBytes;
    this.UnitPixels = unitPixels;
    this.IsIndexed = isIndexed;
    this.CanvasBytesPerPixel = canvasBytesPerPixel;
  }

  /// <summary>The depth exactly as the sample description states it, greyscale marker and all.</summary>
  internal int Depth { get; }

  /// <summary>Bits per coded sample: the depth with the greyscale marker taken off.</summary>
  internal int BitsPerSample { get; }

  /// <summary>How many bytes of the bitstream one coded unit is.</summary>
  internal int UnitBytes { get; }

  /// <summary>How many pixels one coded unit covers.</summary>
  internal int UnitPixels { get; }

  /// <summary>Whether the samples are indices into a colour table rather than colours.</summary>
  internal bool IsIndexed { get; }

  /// <summary>Bytes one pixel occupies in the canvas the decoder keeps between frames.</summary>
  internal int CanvasBytesPerPixel { get; }

  /// <summary>Whether this depth is a grey ramp rather than a colour table.</summary>
  internal bool IsGreyscale => this.Depth > _GREYSCALE_BASE;

  /// <summary>
  /// Works out the layout of a depth, or refuses a depth the Animation compressor has no meaning for.
  /// </summary>
  internal static QuickTimeRleLayout ForDepth(int depth, int streamIndex) => depth switch {
    1 or 33 => new(depth, 1, 2, 16, true, 1),
    2 or 34 => new(depth, 2, 4, 16, true, 1),
    4 or 36 => new(depth, 4, 4, 8, true, 1),
    8 or 40 => new(depth, 8, 4, 4, true, 1),
    16 => new(depth, 16, 2, 1, false, 3),
    24 => new(depth, 24, 3, 1, false, 3),
    32 => new(depth, 32, 4, 1, false, 4),
    _ => throw new NotSupportedException(
      $"Video stream {streamIndex} states a QuickTime Animation depth of {depth}, which is not one the compressor codes. Depths 1, 2, 4, 8, 16, 24 and 32, and the greyscale depths 33, 34, 36 and 40, are read.")
  };

  /// <summary>
  /// Expands one coded unit into the canvas's own layout.
  /// </summary>
  /// <remarks>
  /// Indices are unpacked to a byte each rather than left packed, so that a skip lands on a pixel
  /// and not on a bit: a run at four bits covers eight pixels and a skip may stop between two of
  /// them in the same byte, and a canvas of packed nibbles would have to read-modify-write every one
  /// of those. The picture that comes out says <see cref="FileFormat.Core.PixelFormat.Indexed8"/> for
  /// the same reason, with the colour table carried beside it.
  /// </remarks>
  internal void ExpandUnit(ReadOnlySpan<byte> unit, Span<byte> destination) {
    switch (this.BitsPerSample) {
      case 1:
        // Two bytes, sixteen pixels, most significant bit of the first byte leftmost.
        for (var i = 0; i < 16; ++i)
          destination[i] = (byte)((unit[i >> 3] >> (7 - (i & 7))) & 1);
        return;
      case 2:
        for (var i = 0; i < 16; ++i)
          destination[i] = (byte)((unit[i >> 2] >> (6 - ((i & 3) << 1))) & 3);
        return;
      case 4:
        for (var i = 0; i < 8; ++i)
          destination[i] = (byte)((i & 1) == 0 ? unit[i >> 1] >> 4 : unit[i >> 1] & 0x0F);
        return;
      case 8:
        unit[..4].CopyTo(destination);
        return;
      case 16: {
        // Five bits a channel in a big-endian word, the top bit unused. Repeating the top two bits
        // into the bottom two is what puts an all-ones channel at 255 rather than at 248.
        var packed = BinaryPrimitives.ReadUInt16BigEndian(unit);
        destination[0] = _Expand5((packed >> 10) & 0x1F);
        destination[1] = _Expand5((packed >> 5) & 0x1F);
        destination[2] = _Expand5(packed & 0x1F);
        return;
      }
      case 24:
        unit[..3].CopyTo(destination);
        return;
      default:
        // Thirty-two bits, and the alpha comes first: QuickTime's is ARGB where the picture is RGBA.
        destination[0] = unit[1];
        destination[1] = unit[2];
        destination[2] = unit[3];
        destination[3] = unit[0];
        return;
    }
  }

  private static byte _Expand5(int value) => (byte)((value << 3) | (value >> 2));

  /// <summary>
  /// The colour table a picture of this depth is drawn through.
  /// </summary>
  /// <remarks>
  /// A greyscale depth has no table in the file and needs none: the ramp runs from white at index
  /// zero down to black at the last index, which is the Macintosh convention and the opposite of what
  /// a reader expecting a luminance would draw. It was measured rather than assumed — ffmpeg's own
  /// decode of an eight-bit greyscale sample puts index 0xFF at black and index 0x00 at white.
  /// <para/>
  /// Every other depth carries its table in the sample description, and a stream that states one of
  /// those depths without a table is refused. The Macintosh default palettes would fill the gap, but
  /// nothing here can check them against anything, and a picture drawn through a table that might be
  /// the wrong one is exactly the plausible-but-wrong answer a decoder must not give.
  /// </remarks>
  internal (byte[] Palette, int Count) BuildPalette(ReadOnlySpan<byte> colourTable, int streamIndex) {
    var entries = 1 << this.BitsPerSample;

    if (this.IsGreyscale) {
      var ramp = new byte[entries * 3];
      var top = entries - 1;
      for (var i = 0; i < entries; ++i) {
        var level = (byte)(255 - (i * 255 / top));
        ramp[i * 3] = level;
        ramp[i * 3 + 1] = level;
        ramp[i * 3 + 2] = level;
      }

      return (ramp, entries);
    }

    if (colourTable.IsEmpty)
      throw new NotSupportedException(
        $"Video stream {streamIndex} is coded at {this.Depth} bits and carries no colour table, so there is nothing to say what its indices are colours of. The Macintosh default palettes are not applied here, because a picture drawn through a table that was guessed cannot be told from one drawn through the right one.");

    return (_ReadColourTable(colourTable, entries, streamIndex), entries);
  }

  /// <summary>
  /// Reads a QuickTime colour table: a seed, flags, one less than the number of entries, and then the
  /// entries themselves as four sixteen-bit values apiece.
  /// </summary>
  /// <remarks>
  /// The first of the four is the entry's own index in a table a device owns; in an image
  /// description it is left at zero and the entries are simply in order, so the position is what
  /// decides which index an entry colours. The other three are red, green and blue at sixteen bits,
  /// of which the high byte is the eight this library's pictures are made of.
  /// </remarks>
  private static byte[] _ReadColourTable(ReadOnlySpan<byte> table, int entries, int streamIndex) {
    const int _HEADER = 8;
    const int _ENTRY = 8;

    if (table.Length < _HEADER)
      throw new InvalidDataException(
        $"Video stream {streamIndex} states a colour table of {table.Length} bytes, which is shorter than the eight a table's own header is.");

    var stated = BinaryPrimitives.ReadUInt16BigEndian(table.Slice(6, 2)) + 1;
    if (stated > entries)
      throw new InvalidDataException(
        $"Video stream {streamIndex} states a colour table of {stated} entries where a {entries}-colour depth has room for {entries}.");

    if (table.Length < _HEADER + stated * _ENTRY)
      throw new InvalidDataException(
        $"Video stream {streamIndex} states a colour table of {stated} entries but carries {table.Length - _HEADER} bytes for them, where {stated * _ENTRY} are needed.");

    var palette = new byte[entries * 3];
    for (var i = 0; i < stated; ++i) {
      var entry = table.Slice(_HEADER + i * _ENTRY, _ENTRY);
      palette[i * 3] = entry[2];
      palette[i * 3 + 1] = entry[4];
      palette[i * 3 + 2] = entry[6];
    }

    return palette;
  }
}
