using System;
using System.IO;
using FileFormat.Codecs.Mpeg4;

namespace FileFormat.Codecs.MsMpeg4;

/// <summary>
/// Decodes one coded picture of Microsoft's MPEG-4: its macroblocks and their blocks.
/// </summary>
/// <remarks>
/// One of these exists for the length of one picture. Everything it holds is reset by the next one —
/// the vectors later macroblocks predict from, the coefficients the intra prediction reaches back
/// into — and a decoder that kept any of them across pictures would still produce a picture, wrong
/// from its second frame onward.
/// <para/>
/// The quantiser is not among them, because there is nothing to keep: all three versions state it once
/// in the picture header and give the macroblock layer no way to change it. That single absence is
/// what makes the alternating current prediction here simpler than the standard's, which has to
/// rescale every predictor by the ratio of two quantisers that may differ.
/// <para/>
/// There is one motion vector for a whole macroblock and never four. None of the three has an
/// equivalent of the standard's INTER4V, so the vector predictors are the neighbouring
/// <i>macroblocks</i>' vectors rather than the neighbouring blocks', and the median of ISO/IEC 14496-2
/// 7.6.2 is taken over those.
/// </remarks>
internal sealed class MsMpeg4PictureDecoder {

  /// <summary>Version 3 writes a whole vector in one codeword, biased so that both halves are unsigned.</summary>
  private const int _VECTOR_BIAS = 32;

  /// <summary>
  /// The range a reconstructed vector is brought back into, in half-samples: thirty-one and a half
  /// whole samples either way.
  /// </summary>
  private const int _VECTOR_RANGE = 64;

  private readonly MsMpeg4Version _version;
  private readonly MsMpeg4PictureHeader _header;
  private readonly Mpeg4Frame _target;
  private readonly Mpeg4Frame? _reference;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly int _rounding;
  private readonly MsMpeg4IntraPrediction _intraPrediction;
  private readonly MsMpeg4BlockDecoder _blocks;

  /// <summary>Each macroblock's motion vector, in half-sample units.</summary>
  private readonly short[] _vectorX;

  private readonly short[] _vectorY;

  /// <summary>Whether each macroblock has been decoded, which the vector predictor's edge rules need.</summary>
  private readonly bool[] _isDecoded;

  /// <summary>
  /// Which luminance blocks of this intra picture carried coefficients — version 3 alone, and only in
  /// an intra picture, where the coded block pattern is stated as a difference from its neighbours'.
  /// </summary>
  private readonly MsMpeg4CodedBlockPrediction? _codedBlock;

  private MsMpeg4PictureDecoder(
    MsMpeg4Version version, MsMpeg4PictureHeader header, Mpeg4Frame target, Mpeg4Frame? reference,
    int macroblockWidth, int macroblockHeight, int rounding) {
    this._version = version;
    this._header = header;
    this._target = target;
    this._reference = reference;
    this._macroblockWidth = macroblockWidth;
    this._macroblockHeight = macroblockHeight;
    this._rounding = rounding;

    var count = macroblockWidth * macroblockHeight;
    this._vectorX = new short[count];
    this._vectorY = new short[count];
    this._isDecoded = new bool[count];
    this._intraPrediction = new(macroblockWidth, macroblockHeight, header.SliceHeight);
    this._blocks = new(version, header);

    this._codedBlock = version == MsMpeg4Version.Version3 && header.CodingType == MsMpeg4PictureHeader.IntraCoded
      ? new(macroblockWidth, macroblockHeight)
      : null;
  }

  /// <summary>The picture being reconstructed.</summary>
  internal Mpeg4Frame Target => this._target;

  /// <summary>Prepares to decode a picture whose header has been read.</summary>
  /// <remarks>
  /// The rounding is handed in rather than derived, because which way the half-sample interpolation
  /// rounds is a property of the run of pictures and not of this one: version 3 may alternate it, and
  /// where it does, this picture rounds the other way from the one before it.
  /// </remarks>
  internal static MsMpeg4PictureDecoder BeginPicture(
    MsMpeg4Version version, MsMpeg4PictureHeader header, Mpeg4Frame target, Mpeg4Frame? reference,
    int macroblockWidth, int macroblockHeight, int rounding) {
    ArgumentNullException.ThrowIfNull(header);
    ArgumentNullException.ThrowIfNull(target);

    if (header.CodingType != MsMpeg4PictureHeader.IntraCoded && reference == null)
      throw new InvalidDataException(
        "A Microsoft MPEG-4 predicted picture arrived before any intra picture, so there is nothing for it to be "
        + "predicted from. Decoding must begin at an intra picture.");

    return new(version, header, target, reference, macroblockWidth, macroblockHeight, rounding);
  }

  /// <summary>Decodes every macroblock of the picture, a row at a time.</summary>
  /// <remarks>
  /// A row at a time rather than a slice at a time, even though the picture is divided into slices,
  /// because nothing separates one slice from the next in the bitstream: there is no resynchronisation
  /// marker and no alignment, so the macroblocks are one raster-order run and a slice boundary is only
  /// a place where prediction stops. What the row loop is for is version 1, which forgets what it
  /// predicts a DC from at the start of every row.
  /// </remarks>
  internal void DecodePicture(ref Mpeg4BitReader reader) {
    for (var row = 0; row < this._macroblockHeight; ++row) {
      if (this._version == MsMpeg4Version.Version1)
        this._blocks.ResetLastDc();

      for (var column = 0; column < this._macroblockWidth; ++column) {
        var address = row * this._macroblockWidth + column;

        if (this._header.CodingType == MsMpeg4PictureHeader.IntraCoded)
          this._DecodeIntraPictureMacroblock(ref reader, address);
        else
          this._DecodePredictedPictureMacroblock(ref reader, address);
      }
    }
  }

  // ============================================================================================
  // Intra pictures
  // ============================================================================================

  private void _DecodeIntraPictureMacroblock(ref Mpeg4BitReader reader, int address) {
    int pattern;
    bool predictCoefficients;

    switch (this._version) {
      case MsMpeg4Version.Version3: {
        pattern = this._ReadPredictedPattern(ref reader, address);
        predictCoefficients = reader.ReadBit() == 1;
        break;
      }

      case MsMpeg4Version.Version2: {
        var chroma = MsMpeg4Tables.V2IntraChromaPattern.Read(ref reader);
        predictCoefficients = reader.ReadBit() == 1;
        pattern = (MsMpeg4Tables.CodedBlockPatternY.Read(ref reader) << 2) | chroma;
        break;
      }

      default: {
        var chroma = _ReadVersion1ChromaPattern(ref reader);
        predictCoefficients = false;
        pattern = (MsMpeg4Tables.CodedBlockPatternY.Read(ref reader) << 2) | chroma;
        break;
      }
    }

    this._DecodeIntra(ref reader, address, pattern, predictCoefficients);
  }

  /// <summary>
  /// Reads version 3's whole coded block pattern and undoes the prediction on its luminance half.
  /// </summary>
  /// <remarks>
  /// Each of the four luminance bits is stated as the exclusive-or of what this block does and what
  /// its neighbours did — the block to the left where the two above agree with each other, and the
  /// block above where they do not. It is the same predictor the DC uses in spirit and a different one
  /// in detail, and it is why an intra picture of version 3 cannot be decoded a macroblock at a time
  /// without keeping a grid of what every block did.
  /// </remarks>
  private int _ReadPredictedPattern(ref Mpeg4BitReader reader, int address) {
    var code = MsMpeg4Tables.IntraMacroblockPattern.Read(ref reader);
    var pattern = 0;

    for (var index = 0; index < 6; ++index) {
      var bit = (code >> (5 - index)) & 1;

      if (index < 4) {
        bit ^= this._codedBlock!.Predict(address, index);
        this._codedBlock.Record(address, index, bit);
      }

      pattern |= bit << (5 - index);
    }

    return pattern;
  }

  /// <summary>Reads the six blocks of an intra macroblock, in a picture of either type.</summary>
  private void _DecodeIntra(ref Mpeg4BitReader reader, int address, int pattern, bool predictCoefficients) {
    this._isDecoded[address] = true;
    this._vectorX[address] = 0;
    this._vectorY[address] = 0;

    Span<int> block = stackalloc int[64];
    for (var index = 0; index < 6; ++index) {
      this._blocks.ReadIntra(
        ref reader, block, this._intraPrediction, address, index, predictCoefficients, _IsCoded(pattern, index));

      this._Store(address, index, block);
    }
  }

  // ============================================================================================
  // Predicted pictures
  // ============================================================================================

  private void _DecodePredictedPictureMacroblock(ref Mpeg4BitReader reader, int address) {
    if (this._header.SkipBitsArePresent && reader.ReadBit() == 1) {
      this._CopyFromReference(address);
      return;
    }

    if (this._version == MsMpeg4Version.Version3) {
      var code = MsMpeg4Tables.MacroblockNonIntra.Read(ref reader);

      // Bit six clear says intra, which is the one place in the family where the flag reads inverted.
      if ((code & 0x40) == 0) {
        var predictCoefficients = reader.ReadBit() == 1;
        this._DecodeIntra(ref reader, address, code & 0x3F, predictCoefficients);
        return;
      }

      this._DecodePredicted(ref reader, address, code & 0x3F);
      return;
    }

    var type = this._version == MsMpeg4Version.Version2
      ? MsMpeg4Tables.V2MacroblockType.Read(ref reader)
      : _RefuseWideInterType(MsMpeg4Tables.InterMacroblock.Read(ref reader));

    var chroma = type & 3;

    if ((type & 4) != 0) {
      var predictCoefficients = this._version == MsMpeg4Version.Version2 && reader.ReadBit() == 1;
      var intraLuminance = MsMpeg4Tables.CodedBlockPatternY.Read(ref reader);
      var intraPattern = (intraLuminance << 2) | chroma;

      // Version 1 states an intra macroblock's luminance bits inverted inside a predicted picture and
      // the right way up inside an intra one; version 2 never inverts them.
      if (this._version == MsMpeg4Version.Version1)
        intraPattern ^= 0x3C;

      this._DecodeIntra(ref reader, address, intraPattern, predictCoefficients);
      return;
    }

    var pattern = (MsMpeg4Tables.CodedBlockPatternY.Read(ref reader) << 2) | chroma;

    // The luminance half of the pattern is stated inverted, except in version 2 where both chrominance
    // bits are set. That exception is the odd part and it is real: a version 2 macroblock whose two
    // chrominance bits are both set states its luminance bits the right way up, and every other one
    // states them complemented.
    if (this._version == MsMpeg4Version.Version1 || (pattern & 3) != 3)
      pattern ^= 0x3C;

    this._DecodePredicted(ref reader, address, pattern);
  }

  /// <summary>Reads a predicted macroblock's vector and its six blocks.</summary>
  private void _DecodePredicted(ref Mpeg4BitReader reader, int address, int pattern) {
    var predictedX = this._PredictVector(address, horizontal: true);
    var predictedY = this._PredictVector(address, horizontal: false);

    var (vectorX, vectorY) = this._version == MsMpeg4Version.Version3
      ? this._ReadWholeVector(ref reader, predictedX, predictedY)
      : (_ReadVectorComponent(ref reader, predictedX), _ReadVectorComponent(ref reader, predictedY));

    this._isDecoded[address] = true;
    this._vectorX[address] = (short)vectorX;
    this._vectorY[address] = (short)vectorY;
    this._intraPrediction.MarkUnavailable(address);

    this._ReconstructPredicted(ref reader, address, pattern, vectorX, vectorY);
  }

  /// <summary>Copies a macroblock nothing was coded for out of the reference picture.</summary>
  /// <remarks>
  /// A skipped macroblock is the co-located one of the reference with a zero vector, and its vector
  /// counts as zero for every later macroblock's predictor rather than being absent from the median.
  /// </remarks>
  private void _CopyFromReference(int address) {
    this._isDecoded[address] = true;
    this._vectorX[address] = 0;
    this._vectorY[address] = 0;
    this._intraPrediction.MarkUnavailable(address);

    Span<int> prediction = stackalloc int[64];
    for (var index = 0; index < 6; ++index) {
      this._Predict(prediction, address, index, 0, 0);
      this._Store(address, index, prediction);
    }
  }

  private void _ReconstructPredicted(
    ref Mpeg4BitReader reader, int address, int pattern, int vectorX, int vectorY) {
    Span<int> block = stackalloc int[64];
    Span<int> prediction = stackalloc int[64];

    var chromaX = Mpeg4MotionCompensation.ToChroma(4 * vectorX);
    var chromaY = Mpeg4MotionCompensation.ToChroma(4 * vectorY);

    for (var index = 0; index < 6; ++index) {
      var (x, y) = index < 4 ? (vectorX, vectorY) : (chromaX, chromaY);
      this._Predict(prediction, address, index, x, y);

      if (_IsCoded(pattern, index)) {
        this._blocks.ReadInter(ref reader, block);
        for (var i = 0; i < 64; ++i)
          block[i] += prediction[i];
      } else {
        prediction.CopyTo(block);
      }

      this._Store(address, index, block);
    }
  }

  // ============================================================================================
  // Motion vectors
  // ============================================================================================

  /// <summary>
  /// Reads one component the way versions 1 and 2 do: a magnitude out of H.263's table, then a sign.
  /// </summary>
  /// <remarks>
  /// A magnitude of nought carries no sign bit and means the vector is its own prediction, which is
  /// what makes a run of macroblocks moving together nearly free.
  /// </remarks>
  private static int _ReadVectorComponent(ref Mpeg4BitReader reader, int predicted) {
    var magnitude = MsMpeg4Tables.MotionVectorMagnitude.Read(ref reader);
    if (magnitude == 0)
      return predicted;

    return _Wrap(predicted + (reader.ReadBit() == 1 ? -magnitude : magnitude));
  }

  /// <summary>
  /// Reads both components the way version 3 does: one codeword for the whole vector.
  /// </summary>
  /// <remarks>
  /// Eleven hundred codewords, each of them a pair, with nought the escape into six plain bits per
  /// component. Both halves are biased by thirty-two so that a difference either way is an unsigned
  /// number, and the pair is a difference from the prediction rather than a vector.
  /// </remarks>
  private (int X, int Y) _ReadWholeVector(ref Mpeg4BitReader reader, int predictedX, int predictedY) {
    var code = MsMpeg4Tables.MotionVector[this._header.MotionVectorTableIndex].Read(ref reader);
    var (x, y) = code != 0
      ? (code >> 8, code & 0xFF)
      : (reader.ReadBits(6), reader.ReadBits(6));

    return (_Wrap(x + predictedX - _VECTOR_BIAS), _Wrap(y + predictedY - _VECTOR_BIAS));
  }

  /// <summary>
  /// Brings a reconstructed vector back into the range the format allows.
  /// </summary>
  /// <remarks>
  /// A single add or subtract of the whole range and not a clamp, which is how the far end of the
  /// range is reached at all: a vector near one end predicts a vector near the other with a small
  /// difference. The range is sixty-four half-samples either way — thirty-one and a half whole samples
  /// — where ISO/IEC 14496-2 varies it with the picture's motion code.
  /// </remarks>
  private static int _Wrap(int vector)
    => vector <= -_VECTOR_RANGE ? vector + _VECTOR_RANGE
      : vector >= _VECTOR_RANGE ? vector - _VECTOR_RANGE
      : vector;

  /// <summary>The median of the three neighbouring macroblocks' vectors.</summary>
  private int _PredictVector(int address, bool horizontal) {
    var vectors = horizontal ? this._vectorX : this._vectorY;
    var column = address % this._macroblockWidth;
    var row = address / this._macroblockWidth;

    var above = address - this._macroblockWidth;
    var hasAbove = row > 0 && this._SameSlice(address, above);

    var left = this._CandidateOf(column > 0 ? address - 1 : -1, vectors);
    var top = this._CandidateOf(hasAbove ? above : -1, vectors);
    var topRight = this._CandidateOf(hasAbove && column + 1 < this._macroblockWidth ? above + 1 : -1, vectors);

    return _Median(left, top, topRight);
  }

  private (int Value, bool Valid) _CandidateOf(int neighbour, short[] vectors)
    => neighbour < 0 || !this._isDecoded[neighbour] ? (0, false) : (vectors[neighbour], true);

  private bool _SameSlice(int address, int other)
    => address / this._macroblockWidth / this._header.SliceHeight
       == other / this._macroblockWidth / this._header.SliceHeight;

  /// <summary>
  /// The median of three candidates, with the substitutions ISO/IEC 14496-2 7.6.2 makes where one or
  /// more of them is not there.
  /// </summary>
  private static int _Median((int Value, bool Valid) a, (int Value, bool Valid) b, (int Value, bool Valid) c) {
    var count = (a.Valid ? 1 : 0) + (b.Valid ? 1 : 0) + (c.Valid ? 1 : 0);
    switch (count) {
      case 0:
        return 0;

      case 1:
        return a.Valid ? a.Value : b.Valid ? b.Value : c.Value;
    }

    var x = a.Valid ? a.Value : 0;
    var y = b.Valid ? b.Value : 0;
    var z = c.Valid ? c.Value : 0;

    if (x > y)
      (x, y) = (y, x);

    if (y > z)
      y = z;

    return x > y ? x : y;
  }

  // ============================================================================================
  // Reconstruction
  // ============================================================================================

  private void _Predict(Span<int> prediction, int address, int index, int vectorX, int vectorY) {
    var (plane, stride, origin, width, height) = this._reference!.PlaneOf(index);
    var (left, top) = this._BlockOrigin(address, index);
    var border = index < 4 ? Mpeg4Frame.Border : Mpeg4Frame.Border / 2;

    Mpeg4MotionCompensation.PredictHalfSample(
      prediction, plane, stride, origin, border, width, height, left, top, vectorX, vectorY, this._rounding);
  }

  private void _Store(int address, int index, scoped ReadOnlySpan<int> samples) {
    var (plane, stride, origin, _, _) = this._target.PlaneOf(index);
    var (left, top) = this._BlockOrigin(address, index);

    for (var y = 0; y < 8; ++y) {
      var row = origin + (top + y) * stride + left;
      for (var x = 0; x < 8; ++x) {
        var value = samples[y * 8 + x];
        plane[row + x] = (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
      }
    }
  }

  private static bool _IsCoded(int pattern, int index) => (pattern & (1 << (5 - index))) != 0;

  private (int Left, int Top) _BlockOrigin(int address, int index) {
    var column = address % this._macroblockWidth;
    var row = address / this._macroblockWidth;

    return index < 4
      ? (column * 16 + (index & 1) * 8, row * 16 + (index >> 1) * 8)
      : (column * 8, row * 8);
  }

  /// <summary>
  /// Reads version 1's chrominance pattern, which is H.263's MCBPC table with the wide values refused.
  /// </summary>
  /// <remarks>
  /// H.263's table states a macroblock type and a pattern together, and above three it goes on to
  /// state a change of quantiser and a stuffing code that version 1 has no field for. So a stream that
  /// uses one of those is not a version 1 stream, and saying so here is better than reading a
  /// quantiser change that has nowhere to go.
  /// </remarks>
  private static int _ReadVersion1ChromaPattern(ref Mpeg4BitReader reader) {
    var value = MsMpeg4Tables.IntraMacroblock.Read(ref reader);
    if (value > 3)
      throw new InvalidDataException(
        $"An intra macroblock of this Microsoft MPEG-4 version 1 picture states H.263 MCBPC value {value}. Version 1 "
        + "uses only the first four, which are the coded block pattern alone; the rest change the quantiser or stuff "
        + "the bitstream, and version 1 has no field for either.");

    return value;
  }

  private static int _RefuseWideInterType(int value) {
    if (value > 7)
      throw new InvalidDataException(
        $"A macroblock of this Microsoft MPEG-4 version 1 predicted picture states H.263 MCBPC value {value}. "
        + "Version 1 uses only the first eight, which state whether the macroblock is intra coded and its two "
        + "chrominance bits; the rest change the quantiser or state four motion vectors, and version 1 has neither.");

    return value;
  }
}
