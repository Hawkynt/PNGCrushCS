using System;
using System.IO;
using FileFormat.Codecs.H263;

namespace FileFormat.Codecs.H261;

/// <summary>
/// Decodes one coded picture: its groups of blocks, its macroblocks and their blocks (ITU-T H.261,
/// clauses 4.2.2 through 4.2.4 and 3.2).
/// </summary>
/// <remarks>
/// Reconstruction is written directly into a copy of the reference picture rather than into a fresh
/// blank one, and that is not a performance choice — it is what "macroblocks are not transmitted when
/// they contain no information for that part of the picture" (clause 4.2.3.1) has to mean. A macroblock
/// address gap of more than one is not a bit anywhere in the stream; the macroblocks it skips over are
/// never visited by this decoder at all, so whatever they already hold when decoding starts is what the
/// picture shows there, exactly as H.263's explicit per-macroblock COD flag says the same thing one
/// macroblock at a time. The only way for "never visited" to mean "copy of the reference" is for the
/// canvas to already be a copy of the reference before the first macroblock is read.
/// <para/>
/// <b>Reused from H.263 where the arithmetic is the same, written fresh where H.261 differs.</b> The
/// picture buffer (<see cref="H263Frame"/>), the coefficient dequantisation and zig-zag scan (<see
/// cref="H263Quantisation"/>) and the inverse transform (<see cref="H263InverseDct"/>) are H.263's
/// classes used unchanged, because those are the same formulas H.263 kept from this Recommendation —
/// see <see cref="H261BlockDecoder"/> for exactly which. Motion compensation, the loop filter and the
/// group-of-blocks/macroblock-address layer are H.261's own: its vectors are whole-pixel only (clause
/// 3.2.2, "integer values not exceeding &#177;15"), so there is no half-pixel interpolation to share
/// with H.263's <c>H263MotionCompensation</c>; its loop filter runs on the prediction before the
/// residual is added, which H.263 has no equivalent of at all; and its macroblock address is a
/// difference-coded run rather than H.263's per-macroblock COD bit.
/// </remarks>
internal sealed class H261PictureDecoder {

  /// <summary>Macroblocks across one group of blocks (clause 4.2.3, Figure 8).</summary>
  private const int _GroupWidth = 11;

  /// <summary>Macroblock rows in one group of blocks (clause 4.2.3, Figure 8).</summary>
  private const int _GroupHeight = 3;

  /// <summary>How many groups of blocks a CIF picture's width holds side by side (Figure 6).</summary>
  private const int _CifGroupColumns = 2;

  private readonly H261PictureHeader _header;
  private readonly H263Frame _target;
  private readonly H263Frame? _reference;

  private int _quantiser;

  /// <summary>The vector of the last macroblock decoded in the current group of blocks.</summary>
  private int _previousVectorX;

  private int _previousVectorY;

  /// <summary>Whether that macroblock was one of the two motion-compensated MTYPEs (clause 4.2.3.4, item 3).</summary>
  private bool _previousWasMotionCompensated;

  /// <summary>
  /// That macroblock's address within its group of blocks (1 to 33), or zero at the start of a group,
  /// before which nothing may be predicted from (clause 4.2.3.4, items 1 and 2).
  /// </summary>
  private int _previousLocalAddress;

  private H261PictureDecoder(H261PictureHeader header, H263Frame target, H263Frame? reference) {
    this._header = header;
    this._target = target;
    this._reference = reference;
  }

  /// <summary>The picture being reconstructed.</summary>
  internal H263Frame Target => this._target;

  /// <summary>
  /// Prepares to decode a picture whose header has been read, seeding the target with a copy of the
  /// reference so that a macroblock never visited because it was not transmitted reads back exactly
  /// what the reference held there.
  /// </summary>
  internal static H261PictureDecoder BeginPicture(H261PictureHeader header, H263Frame? reference) {
    ArgumentNullException.ThrowIfNull(header);

    var target = new H263Frame(header.MacroblockWidth, header.MacroblockHeight);
    if (reference != null) {
      Array.Copy(reference.Luma, target.Luma, target.Luma.Length);
      Array.Copy(reference.Cb, target.Cb, target.Cb.Length);
      Array.Copy(reference.Cr, target.Cr, target.Cr.Length);
    }

    return new(header, target, reference);
  }

  // ============================================================================================
  // Group of blocks layer — ITU-T H.261, 4.2.2
  // ============================================================================================

  /// <summary>Decodes every group of blocks the picture holds, in the order clause 4.2.2 requires.</summary>
  internal void DecodePicture(ref H263BitReader reader) {
    var isCif = this._header.IsCif;
    var groupCount = this._header.GroupCount;

    for (var index = 0; index < groupCount; ++index) {
      // Figure 6: a CIF picture's twelve groups are numbered 1 to 12 in raster order across a
      // two-column grid; a QCIF picture is exactly as wide as one CIF group and reuses that grid's
      // left (odd-numbered) column alone, so its three groups are numbered 1, 3 and 5.
      var groupNumber = isCif ? index + 1 : 2 * index + 1;
      var groupColumn = (groupNumber - 1) % _CifGroupColumns;
      var groupRow = (groupNumber - 1) / _CifGroupColumns;

      this._ReadGroupHeader(ref reader, groupNumber);
      this._DecodeGroupMacroblocks(ref reader, groupNumber, groupColumn * _GroupWidth, groupRow * _GroupHeight);
    }
  }

  private void _ReadGroupHeader(ref H263BitReader reader, int expectedNumber) {
    if (reader.BitsRemaining < H261PictureHeader.GroupStartCodeLength + 4)
      throw new InvalidDataException(
        $"The H.261 bitstream ended before group of blocks {expectedNumber} of {this._header.GroupCount}. ITU-T "
        + "H.261 clause 4.2.2 requires a header for every group of a picture, whether or not it carries "
        + "macroblocks.");

    var start = reader.ReadBits(H261PictureHeader.GroupStartCodeLength);
    if (start != H261PictureHeader.GroupStartCode)
      throw new InvalidDataException(
        $"Expected the group of blocks start code (ITU-T H.261 4.2.2.1) before group {expectedNumber} of this "
        + "picture, and read something else. A header is mandatory for every group of a picture, unlike H.263's "
        + "optional ones.");

    var groupNumber = reader.ReadBits(4);
    if (groupNumber == 0)
      throw new InvalidDataException(
        $"A picture start code was reached before group {expectedNumber} of {this._header.GroupCount} of this "
        + "H.261 picture had been read.");

    if (groupNumber is 13 or 14 or 15)
      throw new InvalidDataException($"Group of blocks number {groupNumber} is reserved (ITU-T H.261 4.2.2.2).");

    if (groupNumber != expectedNumber)
      throw new InvalidDataException(
        $"Group of blocks {groupNumber} was read where {expectedNumber} was due. ITU-T H.261 clause 4.2.2 requires "
        + "every group's header, in increasing order, whether or not that group carries macroblocks.");

    var quantiser = reader.ReadBits(5);
    if (quantiser == 0)
      throw new InvalidDataException(
        "An H.261 group of blocks states GQUANT 0. QUANT's range is 1 to 31 (the note under Table 5's reconstruction "
        + "formula); zero is not a step size.");

    this._quantiser = quantiser;

    // GEI/GSPARE, clause 4.2.2.4 and 4.2.2.5 — bytes this decoder has no meaning for, each introduced
    // by a set bit, exactly as PEI/PSPARE work in the picture header.
    while (reader.ReadBit() == 1)
      reader.ReadBits(8);

    // Motion vector prediction never crosses a group boundary: clause 4.2.3.4 resets it to zero for
    // macroblocks 1, 12 and 23 unconditionally, which are every group's own first row addresses.
    this._previousLocalAddress = 0;
    this._previousWasMotionCompensated = false;
  }

  // ============================================================================================
  // Macroblock layer — ITU-T H.261, 4.2.3
  // ============================================================================================

  private void _DecodeGroupMacroblocks(ref H263BitReader reader, int groupNumber, int baseColumn, int baseRow) {
    for (; ; ) {
      // A start code (the next group's, or the next picture's) ends this group's macroblocks — the
      // "start code" row of Table 1, read here as a peek rather than a consuming lookup so the group
      // and picture layers above can each read the bits their own header needs. Fewer than sixteen
      // bits remaining is not on its own a start code — a genuine final macroblock of a packet can be
      // shorter than that — so it is only treated as the clean end of the picture's last group when
      // every bit left is the zero padding a byte-based container pads the picture out with; anything
      // else that short is truncated data and is left for the macroblock read below to refuse.
      if (reader.BitsRemaining >= H261PictureHeader.GroupStartCodeLength
          ? reader.NextBits(H261PictureHeader.GroupStartCodeLength) == H261PictureHeader.GroupStartCode
          : reader.BitsRemaining <= 7 && reader.NextBits(reader.BitsRemaining) == 0)
        break;

      var mba = H261VlcTables.MacroblockAddress.Read(ref reader);
      if (mba == H261VlcTables.MbaStuffing)
        continue;

      var localAddress = this._previousLocalAddress == 0 ? mba : this._previousLocalAddress + mba;
      if (localAddress > 33)
        throw new InvalidDataException(
          $"Macroblock address {localAddress} in group {groupNumber} of this H.261 picture reaches past the "
          + "thirty-three a group holds (ITU-T H.261 clause 4.2.3, Figure 8).");

      if (this._reference == null && localAddress != this._previousLocalAddress + 1)
        throw new InvalidDataException(
          $"Macroblock {localAddress} of group {groupNumber} in this H.261 picture is not transmitted immediately "
          + "after the one before it, which leaves it with nothing to copy: this is the first picture of the "
          + "stream, and clause 3.2.1 predicts from a reference this picture does not have.");

      this._DecodeMacroblock(ref reader, baseColumn, baseRow, localAddress);
    }

    if (this._reference == null && this._previousLocalAddress != 33)
      throw new InvalidDataException(
        $"Group {groupNumber} of this first H.261 picture stops at macroblock {this._previousLocalAddress} of 33, "
        + "leaving the rest of the group uncoded with no reference to copy them from.");
  }

  private void _DecodeMacroblock(ref H263BitReader reader, int baseColumn, int baseRow, int localAddress) {
    var typeIndex = H261VlcTables.MacroblockType.Read(ref reader);
    var type = H261MacroblockType.All[typeIndex];

    if (type.Kind != H261PredictionKind.Intra && this._reference == null)
      throw new InvalidDataException(
        $"Macroblock {localAddress} of this H.261 picture predicts from a reference, but this is the first picture "
        + "of the stream and no reference exists (ITU-T H.261 clause 3.2.1).");

    if (type.HasQuantiser) {
      var quantiser = reader.ReadBits(5);
      if (quantiser == 0)
        throw new InvalidDataException(
          "An H.261 macroblock states MQUANT 0. QUANT's range is 1 to 31; zero is not a step size.");

      this._quantiser = quantiser;
    }

    var vectorX = 0;
    var vectorY = 0;
    if (type.HasMotionVector) {
      var predictorX = this._MotionVectorPredictor(localAddress, horizontal: true);
      var predictorY = this._MotionVectorPredictor(localAddress, horizontal: false);
      vectorX = _ReconstructVector(predictorX, H261VlcTables.MotionVectorDifference.Read(ref reader));
      vectorY = _ReconstructVector(predictorY, H261VlcTables.MotionVectorDifference.Read(ref reader));
    }

    var codedBlockPattern = type.AllBlocksCoded ? 0b11_1111
      : type.HasCodedBlockPattern ? H261VlcTables.CodedBlockPattern.Read(ref reader)
      : 0;

    var isFiltered = type.Kind == H261PredictionKind.InterWithMotionCompensationAndFilter;

    Span<int> block = stackalloc int[64];
    Span<int> residual = stackalloc int[64];
    for (var index = 0; index < 6; ++index) {
      var isCoded = (codedBlockPattern & (1 << (5 - index))) != 0;

      if (type.Kind == H261PredictionKind.Intra) {
        H261BlockDecoder.ReadIntra(ref reader, block, this._quantiser);
      } else {
        this._Predict(block, baseColumn, baseRow, localAddress, index, vectorX, vectorY);
        if (isFiltered)
          H261LoopFilter.Apply(block);

        if (isCoded) {
          H261BlockDecoder.ReadInter(ref reader, residual, this._quantiser);
          for (var i = 0; i < 64; ++i)
            block[i] += residual[i];
        }
      }

      this._Store(baseColumn, baseRow, localAddress, index, block);
    }

    this._previousVectorX = vectorX;
    this._previousVectorY = vectorY;
    this._previousWasMotionCompensated = type.HasMotionVector;
    this._previousLocalAddress = localAddress;
  }

  // ============================================================================================
  // Motion vectors — ITU-T H.261, 4.2.3.4
  // ============================================================================================

  /// <summary>
  /// The predictor for one component of a macroblock's motion vector: the same component of the
  /// previous macroblock's vector, or zero in the three situations clause 4.2.3.4 names.
  /// </summary>
  /// <remarks>
  /// Unlike H.263's median of three spatial neighbours (clause 6.1.1, added when half-pixel motion
  /// compensation was introduced), H.261's predictor looks at exactly one macroblock: the previous one
  /// in coding order, and only when it is contiguous with this one and was itself motion-compensated.
  /// </remarks>
  private int _MotionVectorPredictor(int localAddress, bool horizontal) {
    var isGroupRowStart = localAddress is 1 or 12 or 23;
    var isContiguous = this._previousLocalAddress != 0 && localAddress == this._previousLocalAddress + 1;

    if (isGroupRowStart || !isContiguous || !this._previousWasMotionCompensated)
      return 0;

    return horizontal ? this._previousVectorX : this._previousVectorY;
  }

  /// <summary>
  /// Reconstructs a vector component from its predictor and the coded MVD, choosing whichever of
  /// Table 3's pair of values the code stands for keeps the vector inside &#177;15 whole pixels (clause
  /// 3.2.2 and 4.2.3.4).
  /// </summary>
  private static int _ReconstructVector(int predictor, int mvd) {
    var vector = predictor + mvd;
    if (vector < -15)
      vector += 32;
    else if (vector > 15)
      vector -= 32;

    return vector;
  }

  // ============================================================================================
  // Reconstruction — ITU-T H.261, 3.2.1 and 3.2.2
  // ============================================================================================

  /// <summary>
  /// Fetches an 8x8 prediction block from the reference at a whole-pixel motion vector.
  /// </summary>
  /// <remarks>
  /// Whole pixels only, because clause 3.2.2 gives H.261's vectors integer components — there is no
  /// half-pixel interpolation in this Recommendation at all, which is what makes this a plain copy
  /// rather than the bilinear filter H263MotionCompensation performs. A vector that would read outside
  /// the reference is refused rather than clamped: clause 3.2.2 restricts every vector so that all
  /// pels it references lie inside the coded picture area, and H.261 has no annex that lifts that
  /// restriction the way H.263's Annex D does.
  /// </remarks>
  private void _Predict(
    Span<int> block, int baseColumn, int baseRow, int localAddress, int index, int vectorX, int vectorY) {
    var reference = this._reference
      ?? throw new InvalidDataException(
        $"Block {index} of macroblock {localAddress} of this H.261 picture is predicted, but the picture holds no "
        + "reference to predict from.");

    var isChroma = index >= 4;
    var (plane, planeWidth, planeHeight) = isChroma
      ? (index == 4 ? reference.Cb : reference.Cr, reference.ChromaWidth, reference.ChromaHeight)
      : (reference.Luma, reference.LumaWidth, reference.LumaHeight);

    // Truncated towards zero, not rounded: clause 3.2.2's own words for deriving the chrominance
    // vector from the macroblock's ("luminance") one. C#'s integer division already truncates towards
    // zero for both positive and negative operands, which is exactly this rule.
    var mvX = isChroma ? vectorX / 2 : vectorX;
    var mvY = isChroma ? vectorY / 2 : vectorY;

    var (left, top) = _BlockOrigin(baseColumn, baseRow, localAddress, index);
    var sourceX = left + mvX;
    var sourceY = top + mvY;

    if (sourceX < 0 || sourceY < 0 || sourceX + 8 > planeWidth || sourceY + 8 > planeHeight)
      throw new InvalidDataException(
        $"Block {index} of macroblock {localAddress} of this H.261 picture has a motion vector of ({vectorX}, "
        + $"{vectorY}) whole pixels from ({left}, {top}), which reads outside the {planeWidth}x{planeHeight} "
        + "reference plane. ITU-T H.261 clause 3.2.2 requires every referenced pel to lie inside the coded picture "
        + "area.");

    for (var y = 0; y < 8; ++y) {
      var row = (sourceY + y) * planeWidth + sourceX;
      for (var x = 0; x < 8; ++x)
        block[y * 8 + x] = plane[row + x];
    }
  }

  private void _Store(int baseColumn, int baseRow, int localAddress, int index, ReadOnlySpan<int> samples) {
    var (plane, width, _) = this._target.PlaneOf(index);
    var (left, top) = _BlockOrigin(baseColumn, baseRow, localAddress, index);

    for (var y = 0; y < 8; ++y) {
      var row = (top + y) * width + left;
      for (var x = 0; x < 8; ++x) {
        var value = samples[y * 8 + x];
        plane[row + x] = (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
      }
    }
  }

  /// <summary>
  /// Where one of a macroblock's six blocks sits in its plane (ITU-T H.261, Figure 8 and Figure 10).
  /// </summary>
  private static (int Left, int Top) _BlockOrigin(int baseColumn, int baseRow, int localAddress, int index) {
    var withinGroupColumn = (localAddress - 1) % _GroupWidth;
    var withinGroupRow = (localAddress - 1) / _GroupWidth;
    var macroblockColumn = baseColumn + withinGroupColumn;
    var macroblockRow = baseRow + withinGroupRow;

    return index < 4
      ? (macroblockColumn * 16 + (index & 1) * 8, macroblockRow * 16 + (index >> 1) * 8)
      : (macroblockColumn * 8, macroblockRow * 8);
  }
}
