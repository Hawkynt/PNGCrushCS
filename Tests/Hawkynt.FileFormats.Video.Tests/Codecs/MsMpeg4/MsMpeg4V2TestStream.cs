using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.MsMpeg4.Tests;

/// <summary>
/// Writes Microsoft MPEG-4 version 2 pictures a bit at a time, so a test can state exactly which
/// syntax it is exercising.
/// </summary>
/// <remarks>
/// Every stream in this library's tests is built rather than checked in, and for this codec that
/// matters twice over. Once for the usual reason: the paths worth testing are the ones a real encoder
/// never produces, and ffmpeg's encoder here never emits a predicted macroblock that codes one
/// chrominance block and not the other, nor an intra macroblock inside a predicted picture that codes
/// any chrominance at all — four of the eight macroblock types are unreachable from an encoder. And
/// once because the format has no published specification, so the codes below were derived from the
/// bitstream, and a test that wrote them by asking the decoder's own tables would be checking the
/// decoder against itself. They are written out here instead, so the two statements are independent
/// and a slip in either is a failing test rather than a silent agreement.
/// </remarks>
internal sealed class MsMpeg4V2TestStream {

  /// <summary>Table B-8 (CBPY), the pattern nought and the pattern fifteen.</summary>
  internal const string LuminancePatternNone = "0011";

  internal const string LuminancePatternAll = "11";

  /// <summary>v2_intra_cbpc: no chrominance block coded, and both of them.</summary>
  internal const string IntraChromaNone = "1";

  internal const string IntraChromaBoth = "01";

  /// <summary>v2_intra_cbpc: Cb alone, and Cr alone.</summary>
  internal const string IntraChromaCbOnly = "001";

  internal const string IntraChromaCrOnly = "000";

  /// <summary>v2_mb_type, predicted: no chrominance, Cr, Cb, both.</summary>
  internal const string PredictedChromaNone = "1";

  internal const string PredictedChromaCrOnly = "00";
  internal const string PredictedChromaCbOnly = "011";
  internal const string PredictedChromaBoth = "0100 1";

  /// <summary>v2_mb_type, intra inside a predicted picture: no chrominance, Cr, Cb, both.</summary>
  internal const string IntraInPredictedChromaNone = "0101";

  internal const string IntraInPredictedChromaCrOnly = "0100 001";
  internal const string IntraInPredictedChromaCbOnly = "0100 000";
  internal const string IntraInPredictedChromaBoth = "0100 01";

  /// <summary>A motion vector difference of nought, which carries no sign bit after it.</summary>
  internal const string ZeroVectorComponent = "1";

  /// <summary>
  /// One coefficient: the last of its block, no run before it, magnitude one, positive.
  /// </summary>
  /// <remarks>
  /// The same four bits in both coefficient tables — ISO/IEC 14496-2 Table B-16 row 67 and Table B-17
  /// row 58 — which is why this one constant serves a luminance block and a chrominance block alike.
  /// </remarks>
  internal const string SmallestCoefficient = "0111 0";

  /// <summary>The number of slices, biased the way the picture header states it.</summary>
  private const int _SLICE_COUNT_BIAS = 0x16;

  /// <summary>ISO/IEC 14496-2 Table B-13 with every bit inverted, by size.</summary>
  private static readonly string[] _LuminanceDcSize = [
    "100", "00", "01", "101", "110", "1110", "11110", "111110", "1111110", "11111110",
    "111111110", "1111111110", "11111111110",
  ];

  /// <summary>Table B-14 with every bit inverted.</summary>
  private static readonly string[] _ChrominanceDcSize = [
    "00", "01", "10", "110", "1110", "11110", "111110", "1111110", "11111110", "111111110",
    "1111111110", "11111111110", "111111111110",
  ];

  /// <summary>Table B-12 without its sign bit, by magnitude, for the magnitudes tests reach.</summary>
  private static readonly string[] _VectorMagnitude = [
    "1", "01", "001", "0001", "000011", "0000101", "0000100", "0000011", "000001011", "000001010",
    "000001001",
  ];

  private readonly List<byte> _bytes = [];
  private int _partial;
  private int _partialBits;

  /// <summary>Appends the low <paramref name="count"/> bits of a value, most significant first.</summary>
  internal MsMpeg4V2TestStream Bits(int value, int count) {
    for (var i = count - 1; i >= 0; --i)
      this._Bit((value >> i) & 1);

    return this;
  }

  /// <summary>Appends a code written as its bits; spaces are grouping and are ignored.</summary>
  internal MsMpeg4V2TestStream Code(string code) {
    foreach (var character in code)
      switch (character) {
        case '0': this._Bit(0); break;
        case '1': this._Bit(1); break;
        case ' ': break;
        default: throw new ArgumentException($"'{character}' is not a bit.", nameof(code));
      }

    return this;
  }

  /// <summary>Appends an intra picture's header.</summary>
  internal MsMpeg4V2TestStream IntraPictureHeader(int quantiser, int slices = 1)
    => this.Bits(0, 2).Bits(quantiser, 5).Bits(_SLICE_COUNT_BIAS + slices, 5);

  /// <summary>Appends a predicted picture's header.</summary>
  internal MsMpeg4V2TestStream PredictedPictureHeader(int quantiser, bool skipBitsArePresent)
    => this.Bits(1, 2).Bits(quantiser, 5).Bits(skipBitsArePresent ? 1 : 0, 1);

  /// <summary>Appends an intra DC differential, with its size code and its magnitude bits.</summary>
  internal MsMpeg4V2TestStream Dc(int differential, bool luminance) {
    var size = differential == 0 ? 0 : _BitsNeeded(Math.Abs(differential));
    this.Code((luminance ? _LuminanceDcSize : _ChrominanceDcSize)[size]);
    if (size == 0)
      return this;

    // The magnitude's own top bit carries the sign, in the mapping JPEG uses: a negative differential
    // is written as the value just below the positive range of the same width.
    var value = differential > 0 ? differential : differential + (1 << size) - 1;
    this.Bits(value, size);
    if (size > 8)
      this.Bits(1, 1);

    return this;
  }

  /// <summary>Appends a motion vector difference: its magnitude, and a sign bit unless it is nought.</summary>
  internal MsMpeg4V2TestStream VectorComponent(int difference) {
    var magnitude = Math.Abs(difference);
    this.Code(_VectorMagnitude[magnitude]);
    return magnitude == 0 ? this : this.Bits(difference < 0 ? 1 : 0, 1);
  }

  /// <summary>
  /// Appends the extension header every intra picture ends with.
  /// </summary>
  /// <remarks>
  /// Frames a second and a bit rate, and nothing in either changes a sample. It is written because it
  /// is where an intra picture really ends, so a test that left it out would be checking the decoder
  /// against a picture no encoder produces.
  /// </remarks>
  internal MsMpeg4V2TestStream ExtensionHeader(int framesPerSecond = 25, int kilobitsPerSecond = 200)
    => this.Bits(framesPerSecond, 5).Bits(kilobitsPerSecond, 11);

  /// <summary>The bits written so far, padded with zeroes to the next byte.</summary>
  internal byte[] ToArray() {
    var result = new List<byte>(this._bytes);
    if (this._partialBits > 0)
      result.Add((byte)(this._partial << (8 - this._partialBits)));

    return [.. result];
  }

  // ============================================================================================
  // Whole pictures, for the tests that are about what comes out rather than about one field
  // ============================================================================================

  /// <summary>
  /// An intra picture of one flat grey, and one macroblock's first block moved by a differential.
  /// </summary>
  /// <remarks>
  /// Every block's DC differential is nought except the very first, whose neighbours are all outside
  /// the picture and so all count as mid-grey. So the picture comes out one flat level throughout,
  /// which is what makes it worth building: the level is <c>128 + differential</c> and nothing else in
  /// the reconstruction can move it.
  /// </remarks>
  internal static byte[] FlatIntraPicture(
    int macroblockWidth, int macroblockHeight, int quantiser, int differential = 0, int slices = 1) {
    var s = new MsMpeg4V2TestStream().IntraPictureHeader(quantiser, slices);
    for (var address = 0; address < macroblockWidth * macroblockHeight; ++address) {
      s.Code(IntraChromaNone).Bits(0, 1).Code(LuminancePatternNone);
      for (var block = 0; block < 6; ++block)
        s.Dc(address == 0 && block == 0 ? differential : 0, block < 4);
    }

    return s.ExtensionHeader().ToArray();
  }

  /// <summary>A predicted picture in which every macroblock is skipped, so it repeats its reference.</summary>
  internal static byte[] SkippedPredictedPicture(int macroblockWidth, int macroblockHeight, int quantiser) {
    var s = new MsMpeg4V2TestStream().PredictedPictureHeader(quantiser, skipBitsArePresent: true);
    for (var address = 0; address < macroblockWidth * macroblockHeight; ++address)
      s.Bits(1, 1);

    return s.ToArray();
  }

  /// <summary>
  /// A predicted picture whose macroblocks carry a motion vector and no residual at all.
  /// </summary>
  /// <remarks>
  /// The luminance pattern is written as fifteen and means nought: a predicted macroblock states its
  /// luminance pattern inverted unless both of its chrominance bits are set, which is the one rule of
  /// this format's macroblock layer with no counterpart in the standard.
  /// </remarks>
  internal static byte[] MovedPredictedPicture(
    int macroblockWidth, int macroblockHeight, int quantiser, int vectorX, int vectorY) {
    var s = new MsMpeg4V2TestStream().PredictedPictureHeader(quantiser, skipBitsArePresent: false);
    for (var address = 0; address < macroblockWidth * macroblockHeight; ++address) {
      s.Code(PredictedChromaNone).Code(LuminancePatternAll);

      // Only the first macroblock states the vector; the rest predict it from their neighbours and
      // state a difference of nought, so the whole picture moves by the same amount.
      s.VectorComponent(address == 0 ? vectorX : 0).VectorComponent(address == 0 ? vectorY : 0);
    }

    return s.ToArray();
  }

  private static int _BitsNeeded(int magnitude) {
    var bits = 0;
    while (magnitude > 0) {
      ++bits;
      magnitude >>= 1;
    }

    return bits;
  }

  private void _Bit(int bit) {
    this._partial = (this._partial << 1) | (bit & 1);
    if (++this._partialBits != 8)
      return;

    this._bytes.Add((byte)this._partial);
    this._partial = 0;
    this._partialBits = 0;
  }
}
