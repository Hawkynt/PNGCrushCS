using System;
using System.IO;

namespace FileFormat.Codecs.Mpeg1;

/// <summary>
/// Decodes one coded picture: its header, its slices, its macroblocks and their blocks
/// (ISO/IEC 11172-2, 2.4.2.5 through 2.4.2.7 and 2.4.4).
/// </summary>
/// <remarks>
/// One of these exists for the length of one picture, because everything it holds is reset by the
/// next one: the quantiser scale, the three DC predictors and the four motion vector predictors are
/// all per-slice, and the references are per-picture. A decoder that kept them across pictures would
/// still produce a picture, and it would be wrong in a way that only shows up in the second frame of
/// a group.
/// </remarks>
internal sealed class Mpeg1PictureDecoder {

  /// <summary>Intra-coded: decodable on its own (11172-2, Table 2-D.1).</summary>
  internal const int IntraCoded = 1;

  /// <summary>Predictively coded, from the picture before it.</summary>
  internal const int PredictiveCoded = 2;

  /// <summary>Bidirectionally coded, from the pictures on either side of it.</summary>
  internal const int BidirectionallyCoded = 3;

  /// <summary>DC coded: the still-picture mode of 11172-2, 2.4.2.8.</summary>
  internal const int DcCoded = 4;

  /// <summary>The value the intra DC predictors take at the start of every slice (11172-2, 2.4.4.1).</summary>
  private const int _DC_PREDICTOR_RESET = 1024;

  private readonly Mpeg1SequenceHeader _sequence;
  private readonly Mpeg1Frame _target;
  private readonly Mpeg1Frame? _forwardReference;
  private readonly Mpeg1Frame? _backwardReference;
  private readonly bool[] _decoded;

  private readonly int _forwardFCode;
  private readonly int _backwardFCode;
  private readonly bool _forwardIsFullPel;
  private readonly bool _backwardIsFullPel;

  // Slice state.
  private int _quantiserScale;
  private int _dcLuminance;
  private int _dcCb;
  private int _dcCr;
  private int _forwardX;
  private int _forwardY;
  private int _backwardX;
  private int _backwardY;
  private int _address;
  private bool _previousUsedForward;
  private bool _previousUsedBackward;

  private Mpeg1PictureDecoder(
    Mpeg1SequenceHeader sequence, Mpeg1Frame target, Mpeg1Frame? forwardReference, Mpeg1Frame? backwardReference,
    int codingType, int forwardFCode, bool forwardIsFullPel, int backwardFCode, bool backwardIsFullPel) {
    this._sequence = sequence;
    this._target = target;
    this._forwardReference = forwardReference;
    this._backwardReference = backwardReference;
    this.CodingType = codingType;
    this._forwardFCode = forwardFCode;
    this._forwardIsFullPel = forwardIsFullPel;
    this._backwardFCode = backwardFCode;
    this._backwardIsFullPel = backwardIsFullPel;
    this._decoded = new bool[sequence.MacroblockWidth * sequence.MacroblockHeight];
  }

  /// <summary>Which of I, P, B this picture is.</summary>
  internal int CodingType { get; }

  /// <summary>The picture being reconstructed.</summary>
  internal Mpeg1Frame Target => this._target;

  /// <summary>
  /// Reads a picture header, positioned just past its start code, and prepares to decode its slices.
  /// </summary>
  /// <exception cref="NotSupportedException">The picture is DC coded, which this decoder does not read.</exception>
  /// <exception cref="InvalidDataException">The header is malformed, or the picture predicts from a
  /// reference the stream has not supplied.</exception>
  internal static Mpeg1PictureDecoder BeginPicture(
    ref Mpeg1BitReader reader, Mpeg1SequenceHeader sequence, Mpeg1Frame target,
    Mpeg1Frame? previousAnchor, Mpeg1Frame? currentAnchor) {
    reader.ReadBits(10); // temporal_reference — display order within the group, which the reordering
                         // rule below does not need: an anchor is shown when the next one arrives.
    var codingType = reader.ReadBits(3);
    reader.ReadBits(16); // vbv_delay

    var forwardFCode = 1;
    var backwardFCode = 1;
    var forwardIsFullPel = false;
    var backwardIsFullPel = false;

    if (codingType is PredictiveCoded or BidirectionallyCoded) {
      forwardIsFullPel = reader.ReadBit() == 1;
      forwardFCode = reader.ReadBits(3);
      _RefuseForbiddenFCode(forwardFCode, "forward_f_code");
    }

    if (codingType == BidirectionallyCoded) {
      backwardIsFullPel = reader.ReadBit() == 1;
      backwardFCode = reader.ReadBits(3);
      _RefuseForbiddenFCode(backwardFCode, "backward_f_code");
    }

    // extra_information_picture: bytes nothing in MPEG-1 defines, each introduced by a set bit.
    while (reader.NextBits(1) == 1) {
      reader.Skip(1);
      reader.ReadBits(8);
    }

    reader.Skip(1);

    switch (codingType) {
      case IntraCoded:
        return new(sequence, target, null, null, codingType, forwardFCode, forwardIsFullPel, backwardFCode, backwardIsFullPel);

      case PredictiveCoded:
        return new(
          sequence, target,
          currentAnchor ?? throw new InvalidDataException(
            "A predictively coded MPEG-1 picture arrived before any intra picture, so there is nothing for it to be "
            + "predicted from. Decoding must begin at a sequence header followed by an I picture."),
          null, codingType, forwardFCode, forwardIsFullPel, backwardFCode, backwardIsFullPel);

      case BidirectionallyCoded:
        if (previousAnchor == null || currentAnchor == null)
          throw new InvalidDataException(
            "A bidirectionally coded MPEG-1 picture arrived before both of the pictures it is predicted from had been "
            + "decoded. Decoding must begin at a sequence header followed by an I picture.");

        return new(
          sequence, target, previousAnchor, currentAnchor, codingType,
          forwardFCode, forwardIsFullPel, backwardFCode, backwardIsFullPel);

      case DcCoded:
        throw new NotSupportedException(
          "This MPEG-1 stream holds a D picture (picture_coding_type 4), the DC-only still-picture mode of "
          + "ISO/IEC 11172-2 2.4.2.8. This decoder reads I, P and B pictures; D pictures are not implemented.");

      default:
        throw new InvalidDataException(
          $"The MPEG-1 picture header states picture_coding_type {codingType}, which ISO/IEC 11172-2 leaves forbidden "
          + "or reserved. Only 1 (I), 2 (P), 3 (B) and 4 (D) are defined.");
    }
  }

  private static void _RefuseForbiddenFCode(int fCode, string field) {
    if (fCode == 0)
      throw new InvalidDataException(
        $"The MPEG-1 picture header states {field} 0, which ISO/IEC 11172-2 forbids; the range is 1 to 7.");
  }

  // ============================================================================================
  // Slice layer — 11172-2, 2.4.2.6
  // ============================================================================================

  /// <summary>
  /// Decodes one slice, positioned just past its start code.
  /// </summary>
  /// <param name="reader">The bitstream.</param>
  /// <param name="startCode">The start code's last byte, which is the slice's row counted from one.</param>
  internal void DecodeSlice(ref Mpeg1BitReader reader, byte startCode) {
    var row = startCode - 1;
    if (row >= this._sequence.MacroblockHeight)
      throw new InvalidDataException(
        $"An MPEG-1 slice states vertical position {startCode}, past the {this._sequence.MacroblockHeight} macroblock "
        + $"rows of a {this._sequence.Width}x{this._sequence.Height} picture.");

    this._quantiserScale = _ReadQuantiserScale(ref reader);

    // extra_information_slice, the same shape as the picture's.
    while (reader.NextBits(1) == 1) {
      reader.Skip(1);
      reader.ReadBits(8);
    }

    reader.Skip(1);

    // Everything the slice predicts from starts afresh. This is what makes a slice the unit a
    // decoder can resynchronise on after a transmission error.
    this._dcLuminance = this._dcCb = this._dcCr = _DC_PREDICTOR_RESET;
    this._forwardX = this._forwardY = this._backwardX = this._backwardY = 0;
    this._previousUsedForward = this._previousUsedBackward = false;
    this._address = row * this._sequence.MacroblockWidth - 1;

    var isFirst = true;
    do {
      this._DecodeMacroblock(ref reader, isFirst);
      isFirst = false;
    } while (reader.NextBits(23) != 0);
  }

  private static int _ReadQuantiserScale(ref Mpeg1BitReader reader) {
    var scale = reader.ReadBits(5);
    if (scale == 0)
      throw new InvalidDataException("An MPEG-1 quantiser_scale of zero was read, which ISO/IEC 11172-2 forbids; the range is 1 to 31.");

    return scale;
  }

  /// <summary>
  /// Refuses a picture whose slices left macroblocks undecoded.
  /// </summary>
  /// <remarks>
  /// The alternative is to leave the gap holding whatever the buffer held, which for a freshly
  /// allocated picture is a black band and for a recycled one is part of another frame. Both are a
  /// picture that was never coded, presented as though it had been.
  /// </remarks>
  internal void RefuseIfIncomplete() {
    for (var address = 0; address < this._decoded.Length; ++address)
      if (!this._decoded[address]) {
        var missing = 0;
        foreach (var done in this._decoded)
          if (!done)
            ++missing;

        throw new InvalidDataException(
          $"The slices of this MPEG-1 picture cover {this._decoded.Length - missing} of its {this._decoded.Length} "
          + $"macroblocks; the first one missing is number {address}, at column "
          + $"{address % this._sequence.MacroblockWidth} of row {address / this._sequence.MacroblockWidth}. "
          + "ISO/IEC 11172-2 requires the slices of a picture to cover it completely.");
      }
  }

  // ============================================================================================
  // Macroblock layer — 11172-2, 2.4.2.7
  // ============================================================================================

  private void _DecodeMacroblock(ref Mpeg1BitReader reader, bool isFirstOfSlice) {
    var increment = _ReadAddressIncrement(ref reader);
    var address = this._address + increment;

    // Everything between the last coded macroblock and this one was skipped. Not at the start of a
    // slice, though: there the increment only says where the slice's first macroblock sits, and the
    // macroblocks before it belong to no slice at all — which RefuseIfIncomplete then catches.
    if (!isFirstOfSlice)
      for (var skipped = this._address + 1; skipped < address; ++skipped)
        this._SkipMacroblock(skipped);

    this._address = address;
    if ((uint)address >= (uint)this._decoded.Length)
      throw new InvalidDataException(
        $"An MPEG-1 macroblock address reached {address}, past the {this._decoded.Length} macroblocks of a "
        + $"{this._sequence.Width}x{this._sequence.Height} picture.");

    var type = this._TypeTable().Read(ref reader);
    if ((type & Mpeg1VlcTables.TypeQuant) != 0)
      this._quantiserScale = _ReadQuantiserScale(ref reader);

    var isIntra = (type & Mpeg1VlcTables.TypeIntra) != 0;
    var usesForward = (type & Mpeg1VlcTables.TypeMotionForward) != 0;
    var usesBackward = (type & Mpeg1VlcTables.TypeMotionBackward) != 0;

    if (usesForward) {
      this._forwardX = _ReadVector(ref reader, this._forwardFCode, this._forwardX);
      this._forwardY = _ReadVector(ref reader, this._forwardFCode, this._forwardY);
    }

    if (usesBackward) {
      this._backwardX = _ReadVector(ref reader, this._backwardFCode, this._backwardX);
      this._backwardY = _ReadVector(ref reader, this._backwardFCode, this._backwardY);
    }

    var pattern = (type & Mpeg1VlcTables.TypePattern) != 0
      ? Mpeg1VlcTables.CodedBlockPattern.Read(ref reader)
      : isIntra ? 63 : 0;

    if (isIntra) {
      // An intra macroblock predicts from nothing, so the vectors either side of it do not carry
      // across it either (11172-2, 2.4.4.2).
      this._forwardX = this._forwardY = this._backwardX = this._backwardY = 0;
      this._DecodeIntraMacroblock(ref reader, address);
    } else {
      // A predicted macroblock has no intra DC to be a predictor, so the chain is broken here.
      this._dcLuminance = this._dcCb = this._dcCr = _DC_PREDICTOR_RESET;

      // "No MC, coded" in a P picture: the vector is zero and, being transmitted as an absence
      // rather than as a zero code, it resets the predictors as well. The prediction is still made —
      // a P macroblock is always predicted from the forward reference, and a residual added to
      // nothing would be a picture of the residual.
      if (this.CodingType == PredictiveCoded && !usesForward) {
        this._forwardX = this._forwardY = 0;
        usesForward = true;
      }

      this._DecodePredictedMacroblock(ref reader, address, pattern, usesForward, usesBackward);
    }

    this._previousUsedForward = usesForward;
    this._previousUsedBackward = usesBackward;
    this._decoded[address] = true;
  }

  private Mpeg1VlcTable _TypeTable() => this.CodingType switch {
    IntraCoded => Mpeg1VlcTables.IntraMacroblockType,
    PredictiveCoded => Mpeg1VlcTables.PredictedMacroblockType,
    _ => Mpeg1VlcTables.BidirectionalMacroblockType,
  };

  /// <summary>Reads macroblock_address_increment, taking out the stuffing and the escapes.</summary>
  private static int _ReadAddressIncrement(ref Mpeg1BitReader reader) {
    var increment = 0;
    for (; ; ) {
      var value = Mpeg1VlcTables.MacroblockAddressIncrement.Read(ref reader);
      switch (value) {
        case Mpeg1VlcTables.Stuffing:
          continue;

        case Mpeg1VlcTables.Escape:
          increment += 33;
          continue;

        default:
          return increment + value;
      }
    }
  }

  /// <summary>
  /// Reconstructs one component of one motion vector (11172-2, 2.4.4.2).
  /// </summary>
  /// <remarks>
  /// The vector is coded as a difference from the previous macroblock's, and the difference is
  /// allowed to wrap: where adding it would leave the range the f_code permits, the alternative
  /// value a whole range away is the one that was meant. That is what the second candidate is for,
  /// and it is why the two are computed before either is tested rather than the range being clamped
  /// afterwards — clamping produces a vector nobody coded.
  /// </remarks>
  private static int _ReadVector(ref Mpeg1BitReader reader, int fCode, int previous) {
    var motionCode = Mpeg1VlcTables.MotionCode.Read(ref reader);
    var f = 1 << (fCode - 1);

    var residual = f != 1 && motionCode != 0 ? reader.ReadBits(fCode - 1) : 0;
    var complement = f == 1 || motionCode == 0 ? 0 : f - 1 - residual;

    var difference = motionCode * f;
    var wrapped = 0;
    if (difference > 0) {
      difference -= complement;
      wrapped = difference - 32 * f;
    } else if (difference < 0) {
      difference += complement;
      wrapped = difference + 32 * f;
    }

    var candidate = previous + difference;
    return candidate <= 16 * f - 1 && candidate >= -16 * f ? candidate : previous + wrapped;
  }

  // ============================================================================================
  // Reconstruction
  // ============================================================================================

  private void _DecodeIntraMacroblock(ref Mpeg1BitReader reader, int address) {
    Span<int> block = stackalloc int[64];
    var matrix = this._sequence.IntraMatrix;

    for (var index = 0; index < 6; ++index) {
      var predictor = index switch { < 4 => this._dcLuminance, 4 => this._dcCb, _ => this._dcCr };
      var dc = Mpeg1BlockDecoder.ReadIntra(ref reader, block, index, this._quantiserScale, matrix, predictor);

      switch (index) {
        case < 4: this._dcLuminance = dc; break;
        case 4: this._dcCb = dc; break;
        default: this._dcCr = dc; break;
      }

      var (plane, width, _) = this._target.PlaneOf(index);
      var (left, top) = this._BlockOrigin(address, index);
      for (var y = 0; y < 8; ++y) {
        var row = (top + y) * width + left;
        for (var x = 0; x < 8; ++x)
          plane[row + x] = _ToSample(block[y * 8 + x]);
      }
    }
  }

  private void _DecodePredictedMacroblock(
    ref Mpeg1BitReader reader, int address, int pattern, bool usesForward, bool usesBackward) {
    Span<int> block = stackalloc int[64];
    Span<int> prediction = stackalloc int[64];
    Span<int> backward = stackalloc int[64];
    var matrix = this._sequence.NonIntraMatrix;

    for (var index = 0; index < 6; ++index) {
      this._Predict(prediction, backward, address, index, usesForward, usesBackward);

      var isCoded = (pattern & (1 << (5 - index))) != 0;
      if (isCoded)
        Mpeg1BlockDecoder.ReadNonIntra(ref reader, block, this._quantiserScale, matrix);

      var (plane, width, _) = this._target.PlaneOf(index);
      var (left, top) = this._BlockOrigin(address, index);
      for (var y = 0; y < 8; ++y) {
        var row = (top + y) * width + left;
        for (var x = 0; x < 8; ++x)
          plane[row + x] = _ToSample(isCoded ? prediction[y * 8 + x] + block[y * 8 + x] : prediction[y * 8 + x]);
      }
    }
  }

  /// <summary>Fills one block's prediction from whichever references the macroblock uses.</summary>
  private void _Predict(
    Span<int> prediction, Span<int> scratch, int address, int index, bool usesForward, bool usesBackward) {
    if (usesForward)
      this._PredictFrom(prediction, this._forwardReference!, address, index, this._forwardX, this._forwardY, this._forwardIsFullPel, "forward");

    if (usesBackward) {
      var target = usesForward ? scratch : prediction;
      this._PredictFrom(target, this._backwardReference!, address, index, this._backwardX, this._backwardY, this._backwardIsFullPel, "backward");

      if (usesForward)
        Mpeg1MotionCompensation.Average(prediction, scratch);
    }
  }

  private void _PredictFrom(
    Span<int> prediction, Mpeg1Frame reference, int address, int index, int vectorX, int vectorY, bool isFullPel,
    string direction) {
    // full_pel says the coded vector counts whole pixels, so doubling it puts every vector into the
    // half-pixel units the interpolation works in. The predictors were updated before this, on the
    // coded value, which is why the doubling happens here and not where the vector was read.
    if (isFullPel) {
      vectorX <<= 1;
      vectorY <<= 1;
    }

    var isChroma = index >= 4;
    if (isChroma) {
      vectorX = Mpeg1MotionCompensation.ToChroma(vectorX);
      vectorY = Mpeg1MotionCompensation.ToChroma(vectorY);
    }

    var (referencePlane, planeWidth) = isChroma
      ? (index == 4 ? reference.Cb : reference.Cr, reference.ChromaWidth)
      : (reference.Luma, reference.LumaWidth);

    var (left, top) = this._BlockOrigin(address, index);
    if (Mpeg1MotionCompensation.TryPredict(prediction, referencePlane, planeWidth, left, top, vectorX, vectorY))
      return;

    throw new InvalidDataException(
      $"The {direction} prediction of block {index} of macroblock {address} (column "
      + $"{address % this._sequence.MacroblockWidth}, row {address / this._sequence.MacroblockWidth}) has a motion "
      + $"vector of ({vectorX}, {vectorY}) half-pixels from ({left}, {top}), which reads outside the "
      + $"{planeWidth}x{referencePlane.Length / planeWidth} reference plane. ISO/IEC 11172-2 does not permit a vector "
      + "that points outside the reference picture.");
  }

  /// <summary>
  /// Copies a macroblock nothing was coded for out of the reference it is predicted from.
  /// </summary>
  /// <remarks>
  /// A skipped macroblock means different things in the two predicted picture types. In a P picture
  /// it is the co-located macroblock of the previous anchor, with a zero vector, and the vector
  /// predictors are reset by it. In a B picture it is the previous macroblock's prediction repeated —
  /// same vectors, same direction, no residual — and the predictors are not touched. Treating both as
  /// "copy the co-located block" is a mistake that shows only where a B picture holds runs of skipped
  /// macroblocks over moving content, which is exactly where they are most common.
  /// </remarks>
  private void _SkipMacroblock(int address) {
    if ((uint)address >= (uint)this._decoded.Length)
      throw new InvalidDataException(
        $"An MPEG-1 macroblock address increment skipped past macroblock {address}, past the "
        + $"{this._decoded.Length} a {this._sequence.Width}x{this._sequence.Height} picture holds.");

    switch (this.CodingType) {
      case IntraCoded:
        throw new InvalidDataException(
          $"Macroblock {address} of an MPEG-1 intra picture was skipped. Every macroblock of an I picture is coded; "
          + "ISO/IEC 11172-2 gives a skipped macroblock of an I picture no meaning.");

      case PredictiveCoded:
        this._dcLuminance = this._dcCb = this._dcCr = _DC_PREDICTOR_RESET;
        this._forwardX = this._forwardY = 0;
        this._CopyPrediction(address, usesForward: true, usesBackward: false);
        break;

      default:
        if (!this._previousUsedForward && !this._previousUsedBackward)
          throw new InvalidDataException(
            $"Macroblock {address} of an MPEG-1 bidirectionally coded picture was skipped, but the macroblock before "
            + "it was intra coded or none preceded it, so there is no prediction for it to repeat.");

        this._dcLuminance = this._dcCb = this._dcCr = _DC_PREDICTOR_RESET;
        this._CopyPrediction(address, this._previousUsedForward, this._previousUsedBackward);
        break;
    }

    this._decoded[address] = true;
  }

  private void _CopyPrediction(int address, bool usesForward, bool usesBackward) {
    Span<int> prediction = stackalloc int[64];
    Span<int> scratch = stackalloc int[64];

    for (var index = 0; index < 6; ++index) {
      this._Predict(prediction, scratch, address, index, usesForward, usesBackward);

      var (plane, width, _) = this._target.PlaneOf(index);
      var (left, top) = this._BlockOrigin(address, index);
      for (var y = 0; y < 8; ++y) {
        var row = (top + y) * width + left;
        for (var x = 0; x < 8; ++x)
          plane[row + x] = _ToSample(prediction[y * 8 + x]);
      }
    }
  }

  /// <summary>
  /// Where one of a macroblock's six blocks sits in its plane (11172-2, Figure 2-9).
  /// </summary>
  /// <remarks>
  /// The four luminance blocks are the macroblock's quadrants in reading order; the two chrominance
  /// ones are the whole macroblock at half resolution, which is one block each.
  /// </remarks>
  private (int Left, int Top) _BlockOrigin(int address, int index) {
    var column = address % this._sequence.MacroblockWidth;
    var row = address / this._sequence.MacroblockWidth;

    return index < 4
      ? (column * 16 + (index & 1) * 8, row * 16 + (index >> 1) * 8)
      : (column * 8, row * 8);
  }

  private static byte _ToSample(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
}
