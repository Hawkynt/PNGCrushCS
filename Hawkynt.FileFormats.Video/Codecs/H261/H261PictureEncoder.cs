using System;
using FileFormat.Codecs.H263;

namespace FileFormat.Codecs.H261;

/// <summary>
/// Codes one picture: its header, its groups of blocks, its macroblocks and their blocks (ITU-T H.261,
/// clauses 4.2.1 through 4.2.4 and 3.2).
/// </summary>
/// <remarks>
/// The mirror of <see cref="H261PictureDecoder"/>, and deliberately its mirror in structure as well as
/// in output: the same group numbering off Figure 6, the same macroblock geometry off Figures 8 and 10,
/// the same motion-vector predictor of 4.2.3.4 and the same loop filter of 3.2.3 in the same place in
/// the pipeline. Where the decoder reads a field this writes one, and both walk the picture in the one
/// order the Recommendation allows.
/// <para/>
/// <b>What it chooses, and on what grounds.</b> H.261 leaves an encoder four decisions a picture and
/// several a macroblock, and the ones taken here are the ITU test model's own:
/// <list type="bullet">
/// <item>A macroblock's motion vector, by a logarithmic search of the reference over the &#177;15 whole
/// pixels clause 3.2.2 allows, narrowed further so that every pel referenced lies inside the coded
/// picture area — which the Recommendation requires and this library's decoder refuses a stream
/// for breaking.</item>
/// <item>Whether to use that vector at all, by the test model's bias towards the zero vector: the
/// search has to beat standing still by a margin before the bits a vector costs are spent.</item>
/// <item>Whether the macroblock is better coded on its own, by the test model's comparison of the
/// prediction error against the spread of the source macroblock about its own mean.</item>
/// <item>Whether the loop filter of 3.2.3 belongs on the prediction, by whether it reduces the
/// prediction error. Nothing forces the choice — the filter is switched per macroblock by MTYPE and
/// costs nothing but the wider code — and it is the one part of the Recommendation ffmpeg's own
/// encoder never writes.</item>
/// </list>
/// A macroblock whose residual quantises away entirely and whose prediction is the co-located one is
/// not transmitted at all: the address of the next one that is transmitted skips over it, which is what
/// clause 4.2.3.1's address difference means and what the decoder's seeded canvas reads back as the
/// reference. That is the whole of the skip machinery — H.261 has no "coded with nothing" macroblock.
/// </remarks>
internal sealed class H261PictureEncoder {

  /// <summary>Macroblocks across one group of blocks (clause 4.2.3, Figure 8).</summary>
  private const int _GroupWidth = 11;

  /// <summary>Macroblock rows in one group of blocks (clause 4.2.3, Figure 8).</summary>
  private const int _GroupHeight = 3;

  /// <summary>How many groups of blocks a CIF picture's width holds side by side (Figure 6).</summary>
  private const int _CifGroupColumns = 2;

  /// <summary>The furthest a motion vector may reach, in whole pixels (clause 3.2.2).</summary>
  private const int _MaxVector = 15;

  /// <summary>
  /// How much better than standing still a searched vector has to be before it is worth its bits.
  /// </summary>
  /// <remarks>
  /// The ITU test model's own figure, and the reason it exists is not rate: a vector that barely wins
  /// on prediction error tends to lose on the residual it leaves, because a displaced prediction
  /// carries the reference's own coding noise into a different place each picture.
  /// </remarks>
  private const int _ZeroVectorBias = 100;

  /// <summary>
  /// How much better than the predicted one an intra macroblock has to look before it is coded as one.
  /// </summary>
  /// <remarks>
  /// The test model's own figure again. An intra macroblock costs several times a predicted one, so
  /// the comparison is deliberately reluctant; what it is really there to catch is a macroblock the
  /// reference cannot help with at all — an object appearing, a scene changing — where a predicted
  /// coding would spend more bits than an intra one and still look worse.
  /// </remarks>
  private const int _IntraBias = 500;

  private readonly bool _intra;
  private readonly int _temporalReference;
  private readonly int _quantiser;
  private readonly H263Frame _source;
  private readonly H263Frame _target;
  private readonly H263Frame? _reference;
  private readonly bool _isCif;
  private readonly int _groupCount;
  private readonly H261BitWriter _writer = new();

  private int _previousVectorX;
  private int _previousVectorY;
  private bool _previousWasMotionCompensated;
  private int _previousLocalAddress;

  internal H261PictureEncoder(
    bool intra, int temporalReference, int quantiser, H263Frame source, H263Frame target,
    H263Frame? reference, bool isCif, int groupCount) {
    this._intra = intra || reference == null;
    this._temporalReference = temporalReference;
    this._quantiser = quantiser;
    this._source = source;
    this._target = target;
    this._reference = reference;
    this._isCif = isCif;
    this._groupCount = groupCount;

    // Everything the picture never mentions reads back as the reference left it, which is only true of
    // a canvas that starts as a copy of it — the encoder's side of the decoder's own seeding.
    if (reference != null) {
      Array.Copy(reference.Luma, target.Luma, target.Luma.Length);
      Array.Copy(reference.Cb, target.Cb, target.Cb.Length);
      Array.Copy(reference.Cr, target.Cr, target.Cr.Length);
    }
  }

  /// <summary>Codes the whole picture and answers its bytes, the last one padded with zeroes.</summary>
  internal byte[] Encode() {
    this._WritePictureHeader();

    for (var index = 0; index < this._groupCount; ++index) {
      // Figure 6 again, read the same way the decoder reads it: a QCIF picture is as wide as one CIF
      // group and reuses that grid's odd-numbered column alone.
      var groupNumber = this._isCif ? index + 1 : 2 * index + 1;
      var groupColumn = (groupNumber - 1) % _CifGroupColumns;
      var groupRow = (groupNumber - 1) / _CifGroupColumns;

      this._WriteGroupHeader(groupNumber);
      this._EncodeGroupMacroblocks(groupColumn * _GroupWidth, groupRow * _GroupHeight);
    }

    return this._writer.ToArray();
  }

  // ============================================================================================
  // Picture and group headers — ITU-T H.261, 4.2.1 and 4.2.2
  // ============================================================================================

  private void _WritePictureHeader() {
    this._writer.Write(H261PictureHeader.StartCode, H261PictureHeader.StartCodeLength);
    this._writer.Write(this._temporalReference, 5);

    // PTYPE, clause 4.2.1.3. Split screen, document camera and freeze picture release are instructions
    // to a display rather than to a decoder and none of them is being asked for; then the source
    // format, then the bit that says this is not the still image transmission of Annex D, then the
    // spare bit, which 4.2.1.3 has set to 1 until it is given a meaning.
    this._writer.Write(0, 3);
    this._writer.WriteBit(this._isCif ? 1 : 0);
    this._writer.WriteBit(1);
    this._writer.WriteBit(1);

    // PEI, clause 4.2.1.4: no extra insertion information follows.
    this._writer.WriteBit(0);
  }

  private void _WriteGroupHeader(int groupNumber) {
    this._writer.Write(H261PictureHeader.GroupStartCode, H261PictureHeader.GroupStartCodeLength);
    this._writer.Write(groupNumber, 4);
    this._writer.Write(this._quantiser, 5);

    // GEI, clause 4.2.2.4: no extra insertion information follows.
    this._writer.WriteBit(0);

    // Clause 4.2.3.4 resets the vector predictor at every group's own first row addresses, and the
    // decoder does the same here.
    this._previousLocalAddress = 0;
    this._previousWasMotionCompensated = false;
  }

  // ============================================================================================
  // Macroblock layer — ITU-T H.261, 4.2.3
  // ============================================================================================

  private void _EncodeGroupMacroblocks(int baseColumn, int baseRow) {
    Span<int> samples = stackalloc int[64];
    Span<int> prediction = stackalloc int[64];
    Span<int> residual = stackalloc int[64];
    Span<int> levels = stackalloc int[6 * 64];
    Span<int> reconstruction = stackalloc int[64];

    for (var localAddress = 1; localAddress <= 33; ++localAddress) {
      var (macroblockColumn, macroblockRow) = _MacroblockOf(baseColumn, baseRow, localAddress);

      if (this._intra) {
        this._EncodeIntraMacroblock(localAddress, macroblockColumn, macroblockRow, samples, levels, reconstruction);
        continue;
      }

      this._EncodePredictedMacroblock(
        localAddress, macroblockColumn, macroblockRow, samples, prediction, residual, levels, reconstruction);
    }
  }

  private void _EncodeIntraMacroblock(
    int localAddress, int macroblockColumn, int macroblockRow,
    scoped Span<int> samples, scoped Span<int> levels, scoped Span<int> reconstruction) {
    Span<int> direct = stackalloc int[6];

    for (var index = 0; index < 6; ++index) {
      this._ReadSource(samples, macroblockColumn, macroblockRow, index);
      direct[index] = H261BlockEncoder.QuantiseIntra(samples, this._quantiser, levels.Slice(index * 64, 64));
    }

    this._WriteAddress(localAddress);
    this._writer.WriteCode(H261VlcWriter.MacroblockType[_MacroblockTypeIndex(H261PredictionKind.Intra, coded: true)]);

    for (var index = 0; index < 6; ++index) {
      H261BlockEncoder.WriteIntra(this._writer, direct[index], levels.Slice(index * 64, 64));
      H261BlockEncoder.Reconstruct(levels.Slice(index * 64, 64), this._quantiser, direct[index], reconstruction);
      this._Store(macroblockColumn, macroblockRow, index, reconstruction);
    }

    this._previousVectorX = 0;
    this._previousVectorY = 0;
    this._previousWasMotionCompensated = false;
    this._previousLocalAddress = localAddress;
  }

  private void _EncodePredictedMacroblock(
    int localAddress, int macroblockColumn, int macroblockRow,
    scoped Span<int> samples, scoped Span<int> prediction, scoped Span<int> residual,
    scoped Span<int> levels, scoped Span<int> reconstruction) {
    var reference = this._reference!;

    var zeroError = this._LumaPredictionError(macroblockColumn, macroblockRow, 0, 0);
    var (vectorX, vectorY, searchedError) = this._Search(macroblockColumn, macroblockRow);
    if (searchedError + _ZeroVectorBias >= zeroError) {
      vectorX = 0;
      vectorY = 0;
      searchedError = zeroError;
    }

    if (this._SourceSpread(macroblockColumn, macroblockRow) + _IntraBias < searchedError) {
      this._EncodeIntraMacroblock(localAddress, macroblockColumn, macroblockRow, samples, levels, reconstruction);
      return;
    }

    var filtered = this._FilterHelps(macroblockColumn, macroblockRow, vectorX, vectorY);
    var motionCompensated = filtered || vectorX != 0 || vectorY != 0;

    var codedBlockPattern = 0;
    for (var index = 0; index < 6; ++index) {
      this._ReadSource(samples, macroblockColumn, macroblockRow, index);
      this._Predict(prediction, reference, macroblockColumn, macroblockRow, index, vectorX, vectorY);
      if (filtered)
        H261LoopFilter.Apply(prediction);

      for (var i = 0; i < 64; ++i)
        residual[i] = samples[i] - prediction[i];

      if (H261BlockEncoder.QuantiseInter(residual, this._quantiser, levels.Slice(index * 64, 64)))
        codedBlockPattern |= 1 << (5 - index);
    }

    // Nothing to say and nowhere else to say it from: the address of whichever macroblock is
    // transmitted next steps over this one, and the decoder's canvas already holds the reference here.
    if (codedBlockPattern == 0 && !motionCompensated)
      return;

    var kind = motionCompensated
      ? filtered
        ? H261PredictionKind.InterWithMotionCompensationAndFilter
        : H261PredictionKind.InterWithMotionCompensation
      : H261PredictionKind.Inter;

    this._WriteAddress(localAddress);
    this._writer.WriteCode(H261VlcWriter.MacroblockType[_MacroblockTypeIndex(kind, codedBlockPattern != 0)]);

    if (motionCompensated) {
      this._WriteVector(localAddress, vectorX, horizontal: true);
      this._WriteVector(localAddress, vectorY, horizontal: false);
    }

    if (codedBlockPattern != 0)
      this._writer.WriteCode(H261VlcWriter.CodedBlockPattern[codedBlockPattern]);

    for (var index = 0; index < 6; ++index) {
      var block = levels.Slice(index * 64, 64);
      var coded = (codedBlockPattern & (1 << (5 - index))) != 0;
      if (coded)
        H261BlockEncoder.WriteInter(this._writer, block);

      // Rebuilt exactly as the decoder rebuilds it: the prediction, filtered where MTYPE says so, plus
      // the residual where the pattern says there is one.
      this._Predict(prediction, reference, macroblockColumn, macroblockRow, index, vectorX, vectorY);
      if (filtered)
        H261LoopFilter.Apply(prediction);

      if (coded) {
        H261BlockEncoder.Reconstruct(block, this._quantiser, intraDirect: 0, reconstruction);
        for (var i = 0; i < 64; ++i)
          reconstruction[i] += prediction[i];
      } else {
        prediction.CopyTo(reconstruction);
      }

      this._Store(macroblockColumn, macroblockRow, index, reconstruction);
    }

    this._previousVectorX = vectorX;
    this._previousVectorY = vectorY;
    this._previousWasMotionCompensated = motionCompensated;
    this._previousLocalAddress = localAddress;
  }

  /// <summary>Writes MBA: the absolute address of a group's first transmitted macroblock, or the
  /// difference from the last transmitted one (clause 4.2.3.1).</summary>
  private void _WriteAddress(int localAddress) {
    var address = this._previousLocalAddress == 0 ? localAddress : localAddress - this._previousLocalAddress;
    this._writer.WriteCode(H261VlcWriter.MacroblockAddress[address]);
  }

  /// <summary>Which row of Table 2 states this combination, of the six that need no MQUANT.</summary>
  /// <remarks>
  /// The four rows carrying MQUANT are never written. The quantiser is stated once in every group's
  /// header and left alone across it, which is a choice and not a limitation — there is no rate control
  /// here for a mid-group change to serve, and holding it fixed is what makes the same picture code to
  /// the same bytes.
  /// </remarks>
  private static int _MacroblockTypeIndex(H261PredictionKind kind, bool coded) {
    for (var index = 0; index < H261MacroblockType.All.Length; ++index) {
      var type = H261MacroblockType.All[index];
      if (type.Kind != kind || type.HasQuantiser)
        continue;

      if (kind == H261PredictionKind.Intra || type.HasCodedBlockPattern == coded)
        return index;
    }

    throw new InvalidOperationException(
      $"Table 2/H.261 states no macroblock type for {kind} with{(coded ? string.Empty : "out")} coefficients.");
  }

  // ============================================================================================
  // Motion vectors — ITU-T H.261, 3.2.2 and 4.2.3.4
  // ============================================================================================

  private void _WriteVector(int localAddress, int vector, bool horizontal) {
    var predictor = this._MotionVectorPredictor(localAddress, horizontal);

    // Table 3's codes each stand for two differences thirty-two apart; whichever of the two keeps the
    // reconstruction inside ±15 is the one the decoder will pick, so the encoder states the difference
    // that lands there (4.2.3.4).
    var difference = vector - predictor;
    if (difference < -16)
      difference += 32;
    else if (difference > 15)
      difference -= 32;

    this._writer.WriteCode(H261VlcWriter.MotionVectorDifference[difference]);
  }

  /// <summary>The predictor of clause 4.2.3.4, read exactly as <see cref="H261PictureDecoder"/> reads it.</summary>
  private int _MotionVectorPredictor(int localAddress, bool horizontal) {
    var isGroupRowStart = localAddress is 1 or 12 or 23;
    var isContiguous = this._previousLocalAddress != 0 && localAddress == this._previousLocalAddress + 1;

    if (isGroupRowStart || !isContiguous || !this._previousWasMotionCompensated)
      return 0;

    return horizontal ? this._previousVectorX : this._previousVectorY;
  }

  /// <summary>
  /// The best whole-pixel vector for one macroblock, by a logarithmic search of the reference.
  /// </summary>
  /// <remarks>
  /// Four passes halving the step from eight to one, nine positions each, which reaches every vector
  /// clause 3.2.2 allows while looking at thirty-three of the nine hundred and sixty-one a full search
  /// would. The candidates are clipped to the range that keeps all sixteen by sixteen referenced pels
  /// inside the coded picture — clause 3.2.2 requires it, and the decoder beside this refuses a stream
  /// that breaks it rather than clamping the read.
  /// </remarks>
  private (int X, int Y, int Error) _Search(int macroblockColumn, int macroblockRow) {
    var reference = this._reference!;
    var leftLimit = Math.Max(-_MaxVector, -16 * macroblockColumn);
    var rightLimit = Math.Min(_MaxVector, reference.LumaWidth - 16 * (macroblockColumn + 1));
    var topLimit = Math.Max(-_MaxVector, -16 * macroblockRow);
    var bottomLimit = Math.Min(_MaxVector, reference.LumaHeight - 16 * (macroblockRow + 1));

    var bestX = 0;
    var bestY = 0;
    var bestError = this._LumaPredictionError(macroblockColumn, macroblockRow, 0, 0);

    for (var step = 8; step >= 1; step >>= 1)
      for (var dy = -1; dy <= 1; ++dy)
        for (var dx = -1; dx <= 1; ++dx) {
          if (dx == 0 && dy == 0)
            continue;

          var x = bestX + dx * step;
          var y = bestY + dy * step;
          if (x < leftLimit || x > rightLimit || y < topLimit || y > bottomLimit)
            continue;

          var error = this._LumaPredictionError(macroblockColumn, macroblockRow, x, y);
          if (error >= bestError)
            continue;

          bestError = error;
          bestX = x;
          bestY = y;
        }

    return (bestX, bestY, bestError);
  }

  /// <summary>The absolute prediction error of a macroblock's luminance at one vector.</summary>
  private int _LumaPredictionError(int macroblockColumn, int macroblockRow, int vectorX, int vectorY) {
    var reference = this._reference!;
    var left = macroblockColumn * 16;
    var top = macroblockRow * 16;
    var stride = reference.LumaWidth;
    var error = 0;

    for (var y = 0; y < 16; ++y) {
      var from = (top + y) * stride + left;
      var at = (top + y + vectorY) * stride + left + vectorX;
      for (var x = 0; x < 16; ++x) {
        var difference = this._source.Luma[from + x] - reference.Luma[at + x];
        error += difference < 0 ? -difference : difference;
      }
    }

    return error;
  }

  /// <summary>
  /// How far a macroblock's luminance spreads about its own mean, which is what an intra coding of it
  /// would have to describe.
  /// </summary>
  private int _SourceSpread(int macroblockColumn, int macroblockRow) {
    var left = macroblockColumn * 16;
    var top = macroblockRow * 16;
    var stride = this._source.LumaWidth;

    var sum = 0;
    for (var y = 0; y < 16; ++y) {
      var row = (top + y) * stride + left;
      for (var x = 0; x < 16; ++x)
        sum += this._source.Luma[row + x];
    }

    var mean = (sum + 128) >> 8;
    var spread = 0;
    for (var y = 0; y < 16; ++y) {
      var row = (top + y) * stride + left;
      for (var x = 0; x < 16; ++x) {
        var difference = this._source.Luma[row + x] - mean;
        spread += difference < 0 ? -difference : difference;
      }
    }

    return spread;
  }

  /// <summary>
  /// Whether the loop filter of clause 3.2.3 leaves less for the residual to carry than the unfiltered
  /// prediction does, over all six blocks of the macroblock.
  /// </summary>
  private bool _FilterHelps(int macroblockColumn, int macroblockRow, int vectorX, int vectorY) {
    Span<int> samples = stackalloc int[64];
    Span<int> prediction = stackalloc int[64];
    Span<int> smoothed = stackalloc int[64];

    var plain = 0;
    var filtered = 0;
    for (var index = 0; index < 6; ++index) {
      this._ReadSource(samples, macroblockColumn, macroblockRow, index);
      this._Predict(prediction, this._reference!, macroblockColumn, macroblockRow, index, vectorX, vectorY);
      prediction.CopyTo(smoothed);
      H261LoopFilter.Apply(smoothed);

      for (var i = 0; i < 64; ++i) {
        var a = samples[i] - prediction[i];
        var b = samples[i] - smoothed[i];
        plain += a < 0 ? -a : a;
        filtered += b < 0 ? -b : b;
      }
    }

    return filtered < plain;
  }

  // ============================================================================================
  // Sample access — ITU-T H.261, Figures 8 and 10
  // ============================================================================================

  private void _ReadSource(scoped Span<int> block, int macroblockColumn, int macroblockRow, int index) {
    var (plane, width, _) = this._source.PlaneOf(index);
    var (left, top) = _BlockOrigin(macroblockColumn, macroblockRow, index);

    for (var y = 0; y < 8; ++y) {
      var row = (top + y) * width + left;
      for (var x = 0; x < 8; ++x)
        block[y * 8 + x] = plane[row + x];
    }
  }

  /// <summary>The same whole-pixel copy <see cref="H261PictureDecoder"/> makes, at the same vector.</summary>
  private void _Predict(
    scoped Span<int> block, H263Frame reference, int macroblockColumn, int macroblockRow, int index,
    int vectorX, int vectorY) {
    var isChroma = index >= 4;
    var (plane, planeWidth, _) = isChroma
      ? (index == 4 ? reference.Cb : reference.Cr, reference.ChromaWidth, reference.ChromaHeight)
      : (reference.Luma, reference.LumaWidth, reference.LumaHeight);

    // Truncated towards zero, which is clause 3.2.2's own word for it and what C#'s integer division
    // already does for either sign.
    var mvX = isChroma ? vectorX / 2 : vectorX;
    var mvY = isChroma ? vectorY / 2 : vectorY;

    var (left, top) = _BlockOrigin(macroblockColumn, macroblockRow, index);
    for (var y = 0; y < 8; ++y) {
      var row = (top + y + mvY) * planeWidth + left + mvX;
      for (var x = 0; x < 8; ++x)
        block[y * 8 + x] = plane[row + x];
    }
  }

  private void _Store(int macroblockColumn, int macroblockRow, int index, scoped ReadOnlySpan<int> samples) {
    var (plane, width, _) = this._target.PlaneOf(index);
    var (left, top) = _BlockOrigin(macroblockColumn, macroblockRow, index);

    for (var y = 0; y < 8; ++y) {
      var row = (top + y) * width + left;
      for (var x = 0; x < 8; ++x) {
        var value = samples[y * 8 + x];
        plane[row + x] = (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
      }
    }
  }

  private static (int Column, int Row) _MacroblockOf(int baseColumn, int baseRow, int localAddress)
    => (baseColumn + (localAddress - 1) % _GroupWidth, baseRow + (localAddress - 1) / _GroupWidth);

  private static (int Left, int Top) _BlockOrigin(int macroblockColumn, int macroblockRow, int index)
    => index < 4
      ? (macroblockColumn * 16 + (index & 1) * 8, macroblockRow * 16 + (index >> 1) * 8)
      : (macroblockColumn * 8, macroblockRow * 8);
}
