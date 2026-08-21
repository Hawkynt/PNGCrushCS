using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.Mpeg4.Tests;

/// <summary>
/// Writes MPEG-4 Part 2 visual bitstreams a bit at a time, so a test can state exactly which syntax
/// it is exercising.
/// </summary>
/// <remarks>
/// Every stream in this library's tests is built rather than checked in, and for a codec that matters
/// more than usual: the paths worth testing are the ones a real encoder never produces. ffmpeg's
/// MPEG-4 encoder never emits an interlaced layer, a sprite, a data-partitioned packet, a scalable
/// layer or a shaped object, so comparing against it — which is how the decoder's arithmetic was
/// checked — cannot reach any of the refusals. These can.
/// <para/>
/// The video object layer is written from its defaults with each field overridable, because almost
/// every test here changes exactly one of them and expects the decoder to refuse or accept because of
/// that one. Writing each stream out field by field would bury which field the test was about.
/// </remarks>
internal sealed class Mpeg4TestStream {

  private readonly List<byte> _bytes = [];
  private int _partial;
  private int _partialBits;

  /// <summary>Appends the low <paramref name="count"/> bits of a value, most significant first.</summary>
  internal Mpeg4TestStream Bits(int value, int count) {
    for (var i = count - 1; i >= 0; --i)
      this._Bit((value >> i) & 1);

    return this;
  }

  /// <summary>Appends a code written the way the standard prints it; spaces are grouping.</summary>
  internal Mpeg4TestStream Code(string code) {
    foreach (var character in code)
      switch (character) {
        case '0': this._Bit(0); break;
        case '1': this._Bit(1); break;
        case ' ': break;
        default: throw new ArgumentException($"'{character}' is not a bit.", nameof(code));
      }

    return this;
  }

  /// <summary>Pads with the zero bit and the ones that follow it, which is how every header ends.</summary>
  internal Mpeg4TestStream NextStartCode() {
    this._Bit(0);
    while (this._partialBits != 0)
      this._Bit(1);

    return this;
  }

  /// <summary>Appends a start code: <c>00 00 01</c> and the byte saying what follows.</summary>
  internal Mpeg4TestStream StartCode(byte code) {
    while (this._partialBits != 0)
      this._Bit(0);

    this._bytes.Add(0x00);
    this._bytes.Add(0x00);
    this._bytes.Add(0x01);
    this._bytes.Add(code);
    return this;
  }

  internal byte[] ToArray() {
    while (this._partialBits != 0)
      this._Bit(0);

    return this._bytes.ToArray();
  }

  // --------------------------------------------------------------------------------------------
  // Headers — ISO/IEC 14496-2, 6.2
  // --------------------------------------------------------------------------------------------

  /// <summary>The visual object sequence and visual object headers, which carry nothing this decoder reads.</summary>
  internal Mpeg4TestStream VisualObjectSequence(int profileAndLevel = 0xF1) {
    this.StartCode(Mpeg4StartCode.VisualObjectSequence).Bits(profileAndLevel, 8);
    this.StartCode(Mpeg4StartCode.VisualObject)
      .Bits(0, 1)   // is_visual_object_identifier
      .Bits(1, 4)   // visual_object_type: video
      .Bits(0, 1)   // video_signal_type
      .NextStartCode();
    this.StartCode(Mpeg4StartCode.FirstVideoObject);
    return this;
  }

  /// <summary>
  /// A video object layer header. Every field the decoder reads is a parameter, so that a test can
  /// state the one it is about and leave the rest alone.
  /// </summary>
  internal Mpeg4TestStream VideoObjectLayer(
    int width = 16, int height = 16, int verid = 1, int objectType = 1,
    int shape = 0, int chromaFormat = 1, bool statesControlParameters = true, bool lowDelay = true,
    bool interlaced = false, bool overlappedMotionCompensation = false, int spriteEnable = 0,
    bool notEightBit = false, int quantiserPrecision = 5, int bitsPerPixel = 8,
    bool mpegQuantisation = false, byte[]? intraMatrix = null, byte[]? nonIntraMatrix = null,
    bool quarterSample = false, bool complexityEstimation = false, bool resyncMarkers = false,
    bool dataPartitioned = false, bool newPredictive = false, bool reducedResolution = false,
    bool scalable = false, int timeIncrementResolution = 25,
    int firstMarker = 1, int widthMarker = 1) {
    this.StartCode(Mpeg4StartCode.FirstVideoObjectLayer);
    this.Bits(0, 1);                 // random_accessible_vol
    this.Bits(objectType, 8);

    this.Bits(1, 1);                 // is_object_layer_identifier
    this.Bits(verid, 4);
    this.Bits(1, 3);                 // video_object_layer_priority

    this.Bits(1, 4);                 // aspect_ratio_info: square

    this.Bits(statesControlParameters ? 1 : 0, 1);
    if (statesControlParameters) {
      this.Bits(chromaFormat, 2);
      this.Bits(lowDelay ? 1 : 0, 1);
      this.Bits(0, 1);               // vbv_parameters
    }

    this.Bits(shape, 2);
    this.Bits(firstMarker, 1);
    this.Bits(timeIncrementResolution, 16);
    this.Bits(1, 1);
    this.Bits(0, 1);                 // fixed_vop_rate

    this.Bits(1, 1);
    this.Bits(width, 13);
    this.Bits(widthMarker, 1);
    this.Bits(height, 13);
    this.Bits(1, 1);

    this.Bits(interlaced ? 1 : 0, 1);
    this.Bits(overlappedMotionCompensation ? 0 : 1, 1);
    this.Bits(spriteEnable, verid == 1 ? 1 : 2);

    this.Bits(notEightBit ? 1 : 0, 1);
    if (notEightBit) {
      this.Bits(quantiserPrecision, 4);
      this.Bits(bitsPerPixel, 4);
    }

    this.Bits(mpegQuantisation ? 1 : 0, 1);
    if (mpegQuantisation) {
      this.Bits(intraMatrix == null ? 0 : 1, 1);
      if (intraMatrix != null)
        foreach (var weight in intraMatrix)
          this.Bits(weight, 8);

      this.Bits(nonIntraMatrix == null ? 0 : 1, 1);
      if (nonIntraMatrix != null)
        foreach (var weight in nonIntraMatrix)
          this.Bits(weight, 8);
    }

    if (verid != 1)
      this.Bits(quarterSample ? 1 : 0, 1);

    this.Bits(complexityEstimation ? 0 : 1, 1);
    this.Bits(resyncMarkers ? 0 : 1, 1);
    this.Bits(dataPartitioned ? 1 : 0, 1);

    if (verid != 1) {
      this.Bits(newPredictive ? 1 : 0, 1);
      this.Bits(reducedResolution ? 1 : 0, 1);
    }

    this.Bits(scalable ? 1 : 0, 1);
    return this.NextStartCode();
  }

  /// <summary>A video object plane header.</summary>
  internal Mpeg4TestStream VideoObjectPlane(
    int codingType = 0, int quantiser = 1, int timeIncrement = 0, int seconds = 0,
    bool isCoded = true, int rounding = 0, int intraDcThreshold = 0,
    int forwardFCode = 1, int backwardFCode = 1, int timeIncrementBits = 5, int quantiserPrecision = 5) {
    this.StartCode(Mpeg4StartCode.VideoObjectPlane);
    this.Bits(codingType, 2);

    for (var second = 0; second < seconds; ++second)
      this.Bits(1, 1);

    this.Bits(0, 1);                 // modulo_time_base terminator
    this.Bits(1, 1);                 // marker
    this.Bits(timeIncrement, timeIncrementBits);
    this.Bits(1, 1);                 // marker
    this.Bits(isCoded ? 1 : 0, 1);
    if (!isCoded)
      return this;

    if (codingType == 1)
      this.Bits(rounding, 1);

    this.Bits(intraDcThreshold, 3);
    this.Bits(quantiser, quantiserPrecision);

    if (codingType != 0)
      this.Bits(forwardFCode, 3);

    if (codingType == 2)
      this.Bits(backwardFCode, 3);

    return this;
  }

  /// <summary>
  /// A resync marker and the video packet header behind it (ISO/IEC 14496-2, 6.2.5.2).
  /// </summary>
  /// <remarks>
  /// The stuffing in front of it is a zero bit and then ones until the next byte boundary — and a
  /// whole byte of it when the position is already aligned, which is what makes the stuffing
  /// recognisable rather than merely absent.
  /// </remarks>
  internal Mpeg4TestStream ResyncMarker(
    int macroblockNumber, int numberBits, int quantiser, int quantiserPrecision = 5, int markerLength = 17) {
    this._Bit(0);
    while (this._partialBits != 0)
      this._Bit(1);

    this.Bits(1, markerLength);
    this.Bits(macroblockNumber, numberBits);
    this.Bits(quantiser, quantiserPrecision);
    return this.Bits(0, 1);
  }

  // --------------------------------------------------------------------------------------------
  // Macroblock and block layers — ISO/IEC 14496-2, 6.2.6 and 6.2.7
  // --------------------------------------------------------------------------------------------

  /// <summary>MCBPC for an intra macroblock of an I-VOP with no coded chrominance (Table B-6).</summary>
  internal const string IntraMacroblock = "1";

  /// <summary>MCBPC for an intra macroblock carrying DQUANT (Table B-6, index 4).</summary>
  internal const string IntraMacroblockWithQuantiser = "0001";

  /// <summary>The stuffing codeword both MCBPC tables carry.</summary>
  internal const string MacroblockStuffing = "0000 0000 1";

  /// <summary>MCBPC for a predicted macroblock with no coded chrominance (Table B-7).</summary>
  internal const string InterMacroblock = "1";

  /// <summary>MCBPC for a predicted macroblock with four motion vectors (Table B-7, index 8).</summary>
  internal const string InterMacroblockWithFourVectors = "010";

  /// <summary>MCBPC for an intra macroblock inside a predicted picture (Table B-7, index 12).</summary>
  internal const string IntraMacroblockInPredictedPicture = "0001 1";

  /// <summary>CBPY whose intra reading is 0000: no luminance block carries coefficients.</summary>
  internal const string NoLuminanceCoded = "0011";

  /// <summary>CBPY whose intra reading is 1000: the first luminance block only.</summary>
  internal const string FirstLuminanceCoded = "0001 0";

  /// <summary>CBPY whose intra reading is 1111, which an inter macroblock reads as none coded.</summary>
  internal const string AllLuminanceCoded = "11";

  /// <summary>The coefficient escape of both TCOEF tables.</summary>
  internal const string CoefficientEscape = "0000 011";

  /// <summary>
  /// The whole of one intra macroblock whose six blocks are flat and all the same value.
  /// </summary>
  /// <remarks>
  /// The differential is carried by the first luminance block alone and the other three code zero,
  /// because each of them predicts from the one before it — so a macroblock of one value is a
  /// macroblock whose first block states the difference and whose others state none. Repeating the
  /// differential in all four would produce a macroblock that steps upward across its own quadrants,
  /// which is a picture nobody meant to build.
  /// </remarks>
  internal Mpeg4TestStream FlatIntraMacroblock(int luminanceDc = 0, int chrominanceDc = 0) {
    this.Code(IntraMacroblock).Bits(0, 1).Code(NoLuminanceCoded);
    this.IntraDc(luminanceDc, luminance: true);
    for (var block = 1; block < 4; ++block)
      this.IntraDc(0, luminance: true);

    this.IntraDc(chrominanceDc, luminance: false);
    return this.IntraDc(0, luminance: false);
  }

  /// <summary>Fills <paramref name="count"/> macroblocks with flat intra ones.</summary>
  internal Mpeg4TestStream FlatIntraMacroblocks(int count, int luminanceDc = 0, int chrominanceDc = 0) {
    for (var macroblock = 0; macroblock < count; ++macroblock)
      this.FlatIntraMacroblock(luminanceDc, chrominanceDc);

    return this;
  }

  /// <summary>
  /// An intra block's DC, as its own size code and value (ISO/IEC 14496-2, Tables B-13 to B-15).
  /// </summary>
  internal Mpeg4TestStream IntraDc(int differential, bool luminance) {
    var size = 0;
    while (differential >= 1 << size || differential < -((1 << size) - 1))
      ++size;

    this.Code(luminance ? _LuminanceDcSize(size) : _ChrominanceDcSize(size));
    if (size == 0)
      return this;

    this.Bits(differential > 0 ? differential : differential + (1 << size) - 1, size);
    if (size > 8)
      this.Bits(1, 1);

    return this;
  }

  /// <summary>The third escape form: the whole triple, with a marker on each side of the level.</summary>
  internal Mpeg4TestStream EscapedCoefficient(bool last, int run, int level) {
    this.Code(CoefficientEscape).Bits(3, 2);
    this.Bits(last ? 1 : 0, 1);
    this.Bits(run, 6);
    this.Bits(1, 1);
    this.Bits(level & 0xFFF, 12);
    return this.Bits(1, 1);
  }

  /// <summary>Table B-13, the size of a luminance DC differential.</summary>
  private static string _LuminanceDcSize(int size) => size switch {
    0 => "011", 1 => "11", 2 => "10", 3 => "010", 4 => "001", 5 => "0001", 6 => "0000 1",
    7 => "0000 01", 8 => "0000 001", 9 => "0000 0001", 10 => "0000 0000 1", 11 => "0000 0000 01",
    _ => "0000 0000 001",
  };

  /// <summary>Table B-14, the same for chrominance.</summary>
  private static string _ChrominanceDcSize(int size) => size switch {
    0 => "11", 1 => "10", 2 => "01", 3 => "001", 4 => "0001", 5 => "0000 1", 6 => "0000 01",
    7 => "0000 001", 8 => "0000 0001", 9 => "0000 0000 1", 10 => "0000 0000 01", 11 => "0000 0000 001",
    _ => "0000 0000 0001",
  };

  private void _Bit(int bit) {
    this._partial = (this._partial << 1) | bit;
    if (++this._partialBits != 8)
      return;

    this._bytes.Add((byte)this._partial);
    this._partial = 0;
    this._partialBits = 0;
  }
}
