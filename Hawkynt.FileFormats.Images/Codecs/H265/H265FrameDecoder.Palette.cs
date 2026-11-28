using System;
using System.IO;

namespace FileFormat.Codecs.H265;

/// <summary>
/// Palette mode, the screen content coding tool for areas of a few flat colours — ITU-T H.265,
/// clauses 7.3.8.13, 7.4.9.13 and 8.4.4.2.7.
/// </summary>
/// <remarks>
/// A palette coded unit has no prediction and no transform. It states a short list of colours, then
/// which of them each sample is, in a snake-shaped scan: runs of one index, and runs that repeat the
/// row above. A sample that is none of the colours is an escape, coded as its own quantised value.
/// The list itself is mostly not sent: a predictor carried from unit to unit holds the colours seen
/// recently, and a unit says which of those it reuses before naming any new ones.
/// <para/>
/// The predictor is entropy-coding state as far as the slice structure is concerned. It is reset
/// wherever the context variables are, saved for the next wavefront row where they are, and
/// carried into a dependent slice segment along with them.
/// </remarks>
internal sealed partial class H265FrameDecoder {

  /// <summary><c>levelScale</c> of equation 8-77, the dequantisation scale of an escape value.</summary>
  private static readonly int[] _EscapeLevelScale = [40, 45, 51, 57, 64, 72];

  /// <summary>Whether each block belongs to a palette coded unit, or <c>null</c> where the sequence has no palette mode.</summary>
  private readonly bool[]? _paletteCoded;

  private int[][] _predictorPalette = [];
  private int[][] _nextPredictorPalette = [];
  private int _predictorPaletteSize;
  private int[][] _wavefrontPredictorPalette = [];
  private int _wavefrontPredictorPaletteSize;

  private int[][] _currentPalette = [];
  private bool[] _paletteReused = [];
  private byte[] _paletteIndexMap = [];
  private bool[] _paletteCopyAbove = [];
  private int[] _paletteIndexIdc = [];
  private int[][] _paletteEscapes = [];
  private int[] _traverseScan = [];
  private int _traverseScanLog2Size = -1;

  private void _AllocatePaletteState() {
    var screen = this._sps.ScreenContent;
    var components = this._chromaArrayType == 0 ? 1 : 3;
    var predictor = Math.Max(screen.PaletteMaxPredictorSize, 1);

    this._predictorPalette = _Planes(components, predictor);
    this._nextPredictorPalette = _Planes(components, predictor);
    this._wavefrontPredictorPalette = _Planes(components, predictor);
    this._currentPalette = _Planes(components, Math.Max(screen.PaletteMaxSize, 1));
    this._paletteReused = new bool[predictor];

    // A palette coded unit is never larger than the largest transform block, which is 32x32.
    this._paletteIndexMap = new byte[32 * 32];
    this._paletteCopyAbove = new bool[32 * 32];
    this._paletteIndexIdc = new int[32 * 32 + 1];
    this._paletteEscapes = _Planes(components, 32 * 32);

    static int[][] _Planes(int count, int length) {
      var planes = new int[count][];
      for (var i = 0; i < count; ++i)
        planes[i] = new int[length];
      return planes;
    }
  }

  /// <summary>
  /// The initialisation process for palette predictor variables — clause 9.3.2.3: the picture
  /// parameter set's initialisers where it states any, else the sequence's, else none.
  /// </summary>
  private void _InitializePalettePredictor() {
    if (this._paletteCoded == null)
      return;

    var initializers = this._pps.PalettePredictorInitializers ?? this._sps.ScreenContent.PalettePredictorInitializers;
    if (initializers == null || initializers.Length == 0) {
      this._predictorPaletteSize = 0;
      return;
    }

    var size = initializers[0].Length;
    if (size > this._predictorPalette[0].Length)
      throw new InvalidDataException(
        $"An H.265 parameter set initialises the palette predictor with {size} colours, more than the "
        + $"{this._predictorPalette[0].Length} the sequence lets it hold.");

    // A monochrome initialiser stated for a colour sequence leaves the chroma entries at zero.
    for (var c = 0; c < this._predictorPalette.Length; ++c)
      if (c < initializers.Length)
        Array.Copy(initializers[c], this._predictorPalette[c], size);
      else
        Array.Clear(this._predictorPalette[c], 0, size);

    this._predictorPaletteSize = size;
  }

  private void _StorePalettePredictorForWavefront() {
    if (this._paletteCoded == null)
      return;

    for (var c = 0; c < this._predictorPalette.Length; ++c)
      Array.Copy(this._predictorPalette[c], this._wavefrontPredictorPalette[c], this._predictorPaletteSize);
    this._wavefrontPredictorPaletteSize = this._predictorPaletteSize;
  }

  private void _RestorePalettePredictorFromWavefront() {
    if (this._paletteCoded == null)
      return;

    for (var c = 0; c < this._predictorPalette.Length; ++c)
      Array.Copy(this._wavefrontPredictorPalette[c], this._predictorPalette[c], this._wavefrontPredictorPaletteSize);
    this._predictorPaletteSize = this._wavefrontPredictorPaletteSize;
  }

  /// <summary>Decodes and reconstructs one palette coded unit — <c>palette_coding(x0, y0, nCbS)</c>.</summary>
  private void _DecodePaletteCodingUnit(int x0, int y0, int log2CbSize) {
    var size = 1 << log2CbSize;
    var total = size * size;
    var screen = this._sps.ScreenContent;
    var components = this._predictorPalette.Length;
    var scan = this._TraverseScan(log2CbSize);

    // Which predictor entries this unit reuses: run lengths of entries skipped, each followed by
    // one reused, a run of one ending the list.
    Array.Clear(this._paletteReused, 0, this._predictorPaletteSize);
    var predicted = 0;
    for (var i = 0; i < this._predictorPaletteSize && predicted < screen.PaletteMaxSize; ++i) {
      var run = _DecodeExponentialGolomb(ref this._cabac, 0);
      if (run == 1)
        break;

      if (run > 1)
        i += run - 1;

      if (i >= this._predictorPaletteSize)
        throw new InvalidDataException(
          $"An H.265 palette coded unit at ({x0}, {y0}) reuses predictor entry {i}, but the predictor holds only "
          + $"{this._predictorPaletteSize}.");

      this._paletteReused[i] = true;
      ++predicted;
    }

    var signalled = predicted < screen.PaletteMaxSize ? _DecodeExponentialGolomb(ref this._cabac, 0) : 0;
    var paletteSize = predicted + signalled;
    if (paletteSize > screen.PaletteMaxSize)
      throw new InvalidDataException(
        $"An H.265 palette coded unit at ({x0}, {y0}) holds {paletteSize} colours, more than the "
        + $"{screen.PaletteMaxSize} its sequence allows.");

    // CurrentPaletteEntries, equation 7-82: the reused entries in predictor order, then the new ones.
    var at = 0;
    for (var i = 0; i < this._predictorPaletteSize; ++i) {
      if (!this._paletteReused[i])
        continue;

      for (var c = 0; c < components; ++c)
        this._currentPalette[c][at] = this._predictorPalette[c][i];
      ++at;
    }

    for (var c = 0; c < components; ++c) {
      var bits = c == 0 ? this._sps.BitDepthLuma : this._sps.BitDepthChroma;
      for (var i = 0; i < signalled; ++i)
        this._currentPalette[c][predicted + i] = this._cabac.DecodeBypassBits(bits);
    }

    // A unit with an empty palette is all escapes, so the flag saying whether there are any is
    // only sent where there is a palette to escape from.
    var escapes = paletteSize == 0 || this._cabac.DecodeBypass() != 0;
    var maxIndex = paletteSize - 1 + (escapes ? 1 : 0);

    // A unit with one index value at most codes none: its single index is the inferred zero.
    this._paletteIndexIdc[0] = 0;
    var indicesMinus1 = 0;
    var finalRunCopiesAbove = false;
    var transpose = false;
    if (maxIndex > 0) {
      indicesMinus1 = H265Residual.DecodeRemaining(ref this._cabac, 3 + ((maxIndex + 1) >> 3), false, 15);
      if (indicesMinus1 >= total)
        throw new InvalidDataException(
          $"An H.265 palette coded unit at ({x0}, {y0}) states {indicesMinus1 + 1} palette indices for "
          + $"{total} samples.");

      var adjust = 0;
      for (var i = 0; i <= indicesMinus1; ++i) {
        this._paletteIndexIdc[i] = maxIndex - adjust > 0 ? this._DecodeTruncatedBinary(maxIndex - adjust) : 0;
        adjust = 1;
      }

      finalRunCopiesAbove = this._cabac.DecodeBin(H265CabacContexts.COPY_ABOVE_PALETTE_INDICES_FLAG) != 0;
      transpose = this._cabac.DecodeBin(H265CabacContexts.PALETTE_TRANSPOSE_FLAG) != 0;
    }

    Array.Clear(this._paletteIndexIdc, indicesMinus1 + 1, this._paletteIndexIdc.Length - indicesMinus1 - 1);

    // Escapes are dequantised, so a unit with any sends its quantiser delta and chroma offsets.
    if (escapes) {
      if (this._pps.CuQpDeltaEnabled && !this._quantiserDeltaCoded) {
        this._quantiserDeltaCoded = true;
        this._SetQuantiser(this._DecodeQuantiserDelta());
      }

      if (!this._cuTransquantBypass)
        this._DecodeChromaQpOffset();
    }

    this._DecodePaletteIndexMap(
      x0, y0, size, scan, maxIndex, indicesMinus1 + 1, finalRunCopiesAbove);

    if (escapes)
      this._DecodePaletteEscapes(size, scan, maxIndex, components, transpose);

    this._ReconstructPalette(x0, y0, log2CbSize, maxIndex, escapes, transpose, components);
    this._UpdatePalettePredictor(paletteSize, components, screen.PaletteMaxPredictorSize);

    this._FillBlocks(this._paletteCoded!, x0, y0, size, size, true);
    this._FillBlocks(this._intraPredModeY, x0, y0, size, size, (byte)H265IntraPrediction.DC);
    this._FillBlocks(this._hasCodedResidual, x0, y0, size, size, false);
    this._MarkPredictionEdges(x0, y0, size, size);
    this._MarkTransformEdges(x0, y0, size, size);
  }

  /// <summary>
  /// The index map: runs of one index and runs copying the row above, in traverse scan order — the
  /// loop of clause 7.3.8.13 that sets <c>PaletteIndexMap</c>.
  /// </summary>
  private void _DecodePaletteIndexMap(
    int x0, int y0, int size, int[] scan, int maxIndex, int indices, bool finalRunCopiesAbove) {
    var total = size * size;
    var map = this._paletteIndexMap;
    var copyAbove = this._paletteCopyAbove;
    var remaining = indices;
    var position = 0;

    while (position < total) {
      var here = scan[position];
      var previous = position > 0 ? scan[position - 1] : -1;

      var copying = false;
      if (maxIndex > 0 && position >= size && !copyAbove[previous])
        copying = remaining > 0 && position < total - 1
          ? this._cabac.DecodeBin(H265CabacContexts.COPY_ABOVE_PALETTE_INDICES_FLAG) != 0
          : !(position == total - 1 && remaining > 0);

      var index = 0;
      var coded = 0;
      if (!copying) {
        // PaletteIndexIdc past the last one coded reads as zero, as the array is initialised.
        coded = this._paletteIndexIdc[indices - remaining];
        index = coded;

        // Equations 7-83 and 7-84: the index the sample would have had by continuing the run
        // before it is never coded, so every coded index at or above it is one too low.
        if (previous >= 0) {
          var reference = copyAbove[previous] ? map[here - size] : map[previous];
          if (index >= reference)
            ++index;
        }
      }

      var runMinus1 = total - position - 1;
      if (maxIndex > 0) {
        if (!copying)
          --remaining;

        if (remaining > 0 || copying != finalRunCopiesAbove) {
          var maxRunMinus1 = total - position - 1 - remaining - (finalRunCopiesAbove ? 1 : 0);
          if (maxRunMinus1 < 0)
            throw new InvalidDataException(
              $"An H.265 palette coded unit at ({x0}, {y0}) has more indices and runs left than samples.");

          runMinus1 = maxRunMinus1 > 0 ? this._DecodePaletteRun(copying, coded, maxRunMinus1) : 0;
        }
      }

      if (position + runMinus1 >= total)
        throw new InvalidDataException(
          $"An H.265 palette coded unit at ({x0}, {y0}) codes a run past its last sample.");

      for (var i = 0; i <= runMinus1; ++i) {
        var target = scan[position++];
        copyAbove[target] = copying;
        map[target] = copying ? map[target - size] : (byte)index;
      }
    }
  }

  /// <summary>
  /// <c>palette_run_prefix</c> and <c>palette_run_suffix</c>, giving <c>PaletteRunMinus1</c> —
  /// equations 7-85 and 7-86 and clause 9.3.4.2.8.
  /// </summary>
  /// <remarks>
  /// The prefix is the position of the run's most significant bit plus one, truncated unary up to
  /// the longest run still possible; its first five bins are context coded, the first of a run of
  /// one index by how large that index was coded. The suffix is the bits below the leading one,
  /// truncated binary where the longest possible run cuts the range short.
  /// </remarks>
  private int _DecodePaletteRun(bool copying, int codedIndex, int maxRunMinus1) {
    var longest = 1;
    while (1 << longest <= maxRunMinus1)
      ++longest;

    var prefix = 0;
    while (prefix < longest) {
      int bin;
      if (prefix > 4)
        bin = this._cabac.DecodeBypass();
      else {
        int context;
        if (copying)
          context = prefix == 0 ? 5 : prefix < 3 ? 6 : 7;
        else if (prefix == 0)
          context = codedIndex < 1 ? 0 : codedIndex < 3 ? 1 : 2;
        else
          context = prefix < 3 ? 3 : 4;

        bin = this._cabac.DecodeBin(H265CabacContexts.PALETTE_RUN_PREFIX + context);
      }

      if (bin == 0)
        break;

      ++prefix;
    }

    if (prefix < 2)
      return prefix;

    var offset = 1 << (prefix - 1);
    if (maxRunMinus1 == offset)
      return offset;

    var suffixMax = offset << 1 > maxRunMinus1 ? maxRunMinus1 - offset : offset - 1;
    return offset + this._DecodeTruncatedBinary(suffixMax);
  }

  /// <summary>The TB binarization of clause 9.3.3.6, every bin bypassed.</summary>
  private int _DecodeTruncatedBinary(int cMax) {
    if (cMax <= 0)
      return 0;

    var n = cMax + 1;
    var k = 0;
    while (2 << k <= n)
      ++k;

    var u = (1 << (k + 1)) - n;
    var value = this._cabac.DecodeBypassBits(k);
    if (value < u)
      return value;

    return ((value << 1) | this._cabac.DecodeBypass()) - u;
  }

  /// <summary>
  /// <c>palette_escape_val</c> for every escape sample, component by component in scan order. A
  /// subsampled chroma plane has an escape only where a chroma sample sits.
  /// </summary>
  private void _DecodePaletteEscapes(int size, int[] scan, int maxIndex, int components, bool transpose) {
    var total = size * size;
    var log2Size = 0;
    while (1 << log2Size < size)
      ++log2Size;

    for (var c = 0; c < components; ++c) {
      var bits = c == 0 ? this._sps.BitDepthLuma : this._sps.BitDepthChroma;
      var escapes = this._paletteEscapes[c];

      for (var position = 0; position < total; ++position) {
        var at = scan[position];
        if (this._paletteIndexMap[at] != maxIndex)
          continue;

        if (c > 0 && !this._ChromaSampleAt(at & (size - 1), at >> log2Size, transpose))
          continue;

        escapes[at] = this._cuTransquantBypass
          ? this._cabac.DecodeBypassBits(bits)
          : _DecodeExponentialGolomb(ref this._cabac, 3);
      }
    }
  }

  /// <summary>Whether the index map position (<paramref name="x"/>, <paramref name="y"/>) carries a chroma sample.</summary>
  private bool _ChromaSampleAt(int x, int y, bool transpose) => this._chromaArrayType switch {
    1 => (x & 1) == 0 && (y & 1) == 0,
    2 => transpose ? (y & 1) == 0 : (x & 1) == 0,
    _ => true,
  };

  /// <summary>
  /// Writes the unit's samples — clause 8.4.4.2.7 and equations 8-13 to 8-20.
  /// </summary>
  /// <remarks>
  /// The index map is laid out the way it was scanned. A transposed unit scanned its columns as
  /// rows, so picture sample (x, y) reads the map at (y, x) — and a subsampled chroma sample reads it
  /// where its luma co-site lands after that swap.
  /// </remarks>
  private void _ReconstructPalette(
    int x0, int y0, int log2Size, int maxIndex, bool escapes, bool transpose, int components) {
    var size = 1 << log2Size;
    var qpY = this._QuantiserAt(x0, y0);

    for (var c = 0; c < components; ++c) {
      var shiftX = c == 0 ? 0 : this._chromaShiftX;
      var shiftY = c == 0 ? 0 : this._chromaShiftY;
      var width = size >> shiftX;
      var height = size >> shiftY;
      var plane = c == 0 ? this._picture.Luma : this._picture.Chroma(c - 1);
      var stride = c == 0 ? this._picture.Width : this._picture.ChromaWidth;
      var left = x0 >> shiftX;
      var top = y0 >> shiftY;
      var bitDepth = c == 0 ? this._sps.BitDepthLuma : this._sps.BitDepthChroma;
      var maximum = (1 << bitDepth) - 1;
      var palette = this._currentPalette[c];
      var escapeValues = this._paletteEscapes[c];

      // Equation 8-77's quantiser, Qp'Y or Qp'Cb or Qp'Cr — never below zero.
      var qp = Math.Max(0, c == 0 ? this._LumaQuantiser(qpY) : this._ChromaQuantiser(qpY, c));
      long scale = _EscapeLevelScale[qp % 6];
      var shift = qp / 6;

      for (var y = 0; y < height; ++y) {
        var row = (top + y) * stride + left;
        for (var x = 0; x < width; ++x) {
          var mapX = transpose ? y << shiftY : x << shiftX;
          var mapY = transpose ? x << shiftX : y << shiftY;
          var at = (mapY << log2Size) + mapX;
          var index = this._paletteIndexMap[at];

          int value;
          if (!escapes || index != maxIndex)
            value = palette[index];
          else if (this._cuTransquantBypass)
            value = escapeValues[at];
          else
            value = (int)Math.Clamp(((escapeValues[at] * scale << shift) + 32) >> 6, 0, maximum);

          plane[row + x] = (ushort)value;
        }
      }
    }
  }

  /// <summary>
  /// The predictor for the next unit — equation 8-79: this unit's palette first, then every entry of
  /// the old predictor it did not reuse, until the predictor is full.
  /// </summary>
  private void _UpdatePalettePredictor(int paletteSize, int components, int maxPredictorSize) {
    var next = this._nextPredictorPalette;
    for (var c = 0; c < components; ++c)
      Array.Copy(this._currentPalette[c], next[c], paletteSize);

    var size = paletteSize;
    for (var i = 0; i < this._predictorPaletteSize && size < maxPredictorSize; ++i) {
      if (this._paletteReused[i])
        continue;

      for (var c = 0; c < components; ++c)
        next[c][size] = this._predictorPalette[c][i];
      ++size;
    }

    (this._predictorPalette, this._nextPredictorPalette) = (next, this._predictorPalette);
    this._predictorPaletteSize = size;
  }

  /// <summary>The traverse scan of clause 6.5.6 for a square block, as row-major positions within it.</summary>
  private int[] _TraverseScan(int log2Size) {
    if (log2Size == this._traverseScanLog2Size)
      return this._traverseScan;

    var size = 1 << log2Size;
    var scan = new int[size * size];
    var i = 0;
    for (var y = 0; y < size; ++y)
      if ((y & 1) == 0)
        for (var x = 0; x < size; ++x)
          scan[i++] = (y << log2Size) + x;
      else
        for (var x = size - 1; x >= 0; --x)
          scan[i++] = (y << log2Size) + x;

    this._traverseScan = scan;
    this._traverseScanLog2Size = log2Size;
    return scan;
  }
}
