using System;
using System.IO;

namespace FileFormat.Codecs.Mpeg;

/// <summary>
/// Decodes one coded picture: its header, its extensions, its slices, its macroblocks and their
/// blocks (ISO/IEC 11172-2, 2.4.2.5 through 2.4.2.8 and 2.4.4; ISO/IEC 13818-2, 6.2.3 through 6.2.6
/// and 7.2 through 7.6).
/// </summary>
/// <remarks>
/// One of these exists for the length of one picture, because everything it holds is reset by the
/// next one: the quantiser scale, the three DC predictors and the motion vector predictors are all
/// per-slice, and the references are per-picture. A decoder that kept them across pictures would
/// still produce a picture, and it would be wrong in a way that only shows up in the second frame of
/// a group.
/// <para/>
/// A field picture still reconstructs into a full frame buffer. Its macroblock coordinates are field
/// coordinates and every reconstructed row lands on one parity of that frame. That is important for
/// the second field of a P-coded frame: H.262 makes the first reconstructed field immediately
/// available as one of the two most recently decoded reference fields, so the two parities may come
/// from different frame buffers while that second field is being decoded.
/// </remarks>
internal sealed class MpegPictureDecoder {

  internal const int IntraCoded = 1;
  internal const int PredictiveCoded = 2;
  internal const int BidirectionallyCoded = 3;
  internal const int DcCoded = 4;

  /// <summary>Field-based prediction in both frame and field pictures.</summary>
  private const int _MOTION_FIELD = 1;

  /// <summary>Frame-based in a frame picture; 16x8 motion compensation in a field picture.</summary>
  private const int _MOTION_FRAME_OR_16X8 = 2;

  private const int _MOTION_DUAL_PRIME = 3;

  private readonly MpegSequenceHeader _sequence;
  private readonly MpegFrame _target;
  private readonly MpegFrame? _forwardReference;
  private readonly MpegFrame? _backwardReference;

  /// <summary>
  /// The first reconstructed field of the coded frame currently being completed. It is a forward
  /// reference only for the second P field, and only for the parity that first field reconstructed.
  /// </summary>
  private readonly MpegFrame? _pairedFieldReference;

  private readonly int _pairedFieldParity;
  private readonly bool _isSecondField;
  private readonly int _firstFieldCodingType;
  private readonly bool _isFieldPicture;
  private readonly int _fieldParity;
  private readonly bool _topFieldFirst;
  private readonly int _macroblockRows;
  private readonly bool[] _decoded;
  private readonly MpegBlockRules _rules;

  /// <summary>f_code[s][t]: s is forward or backward, t is horizontal or vertical (13818-2, 6.3.10).</summary>
  private readonly int[,] _fCode = new int[2, 2];

  /// <summary>full_pel_vector, which exists only in MPEG-1, indexed by direction.</summary>
  private readonly bool[] _isFullPel = new bool[2];

  private readonly bool _framePredFrameDct;
  private readonly bool _concealmentMotionVectors;
  private readonly bool _nonLinearQuantiser;

  /// <summary>The macroblock's chrominance tile: 8 wide in 4:2:x, 16 wide in 4:4:4.</summary>
  private readonly int _chromaTileWidth;

  /// <summary>Eight field/frame rows in 4:2:0, sixteen in 4:2:2 and 4:4:4.</summary>
  private readonly int _chromaTileHeight;
  private readonly int _blockCount;

  /// <summary>The macroblock's prediction, one buffer per component, in the tile's own coordinates.</summary>
  private readonly int[][] _prediction;

  /// <summary>A second prediction, used for B-direction averaging and dual-prime opposite-parity prediction.</summary>
  private readonly int[][] _scratch;

  // Slice state.
  private int _quantiserScale;
  private readonly int[] _dcPredictor = new int[3];

  /// <summary>PMV[r][s][t], the motion vector predictors (13818-2, 7.6.3).</summary>
  private readonly int[,,] _predictor = new int[2, 2, 2];

  /// <summary>The reconstructed motion vectors, in the units used by the selected prediction mode.</summary>
  private readonly int[,,] _motionVector = new int[2, 2, 2];

  /// <summary>motion_vertical_field_select[r][s]: zero top, one bottom.</summary>
  private readonly bool[,] _fieldSelect = new bool[2, 2];

  /// <summary>dmvector[t], the dual-prime differential vector from Table B.11.</summary>
  private readonly int[] _dmVector = new int[2];

  private int _address;
  private bool _previousUsedForward;
  private bool _previousUsedBackward;

  private MpegPictureDecoder(
    MpegSequenceHeader sequence, MpegFrame target, MpegFrame? forwardReference, MpegFrame? backwardReference,
    int codingType, MpegPictureHeader header,
    MpegFrame? pairedFieldReference, int pairedFieldParity, bool isSecondField, int firstFieldCodingType) {
    this._sequence = sequence;
    this._target = target;
    this._forwardReference = forwardReference;
    this._backwardReference = backwardReference;
    this._pairedFieldReference = pairedFieldReference;
    this._pairedFieldParity = pairedFieldParity;
    this._isSecondField = isSecondField;
    this._firstFieldCodingType = firstFieldCodingType;
    this.CodingType = codingType;
    this.PictureStructure = header.PictureStructure;
    this._isFieldPicture = sequence.IsMpeg2 && header.PictureStructure != 3;
    this._fieldParity = this._isFieldPicture ? header.PictureStructure - 1 : -1;
    this._topFieldFirst = header.TopFieldFirst;
    this._macroblockRows = this._isFieldPicture ? sequence.MacroblockHeight / 2 : sequence.MacroblockHeight;
    this._decoded = new bool[sequence.MacroblockWidth * this._macroblockRows];

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

    this._chromaTileWidth = sequence.ChromaFormat == MpegChromaFormat.Yuv444 ? 16 : 8;
    this._chromaTileHeight = sequence.ChromaFormat == MpegChromaFormat.Yuv420 ? 8 : 16;
    this._blockCount = sequence.BlockCount;

    var chromaSamples = this._chromaTileWidth * this._chromaTileHeight;
    this._prediction = [new int[256], new int[chromaSamples], new int[chromaSamples]];
    this._scratch = [new int[256], new int[chromaSamples], new int[chromaSamples]];
  }

  internal int CodingType { get; }

  internal int PictureStructure { get; }

  internal bool IsFieldPicture => this._isFieldPicture;

  internal int FieldParity => this._fieldParity;

  internal MpegFrame Target => this._target;

  /// <summary>Prepares to decode the slices of a picture whose header and MPEG-2 coding extension are complete.</summary>
  internal static MpegPictureDecoder BeginPicture(
    MpegSequenceHeader sequence, MpegFrame target,
    MpegFrame? previousAnchor, MpegFrame? currentAnchor, MpegPictureHeader header,
    MpegFrame? pairedFieldReference = null, int pairedFieldParity = -1,
    bool isSecondField = false, int firstFieldCodingType = 0) {
    ArgumentNullException.ThrowIfNull(header);

    var codingType = header.CodingType;
    switch (codingType) {
      case IntraCoded:
        return new(
          sequence, target, null, null, codingType, header,
          pairedFieldReference, pairedFieldParity, isSecondField, firstFieldCodingType);

      case PredictiveCoded:
        // The second field of a coded I-frame may be P-coded and predict exclusively from the first
        // I field. At the start of a sequence there is deliberately no complete current anchor yet.
        if (currentAnchor == null && pairedFieldReference == null)
          throw new InvalidDataException(
            "A predictively coded MPEG picture arrived before any reference picture, so there is nothing for it to be "
            + "predicted from. Decoding must begin at a sequence header followed by an I picture or I field.");

        return new(
          sequence, target, currentAnchor, null, codingType, header,
          pairedFieldReference, pairedFieldParity, isSecondField, firstFieldCodingType);

      case BidirectionallyCoded:
        if (previousAnchor == null || currentAnchor == null)
          throw new InvalidDataException(
            "A bidirectionally coded MPEG picture arrived before both reference frames had been decoded. Decoding "
            + "must begin at a sequence header followed by an I picture.");

        return new(
          sequence, target, previousAnchor, currentAnchor, codingType, header,
          null, -1, isSecondField, firstFieldCodingType);

      case DcCoded when sequence.IsMpeg2:
        throw new InvalidDataException(
          "An MPEG-2 picture states picture_coding_type 4. ISO/IEC 13818-2 6.3.9 permits only 1 (I), 2 (P) and 3 (B); "
          + "type 4 is the D-picture syntax of MPEG-1 and is not permitted in MPEG-2.");

      case DcCoded:
        return new(sequence, target, null, null, codingType, header, null, -1, false, 0);

      default:
        throw new InvalidDataException(
          $"The MPEG picture header states picture_coding_type {codingType}, which the standard leaves forbidden or "
          + "reserved. Only 1 (I), 2 (P), 3 (B) and, in MPEG-1, 4 (D) are defined.");
    }
  }

  // ============================================================================================
  // Slice layer — 11172-2, 2.4.2.6 and 13818-2, 6.2.4
  // ============================================================================================

  internal void DecodeSlice(ref MpegBitReader reader, byte startCode) {
    var row = startCode - 1;

    if (this._sequence.IsMpeg2 && this._sequence.Height > 2800)
      row += reader.ReadBits(3) << 7;

    if (row >= this._macroblockRows)
      throw new InvalidDataException(
        $"An MPEG slice states vertical position {row + 1}, past the {this._macroblockRows} macroblock rows of this "
        + $"{(this._isFieldPicture ? "field" : "frame")} picture.");

    this._quantiserScale = this._ReadQuantiserScale(ref reader);

    if (this._sequence.IsMpeg2 && reader.NextBits(1) == 1) {
      reader.Skip(1); // intra_slice_flag
      reader.Skip(1); // intra_slice
      reader.ReadBits(7);

      while (reader.NextBits(1) == 1) {
        reader.Skip(1);
        reader.ReadBits(8);
      }
    } else {
      while (reader.NextBits(1) == 1) {
        reader.Skip(1);
        reader.ReadBits(8);
      }
    }

    reader.Skip(1);

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
    Array.Clear(this._dmVector);
  }

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
          + "Both standards require the coded slice structure used here to cover the picture completely.");
      }
  }

  // ============================================================================================
  // Macroblock layer — 11172-2, 2.4.2.7 and 13818-2, 6.2.5
  // ============================================================================================

  private void _DecodeMacroblock(ref MpegBitReader reader, bool isFirstOfSlice) {
    var increment = _ReadAddressIncrement(ref reader);
    var address = this._address + increment;

    if (!isFirstOfSlice)
      for (var skipped = this._address + 1; skipped < address; ++skipped)
        this._SkipMacroblock(skipped);

    this._address = address;
    if ((uint)address >= (uint)this._decoded.Length)
      throw new InvalidDataException(
        $"An MPEG macroblock address reached {address}, past the {this._decoded.Length} macroblocks of this picture.");

    var type = this.CodingType == DcCoded ? this._ReadDcMacroblockType(ref reader, address) : this._TypeTable().Read(ref reader);
    var isIntra = (type & MpegVlcTables.TypeIntra) != 0;
    var usesForward = (type & MpegVlcTables.TypeMotionForward) != 0;
    var usesBackward = (type & MpegVlcTables.TypeMotionBackward) != 0;

    var motionType = _MOTION_FRAME_OR_16X8;
    var isFieldDct = false;

    if (this._sequence.IsMpeg2) {
      if (usesForward || usesBackward)
        motionType = this._isFieldPicture || !this._framePredFrameDct
          ? reader.ReadBits(2)
          : _MOTION_FRAME_OR_16X8;

      // dct_type exists only in frame pictures. A field picture is already field organised, so the
      // transform blocks simply contain successive rows of that field.
      if (!this._isFieldPicture && !this._framePredFrameDct
          && (isIntra || (type & MpegVlcTables.TypePattern) != 0))
        isFieldDct = reader.ReadBit() == 1;

      this._ValidateMotionType(motionType, usesForward, usesBackward, address);
    }

    if ((type & MpegVlcTables.TypeQuant) != 0)
      this._quantiserScale = this._ReadQuantiserScale(ref reader);

    var readsConcealmentVector = isIntra && this._concealmentMotionVectors;
    if (usesForward || readsConcealmentVector)
      this._ReadMotionVectors(
        ref reader, direction: 0,
        readsConcealmentVector
          ? this._isFieldPicture ? _MOTION_FIELD : _MOTION_FRAME_OR_16X8
          : motionType);

    if (usesBackward)
      this._ReadMotionVectors(ref reader, direction: 1, motionType);

    if (readsConcealmentVector)
      reader.Skip(1); // marker_bit

    var pattern = (type & MpegVlcTables.TypePattern) != 0
      ? this._ReadCodedBlockPattern(ref reader)
      : isIntra ? (1 << this._blockCount) - 1 : 0;

    if (isIntra) {
      if (!readsConcealmentVector)
        this._ResetMotionVectorPredictors();

      if (this.CodingType == DcCoded)
        this._DecodeDcMacroblock(ref reader, address, pattern);
      else
        this._DecodeIntraMacroblock(ref reader, address, pattern, isFieldDct);
    } else {
      this._ResetDcPredictors();

      if (this.CodingType == PredictiveCoded && !usesForward) {
        if (this._IsSecondPFieldAfterI())
          throw new InvalidDataException(
            $"Macroblock {address} of the second P field after an I field carries no forward motion vector. "
            + "ISO/IEC 13818-2 7.6.3.5 forbids that case because the prediction must come exclusively from the "
            + "first I field of the coded frame.");

        this._ResetMotionVectorPredictors();
        usesForward = true;
        if (this._isFieldPicture) {
          motionType = _MOTION_FIELD;
          this._fieldSelect[0, 0] = this._fieldParity != 0;
        } else {
          motionType = _MOTION_FRAME_OR_16X8;
        }
      }

      this._DecodePredictedMacroblock(ref reader, address, pattern, usesForward, usesBackward, motionType, isFieldDct);
    }

    if (this.CodingType == DcCoded && reader.ReadBit() != 1)
      throw new InvalidDataException(
        $"Macroblock {address} of an MPEG-1 D picture has end_of_macroblock 0. ISO/IEC 11172-2 2.4.2.7 requires the "
        + "one-bit marker after the six DC-only blocks to be 1.");

    this._previousUsedForward = usesForward;
    this._previousUsedBackward = usesBackward;
    this._decoded[address] = true;
  }

  private int _ReadDcMacroblockType(ref MpegBitReader reader, int address) {
    if (reader.ReadBit() != 1)
      throw new InvalidDataException(
        $"Macroblock {address} of an MPEG-1 D picture has macroblock_type code 0. Table B.2d defines only the "
        + "one-bit code 1, meaning an intra macroblock with no quantiser, motion or pattern fields.");

    return MpegVlcTables.TypeIntra;
  }

  private void _ValidateMotionType(int motionType, bool usesForward, bool usesBackward, int address) {
    if (motionType == 0)
      throw new InvalidDataException(
        $"Macroblock {address} of this MPEG-2 {(this._isFieldPicture ? "field" : "frame")} picture states "
        + $"{(this._isFieldPicture ? "field_motion_type" : "frame_motion_type")} 0, which the corresponding H.262 "
        + "table leaves reserved.");

    if (motionType != _MOTION_DUAL_PRIME)
      return;

    if (this.CodingType != PredictiveCoded || !usesForward || usesBackward)
      throw new InvalidDataException(
        $"Macroblock {address} requests dual-prime prediction outside a forward-predicted P picture. H.262 Tables "
        + "7-13 and 7-14 define dual-prime only for macroblock_motion_forward=1 and macroblock_motion_backward=0.");

    if (this._IsSecondPFieldAfterI())
      throw new InvalidDataException(
        $"Macroblock {address} requests dual-prime in the second P field of a coded frame whose first field is I. "
        + "ISO/IEC 13818-2 7.6.3.5 explicitly forbids dual-prime there.");
  }

  private MpegVlcTable _TypeTable() => this.CodingType switch {
    IntraCoded => MpegVlcTables.IntraMacroblockType,
    PredictiveCoded => MpegVlcTables.PredictedMacroblockType,
    _ => MpegVlcTables.BidirectionalMacroblockType,
  };

  /// <summary>Reads the six base CBP bits and the format-defined extension bits.</summary>
  private int _ReadCodedBlockPattern(ref MpegBitReader reader) {
    if (this._sequence.ChromaFormat == MpegChromaFormat.Yuv420)
      return MpegVlcTables.CodedBlockPattern.Read(ref reader);

    var pattern = MpegVlcTables.CodedBlockPatternWithZero.Read(ref reader);
    return this._sequence.ChromaFormat == MpegChromaFormat.Yuv422
      ? (pattern << 2) | reader.ReadBits(2)
      : (pattern << 6) | reader.ReadBits(6);
  }

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

  private void _ReadMotionVectors(ref MpegBitReader reader, int direction, int motionType) {
    if (!this._sequence.IsMpeg2 || (!this._isFieldPicture && motionType == _MOTION_FRAME_OR_16X8)) {
      this._ReadMotionVector(ref reader, r: 0, direction, isFieldFormat: false);
      this._CopyFirstPredictorToSecond(direction);
      return;
    }

    if (motionType == _MOTION_DUAL_PRIME) {
      this._ReadMotionVector(ref reader, r: 0, direction, isFieldFormat: true);
      this._CopyFirstPredictorToSecond(direction);
      this._dmVector[0] = _ReadDmVector(ref reader);
      this._dmVector[1] = _ReadDmVector(ref reader);
      return;
    }

    if (this._isFieldPicture && motionType == _MOTION_FIELD) {
      this._ReadFieldSelect(ref reader, r: 0, direction);
      this._ReadMotionVector(ref reader, r: 0, direction, isFieldFormat: true);
      this._CopyFirstPredictorToSecond(direction);
      return;
    }

    // Frame-picture field prediction has one vector per destination field. Field-picture 16x8 has
    // one per half of the field macroblock. Both carry a field-select bit before each vector.
    for (var r = 0; r < 2; ++r) {
      this._ReadFieldSelect(ref reader, r, direction);
      this._ReadMotionVector(ref reader, r, direction, isFieldFormat: true);
    }
  }

  private void _ReadFieldSelect(ref MpegBitReader reader, int r, int direction) {
    var selected = reader.ReadBit() == 1;
    this._fieldSelect[r, direction] = selected;

    if (direction == 0 && this._IsSecondPFieldAfterI() && (selected ? 1 : 0) == this._fieldParity)
      throw new InvalidDataException(
        "The second P field of a coded frame whose first field is I selects a forward reference field of the same "
        + "parity as itself. ISO/IEC 13818-2 7.6.3.5 requires every such prediction to use the first I field instead.");
  }

  private void _CopyFirstPredictorToSecond(int direction) {
    this._predictor[1, direction, 0] = this._predictor[0, direction, 0];
    this._predictor[1, direction, 1] = this._predictor[0, direction, 1];
  }

  private void _ReadMotionVector(ref MpegBitReader reader, int r, int direction, bool isFieldFormat) {
    this._ReadMotionVectorComponent(ref reader, r, direction, component: 0, halveThePrediction: false);

    // Only field-format vectors in a FRAME picture scale the vertical PMV between frame-line and
    // field-line units. In a field picture both PMV and vector are already counted in field lines.
    this._ReadMotionVectorComponent(
      ref reader, r, direction, component: 1,
      halveThePrediction: isFieldFormat && !this._isFieldPicture);
  }

  private void _ReadMotionVectorComponent(
    ref MpegBitReader reader, int r, int direction, int component, bool halveThePrediction) {
    var fCode = this._fCode[direction, component];
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
    if (halveThePrediction)
      prediction >>= 1; // H.262 DIV 2: floor, not C# truncation toward zero.

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

  /// <summary>Table B.11: 0 -> 0, 10 -> +1, 11 -> -1.</summary>
  private static int _ReadDmVector(ref MpegBitReader reader) {
    if (reader.ReadBit() == 0)
      return 0;

    return reader.ReadBit() == 0 ? 1 : -1;
  }

  // ============================================================================================
  // Reconstruction
  // ============================================================================================

  private void _DecodeIntraMacroblock(ref MpegBitReader reader, int address, int pattern, bool isFieldDct) {
    Span<int> block = stackalloc int[64];

    for (var index = 0; index < this._blockCount; ++index) {
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

  private void _DecodeDcMacroblock(ref MpegBitReader reader, int address, int pattern) {
    Span<int> block = stackalloc int[64];

    for (var index = 0; index < this._blockCount; ++index) {
      if ((pattern & (1 << (this._blockCount - 1 - index))) == 0)
        throw new InvalidDataException(
          $"Block {index} of D-picture macroblock {address} is not coded. Every block of a D-picture macroblock carries its intra DC.");

      var (component, tileX, tileY, rowStep) = this._BlockLayout(index, isFieldDct: false);
      var isChroma = component != 0;
      this._dcPredictor[component] = this._ReadDcOnlyBlock(ref reader, block, isChroma, this._dcPredictor[component]);
      this._WriteBlock(block, address, component, tileX, tileY, rowStep, prediction: null);
    }
  }

  private int _ReadDcOnlyBlock(
    ref MpegBitReader reader, scoped Span<int> block, bool isChroma, int dcPredictor) {
    block.Clear();

    var size = this._rules.DcSizeTable(isChroma).Read(ref reader);
    var differential = 0;
    if (size > 0) {
      var bits = reader.ReadBits(size);
      differential = (bits & (1 << (size - 1))) != 0 ? bits : bits - (1 << size) + 1;
    }

    var dc = dcPredictor + differential;
    block[0] = Math.Clamp(dc * this._rules.IntraDcMultiplier, -2048, 2047);
    MpegInverseDct.Transform(block);
    return dc;
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

  private void _WriteBlock(
    ReadOnlySpan<int> block, int address, int component, int tileX, int tileY, int rowStep, int[]? prediction) {
    var (plane, planeWidth, _) = this._target.PlaneOf(component);
    var tileWidth = component == 0 ? 16 : this._chromaTileWidth;
    var tileHeight = component == 0 ? 16 : this._chromaTileHeight;

    var column = address % this._sequence.MacroblockWidth;
    var row = address / this._sequence.MacroblockWidth;
    var pictureLineStep = this._isFieldPicture ? 2 : 1;
    var pictureLineOffset = this._isFieldPicture ? this._fieldParity : 0;
    var originX = column * tileWidth + tileX;
    var originY = (row * tileHeight + tileY) * pictureLineStep + pictureLineOffset;

    for (var y = 0; y < 8; ++y) {
      var target = (originY + y * rowStep * pictureLineStep) * planeWidth + originX;
      var inTile = (tileY + y * rowStep) * tileWidth + tileX;

      for (var x = 0; x < 8; ++x) {
        var value = prediction == null ? 0 : prediction[inTile + x];
        if (!block.IsEmpty)
          value += block[y * 8 + x];

        plane[target + x] = _ToSample(value);
      }
    }
  }

  /// <summary>Returns a coded block's component and position in its macroblock prediction tile.</summary>
  private (int Component, int X, int Y, int RowStep) _BlockLayout(int index, bool isFieldDct) {
    if (index < 4)
      return isFieldDct
        ? (0, (index & 1) * 8, index >> 1, 2)
        : (0, (index & 1) * 8, (index >> 1) * 8, 1);

    var component = (index & 1) == 0 ? 1 : 2;
    if (this._sequence.ChromaFormat == MpegChromaFormat.Yuv420)
      return (component, 0, 0, 1);

    if (this._sequence.ChromaFormat == MpegChromaFormat.Yuv422) {
      var half = index < 6 ? 0 : 1;
      return isFieldDct ? (component, 0, half, 2) : (component, 0, half * 8, 1);
    }

    // H.262 Figure 6-12 orders 4:4:4 as Cb 4,6,8,10 and Cr 5,7,9,11. Within
    // each component that means top-left, bottom-left, top-right, bottom-right in frame DCT order.
    // With field DCT the vertical pair becomes top-field/bottom-field exactly as for luma.
    var local = (index - 4) >> 1;
    var x = (local >> 1) * 8;
    var vertical = local & 1;
    return isFieldDct
      ? (component, x, vertical, 2)
      : (component, x, vertical * 8, 1);
  }

  // ============================================================================================
  // Prediction
  // ============================================================================================

  private void _FormPrediction(int address, bool usesForward, bool usesBackward, int motionType) {
    if (usesForward)
      this._PredictFrom(this._prediction, address, direction: 0, motionType, "forward");

    if (!usesBackward)
      return;

    var target = usesForward ? this._scratch : this._prediction;
    this._PredictFrom(target, address, direction: 1, motionType, "backward");

    if (!usesForward)
      return;

    for (var component = 0; component < 3; ++component)
      MpegMotionCompensation.Average(this._prediction[component], this._scratch[component]);
  }

  private void _PredictFrom(int[][] destination, int address, int direction, int motionType, string named) {
    if (this._isFieldPicture) {
      this._PredictFieldPictureFrom(destination, address, direction, motionType, named);
      return;
    }

    switch (motionType) {
      case _MOTION_FIELD:
        this._PredictFramePictureByFields(destination, address, direction, named);
        return;

      case _MOTION_DUAL_PRIME:
        this._PredictFramePictureDualPrime(destination, address, named);
        return;

      default:
        this._PredictFramePictureAsFrame(destination, address, direction, named);
        return;
    }
  }

  private void _PredictFramePictureAsFrame(int[][] destination, int address, int direction, string named) {
    var reference = this._BaseReference(direction);
    var column = address % this._sequence.MacroblockWidth;
    var row = address / this._sequence.MacroblockWidth;

    for (var component = 0; component < 3; ++component) {
      var (plane, planeWidth, planeHeight) = reference.PlaneOf(component);
      var tileWidth = component == 0 ? 16 : this._chromaTileWidth;
      var tileHeight = component == 0 ? 16 : this._chromaTileHeight;
      var (vectorX, vectorY) = this._ScaleVector(component, direction, this._motionVector[0, direction, 0], this._motionVector[0, direction, 1]);

      if (!MpegMotionCompensation.TryPredict(
            destination[component], tileWidth, 0,
            plane, planeWidth, 0, planeWidth, planeHeight,
            column * tileWidth, row * tileHeight, tileWidth, tileHeight, vectorX, vectorY))
        throw _OutOfReference(named, address, column, row, vectorX, vectorY, planeWidth, planeHeight, "frame");
    }
  }

  private void _PredictFramePictureByFields(int[][] destination, int address, int direction, string named) {
    var column = address % this._sequence.MacroblockWidth;
    var row = address / this._sequence.MacroblockWidth;

    for (var component = 0; component < 3; ++component) {
      var tileWidth = component == 0 ? 16 : this._chromaTileWidth;
      var tileHeight = component == 0 ? 16 : this._chromaTileHeight;

      for (var predictedParity = 0; predictedParity < 2; ++predictedParity) {
        var referenceParity = this._fieldSelect[predictedParity, direction] ? 1 : 0;
        var reference = this._ReferenceForField(direction, referenceParity);
        var (plane, planeWidth, planeHeight) = reference.PlaneOf(component);
        var (vectorX, vectorY) = this._ScaleVector(
          component, direction,
          this._motionVector[predictedParity, direction, 0], this._motionVector[predictedParity, direction, 1]);

        if (!MpegMotionCompensation.TryPredict(
              destination[component], tileWidth * 2, predictedParity * tileWidth,
              plane, planeWidth * 2, referenceParity * planeWidth, planeWidth, planeHeight / 2,
              column * tileWidth, row * tileHeight / 2,
              tileWidth, tileHeight / 2, vectorX, vectorY))
          throw _OutOfReference(
            named, address, column, row, vectorX, vectorY, planeWidth, planeHeight,
            $"{(predictedParity == 0 ? "top" : "bottom")} field from the {(referenceParity == 0 ? "top" : "bottom")} field");
      }
    }
  }

  private void _PredictFieldPictureFrom(int[][] destination, int address, int direction, int motionType, string named) {
    if (motionType == _MOTION_DUAL_PRIME) {
      this._PredictFieldPictureDualPrime(destination, address, named);
      return;
    }

    var column = address % this._sequence.MacroblockWidth;
    var row = address / this._sequence.MacroblockWidth;

    for (var component = 0; component < 3; ++component) {
      var tileWidth = component == 0 ? 16 : this._chromaTileWidth;
      var tileHeight = component == 0 ? 16 : this._chromaTileHeight;

      if (motionType == _MOTION_FIELD) {
        var referenceParity = this._fieldSelect[0, direction] ? 1 : 0;
        this._PredictFieldRegion(
          destination[component], component, address, direction, named,
          destinationStart: 0, destinationStride: tileWidth,
          referenceParity, blockY: row * tileHeight,
          width: tileWidth, height: tileHeight,
          this._motionVector[0, direction, 0], this._motionVector[0, direction, 1]);
        continue;
      }

      // field_motion_type 10: independent upper and lower 16x8 predictions.
      var halfHeight = tileHeight / 2;
      for (var r = 0; r < 2; ++r) {
        var referenceParity = this._fieldSelect[r, direction] ? 1 : 0;
        this._PredictFieldRegion(
          destination[component], component, address, direction, named,
          destinationStart: r * halfHeight * tileWidth, destinationStride: tileWidth,
          referenceParity, blockY: row * tileHeight + r * halfHeight,
          width: tileWidth, height: halfHeight,
          this._motionVector[r, direction, 0], this._motionVector[r, direction, 1]);
      }
    }
  }

  private void _PredictFramePictureDualPrime(int[][] destination, int address, string named) {
    var column = address % this._sequence.MacroblockWidth;
    var row = address / this._sequence.MacroblockWidth;

    for (var component = 0; component < 3; ++component) {
      var tileWidth = component == 0 ? 16 : this._chromaTileWidth;
      var tileHeight = component == 0 ? 16 : this._chromaTileHeight;

      for (var predictedParity = 0; predictedParity < 2; ++predictedParity) {
        var same = this._ScaleVector(component, 0, this._motionVector[0, 0, 0], this._motionVector[0, 0, 1]);
        var derivedRaw = this._DerivedDualPrimeVector(predictedParity);
        var opposite = this._ScaleVector(component, 0, derivedRaw.X, derivedRaw.Y);

        this._PredictFieldRegion(
          destination[component], component, address, direction: 0, named,
          destinationStart: predictedParity * tileWidth, destinationStride: tileWidth * 2,
          referenceParity: predictedParity, blockY: row * tileHeight / 2,
          width: tileWidth, height: tileHeight / 2,
          same.X, same.Y, vectorAlreadyScaled: true);

        this._PredictFieldRegion(
          this._scratch[component], component, address, direction: 0, named,
          destinationStart: predictedParity * tileWidth, destinationStride: tileWidth * 2,
          referenceParity: 1 - predictedParity, blockY: row * tileHeight / 2,
          width: tileWidth, height: tileHeight / 2,
          opposite.X, opposite.Y, vectorAlreadyScaled: true);

        _AverageRegion(
          destination[component], this._scratch[component],
          predictedParity * tileWidth, tileWidth * 2, tileWidth, tileHeight / 2);
      }
    }
  }

  private void _PredictFieldPictureDualPrime(int[][] destination, int address, string named) {
    var row = address / this._sequence.MacroblockWidth;

    for (var component = 0; component < 3; ++component) {
      var tileWidth = component == 0 ? 16 : this._chromaTileWidth;
      var tileHeight = component == 0 ? 16 : this._chromaTileHeight;
      var same = this._ScaleVector(component, 0, this._motionVector[0, 0, 0], this._motionVector[0, 0, 1]);
      var derivedRaw = this._DerivedDualPrimeVector(this._fieldParity);
      var opposite = this._ScaleVector(component, 0, derivedRaw.X, derivedRaw.Y);

      this._PredictFieldRegion(
        destination[component], component, address, direction: 0, named,
        destinationStart: 0, destinationStride: tileWidth,
        referenceParity: this._fieldParity, blockY: row * tileHeight,
        width: tileWidth, height: tileHeight,
        same.X, same.Y, vectorAlreadyScaled: true);

      this._PredictFieldRegion(
        this._scratch[component], component, address, direction: 0, named,
        destinationStart: 0, destinationStride: tileWidth,
        referenceParity: 1 - this._fieldParity, blockY: row * tileHeight,
        width: tileWidth, height: tileHeight,
        opposite.X, opposite.Y, vectorAlreadyScaled: true);

      MpegMotionCompensation.Average(destination[component], this._scratch[component]);
    }
  }

  private void _PredictFieldRegion(
    int[] destination, int component, int address, int direction, string named,
    int destinationStart, int destinationStride, int referenceParity, int blockY,
    int width, int height, int vectorX, int vectorY, bool vectorAlreadyScaled = false) {
    var reference = this._ReferenceForField(direction, referenceParity);
    var (plane, planeWidth, planeHeight) = reference.PlaneOf(component);
    var column = address % this._sequence.MacroblockWidth;
    var row = address / this._sequence.MacroblockWidth;
    var tileWidth = component == 0 ? 16 : this._chromaTileWidth;

    if (!vectorAlreadyScaled)
      (vectorX, vectorY) = this._ScaleVector(component, direction, vectorX, vectorY);

    if (!MpegMotionCompensation.TryPredict(
          destination, destinationStride, destinationStart,
          plane, planeWidth * 2, referenceParity * planeWidth, planeWidth, planeHeight / 2,
          column * tileWidth, blockY, width, height, vectorX, vectorY))
      throw _OutOfReference(
        named, address, column, row, vectorX, vectorY, planeWidth, planeHeight,
        $"{(referenceParity == 0 ? "top" : "bottom")} reference field");
  }

  /// <summary>H.262 7.6.3.6, including //2 rounding to nearest with half values away from zero.</summary>
  private (int X, int Y) _DerivedDualPrimeVector(int predictedParity) {
    var referenceParity = 1 - predictedParity;
    int m;
    if (this._isFieldPicture) {
      m = 1;
    } else if (predictedParity == 0) {
      m = this._topFieldFirst ? 1 : 3;
    } else {
      m = this._topFieldFirst ? 3 : 1;
    }

    var e = referenceParity == 0 ? 1 : -1;
    return (
      _RoundHalfAwayFromZero(this._motionVector[0, 0, 0] * m) + this._dmVector[0],
      _RoundHalfAwayFromZero(this._motionVector[0, 0, 1] * m) + e + this._dmVector[1]);
  }

  private static int _RoundHalfAwayFromZero(int value)
    => value >= 0 ? (value + 1) / 2 : (value - 1) / 2;

  private static void _AverageRegion(
    int[] destination, int[] other, int start, int stride, int width, int height) {
    for (var y = 0; y < height; ++y) {
      var row = start + y * stride;
      for (var x = 0; x < width; ++x)
        destination[row + x] = (destination[row + x] + other[row + x] + 1) >> 1;
    }
  }

  private MpegFrame _BaseReference(int direction)
    => (direction == 0 ? this._forwardReference : this._backwardReference)
       ?? throw new InvalidDataException(
         $"This MPEG picture requests a {(direction == 0 ? "forward" : "backward")} prediction before that reference exists.");

  private MpegFrame _ReferenceForField(int direction, int referenceParity) {
    if (direction == 0 && this._isFieldPicture && this._isSecondField
        && this._pairedFieldReference != null && referenceParity == this._pairedFieldParity)
      return this._pairedFieldReference;

    return this._BaseReference(direction);
  }

  /// <summary>Scales a luminance motion vector into the selected component's sample grid.</summary>
  private (int X, int Y) _ScaleVector(int component, int direction, int vectorX, int vectorY) {
    if (this._isFullPel[direction]) {
      vectorX <<= 1;
      vectorY <<= 1;
    }

    if (component == 0 || this._sequence.ChromaFormat == MpegChromaFormat.Yuv444)
      return (vectorX, vectorY);

    vectorX = MpegMotionCompensation.ToChroma(vectorX);
    if (this._sequence.ChromaFormat == MpegChromaFormat.Yuv420)
      vectorY = MpegMotionCompensation.ToChroma(vectorY);

    return (vectorX, vectorY);
  }

  private static InvalidDataException _OutOfReference(
    string named, int address, int column, int row, int vectorX, int vectorY, int planeWidth, int planeHeight,
    string what)
    => new(
      $"The {named} prediction of the {what} of macroblock {address} (column {column}, row {row}) has a motion vector "
      + $"of ({vectorX}, {vectorY}) half-samples, which reads outside the {planeWidth}x{planeHeight} reference plane. "
      + "Neither ISO/IEC 11172-2 nor ISO/IEC 13818-2 permits a vector that points outside the reference picture.");

  private bool _IsSecondPFieldAfterI()
    => this._isFieldPicture && this._isSecondField
       && this.CodingType == PredictiveCoded && this._firstFieldCodingType == IntraCoded;

  // ============================================================================================
  // Skipped macroblocks
  // ============================================================================================

  private void _SkipMacroblock(int address) {
    if ((uint)address >= (uint)this._decoded.Length)
      throw new InvalidDataException(
        $"An MPEG macroblock address increment skipped past macroblock {address}, past the {this._decoded.Length} "
        + "macroblocks this picture holds.");

    switch (this.CodingType) {
      case IntraCoded:
      case DcCoded:
        throw new InvalidDataException(
          $"Macroblock {address} of an MPEG {(this.CodingType == DcCoded ? "D" : "intra")} picture was skipped. Every "
          + $"macroblock of an {(this.CodingType == DcCoded ? "D" : "I")} picture is coded; the standard gives a skipped "
          + "macroblock there no meaning.");

      case PredictiveCoded:
        if (this._IsSecondPFieldAfterI())
          throw new InvalidDataException(
            $"Macroblock {address} is skipped in the second P field after an I field. ISO/IEC 13818-2 7.6.3.5 forbids "
            + "skipped macroblocks in that field.");

        this._ResetDcPredictors();
        this._ResetMotionVectorPredictors();
        if (this._isFieldPicture)
          this._fieldSelect[0, 0] = this._fieldParity != 0;
        this._CopyPrediction(address, usesForward: true, usesBackward: false);
        break;

      default:
        if (!this._previousUsedForward && !this._previousUsedBackward)
          throw new InvalidDataException(
            $"Macroblock {address} of an MPEG bidirectionally coded picture was skipped, but the macroblock before "
            + "it was intra coded or none preceded it, so there is no prediction for it to repeat.");

        this._ResetDcPredictors();
        this._TakeVectorsFromPredictors();
        if (this._isFieldPicture) {
          for (var direction = 0; direction < 2; ++direction)
            this._fieldSelect[0, direction] = this._fieldParity != 0;
        }

        this._CopyPrediction(address, this._previousUsedForward, this._previousUsedBackward);
        break;
    }

    this._decoded[address] = true;
  }

  private void _TakeVectorsFromPredictors() {
    for (var direction = 0; direction < 2; ++direction)
      for (var component = 0; component < 2; ++component)
        this._motionVector[0, direction, component] = this._predictor[0, direction, component];
  }

  private void _CopyPrediction(int address, bool usesForward, bool usesBackward) {
    var motionType = this._isFieldPicture ? _MOTION_FIELD : _MOTION_FRAME_OR_16X8;
    this._FormPrediction(address, usesForward, usesBackward, motionType);

    for (var index = 0; index < this._blockCount; ++index) {
      var (component, tileX, tileY, rowStep) = this._BlockLayout(index, isFieldDct: false);
      this._WriteBlock(default, address, component, tileX, tileY, rowStep, this._prediction[component]);
    }
  }

  private static byte _ToSample(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
}
