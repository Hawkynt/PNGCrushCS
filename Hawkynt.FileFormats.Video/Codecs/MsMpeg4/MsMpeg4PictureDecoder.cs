using System;
using System.IO;
using FileFormat.Codecs.Mpeg4;

namespace FileFormat.Codecs.MsMpeg4;

/// <summary>
/// Decodes one coded picture of Microsoft's MPEG-4 version 2: its macroblocks and their blocks.
/// </summary>
/// <remarks>
/// One of these exists for the length of one picture. Everything it holds is reset by the next one —
/// the vectors later macroblocks predict from, the coefficients the intra prediction reaches back
/// into — and a decoder that kept any of them across pictures would still produce a picture, wrong
/// from its second frame onward.
/// <para/>
/// The quantiser is not among them, because there is nothing to keep: version 2 states it once in the
/// picture header and gives the macroblock layer no way to change it. That single absence is what
/// makes the alternating current prediction here simpler than the standard's, which has to rescale
/// every predictor by the ratio of two quantisers that may differ.
/// <para/>
/// There is one motion vector for a whole macroblock and never four. The format has no equivalent of
/// the standard's INTER4V, so the vector predictors are the neighbouring <i>macroblocks</i>' vectors
/// rather than the neighbouring blocks', and the median of ISO/IEC 14496-2 7.6.2 is taken over those.
/// </remarks>
internal sealed class MsMpeg4PictureDecoder {

  private readonly MsMpeg4PictureHeader _header;
  private readonly Mpeg4Frame _target;
  private readonly Mpeg4Frame? _reference;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly MsMpeg4IntraPrediction _intraPrediction;

  /// <summary>Each macroblock's motion vector, in half-sample units.</summary>
  private readonly short[] _vectorX;

  private readonly short[] _vectorY;

  /// <summary>Whether each macroblock has been decoded, which the vector predictor's edge rules need.</summary>
  private readonly bool[] _isDecoded;

  private MsMpeg4PictureDecoder(
    MsMpeg4PictureHeader header, Mpeg4Frame target, Mpeg4Frame? reference,
    int macroblockWidth, int macroblockHeight) {
    this._header = header;
    this._target = target;
    this._reference = reference;
    this._macroblockWidth = macroblockWidth;
    this._macroblockHeight = macroblockHeight;

    var count = macroblockWidth * macroblockHeight;
    this._vectorX = new short[count];
    this._vectorY = new short[count];
    this._isDecoded = new bool[count];
    this._intraPrediction = new(macroblockWidth, macroblockHeight, header.SliceHeight);
  }

  /// <summary>The picture being reconstructed.</summary>
  internal Mpeg4Frame Target => this._target;

  /// <summary>Prepares to decode a picture whose header has been read.</summary>
  internal static MsMpeg4PictureDecoder BeginPicture(
    MsMpeg4PictureHeader header, Mpeg4Frame target, Mpeg4Frame? reference,
    int macroblockWidth, int macroblockHeight) {
    ArgumentNullException.ThrowIfNull(header);
    ArgumentNullException.ThrowIfNull(target);

    if (header.CodingType != MsMpeg4PictureHeader.IntraCoded && reference == null)
      throw new InvalidDataException(
        "A Microsoft MPEG-4 version 2 predicted picture arrived before any intra picture, so there is nothing for it "
        + "to be predicted from. Decoding must begin at an intra picture.");

    return new(header, target, reference, macroblockWidth, macroblockHeight);
  }

  /// <summary>Decodes every macroblock of the picture.</summary>
  internal void DecodePicture(ref Mpeg4BitReader reader) {
    var count = this._macroblockWidth * this._macroblockHeight;

    for (var address = 0; address < count; ++address)
      if (this._header.CodingType == MsMpeg4PictureHeader.IntraCoded)
        this._DecodeIntraPictureMacroblock(ref reader, address);
      else
        this._DecodePredictedPictureMacroblock(ref reader, address);
  }

  // ============================================================================================
  // Intra pictures
  // ============================================================================================

  private void _DecodeIntraPictureMacroblock(ref Mpeg4BitReader reader, int address) {
    var chromaPattern = MsMpeg4VlcTables.IntraChromaPattern.Read(ref reader);
    var predictCoefficients = reader.ReadBit() == 1;
    var luminancePattern = Mpeg4VlcTables.LuminancePattern.Read(ref reader);

    this._DecodeIntra(ref reader, address, (luminancePattern << 2) | chromaPattern, predictCoefficients);
  }

  /// <summary>Reads the six blocks of an intra macroblock, in a picture of either type.</summary>
  private void _DecodeIntra(ref Mpeg4BitReader reader, int address, int pattern, bool predictCoefficients) {
    this._isDecoded[address] = true;
    this._vectorX[address] = 0;
    this._vectorY[address] = 0;

    Span<int> block = stackalloc int[64];
    for (var index = 0; index < 6; ++index) {
      MsMpeg4BlockDecoder.ReadIntra(
        ref reader, block, this._intraPrediction, address, index,
        this._header.Quantiser, predictCoefficients, _IsCoded(pattern, index));

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

    var macroblockType = MsMpeg4VlcTables.MacroblockType.Read(ref reader);
    var chromaPattern = MsMpeg4VlcTables.ChromaPatternOf(macroblockType);

    if (MsMpeg4VlcTables.IsIntra(macroblockType)) {
      var predictCoefficients = reader.ReadBit() == 1;
      var intraLuminance = Mpeg4VlcTables.LuminancePattern.Read(ref reader);
      this._DecodeIntra(ref reader, address, (intraLuminance << 2) | chromaPattern, predictCoefficients);
      return;
    }

    var luminancePattern = Mpeg4VlcTables.LuminancePattern.Read(ref reader);
    var pattern = (luminancePattern << 2) | chromaPattern;

    // The luminance half of the pattern is stated inverted, except where both chrominance blocks are
    // coded. That exception is the odd part and it is real: a macroblock whose two chrominance bits
    // are both set states its luminance bits the right way up, and every other one states them
    // complemented.
    if ((pattern & 3) != 3)
      pattern ^= 0x3C;

    var vectorX = this._ReadVector(ref reader, address, horizontal: true);
    var vectorY = this._ReadVector(ref reader, address, horizontal: false);

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
        MsMpeg4BlockDecoder.ReadInter(ref reader, block, this._header.Quantiser);
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

  private int _ReadVector(ref Mpeg4BitReader reader, int address, bool horizontal) {
    var magnitude = MsMpeg4VlcTables.MotionVectorMagnitude.Read(ref reader);
    var difference = magnitude == 0 ? 0 : reader.ReadBit() == 1 ? -magnitude : magnitude;

    return _Wrap(this._PredictVector(address, horizontal) + difference);
  }

  /// <summary>
  /// Brings a reconstructed vector back into the range the format allows.
  /// </summary>
  /// <remarks>
  /// A single add or subtract of the whole range and not a clamp, which is how the far end of the
  /// range is reached at all: a vector near one end predicts a vector near the other with a small
  /// difference. The range is sixty-four half-samples either way — thirty-one and a half whole samples
  /// — where ISO/IEC 14496-2 varies it with the picture's motion code. This is the one place the
  /// format's own description calls the vectors "ISO-MPEG4 except for the limited range".
  /// </remarks>
  private static int _Wrap(int vector) => vector <= -64 ? vector + 64 : vector >= 64 ? vector - 64 : vector;

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

    // Rounding is fixed at nought. Versions 1 and 2 have no flip-flop rounding bit — version 3 was
    // where Microsoft added one — so the half-sample interpolation always rounds a half upward.
    Mpeg4MotionCompensation.PredictHalfSample(
      prediction, plane, stride, origin, border, width, height, left, top, vectorX, vectorY, rounding: 0);
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
}
