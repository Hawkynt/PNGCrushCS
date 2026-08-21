using System;
using System.Collections.Generic;
using System.Text;

namespace FileFormat.Codecs.Theora.Tests;

/// <summary>How a built stream's headers and frames differ from the ordinary ones.</summary>
/// <remarks>Public because NUnit reaches test case sources from outside the assembly.</remarks>
public sealed class TheoraTestOptions {

  /// <summary>The coded frame's width in macro blocks; the frame is sixteen times this.</summary>
  public int MacroBlocksWide { get; init; } = 1;

  public int MacroBlocksHigh { get; init; } = 1;

  /// <summary>The displayable picture, which defaults to the whole coded frame.</summary>
  public int PictureWidth { get; init; }

  public int PictureHeight { get; init; }

  public int PictureX { get; init; }

  public int PictureY { get; init; }

  /// <summary>0 is 4:2:0, 1 is reserved, 2 is 4:2:2, 3 is 4:4:4.</summary>
  public int PixelFormat { get; init; }

  public int VersionMajor { get; init; } = 3;

  public int VersionMinor { get; init; } = 2;

  /// <summary>Written into the identification header's three reserved bits.</summary>
  public int ReservedBits { get; init; }

  /// <summary>
  /// The value every entry of the one base matrix takes.
  /// </summary>
  /// <remarks>
  /// With a scale of 100 this multiplies out to a quantiser of four times it, so the default of 16
  /// gives 64 for every coefficient of every matrix and makes the arithmetic in a test's comment
  /// something a reader can follow.
  /// </remarks>
  public int BaseMatrixValue { get; init; } = 16;

  /// <summary>The value every entry of both scale tables takes.</summary>
  public int ScaleValue { get; init; } = 100;

  /// <summary>The value every loop filter limit takes; zero disables the filter.</summary>
  public int LoopFilterLimit { get; init; }

  /// <summary>Writes a Huffman table with more entries than the format allows.</summary>
  public bool OversizedHuffmanTable { get; init; }

  /// <summary>Writes quant ranges whose sizes sum past the end of the quantisation scale.</summary>
  public bool OverlongQuantRanges { get; init; }
}

/// <summary>
/// Builds Theora headers and frames a bit at a time, so a test can put a token exactly where it
/// wants it.
/// </summary>
/// <remarks>
/// Everything asserted against a stream built here was worked out from the specification rather than
/// recorded from a run, so that where a number disagrees with the decoder the arithmetic in the
/// test's comment says which of the two is wrong. The decoder's arithmetic itself was checked by
/// decoding twenty-four streams here and in ffmpeg and comparing the sample planes frame by frame;
/// what these add is what that comparison cannot reach, which is mostly the refusals — by definition
/// no valid stream produces one.
/// </remarks>
internal sealed class TheoraTestStream {

  private readonly List<byte> _bytes = [];
  private int _bitCount;
  private int _partial;

  /// <summary>The keyframe granule shift libtheora writes, and this builder's.</summary>
  internal const int GRANULE_SHIFT = 6;

  /// <summary>Writes an unsigned integer of the given width, most significant bit first.</summary>
  internal TheoraTestStream Bits(int count, uint value) {
    for (var i = count - 1; i >= 0; --i) {
      this._partial = (this._partial << 1) | (int)((value >> i) & 1);
      if (++this._bitCount != 8)
        continue;

      this._bytes.Add((byte)this._partial);
      this._partial = 0;
      this._bitCount = 0;
    }

    return this;
  }

  internal TheoraTestStream Bit(int value) => this.Bits(1, (uint)value);

  /// <summary>Finishes the packet, padding the last byte with the zeroes an encoder must write.</summary>
  internal byte[] Finish() {
    if (this._bitCount > 0)
      this._bytes.Add((byte)(this._partial << (8 - this._bitCount)));

    return this._bytes.ToArray();
  }

  // ============================================================================================
  // Headers
  // ============================================================================================

  /// <summary>The identification header — section 6.2, forty-two bytes.</summary>
  internal static byte[] IdentificationHeader(TheoraTestOptions options) {
    var stream = new TheoraTestStream();
    stream.Bits(8, 0x80);
    _Magic(stream);

    stream.Bits(8, (uint)options.VersionMajor);
    stream.Bits(8, (uint)options.VersionMinor);
    stream.Bits(8, 1);
    stream.Bits(16, (uint)options.MacroBlocksWide);
    stream.Bits(16, (uint)options.MacroBlocksHigh);
    stream.Bits(24, (uint)(options.PictureWidth > 0 ? options.PictureWidth : options.MacroBlocksWide * 16));
    stream.Bits(24, (uint)(options.PictureHeight > 0 ? options.PictureHeight : options.MacroBlocksHigh * 16));
    stream.Bits(8, (uint)options.PictureX);
    stream.Bits(8, (uint)options.PictureY);
    stream.Bits(32, 25);
    stream.Bits(32, 1);
    stream.Bits(24, 1);
    stream.Bits(24, 1);
    stream.Bits(8, 0);
    stream.Bits(24, 0);
    stream.Bits(6, 0);
    stream.Bits(5, GRANULE_SHIFT);
    stream.Bits(2, (uint)options.PixelFormat);
    stream.Bits(3, (uint)options.ReservedBits);
    return stream.Finish();
  }

  /// <summary>The comment header, which decoding reads nothing out of.</summary>
  internal static byte[] CommentHeader() {
    var stream = new TheoraTestStream();
    stream.Bits(8, 0x81);
    _Magic(stream);
    stream.Bits(32, 0);
    stream.Bits(32, 0);
    return stream.Finish();
  }

  /// <summary>
  /// The setup header — section 6.4.
  /// </summary>
  /// <remarks>
  /// The simplest one the format permits: one base matrix, one quant range covering the whole scale,
  /// every set of ranges after the first copied from the one before, and eighty identical Huffman
  /// tables of thirty-two five-bit codes — so a token's code is its own value and a test can write a
  /// token by writing the number.
  /// </remarks>
  internal static byte[] SetupHeader(TheoraTestOptions options) {
    var stream = new TheoraTestStream();
    stream.Bits(8, 0x82);
    _Magic(stream);

    // The loop filter limits: a width, then sixty-four values at that width. A width of zero writes
    // no values at all and leaves every limit zero, which turns the filter off.
    var limitBits = options.LoopFilterLimit == 0 ? 0 : 7;
    stream.Bits(3, (uint)limitBits);
    for (var index = 0; index < 64; ++index)
      stream.Bits(limitBits, (uint)options.LoopFilterLimit);

    // The AC and DC scale tables, seven bits each so that the default of 100 fits.
    for (var table = 0; table < 2; ++table) {
      stream.Bits(4, 6);
      for (var index = 0; index < 64; ++index)
        stream.Bits(7, (uint)options.ScaleValue);
    }

    // One base matrix, so the fields naming a base matrix are zero bits wide.
    stream.Bits(9, 0);
    for (var coefficient = 0; coefficient < 64; ++coefficient)
      stream.Bits(8, (uint)options.BaseMatrixValue);

    for (var type = 0; type < 2; ++type)
    for (var plane = 0; plane < 3; ++plane) {
      if (type > 0 || plane > 0) {
        // Copied from a set already read: from the same plane of the previous quantisation type
        // where there is one, and otherwise from the set read immediately before.
        stream.Bit(0);
        if (type > 0)
          stream.Bit(1);

        continue;
      }

      // One range covering every quantisation index. Its size field is six bits wide, because
      // ilog(62) is six, and the size is the value read plus one — so 62 means 63.
      stream.Bits(6, options.OverlongQuantRanges ? 63u : 62u);
    }

    for (var table = 0; table < 80; ++table)
      _HuffmanTable(stream, options.OversizedHuffmanTable);

    return stream.Finish();
  }

  /// <summary>
  /// One Huffman table: a full tree of thirty-two leaves, each five bits deep.
  /// </summary>
  /// <remarks>
  /// The leaves come out in code order because the tree is written depth first, left before right,
  /// so the leaf reached by the bits of <c>n</c> carries token <c>n</c>. A test writes a token by
  /// writing its value in five bits.
  /// </remarks>
  private static void _HuffmanTable(TheoraTestStream stream, bool oversized) {
    var depth = oversized ? 6 : 5;
    _Node(0, 0);
    return;

    void _Node(int level, int code) {
      if (level == depth) {
        stream.Bit(1);
        stream.Bits(5, (uint)(code & 31));
        return;
      }

      stream.Bit(0);
      _Node(level + 1, code << 1);
      _Node(level + 1, (code << 1) | 1);
    }
  }

  /// <summary>The three header packets, framed the way a container hands them across.</summary>
  /// <remarks>
  /// Xiph lacing: a count of packets less one, then the length of every packet but the last as a run
  /// of 255s and a remainder, then the packets end to end. The last length is not stated because it
  /// is whatever is left.
  /// </remarks>
  internal static byte[] CodecPrivateData(TheoraTestOptions? options = null) {
    options ??= new();
    return Lace([IdentificationHeader(options), CommentHeader(), SetupHeader(options)]);
  }

  /// <summary>Frames any list of packets in Xiph lacing.</summary>
  internal static byte[] Lace(IReadOnlyList<byte[]> packets) {
    var result = new List<byte> { (byte)(packets.Count - 1) };

    for (var packet = 0; packet < packets.Count - 1; ++packet) {
      var remaining = packets[packet].Length;
      while (remaining >= 255) {
        result.Add(255);
        remaining -= 255;
      }

      result.Add((byte)remaining);
    }

    foreach (var packet in packets)
      result.AddRange(packet);

    return result.ToArray();
  }

  private static void _Magic(TheoraTestStream stream) {
    foreach (var letter in Encoding.ASCII.GetBytes("theora"))
      stream.Bits(8, letter);
  }

  // ============================================================================================
  // Frames
  // ============================================================================================

  /// <summary>
  /// An intra frame in which every block is ended immediately by one end-of-block run.
  /// </summary>
  /// <remarks>
  /// The shortest valid frame there is. Every block of an intra frame is coded, so nothing says
  /// which; every one is INTRA, so no mode or motion vector is written; and one end-of-block token
  /// with a run of zero — which the format defines as "every coded block not yet finished" — ends
  /// all of them at the first coefficient.
  /// </remarks>
  internal static byte[] EmptyIntraFrame(int quantisationIndex = 0) {
    var stream = _IntraFrameHeader(quantisationIndex);

    // The first coefficient pass: the two codebook choices, then the run.
    stream.Bits(4, 0);
    stream.Bits(4, 0);
    _EndOfBlockRun(stream, 0);

    // The second pass reads its codebook choices even though no block is left to use them.
    stream.Bits(4, 0);
    stream.Bits(4, 0);
    return stream.Finish();
  }

  /// <summary>
  /// An intra frame whose first block carries one direct-current coefficient and nothing else.
  /// </summary>
  /// <param name="token">The coefficient token: 9 is +1, 10 is −1, 11 is +2, 12 is −2.</param>
  internal static byte[] IntraFrameWithDirectCurrent(int token, int quantisationIndex = 0) {
    var stream = _IntraFrameHeader(quantisationIndex);

    stream.Bits(4, 0);
    stream.Bits(4, 0);
    // The first block in coded order takes the coefficient; one end-of-block run finishes the rest.
    stream.Bits(5, (uint)token);
    _EndOfBlockRun(stream, 0);

    // The first block is now at the second coefficient, and the run started above still has one
    // marker left over to end it with, so nothing more is written.
    stream.Bits(4, 0);
    stream.Bits(4, 0);
    return stream.Finish();
  }

  /// <summary>
  /// An inter frame in which nothing is coded, which reconstructs to a copy of the previous frame.
  /// </summary>
  /// <remarks>
  /// Every super block is written as fully uncoded, so no mode, no motion vector and no coefficient
  /// follows. The one-bit motion vector coding flag is written all the same: section 7.5.2 reads it
  /// whether or not any vector needs it.
  /// </remarks>
  internal static byte[] EmptyInterFrame(int superBlocks, int quantisationIndex = 0) {
    var stream = new TheoraTestStream();
    stream.Bit(0);
    stream.Bit(1);
    stream.Bits(6, (uint)quantisationIndex);
    stream.Bit(0);

    // No super block is partially coded, and none of them is fully coded either.
    _LongRun(stream, 0, superBlocks);
    _LongRun(stream, 0, superBlocks);

    // The mode coding scheme, which is read even though no macro block has a coded luma block.
    stream.Bits(3, 7);
    stream.Bit(0);

    // Both coefficient passes still state their codebooks.
    stream.Bits(4, 0);
    stream.Bits(4, 0);
    stream.Bits(4, 0);
    stream.Bits(4, 0);
    return stream.Finish();
  }

  private static TheoraTestStream _IntraFrameHeader(int quantisationIndex) {
    var stream = new TheoraTestStream();
    stream.Bit(0);
    stream.Bit(0);
    stream.Bits(6, (uint)quantisationIndex);
    stream.Bit(0);
    stream.Bits(3, 0);
    return stream;
  }

  /// <summary>Writes end-of-block token 6, whose twelve-bit run of zero means every unfinished block.</summary>
  private static void _EndOfBlockRun(TheoraTestStream stream, uint run) {
    stream.Bits(5, 6);
    stream.Bits(12, run);
  }

  /// <summary>
  /// Writes a run-length coded bit string of one run — section 7.2.1.
  /// </summary>
  /// <remarks>
  /// The value, then a unary code saying which range the length falls in, then the offset within it.
  /// Only the ranges a test needs are written.
  /// </remarks>
  private static void _LongRun(TheoraTestStream stream, int value, int length) {
    stream.Bit(value);

    int[] starts = [1, 2, 4, 6, 10, 18, 34];
    int[] extraBits = [0, 1, 1, 2, 3, 4, 12];

    for (var code = 0; code < starts.Length; ++code) {
      var span = 1 << extraBits[code];
      if (length >= starts[code] + span)
        continue;

      // The unary prefix: `code` ones and a zero, except for the last code which is all ones.
      for (var i = 0; i < code; ++i)
        stream.Bit(1);

      if (code < starts.Length - 1)
        stream.Bit(0);

      stream.Bits(extraBits[code], (uint)(length - starts[code]));
      return;
    }

    throw new ArgumentOutOfRangeException(nameof(length), length, "This builder writes one run, and this one is longer than a single code can state.");
  }
}
