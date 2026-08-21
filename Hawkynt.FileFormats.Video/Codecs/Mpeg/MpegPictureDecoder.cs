using System;
using System.IO;

namespace FileFormat.Codecs.Mpeg;

/// <summary>
/// Decodes one coded picture: its header, its extensions, its slices, its macroblocks and their
/// blocks (ISO/IEC 11172-2, 2.4.2.5 through 2.4.2.7 and 2.4.4; ISO/IEC 13818-2, 6.2.3 through 6.2.6
/// and 7.2 through 7.6).
/// </summary>
/// <remarks>
/// One of these exists for the length of one picture, because everything it holds is reset by the
/// next one: the quantiser scale, the three DC predictors and the motion vector predictors are all
/// per-slice, and the references are per-picture. A decoder that kept them across pictures would
/// still produce a picture, and it would be wrong in a way that only shows up in the second frame of
/// a group.
/// <para/>
/// <b>The prediction is formed a macroblock at a time and not a block at a time</b>, which is a
/// change MPEG-2 forces and MPEG-1 does not mind. In MPEG-1 a macroblock's six blocks all sit at
/// fixed places and share one motion vector, so predicting each separately gives the same samples.
/// In MPEG-2 they need not: field-based prediction gives the top and bottom field lines of the
/// macroblock two different vectors pointing into two different reference fields, and field DCT
/// re-cuts the four luminance blocks so that one block holds every other line of the whole
/// macroblock. Neither survives a per-block prediction. Forming the whole macroblock first and then
/// adding the residual blocks into it is the model the standard itself uses, and it is the only one
/// in which both of those work out without a special case.
/// </remarks>
internal sealed class MpegPictureDecoder {

  /// <summary>Intra-coded: decodable on its own (11172-2, Table 2-D.1; 13818-2, Table 6-12).</summary>
  internal const int IntraCoded = 1;

  /// <summary>Predictively coded, from the picture before it.</summary>
  internal const int PredictiveCoded = 2;

  /// <summary>Bidirectionally coded, from the pictures on either side of it.</summary>
  internal const int BidirectionallyCoded = 3;

  /// <summary>DC coded: the still-picture mode of 11172-2, 2.4.2.8.</summary>
  internal const int DcCoded = 4;

  /// <summary>frame_motion_type "Field-based": two vectors, one per field of the macroblock.</summary>
  private const int _MOTION_FIELD = 1;

  /// <summary>frame_motion_type "Frame-based": one vector for the whole macroblock.</summary>
  private const int _MOTION_FRAME = 2;

  /// <summary>frame_motion_type "Dual-prime".</summary>
  private const int _MOTION_DUAL_PRIME = 3;

  private readonly MpegSequenceHeader _sequence;
  private readonly MpegFrame _target;
  private readonly MpegFrame? _forwardReference;
  private readonly MpegFrame? _backwardReference;
  private readonly bool[] _decoded;
  private readonly MpegBlockRules _rules;

  /// <summary>f_code[s][t]: s is forward or backward, t is horizontal or vertical (13818-2, 6.3.10).</summary>
  private readonly int[,] _fCode = new int[2, 2];

  /// <summary>full_pel_vector, which exists only in MPEG-1, indexed by direction.</summary>
  private readonly bool[] _isFullPel = new bool[2];

  private readonly bool _framePredFrameDct;
  private readonly bool _concealmentMotionVectors;
  private readonly bool _nonLinearQuantiser;

  /// <summary>The macroblock's chrominance tile: 8x8 in 4:2:0, 8x16 in 4:2:2.</summary>
  private readonly int _chromaTileWidth;

  private readonly int _chromaTileHeight;
  private readonly int _blockCount;

  /// <summary>The macroblock's prediction, one buffer per component, in the tile's own coordinates.</summary>
  private readonly int[][] _prediction;

  /// <summary>The second prediction of a bidirectionally predicted macroblock, before averaging.</summary>
  private readonly int[][] _scratch;

  // Slice state.
  private int _quantiserScale;
  private readonly int[] _dcPredictor = new int[3];

  /// <summary>PMV[r][s][t], the motion vector predictors (13818-2, 7.6.3).</summary>
  private readonly int[,,] _predictor = new int[2, 2, 2];

  /// <summary>
  /// The vectors actually used, which are not always the predictors.
  /// </summary>
  /// <remarks>
  /// In a frame picture with field-based prediction the vertical predictor is held in frame lines
  /// while the vector applied is in field lines, so the two differ by a factor of two and the
  /// standard keeps them apart. Storing only one of them and halving it again at prediction time
  /// works until two field-predicted macroblocks follow one another, at which point the second one's
  /// vector is predicted from a value that was already halved.
  /// </remarks>
  private readonly int[,,] _motionVector = new int[2, 2, 2];

  /// <summary>motion_vertical_field_select[r][s]: which field of the reference a vector points into.</summary>
  private readonly bool[,] _fieldSelect = new bool[2, 2];

  private int _address;
  private bool _previousUsedForward;
  private bool _previousUsedBackward;

  private MpegPictureDecoder(
    MpegSequenceHeader sequence, MpegFrame target, MpegFrame? forwardReference, MpegFrame? backwardReference,
    int codingType, MpegPictureHeader header) {
    this._sequence = sequence;
    this._target = target;
    this._forwardReference = forwardReference;
    this._backwardReference = backwardReference;
    this.CodingType = codingType;
    this._decoded = new bool[sequence.MacroblockWidth * sequence.MacroblockHeight];

    this._fCode[0, 0] = header.ForwardHorizontalFCode;
    this._fCode[0, 1] = header.ForwardVerticalFCode;
    this._fCode[1, 0] = header.BackwardHorizontalFCode;
    this._fCode[1, 1] = header.BackwardVerticalFCode;
    this._isFullPel[0] = header.ForwardIsFullPel;
    this._isFullPel[1] = header.BackwardIsFullPel;
    this._framePredFrameDct = header.FramePredFrameDct;
    this._concealmentMotionVectors = header.ConcealmentMotionVectors;
    this._nonLinearQuantiser = header.NonLinearQuantiser;

    this._rules = new() {
      IsMpeg2 = sequence.IsMpeg2,
      UseIntraCoefficientTable = header.IntraVlcFormat,
      Scan = header.AlternateScan ? MpegQuantisation.AlternateScan : MpegQuantisation.ZigZagScan,
      IntraDcMultiplier = 8 >> header.IntraDcPrecision,
    };

    this._chromaTileWidth = 8;
    this._chromaTileHeight = sequence.ChromaFormat == MpegChromaFormat.Yuv420 ? 8 : 16;
    this._blockCount = sequence.BlockCount;

    var chromaSamples = this._chromaTileWidth * this._chromaTileHeight;
    this._prediction = [new int[256], new int[chromaSamples], new int[chromaSamples]];
    this._scratch = [new int[256], new int[chromaSamples], new int[chromaSamples]];
  }

  /// <summary>Which of I, P, B this picture is.</summary>
  internal int CodingType { get; }

  /// <summary>The picture being reconstructed.</summary>
  internal MpegFrame Target => this._target;

  /// <summary>
  /// Prepares to decode the slices of a picture whose header has been read.
  /// </summary>
  /// <remarks>
  /// The header arrives already parsed rather than being read here, because in MPEG-2 it is not
  /// finished when its start code's fields end: a picture coding extension follows one start code
  /// later and replaces most of it. So the picture cannot be begun until both have been seen, and a
  /// method that read the header itself could not be handed one to test it with either.
  /// </remarks>
  /// <exception cref="NotSupportedException">The picture is DC coded, which this decoder does not read.</exception>
  /// <exception cref="InvalidDataException">The picture predicts from a reference the stream has not supplied.</exception>
  internal static MpegPictureDecoder BeginPicture(
    MpegSequenceHeader sequence, MpegFrame target,
    MpegFrame? previousAnchor, MpegFrame? currentAnchor, MpegPictureHeader header) {
    ArgumentNullException.ThrowIfNull(header);

    var codingType = header.CodingType;
    switch (codingType) {
      case IntraCoded:
        return new(sequence, target, null, null, codingType, header);

      case PredictiveCoded:
        return new(
          sequence, target,
          currentAnchor ?? throw new InvalidDataException(
            "A predictively coded MPEG picture arrived before any intra picture, so there is nothing for it to be "
            + "predicted from. Decoding must begin at a sequence header followed by an I picture."),
          null, codingType, header);

      case BidirectionallyCoded:
        if (previousAnchor == null || currentAnchor == null)
          throw new InvalidDataException(
            "A bidirectionally coded MPEG picture arrived before both of the pictures it is predicted from had been "
            + "decoded. Decoding must begin at a sequence header followed by an I picture.");

        return new(sequence, target, previousAnchor, currentAnchor, codingType, header);

      case DcCoded:
        throw new NotSupportedException(
          "This MPEG-1 stream holds a D picture (picture_coding_type 4), the DC-only still-picture mode of "
          + "ISO/IEC 11172-2 2.4.2.8. This decoder reads I, P and B pictures; D pictures are not implemented.");

      default:
        throw new InvalidDataException(
          $"The MPEG picture header states picture_coding_type {codingType}, which the standard leaves forbidden or "
          + "reserved. Only 1 (I), 2 (P), 3 (B) and 4 (D) are defined.");
    }
  }

  // ============================================================================================
  // Slice layer — 11172-2, 2.4.2.6 and 13818-2, 6.2.4
  // ============================================================================================

  /// <summary>
  /// Decodes one slice, positioned just past its start code.
  /// </summary>
  /// <param name="reader">The bitstream.</param>
  /// <param name="startCode">The start code's last byte, which is the slice's row counted from one.</param>
  internal void DecodeSlice(ref MpegBitReader reader, byte startCode) {
    var row = startCode - 1;

    // Past 2800 lines a picture has more macroblock rows than a start code can count, so the top
    // three bits of the row move into the slice header. Below that the field is absent entirely,
    // which is why this is conditional on the picture's height and not on the standard.
    if (this._sequence.IsMpeg2 && this._sequence.Height > 2800)
      row += reader.ReadBits(3) << 7;

    if (row >= this._sequence.MacroblockHeight)
      throw new InvalidDataException(
        $"An MPEG slice states vertical position {row + 1}, past the {this._sequence.MacroblockHeight} macroblock "
        + $"rows of a {this._sequence.Width}x{this._sequence.Height} picture.");

    this._quantiserScale = this._ReadQuantiserScale(ref reader);

    if (this._sequence.IsMpeg2 && reader.NextBits(1) == 1) {
      // intra_slice_flag, intra_slice and seven reserved bits. intra_slice tells an editor whether
      // the slice is all intra macroblocks; nothing in the reconstruction depends on it, and a
      // decoder that trusted it over the macroblock types would be taking a hint for a fact.
      reader.Skip(1);
      reader.Skip(1);
      reader.ReadBits(7);

      while (reader.NextBits(1) == 1) {
        reader.Skip(1);
        reader.ReadBits(8);
      }
    } else {
      // extra_information_slice, the same shape as the picture's.
      while (reader.NextBits(1) == 1) {
        reader.Skip(1);
        reader.ReadBits(8);
      }
    }

    reader.Skip(1);

    // Everything the slice predicts from starts afresh. This is what makes a slice the unit a
    // decoder can resynchronise on after a transmission error.
    this._ResetDcPredictors();
    this._ResetMotionVectorPredictors();
    this._previousUsedForward = this._previousUsedBackward = false;
    this._address = row * this._sequence.MacroblockWidth - 1;

    var isFirst = true;
    do {
      this._DecodeMacroblock(ref reader, isFirst);
      isFirst = false;
    } while (reader.NextBits(23) != 0);
  }

  private int _ReadQuantiserScale(ref MpegBitReader reader) {
    var code = reader.ReadBits(5);
    if (code == 0)
      throw new InvalidDataException(
        "An MPEG quantiser_scale_code of zero was read, which the standard forbids; the range is 1 to 31.");

    return this._sequence.IsMpeg2 ? MpegQuantisation.ScaleOf(code, this._nonLinearQuantiser) : code;
  }

  private void _ResetDcPredictors() {
    var reset = this._rules.IntraDcPredictorReset;
    this._dcPredictor[0] = this._dcPredictor[1] = this._dcPredictor[2] = reset;
  }

  private void _ResetMotionVectorPredictors() {
    Array.Clear(this._predictor);
    Array.Clear(this._motionVector);
    Array.Clear(this._fieldSelect);
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
          $"The slices of this MPEG picture cover {this._decoded.Length - missing} of its {this._decoded.Length} "
          + $"macroblocks; the first one missing is number {address}, at column "
          + $"{address % this._sequence.MacroblockWidth} of row {address / this._sequence.MacroblockWidth}. "
          + "Both standards require the slices of a picture to cover it completely.");
      }
  }

  // ============================================================================================
  // Macroblock layer — 11172-2, 2.4.2.7 and 13818-2, 6.2.5
  // ============================================================================================

  private void _DecodeMacroblock(ref MpegBitReader reader, bool isFirstOfSlice) {
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
        $"An MPEG macroblock address reached {address}, past the {this._decoded.Length} macroblocks of a "
        + $"{this._sequence.Width}x{this._sequence.Height} picture.");

    var type = this._TypeTable().Read(ref reader);
    var isIntra = (type & MpegVlcTables.TypeIntra) != 0;
    var usesForward = (type & MpegVlcTables.TypeMotionForward) != 0;
    var usesBackward = (type & MpegVlcTables.TypeMotionBackward) != 0;

    var motionType = _MOTION_FRAME;
    var isFieldDct = false;

    if (this._sequence.IsMpeg2) {
      if (usesForward || usesBackward)
        motionType = this._framePredFrameDct ? _MOTION_FRAME : reader.ReadBits(2);

      if (!this._framePredFrameDct && (isIntra || (type & MpegVlcTables.TypePattern) != 0))
        isFieldDct = reader.ReadBit() == 1;

      this._RefuseUnsupportedMotionType(motionType, address);
    }

    if ((type & MpegVlcTables.TypeQuant) != 0)
      this._quantiserScale = this._ReadQuantiserScale(ref reader);

    // An intra macroblock carrying concealment vectors codes a forward vector that says where the
    // macroblock would have been predicted from had its own data been lost. Nothing in the
    // reconstruction of an intact stream uses it — but it is in the bitstream, it updates the
    // predictors the next macroblock reads, and skipping over it as though it were not there would
    // leave every following code in the slice read from the wrong bit.
    var readsConcealmentVector = isIntra && this._concealmentMotionVectors;
    if (usesForward || readsConcealmentVector)
      this._ReadMotionVectors(ref reader, direction: 0, readsConcealmentVector ? _MOTION_FRAME : motionType);

    if (usesBackward)
      this._ReadMotionVectors(ref reader, direction: 1, motionType);

    if (readsConcealmentVector)
      reader.Skip(1); // marker_bit

    var pattern = (type & MpegVlcTables.TypePattern) != 0
      ? this._ReadCodedBlockPattern(ref reader)
      : isIntra ? (1 << this._blockCount) - 1 : 0;

    if (isIntra) {
      // An intra macroblock predicts from nothing, so the vectors either side of it do not carry
      // across it either (11172-2, 2.4.4.2; 13818-2, 7.6.3.4) — unless it carried concealment
      // vectors, which are exactly a statement of what the predictors should become.
      if (!readsConcealmentVector)
        this._ResetMotionVectorPredictors();

      this._DecodeIntraMacroblock(ref reader, address, pattern, isFieldDct);
    } else {
      // A predicted macroblock has no intra DC to be a predictor, so the chain is broken here.
      this._ResetDcPredictors();

      // "No MC, coded" in a P picture: the vector is zero and, being transmitted as an absence
      // rather than as a zero code, it resets the predictors as well. The prediction is still made —
      // a P macroblock is always predicted from the forward reference, and a residual added to
      // nothing would be a picture of the residual.
      if (this.CodingType == PredictiveCoded && !usesForward) {
        this._ResetMotionVectorPredictors();
        usesForward = true;
        motionType = _MOTION_FRAME;
      }

      this._DecodePredictedMacroblock(ref reader, address, pattern, usesForward, usesBackward, motionType, isFieldDct);
    }

    this._previousUsedForward = usesForward;
    this._previousUsedBackward = usesBackward;
    this._decoded[address] = true;
  }

  /// <summary>
  /// Refuses the two motion types this decoder does not form a prediction for.
  /// </summary>
  /// <remarks>
  /// Both are refused rather than approximated. Dual-prime derives a second vector from the coded
  /// one and the distance between fields and averages two predictions; treating it as an ordinary
  /// field prediction would read the right bits and produce a picture that is subtly wrong wherever
  /// anything moves. There is no encoder to hand that emits either, so neither could be measured,
  /// and unmeasured motion compensation is exactly the thing this decoder exists not to ship.
  /// </remarks>
  private void _RefuseUnsupportedMotionType(int motionType, int address) {
    switch (motionType) {
      case _MOTION_FRAME:
      case _MOTION_FIELD:
        return;

      case _MOTION_DUAL_PRIME:
        throw new NotSupportedException(
          $"Macroblock {address} of this MPEG-2 picture states frame_motion_type 3 (Dual-prime), the mode of "
          + "ISO/IEC 13818-2 7.6.3.6 that derives a second motion vector from the coded one and the field distance. "
          + "Dual-prime prediction is not implemented.");

      default:
        throw new InvalidDataException(
          $"Macroblock {address} of this MPEG-2 picture states frame_motion_type 0, which ISO/IEC 13818-2 Table 6-17 "
          + "leaves reserved.");
    }
  }

  private MpegVlcTable _TypeTable() => this.CodingType switch {
    IntraCoded => MpegVlcTables.IntraMacroblockType,
    PredictiveCoded => MpegVlcTables.PredictedMacroblockType,
    _ => MpegVlcTables.BidirectionalMacroblockType,
  };

  /// <summary>
  /// Reads coded_block_pattern, which in 4:2:2 is the six-bit code plus two bits of its own.
  /// </summary>
  /// <remarks>
  /// The extra bits are a plain fixed-length field and not part of the variable-length code, because
  /// the two chrominance blocks they cover do not exist in 4:2:0 and the table would otherwise have
  /// had to be replaced rather than extended.
  /// </remarks>
  private int _ReadCodedBlockPattern(ref MpegBitReader reader) {
    if (this._sequence.ChromaFormat == MpegChromaFormat.Yuv420)
      return MpegVlcTables.CodedBlockPattern.Read(ref reader);

    var pattern = MpegVlcTables.CodedBlockPatternWithZero.Read(ref reader);
    return (pattern << 2) | reader.ReadBits(2);
  }

  /// <summary>Reads macroblock_address_increment, taking out the stuffing and the escapes.</summary>
  private static int _ReadAddressIncrement(ref MpegBitReader reader) {
    var increment = 0;
    for (; ; ) {
      var value = MpegVlcTables.MacroblockAddressIncrement.Read(ref reader);
      switch (value) {
        case MpegVlcTables.Stuffing:
          continue;

        case MpegVlcTables.Escape:
          increment += 33;
          continue;

        default:
          return increment + value;
      }
    }
  }

  // ============================================================================================
  // Motion vectors — 11172-2, 2.4.4.2 and 13818-2, 6.2.5.2 and 7.6.3
  // ============================================================================================

  /// <summary>
  /// Reads one direction's motion vectors: one for frame-based prediction, two for field-based
  /// (13818-2, 6.2.5.2).
  /// </summary>
  private void _ReadMotionVectors(ref MpegBitReader reader, int direction, int motionType) {
    if (motionType != _MOTION_FIELD) {
      this._ReadMotionVector(ref reader, r: 0, direction, isFieldFormat: false);

      // A macroblock that codes one vector sets both predictors to it (13818-2, 7.6.3.3). The second
      // predictor is not spare state: the next field-predicted macroblock predicts its bottom
      // field's vector from it, and if it were left holding whatever the last field-predicted
      // macroblock put there, that prediction would be made from a vector belonging to some other
      // part of the picture. The result is a bottom field that is nearly right, in one macroblock,
      // growing through every P picture that follows and resetting at the next I — which is exactly
      // what a fault in motion compensation looks like and nothing like a rounding difference.
      this._predictor[1, direction, 0] = this._predictor[0, direction, 0];
      this._predictor[1, direction, 1] = this._predictor[0, direction, 1];
      return;
    }

    for (var r = 0; r < 2; ++r) {
      this._fieldSelect[r, direction] = reader.ReadBit() == 1;
      this._ReadMotionVector(ref reader, r, direction, isFieldFormat: true);
    }
  }

  private void _ReadMotionVector(ref MpegBitReader reader, int r, int direction, bool isFieldFormat) {
    this._ReadMotionVectorComponent(ref reader, r, direction, component: 0, halveThePrediction: false);
    this._ReadMotionVectorComponent(ref reader, r, direction, component: 1, halveThePrediction: isFieldFormat);
  }

  /// <summary>
  /// Reconstructs one component of one motion vector (11172-2, 2.4.4.2 and 13818-2, 7.6.3.1).
  /// </summary>
  /// <remarks>
  /// The vector is coded as a difference from the previous macroblock's, and the difference is
  /// allowed to wrap: where adding it would leave the range the f_code permits, the alternative
  /// value a whole range away is the one that was meant. Clamping instead would produce a vector
  /// nobody coded.
  /// <para/>
  /// One reconstruction serves both standards. MPEG-1 spells the escape arithmetic as a complement
  /// subtracted from a multiple and MPEG-2 as a multiple added to a residual, and the two are the
  /// same number for every input; writing it once means the MPEG-1 path is exercised by every MPEG-2
  /// stream and the other way about.
  /// <para/>
  /// The halving is the part that only exists in MPEG-2. A frame picture's vertical predictors are
  /// kept in frame lines so that a frame-predicted macroblock can follow a field-predicted one and
  /// still predict from it, while a field-based vector counts field lines — half as many. So the
  /// predictor is halved on the way in and the result doubled on the way out, and the vector this
  /// returns is the one in the units the prediction is actually formed in.
  /// </remarks>
  private void _ReadMotionVectorComponent(
    ref MpegBitReader reader, int r, int direction, int component, bool halveThePrediction) {
    var fCode = this._fCode[direction, component];

    // An MPEG-2 picture that uses no vectors in a direction states f_code 15 there, which is the
    // value meaning "unused" and not a range. A macroblock that then codes a vector against it is
    // asking for a motion_residual of fourteen bits, which would be read out of the next few codes
    // and produce a vector of some thousands of samples — caught eventually by the check that a
    // vector points into the reference, and reported as a wild vector rather than as the header
    // field that made it one.
    if (fCode is < 1 or > 9)
      throw new InvalidDataException(
        $"A macroblock of this MPEG picture codes a {(direction == 0 ? "forward" : "backward")} motion vector, but "
        + $"the picture states f_code {fCode} for its {(component == 0 ? "horizontal" : "vertical")} component. "
        + "ISO/IEC 13818-2 6.3.10 allows 1 to 9, and 15 to mean that the direction carries no vectors at all.");

    var motionCode = MpegVlcTables.MotionCode.Read(ref reader);

    var f = 1 << (fCode - 1);
    var residual = f != 1 && motionCode != 0 ? reader.ReadBits(fCode - 1) : 0;

    int delta;
    if (f == 1 || motionCode == 0)
      delta = motionCode;
    else {
      delta = (Math.Abs(motionCode) - 1) * f + residual + 1;
      if (motionCode < 0)
        delta = -delta;
    }

    var prediction = this._predictor[r, direction, component];

    // 13818-2 7.6.3.1 writes this halving as DIV, which the standard defines as flooring, and not as
    // the division that truncates towards zero — the two disagree for every odd negative predictor,
    // which is half of all upward motion. An arithmetic shift is the flooring one.
    if (halveThePrediction)
      prediction >>= 1;

    var high = 16 * f - 1;
    var low = -16 * f;
    var range = 32 * f;

    var vector = prediction + delta;
    if (vector < low)
      vector += range;
    else if (vector > high)
      vector -= range;

    this._motionVector[r, direction, component] = vector;
    this._predictor[r, direction, component] = halveThePrediction ? vector * 2 : vector;
  }

  // ============================================================================================
  // Reconstruction
  // ============================================================================================

  private void _DecodeIntraMacroblock(ref MpegBitReader reader, int address, int pattern, bool isFieldDct) {
    Span<int> block = stackalloc int[64];

    for (var index = 0; index < this._blockCount; ++index) {
      // An intra macroblock codes every one of its blocks, so the pattern is all ones and this is
      // not a condition in practice; it is written as one anyway because the loop below is shared
      // with nothing and a pattern that ever said otherwise would be a stream this cannot read.
      if ((pattern & (1 << (this._blockCount - 1 - index))) == 0)
        throw new InvalidDataException(
          $"Block {index} of intra macroblock {address} is not coded. Every block of an intra macroblock is coded.");

      var (component, tileX, tileY, rowStep) = this._BlockLayout(index, isFieldDct);
      var isChroma = component != 0;
      var matrix = isChroma ? this._sequence.ChromaIntraMatrix : this._sequence.IntraMatrix;

      this._dcPredictor[component] = MpegBlockDecoder.ReadIntra(
        ref reader, block, isChroma, this._quantiserScale, matrix, this._dcPredictor[component], this._rules);

      this._WriteBlock(block, address, component, tileX, tileY, rowStep, prediction: null);
    }
  }

  private void _DecodePredictedMacroblock(
    ref MpegBitReader reader, int address, int pattern, bool usesForward, bool usesBackward, int motionType,
    bool isFieldDct) {
    Span<int> block = stackalloc int[64];

    this._FormPrediction(address, usesForward, usesBackward, motionType);

    var matrix = this._sequence.NonIntraMatrix;
    var chromaMatrix = this._sequence.ChromaNonIntraMatrix;

    for (var index = 0; index < this._blockCount; ++index) {
      var (component, tileX, tileY, rowStep) = this._BlockLayout(index, isFieldDct);
      var isCoded = (pattern & (1 << (this._blockCount - 1 - index))) != 0;
      if (isCoded)
        MpegBlockDecoder.ReadNonIntra(
          ref reader, block, this._quantiserScale, component == 0 ? matrix : chromaMatrix, this._rules);

      this._WriteBlock(
        isCoded ? block : default, address, component, tileX, tileY, rowStep, this._prediction[component]);
    }
  }

  /// <summary>
  /// Writes one 8x8 block into the target picture, adding it to the macroblock's prediction where
  /// there is one.
  /// </summary>
  /// <param name="block">The transformed residual, or an empty span for a block nothing was coded for.</param>
  /// <param name="prediction">The macroblock's prediction tile, or <c>null</c> for an intra block.</param>
  private void _WriteBlock(
    ReadOnlySpan<int> block, int address, int component, int tileX, int tileY, int rowStep, int[]? prediction) {
    var (plane, planeWidth, _) = this._target.PlaneOf(component);
    var tileWidth = component == 0 ? 16 : this._chromaTileWidth;
    var tileHeight = component == 0 ? 16 : this._chromaTileHeight;

    var column = address % this._sequence.MacroblockWidth;
    var row = address / this._sequence.MacroblockWidth;
    var originX = column * tileWidth + tileX;
    var originY = row * tileHeight + tileY;

    for (var y = 0; y < 8; ++y) {
      var target = (originY + y * rowStep) * planeWidth + originX;
      var inTile = (tileY + y * rowStep) * tileWidth + tileX;

      for (var x = 0; x < 8; ++x) {
        var value = prediction == null ? 0 : prediction[inTile + x];
        if (!block.IsEmpty)
          value += block[y * 8 + x];

        plane[target + x] = _ToSample(value);
      }
    }
  }

  /// <summary>
  /// Where one of a macroblock's blocks sits inside the macroblock, and how far apart its rows are.
  /// </summary>
  /// <remarks>
  /// The four luminance blocks are the macroblock's quadrants in reading order (11172-2, Figure 2-9;
  /// 13818-2, Figure 6-10) — until <c>dct_type</c> says the macroblock was transformed by field, at
  /// which point the same four blocks are the top field's sixteen-by-eight and the bottom field's,
  /// each cut in half down the middle. That is what the row step is for: a field-organised block
  /// holds every other line of the macroblock, so its eight rows are sixteen lines.
  /// <para/>
  /// 4:2:0 chrominance is one block per component whichever way the luminance was transformed, since
  /// eight lines of chrominance cover sixteen of luminance and there is nothing to split. 4:2:2
  /// chrominance is two blocks per component, stacked, and those do split by field along with the
  /// luminance (13818-2, Figure 6-13).
  /// </remarks>
  private (int Component, int X, int Y, int RowStep) _BlockLayout(int index, bool isFieldDct) {
    if (index < 4)
      return isFieldDct
        ? (0, (index & 1) * 8, index >> 1, 2)
        : (0, (index & 1) * 8, (index >> 1) * 8, 1);

    var component = (index & 1) == 0 ? 1 : 2;
    if (this._sequence.ChromaFormat == MpegChromaFormat.Yuv420)
      return (component, 0, 0, 1);

    var half = index < 6 ? 0 : 1;
    return isFieldDct ? (component, 0, half, 2) : (component, 0, half * 8, 1);
  }

  // ============================================================================================
  // Prediction
  // ============================================================================================

  /// <summary>Fills the macroblock's prediction from whichever references it uses.</summary>
  private void _FormPrediction(int address, bool usesForward, bool usesBackward, int motionType) {
    if (usesForward)
      this._PredictFrom(this._prediction, this._forwardReference!, address, direction: 0, motionType, "forward");

    if (!usesBackward)
      return;

    var target = usesForward ? this._scratch : this._prediction;
    this._PredictFrom(target, this._backwardReference!, address, direction: 1, motionType, "backward");

    if (!usesForward)
      return;

    for (var component = 0; component < 3; ++component)
      MpegMotionCompensation.Average(this._prediction[component], this._scratch[component]);
  }

  private void _PredictFrom(
    int[][] destination, MpegFrame reference, int address, int direction, int motionType, string named) {
    var column = address % this._sequence.MacroblockWidth;
    var row = address / this._sequence.MacroblockWidth;

    for (var component = 0; component < 3; ++component) {
      var (plane, planeWidth, planeHeight) = reference.PlaneOf(component);
      var tileWidth = component == 0 ? 16 : this._chromaTileWidth;
      var tileHeight = component == 0 ? 16 : this._chromaTileHeight;
      var blockX = column * tileWidth;
      var blockY = row * tileHeight;

      if (motionType != _MOTION_FIELD) {
        var (vectorX, vectorY) = this._ScaleVector(component, r: 0, direction);
        if (!MpegMotionCompensation.TryPredict(
              destination[component], tileWidth, 0, plane, planeWidth, 0, planeWidth, planeHeight,
              blockX, blockY, tileWidth, tileHeight, vectorX, vectorY))
          throw _OutOfReference(named, address, column, row, vectorX, vectorY, planeWidth, planeHeight, "frame");

        continue;
      }

      // Field-based prediction in a frame picture: the macroblock's even lines are predicted by the
      // first vector and its odd lines by the second, each from whichever field of the reference its
      // own field select names. Both the source and the destination are therefore read at twice the
      // stride, starting one row in for the bottom one.
      for (var r = 0; r < 2; ++r) {
        var field = this._fieldSelect[r, direction] ? 1 : 0;
        var (vectorX, vectorY) = this._ScaleVector(component, r, direction);

        if (!MpegMotionCompensation.TryPredict(
              destination[component], tileWidth * 2, r * tileWidth,
              plane, planeWidth * 2, field * planeWidth, planeWidth, planeHeight / 2,
              blockX, blockY / 2, tileWidth, tileHeight / 2, vectorX, vectorY))
          throw _OutOfReference(
            named, address, column, row, vectorX, vectorY, planeWidth, planeHeight,
            $"{(r == 0 ? "top" : "bottom")} field from the {(field == 0 ? "top" : "bottom")} field");
      }
    }
  }

  /// <summary>
  /// A motion vector in the units the plane it is about to be applied to counts in.
  /// </summary>
  /// <remarks>
  /// Two adjustments, in this order and not the other. MPEG-1's <c>full_pel_vector</c> says the
  /// coded vector counts whole pixels, so it is doubled into the half-pixel units the interpolation
  /// works in; the predictors were updated before this on the coded value, which is why the doubling
  /// happens here and not where the vector was read. Then the chrominance scaling, which depends on
  /// the format: 4:2:0 halves both components because its chrominance planes are half size in both
  /// directions, and 4:2:2 halves only the horizontal one because its are full height.
  /// </remarks>
  private (int X, int Y) _ScaleVector(int component, int r, int direction) {
    var vectorX = this._motionVector[r, direction, 0];
    var vectorY = this._motionVector[r, direction, 1];

    if (this._isFullPel[direction]) {
      vectorX <<= 1;
      vectorY <<= 1;
    }

    if (component == 0)
      return (vectorX, vectorY);

    return (
      MpegMotionCompensation.ToChroma(vectorX),
      this._sequence.ChromaFormat == MpegChromaFormat.Yuv420 ? MpegMotionCompensation.ToChroma(vectorY) : vectorY);
  }

  private static InvalidDataException _OutOfReference(
    string named, int address, int column, int row, int vectorX, int vectorY, int planeWidth, int planeHeight,
    string what)
    => new(
      $"The {named} prediction of the {what} of macroblock {address} (column {column}, row {row}) has a motion vector "
      + $"of ({vectorX}, {vectorY}) half-samples, which reads outside the {planeWidth}x{planeHeight} reference plane. "
      + "Neither ISO/IEC 11172-2 nor ISO/IEC 13818-2 permits a vector that points outside the reference picture.");

  /// <summary>
  /// Reconstructs a macroblock nothing was coded for out of the reference it is predicted from.
  /// </summary>
  /// <remarks>
  /// A skipped macroblock means different things in the two predicted picture types. In a P picture
  /// it is the co-located macroblock of the previous anchor, with a zero vector, and the vector
  /// predictors are reset by it. In a B picture it is predicted from the vector predictors as they
  /// stand, in whichever direction the previous macroblock used, and the predictors are not touched.
  /// Treating both as "copy the co-located block" is a mistake that shows only where a B picture
  /// holds runs of skipped macroblocks over moving content, which is exactly where they are most
  /// common.
  /// <para/>
  /// Both are frame predicted, and the B case is so <b>however the previous macroblock was
  /// predicted</b> — 13818-2 7.6.6.4 says the prediction shall be made as if <c>frame_motion_type</c>
  /// were Frame-based, without qualification. So a skip following a field-predicted macroblock
  /// discards that macroblock's two vectors and both of its field selects and predicts once from the
  /// whole reference frame; only the direction carries over. The predictors are the right units for
  /// that already, which is the reason a field-based vertical vector is doubled on its way into them.
  /// Repeating the previous macroblock's field prediction instead is a plausible reading, produces a
  /// picture, and is wrong.
  /// </remarks>
  private void _SkipMacroblock(int address) {
    if ((uint)address >= (uint)this._decoded.Length)
      throw new InvalidDataException(
        $"An MPEG macroblock address increment skipped past macroblock {address}, past the "
        + $"{this._decoded.Length} a {this._sequence.Width}x{this._sequence.Height} picture holds.");

    switch (this.CodingType) {
      case IntraCoded:
        throw new InvalidDataException(
          $"Macroblock {address} of an MPEG intra picture was skipped. Every macroblock of an I picture is coded; "
          + "neither standard gives a skipped macroblock of an I picture a meaning.");

      case PredictiveCoded:
        this._ResetDcPredictors();
        this._ResetMotionVectorPredictors();
        this._CopyPrediction(address, usesForward: true, usesBackward: false);
        break;

      default:
        if (!this._previousUsedForward && !this._previousUsedBackward)
          throw new InvalidDataException(
            $"Macroblock {address} of an MPEG bidirectionally coded picture was skipped, but the macroblock before "
            + "it was intra coded or none preceded it, so there is no prediction for it to repeat.");

        this._ResetDcPredictors();
        this._TakeVectorsFromPredictors();
        this._CopyPrediction(address, this._previousUsedForward, this._previousUsedBackward);
        break;
    }

    this._decoded[address] = true;
  }

  /// <summary>
  /// Makes the first vector of each direction the predictor, which is what a skipped macroblock of a
  /// B picture is predicted with (13818-2, 7.6.6.4).
  /// </summary>
  private void _TakeVectorsFromPredictors() {
    for (var direction = 0; direction < 2; ++direction)
      for (var component = 0; component < 2; ++component)
        this._motionVector[0, direction, component] = this._predictor[0, direction, component];
  }

  private void _CopyPrediction(int address, bool usesForward, bool usesBackward) {
    this._FormPrediction(address, usesForward, usesBackward, _MOTION_FRAME);

    for (var index = 0; index < this._blockCount; ++index) {
      var (component, tileX, tileY, rowStep) = this._BlockLayout(index, isFieldDct: false);
      this._WriteBlock(default, address, component, tileX, tileY, rowStep, this._prediction[component]);
    }
  }

  private static byte _ToSample(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
}
