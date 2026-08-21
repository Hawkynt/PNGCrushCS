using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.Vp8.Tests;

/// <summary>
/// Writes VP8 frames, so a test can state exactly which syntax it is exercising.
/// </summary>
/// <remarks>
/// Every stream in these tests is built rather than checked in. For VP8 that matters twice over.
/// The decoder's arithmetic was checked by decoding real streams here and in ffmpeg and comparing
/// the sample planes, which settles everything an encoder emits and nothing it does not —
/// and libvpx does not emit a reserved bitstream version, a broken start code, or a frame that names
/// a reference buffer before one exists. Those are the paths that have to refuse, and a refusal is
/// by definition unreachable from a valid file.
/// <para/>
/// The other half is the boolean coder. Everything in a VP8 frame past its first ten bytes is
/// arithmetic-coded, so a stream cannot be assembled by writing bits: it has to be encoded, with the
/// same probabilities the decoder will read it with. <see cref="Bool"/> below is the encoder of RFC
/// 6386 section 7.3, which is the counterpart of the decoder under test rather than a copy of it.
/// </remarks>
internal sealed class Vp8TestStream {

  private readonly List<byte> _output = [];
  private uint _range = 255;
  private uint _bottom;
  private int _bitCount = 24;

  // ============================================================================================
  // The boolean encoder — RFC 6386, 7.3
  // ============================================================================================

  /// <summary>Writes one bool whose chance of being zero the reader will take as <paramref name="probability"/>/256.</summary>
  internal Vp8TestStream Bool(int probability, int value) {
    var split = 1 + (((this._range - 1) * (uint)probability) >> 8);
    if (value != 0) {
      this._bottom += split;
      this._range -= split;
    } else
      this._range = split;

    while (this._range < 128) {
      this._range <<= 1;
      if ((this._bottom & (1u << 31)) != 0)
        this._CarryIntoOutput();

      this._bottom <<= 1;
      if (--this._bitCount != 0)
        continue;

      this._output.Add((byte)(this._bottom >> 24));
      this._bottom &= (1u << 24) - 1;
      this._bitCount = 8;
    }

    return this;
  }

  /// <summary>Writes a bit at even odds.</summary>
  internal Vp8TestStream Flag(int value) => this.Bool(128, value);

  /// <summary>Writes an unsigned value, high-order bit first, each bit at even odds.</summary>
  internal Vp8TestStream Literal(int bits, int value) {
    while (bits-- > 0)
      this.Flag((value >> bits) & 1);

    return this;
  }

  /// <summary>
  /// Writes a tree-coded value as the string of bits RFC 6386 prints for it, and answers the value
  /// the tree gives that string.
  /// </summary>
  /// <remarks>
  /// The code comes from the standard and the tree comes from the decoder, so a test that writes a
  /// code and asserts what it decodes to is checking one against the other. Writing the value and
  /// letting the tree find the path would check the tree against itself.
  /// </remarks>
  internal int Coded(ReadOnlySpan<sbyte> tree, ReadOnlySpan<byte> probabilities, int offset, string code) {
    var node = 0;
    foreach (var character in code) {
      if (character == ' ')
        continue;

      var bit = character switch {
        '0' => 0,
        '1' => 1,
        _ => throw new ArgumentException($"'{character}' is not a bit.", nameof(code)),
      };

      if (node < 0)
        throw new ArgumentException($"The code '{code}' runs past a leaf of the tree.", nameof(code));

      this.Bool(probabilities[offset + (node >> 1)], bit);
      node = tree[node + bit];
    }

    if (node > 0)
      throw new ArgumentException($"The code '{code}' stops at an interior node of the tree.", nameof(code));

    return -node;
  }

  /// <summary>Finishes the partition and hands back its bytes.</summary>
  internal byte[] Finish() {
    var count = this._bitCount;
    var value = this._bottom;

    if ((value & (1u << (32 - count))) != 0)
      this._CarryIntoOutput();

    value <<= count & 7;
    count >>= 3;
    while (--count >= 0)
      value <<= 8;

    count = 4;
    while (--count >= 0) {
      this._output.Add((byte)(value >> 24));
      value <<= 8;
    }

    return this._output.ToArray();
  }

  private void _CarryIntoOutput() {
    var at = this._output.Count - 1;
    while (at >= 0 && this._output[at] == 255) {
      this._output[at] = 0;
      --at;
    }

    if (at >= 0)
      ++this._output[at];
  }

  // ============================================================================================
  // Whole frames
  // ============================================================================================

  /// <summary>What a built key frame should contain.</summary>
  internal sealed class KeyFrame {
    internal int Width = 16;
    internal int Height = 16;
    internal int LoopFilterLevel;
    internal int QuantiserIndex;

    /// <summary>
    /// The code of the token to place at the first coefficient of every macroblock's Y2 block, or
    /// <c>null</c> for macroblocks that declare themselves free of coefficients.
    /// </summary>
    internal string? Y2DirectCurrentToken;

    /// <summary>The bitstream version to state in the frame tag.</summary>
    internal int Version;
  }

  /// <summary>
  /// Builds a key frame in which every macroblock is DC-predicted, optionally with one coefficient
  /// in its Y2 block and nothing else.
  /// </summary>
  /// <remarks>
  /// DC prediction with nothing above or to the left fills with 128, and every macroblock after the
  /// first averages neighbours that are themselves 128 — so the picture is flat, and any residue
  /// added to it lands on a known value.
  /// </remarks>
  internal static byte[] BuildKeyFrame(KeyFrame frame) {
    var columns = (frame.Width + 15) / 16;
    var rows = (frame.Height + 15) / 16;
    var skipped = frame.Y2DirectCurrentToken == null;

    var header = new Vp8TestStream();
    header.Literal(2, 0); // colour space and clamping type, both reserved as zero
    header.Flag(0); // segmentation disabled
    header.Flag(0); // the normal loop filter
    header.Literal(6, frame.LoopFilterLevel);
    header.Literal(3, 0); // sharpness
    header.Flag(0); // no per-macroblock filter adjustments
    header.Literal(2, 0); // one token partition
    header.Literal(7, frame.QuantiserIndex);
    for (var i = 0; i < 5; ++i)
      header.Flag(0); // no quantiser deltas

    header.Flag(1); // keep the entropy state
    foreach (var probability in Vp8Tables.CoefficientUpdateProbabilities)
      header.Bool(probability, 0); // no token probability updates

    header.Flag(1); // macroblocks may declare themselves free of coefficients
    header.Literal(8, 128); // and the probability that they do not

    for (var macroblock = 0; macroblock < columns * rows; ++macroblock) {
      header.Bool(128, skipped ? 1 : 0);
      // DC_PRED is "100" under the key frame luma tree and "0" under the chroma tree (RFC 6386,
      // 11.2 and 11.4).
      header.Coded(Vp8Trees.KeyFrameLumaMode, Vp8Trees.KeyFrameLumaModeProbabilities, 0, "100");
      header.Coded(Vp8Trees.ChromaMode, Vp8Trees.KeyFrameChromaModeProbabilities, 0, "0");
    }

    var tokens = new Vp8TestStream();
    if (!skipped)
      for (var macroblock = 0; macroblock < columns * rows; ++macroblock)
        _WriteY2OnlyResidue(tokens, frame.Y2DirectCurrentToken!);

    return _Assemble(header.Finish(), tokens.Finish(), frame.Width, frame.Height, frame.Version);
  }

  /// <summary>
  /// Writes the twenty-five blocks of a macroblock: one token in the Y2 block and end-of-block
  /// everywhere else.
  /// </summary>
  private static void _WriteY2OnlyResidue(Vp8TestStream tokens, string token) {
    var probabilities = Vp8Tables.DefaultCoefficientProbabilities;

    // The Y2 block: the given token at the first coefficient, then end-of-block at the second. Its
    // context is zero because nothing above or to the left of this macroblock held anything, and the
    // second position's context is two because the coefficient just written exceeds one.
    tokens.Coded(Vp8Trees.Token, probabilities,
      Vp8Tables.CoefficientProbabilityOffset(Vp8CoefficientPlane.Y2, Vp8Trees.CoefficientBands[0], 0), token);
    tokens.Flag(0); // a positive coefficient
    tokens.Coded(Vp8Trees.Token, probabilities,
      Vp8Tables.CoefficientProbabilityOffset(Vp8CoefficientPlane.Y2, Vp8Trees.CoefficientBands[1], 2), "0");

    // The sixteen luma blocks, which start at the second coefficient because the first is the Y2
    // block's business, and the eight chroma blocks, which start at the first.
    for (var block = 0; block < 16; ++block)
      tokens.Coded(Vp8Trees.Token, probabilities,
        Vp8Tables.CoefficientProbabilityOffset(Vp8CoefficientPlane.LUMA_AFTER_Y2, Vp8Trees.CoefficientBands[1], 0), "0");

    for (var block = 0; block < 8; ++block)
      tokens.Coded(Vp8Trees.Token, probabilities,
        Vp8Tables.CoefficientProbabilityOffset(Vp8CoefficientPlane.CHROMA, Vp8Trees.CoefficientBands[0], 0), "0");
  }

  /// <summary>Puts the frame tag, the key frame header and the two partitions together.</summary>
  private static byte[] _Assemble(byte[] header, byte[] tokens, int width, int height, int version) {
    var tag = ((version & 7) << 1) | (1 << 4) | (header.Length << 5);
    var frame = new byte[10 + header.Length + tokens.Length];

    frame[0] = (byte)tag;
    frame[1] = (byte)(tag >> 8);
    frame[2] = (byte)(tag >> 16);
    frame[3] = 0x9D;
    frame[4] = 0x01;
    frame[5] = 0x2A;
    frame[6] = (byte)width;
    frame[7] = (byte)(width >> 8);
    frame[8] = (byte)height;
    frame[9] = (byte)(height >> 8);

    header.CopyTo(frame, 10);
    tokens.CopyTo(frame, 10 + header.Length);
    return frame;
  }
}
