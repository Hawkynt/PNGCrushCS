using System;

namespace FileFormat.Codecs.Smacker;

/// <summary>
/// Paints one picture from a Smacker video chunk: a stream of Huffman-coded block descriptors, each
/// naming a run of same-typed 4x4 blocks, over a canvas that starts as whatever the picture before it
/// left there.
/// </summary>
/// <remarks>
/// Four block types, the low two bits of a descriptor read through the <c>Type</c> table: a mono block
/// of two colours chosen per pixel by a sixteen-bit map; a full block of sixteen colours; a skipped
/// block, which paints nothing and leaves whatever the canvas already held — the whole of this format's
/// inter-frame coding; and a solid block filled with one colour the descriptor's own high byte carries.
/// A descriptor's run length is not stored directly but as a six-bit index into a table running 1 to 59
/// and then 128, 256, 512, 1024 and 2048, so one descriptor can cover the rest of a small picture.
/// <para/>
/// <b>A full block's shape depends on the file's revision and is read once for a whole run.</b>
/// <c>SMK2</c> pairs two <c>Full</c> symbols to a row, the first symbol's low and high bytes painting
/// the row's third and fourth pixels and the second symbol's its first and second. <c>SMK4</c> reads
/// one or two bits immediately after a full run's own descriptor, before any of the run's blocks are
/// painted, and they choose between that shape and two more: a one bit picks a block of four solid 2x2
/// quadrants, two colours to a symbol; a zero then a one picks the row coding with each row painted
/// twice; a zero then a zero is <c>SMK2</c>'s own shape. Reading those bits once for the run rather than
/// once a block is what RAD's "prepended... determining sub-type of all following blocks in the chain"
/// means.
/// </remarks>
internal static class SmackerPictureDecoder {

  private const int _BLOCK = 4;

  private const int _TYPE_MONO = 0;
  private const int _TYPE_FULL = 1;
  private const int _TYPE_SKIP = 2;
  private const int _TYPE_SOLID = 3;

  /// <summary>How many blocks a descriptor's six-bit run index stands for. Copied exactly rather than
  /// generated: the first sixty entries count up one at a time and the last five do not.</summary>
  private static readonly int[] _RUN_LENGTHS = [
    1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
    17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32,
    33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48,
    49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 128, 256, 512, 1024, 2048,
  ];

  /// <summary>The three shapes a full block can take. <see cref="Rows"/> is the only one
  /// <c>SMK2</c> ever emits.</summary>
  private enum _FullShape { Rows, Quadrants, DoubledRows }

  internal static void Decode(
    ReadOnlySpan<byte> videoChunk,
    SmackerSymbolTable mmap,
    SmackerSymbolTable mclr,
    SmackerSymbolTable full,
    SmackerSymbolTable type,
    bool isVersion4,
    byte[] canvas,
    int width,
    int height
  ) {
    mmap.ResetHistory();
    mclr.ResetHistory();
    full.ResetHistory();
    type.ResetHistory();

    var reader = new SmackerBitReader(videoChunk);
    var blocksAcross = width / _BLOCK;
    var totalBlocks = blocksAcross * (height / _BLOCK);

    for (var blockIndex = 0; blockIndex < totalBlocks;) {
      var descriptor = type.Decode(ref reader);
      var blockType = descriptor & 3;
      var run = _RUN_LENGTHS[(descriptor >> 2) & 0x3F];

      var shape = _FullShape.Rows;
      if (blockType == _TYPE_FULL && isVersion4)
        shape = reader.ReadBit() != 0 ? _FullShape.Quadrants
          : reader.ReadBit() != 0 ? _FullShape.DoubledRows
          : _FullShape.Rows;

      if (blockType == _TYPE_SKIP) {
        blockIndex += Math.Min(run, totalBlocks - blockIndex);
        continue;
      }

      for (; run > 0 && blockIndex < totalBlocks; --run, ++blockIndex) {
        var at = blockIndex / blocksAcross * (width * _BLOCK) + blockIndex % blocksAcross * _BLOCK;
        switch (blockType) {
          case _TYPE_MONO:
            _PaintMono(ref reader, mclr, mmap, canvas, at, width);
            break;
          case _TYPE_FULL:
            switch (shape) {
              case _FullShape.Rows: _PaintRows(ref reader, full, canvas, at, width, doubled: false); break;
              case _FullShape.Quadrants: _PaintQuadrants(ref reader, full, canvas, at, width); break;
              default: _PaintRows(ref reader, full, canvas, at, width, doubled: true); break;
            }

            break;
          default:
            _Fill(canvas, at, width, (byte)(descriptor >> 8));
            break;
        }
      }
    }
  }

  /// <summary>A mono block: two colours one <c>MClr</c> symbol carries — its high byte the colour a set
  /// map bit picks, its low byte the colour a clear one picks — and a sixteen-bit map from <c>MMap</c>
  /// whose bit zero is the block's top left pixel and each following bit the next in reading
  /// order.</summary>
  private static void _PaintMono(ref SmackerBitReader reader, SmackerSymbolTable mclr, SmackerSymbolTable mmap, byte[] canvas, int at, int stride) {
    var colours = mclr.Decode(ref reader);
    var low = (byte)colours;
    var high = (byte)(colours >> 8);
    var map = mmap.Decode(ref reader);

    for (var row = 0; row < _BLOCK; ++row, at += stride)
      for (var column = 0; column < _BLOCK; ++column, map >>= 1)
        canvas[at + column] = (map & 1) != 0 ? high : low;
  }

  /// <summary>A full block coded a row at a time: two <c>Full</c> symbols a row, the first painting the
  /// row's third and fourth pixels from its low and high bytes and the second its first and second.
  /// <paramref name="doubled"/> paints each pair of rows from one pair of symbols, which is
  /// <c>SMK4</c>'s second extra shape.</summary>
  private static void _PaintRows(ref SmackerBitReader reader, SmackerSymbolTable full, byte[] canvas, int at, int stride, bool doubled) {
    var step = doubled ? 2 : 1;
    for (var row = 0; row < _BLOCK; row += step) {
      var right = full.Decode(ref reader);
      var left = full.Decode(ref reader);
      canvas[at] = (byte)left;
      canvas[at + 1] = (byte)(left >> 8);
      canvas[at + 2] = (byte)right;
      canvas[at + 3] = (byte)(right >> 8);
      at += stride;

      if (!doubled)
        continue;

      canvas.AsSpan(at - stride, _BLOCK).CopyTo(canvas.AsSpan(at, _BLOCK));
      at += stride;
    }
  }

  /// <summary><c>SMK4</c>'s first extra shape: four solid 2x2 quadrants, two to a <c>Full</c> symbol —
  /// the first symbol's low byte the top left quadrant and its high byte the top right, the second
  /// symbol's the two below them.</summary>
  private static void _PaintQuadrants(ref SmackerBitReader reader, SmackerSymbolTable full, byte[] canvas, int at, int stride) {
    for (var half = 0; half < 2; ++half) {
      var colours = full.Decode(ref reader);
      var left = (byte)colours;
      var right = (byte)(colours >> 8);
      for (var row = 0; row < 2; ++row, at += stride) {
        canvas[at] = left;
        canvas[at + 1] = left;
        canvas[at + 2] = right;
        canvas[at + 3] = right;
      }
    }
  }

  private static void _Fill(byte[] canvas, int at, int stride, byte colour) {
    for (var row = 0; row < _BLOCK; ++row, at += stride)
      canvas.AsSpan(at, _BLOCK).Fill(colour);
  }
}
