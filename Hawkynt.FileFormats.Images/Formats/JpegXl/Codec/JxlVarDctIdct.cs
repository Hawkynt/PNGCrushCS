using System;

namespace FileFormat.JpegXl.Codec;

// =====================================================================================
// VarDCT inverse DCT engine (ISO/IEC 18181-1 §G.4 and §G.5).
//
// libjxl reference (BSD-3-Clause):
//   - lib/jxl/dct-inl.h              (templated 1-D DCT/IDCT, separable rows-then-cols)
//   - lib/jxl/dec_transforms-inl.h   (TransformToPixels per-strategy dispatch,
//                                     IDCT2TopBlock, AFVIDCT4x4, AFVTransformToPixels)
//   - lib/jxl/dct_scales.h           (kSqrt2 / kSqrt0_5 / DCTResampleScales)
//   - lib/jxl/ac_strategy.h          (AcStrategyType enum + covered_blocks_x/y)
//   - lib/jxl/ac_strategy.cc         (natural coefficient order)
//
// SCALING CONVENTION (matches libjxl):
//   Forward:  X[k]   = (1/N) * sum_{n=0..N-1} x[n] * cos((n + 0.5) * k * pi / N)
//   Inverse:  x[n]   = X[0] + 2 * sum_{k=1..N-1} X[k] * cos((n + 0.5) * k * pi / N)
//
//   With this convention, X[0] is the *mean* of the spatial samples (DC = average),
//   and a DC-only coefficient block ([c, 0, 0, ...]) reconstructs to a flat block
//   with every pixel equal to `c`. This matches libjxl's
//   `CoeffBundle::StoreToBlockAndScale` which multiplies forward output by 1/N.
//
// SEPARABLE 2-D IDCT:
//   For an H x W block, the 2-D IDCT factors into 1-D IDCTs along columns then
//   rows (or vice versa — the result is the same because IDCT is linear and
//   separable). We use rows-then-columns to match libjxl's traversal order in
//   `ComputeScaledIDCT`.
//
// COEFFICIENT LAYOUT:
//   The `coeffs` array passed to InverseDct8x8 / InverseAcStrategy is in
//   **natural order** (row-major H x W after un-zig-zag), not the bitstream
//   scan/zigzag order. The caller (frame decoder) is responsible for un-zigzag
//   before calling this engine. This matches libjxl's split between
//   `dec_ans.cc` (decodes scan-order coeffs) and the IDCT path which always
//   sees natural-order data.
//
// AXIS CONVENTION (PNGCrushCS, kept for backward compatibility):
//   `Dct{W}x{H}` strategy names parse as **width × height**:
//     - Dct16x8  -> W=16, H=8 (rows are 16 wide; 8 rows tall)
//     - Dct8x16  -> W=8,  H=16
//   This is the OPPOSITE of libjxl's `DCT{ROWS}X{COLS}` / `<ROWS, COLS>` order
//   in `ComputeScaledIDCT<>`. The two are equivalent for square strategies.
//   The numeric covered-block area matches libjxl's
//   `covered_blocks_x() * 8`, `covered_blocks_y() * 8`.
// =====================================================================================

internal static class JxlVarDctIdct {

  // -------------------------------------------------------------------------
  // Public API
  // -------------------------------------------------------------------------

  /// <summary>Inverse-DCT an 8x8 block in place (separable 1-D IDCTs along rows
  /// then columns). Input: dequantized DCT coefficients in **natural order**
  /// (un-zig-zagged, row-major y * 8 + x). Output: spatial pixel values
  /// (still float, possibly negative — XYB conversion happens later).</summary>
  /// <param name="coeffs">Length-64 array of natural-order DCT coefficients.</param>
  /// <param name="outputPixels">Length-64 destination for spatial pixel values.</param>
  public static void InverseDct8x8(float[] coeffs, float[] outputPixels) {
    if (coeffs is null) throw new ArgumentNullException(nameof(coeffs));
    if (outputPixels is null) throw new ArgumentNullException(nameof(outputPixels));
    if (coeffs.Length != 64) throw new ArgumentException("DCT8x8 coeffs must have length 64.", nameof(coeffs));
    if (outputPixels.Length != 64) throw new ArgumentException("DCT8x8 output must have length 64.", nameof(outputPixels));

    _InverseDct2D(coeffs, outputPixels, width: 8, height: 8);
  }

  /// <summary>Per-AC-strategy IDCT. The AC strategy determines block shape and
  /// which DCT variant runs. Dispatches every strategy defined in
  /// <see cref="JxlAcStrategyType"/>: square/rectangular separable DCTs of all
  /// sizes (4×4 sub-tile through 256×256), the multi-level 2×2 Hadamard tower
  /// (DCT2x2), the IDENTITY/Hornuss DC-prediction transform, and the four AFV
  /// (asymmetric/fovea) variants.</summary>
  /// <param name="strategy">AC strategy selected for this block in the bitstream.</param>
  /// <param name="coeffs">Natural-order DCT coefficients, length = W * H.</param>
  /// <param name="outputPixels">Destination spatial pixels, length = W * H.</param>
  public static void InverseAcStrategy(JxlAcStrategyType strategy, float[] coeffs, float[] outputPixels) {
    if (coeffs is null) throw new ArgumentNullException(nameof(coeffs));
    if (outputPixels is null) throw new ArgumentNullException(nameof(outputPixels));

    var (w, h) = BlockSize(strategy);
    var expected = w * h;
    if (coeffs.Length != expected) throw new ArgumentException($"AC strategy {strategy} expects {expected} coefficients, got {coeffs.Length}.", nameof(coeffs));
    if (outputPixels.Length != expected) throw new ArgumentException($"AC strategy {strategy} expects {expected} output pixels, got {outputPixels.Length}.", nameof(outputPixels));

    switch (strategy) {
      // Plain separable IDCTs — square or rectangular. Engine supports any
      // power-of-two dimensions, so all of these route to the same helper.
      case JxlAcStrategyType.Dct8x8:
      case JxlAcStrategyType.Dct16x16:
      case JxlAcStrategyType.Dct32x32:
      case JxlAcStrategyType.Dct16x8:
      case JxlAcStrategyType.Dct8x16:
      case JxlAcStrategyType.Dct32x8:
      case JxlAcStrategyType.Dct8x32:
      case JxlAcStrategyType.Dct32x16:
      case JxlAcStrategyType.Dct16x32:
      case JxlAcStrategyType.Dct64x64:
      case JxlAcStrategyType.Dct64x32:
      case JxlAcStrategyType.Dct32x64:
      case JxlAcStrategyType.Dct128x128:
      case JxlAcStrategyType.Dct128x64:
      case JxlAcStrategyType.Dct64x128:
      case JxlAcStrategyType.Dct256x256:
      case JxlAcStrategyType.Dct256x128:
      case JxlAcStrategyType.Dct128x256:
        _InverseDct2D(coeffs, outputPixels, w, h);
        return;

      // 4x4 IDCT in each of four 4x4 sub-quadrants of an 8x8 block. The four
      // quadrant DCs are encoded as a 2x2 Hadamard block at coeffs[0,0],[0,1],
      // [1,0],[1,1]; AC coefficients are interleaved at stride 2.
      case JxlAcStrategyType.Dct4x4:
        _InverseDct4x4QuadBlock(coeffs, outputPixels);
        return;

      // 4x8 IDCT in each of two 4x8 sub-strips of an 8x8 block (stacked
      // vertically). Quadrant DCs encoded as 1x2 Hadamard at coeffs[0],coeffs[8].
      case JxlAcStrategyType.Dct4x8:
        _InverseDct4x8DualBlock(coeffs, outputPixels);
        return;

      // 8x4 IDCT in each of two 8x4 sub-strips of an 8x8 block (stacked
      // horizontally). Same Hadamard-DC trick as Dct4x8.
      case JxlAcStrategyType.Dct8x4:
        _InverseDct8x4DualBlock(coeffs, outputPixels);
        return;

      // Multi-level 2x2 Hadamard tower across an 8x8 block: applied at S=2,4,8.
      case JxlAcStrategyType.Dct2x2:
        _InverseDct2x2Tower(coeffs, outputPixels);
        return;

      // Hornuss / IDENTITY: 8x8 block with DC pre-prediction reversal.
      case JxlAcStrategyType.Hornuss:
        _InverseHornuss(coeffs, outputPixels);
        return;

      // AFV (Asymmetric Fovea) variants: 4x4 special basis + 4x4 IDCT + 4x8 IDCT.
      case JxlAcStrategyType.Afv0:
        _InverseAfv(coeffs, outputPixels, afvKind: 0);
        return;
      case JxlAcStrategyType.Afv1:
        _InverseAfv(coeffs, outputPixels, afvKind: 1);
        return;
      case JxlAcStrategyType.Afv2:
        _InverseAfv(coeffs, outputPixels, afvKind: 2);
        return;
      case JxlAcStrategyType.Afv3:
        _InverseAfv(coeffs, outputPixels, afvKind: 3);
        return;

      default:
        throw new NotImplementedException($"Unknown AC strategy {strategy}.");
    }
  }

  /// <summary>Returns the (width, height) in pixels that the given AC strategy
  /// covers. DCT8x8 -> (8, 8), DCT16x16 -> (16, 16), DCT8x16 -> (8, 16),
  /// AFV0..3 -> (8, 8), Hornuss -> (8, 8). Matches libjxl's
  /// AcStrategy::covered_blocks_x() * 8, covered_blocks_y() * 8.</summary>
  /// <summary>The pixels a transform covers, wide by high.</summary>
  /// <remarks>
  /// Taken from how many 8x8 blocks the transform covers in each direction,
  /// which is the format's own table. It used to be a list of its own, and that
  /// list read every rectangular shape's name as width-by-height where the
  /// format states it as rows-by-columns — so a sixteen-by-eight was handed to
  /// the inverse transform as its own transpose, and the pixels came back
  /// turned on their side. The shapes that are square could not show it.
  ///
  /// <para>The four small shapes that fit inside one block — the two-by-two and
  /// four-by-four sub-divisions, the four-by-eight pair, the Hornuss and the
  /// four fovea variants — are all carried in an 8x8 block whatever their
  /// name.</para>
  /// </remarks>
  public static (int W, int H) BlockSize(JxlAcStrategyType strategy) {
    if (!JxlAcStrategyGeometry.IsValid((int)strategy))
      throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "Unknown AC strategy.");

    return (JxlAcStrategyGeometry.BlocksWide(strategy) * 8, JxlAcStrategyGeometry.BlocksHigh(strategy) * 8);
  }

  // -------------------------------------------------------------------------
  // 2-D separable IDCT
  // -------------------------------------------------------------------------

  /// <summary>Separable 2-D IDCT for square or rectangular blocks of the same
  /// 1-D scaling convention as libjxl. Operates rows-first, then columns:
  /// <list type="number">
  ///   <item>For each row y, run 1-D IDCT of length <c>width</c> on coeffs[y, *].</item>
  ///   <item>For each column x, run 1-D IDCT of length <c>height</c> on the
  ///         row-IDCT results.</item>
  /// </list>
  /// Both passes use the libjxl scaling: forward applies 1/N, so a DC-only
  /// 2-D coefficient (c, 0, ...) reconstructs to a flat block of value c.</summary>
  private static void _InverseDct2D(float[] coeffs, float[] outputPixels, int width, int height) {
    // Intermediate buffer: row-IDCT results.
    var tmp = new float[width * height];
    var rowBuf = new float[width];

    // Pass 1: 1-D IDCT along each row (length = width).
    for (var y = 0; y < height; y++) {
      var rowOffset = y * width;
      _LibjxlIdct1D(coeffs, rowOffset, rowBuf, 0, width);
      Array.Copy(rowBuf, 0, tmp, rowOffset, width);
    }

    // Pass 2: 1-D IDCT along each column (length = height). We read column x
    // from tmp with stride=width and write column x into outputPixels with
    // stride=width.
    var colBuf = new float[height];
    var colOut = new float[height];
    for (var x = 0; x < width; x++) {
      // Gather column.
      for (var y = 0; y < height; y++) colBuf[y] = tmp[y * width + x];
      _LibjxlIdct1D(colBuf, 0, colOut, 0, height);
      // Scatter column.
      for (var y = 0; y < height; y++) outputPixels[y * width + x] = colOut[y];
    }
  }

  /// <summary>libjxl-compatible 1-D IDCT (Lee/Loeffler fast DCT).
  /// Direct C# port of <c>IDCT1DImpl&lt;N, SZ&gt;</c> + <c>CoeffBundle</c> from
  /// <c>lib/jxl/dct-inl.h</c>. Uses <c>WcMultipliers&lt;N&gt;::kMultipliers[i] =
  /// 1/(2*cos((i+0.5)*pi/N))</c> from <c>dct_scales.h</c>.
  ///
  /// <para>Coefficients are stored "scaled" by the encoder (forward DCT applies
  /// 1/N): DC = block mean, AC scaled accordingly. The IDCT here recovers
  /// spatial samples without any further normalization. For DC-only input
  /// <c>[c, 0, ..., 0]</c> the output is a flat block of value <c>c</c>.</para>
  ///
  /// <para>Algorithm (top-down):
  /// <list type="number">
  ///   <item><c>ForwardEvenOdd</c>: split <c>from[]</c> into evens then odds in <c>tmp[]</c>.</item>
  ///   <item>Recursive IDCT on the even half.</item>
  ///   <item><c>BTranspose</c> on the odd half: cumulative-add backwards then ×sqrt(2) at index 0.</item>
  ///   <item>Recursive IDCT on the odd half.</item>
  ///   <item><c>MultiplyAndAdd</c>: combine evens with odds via W[i] multipliers
  ///         producing a "butterfly" output where <c>out[i] = ev[i] + W[i]·od[i]</c>
  ///         and <c>out[N-1-i] = ev[i] - W[i]·od[i]</c>.</item>
  /// </list></para>
  /// </summary>
  internal static void _LibjxlIdct1D(float[] from, int fromOffset, float[] to, int toOffset, int n) {
    if (n == 1) {
      to[toOffset] = from[fromOffset];
      return;
    }
    // Each recursion level uses N slots for its evens/odds + grows scratch
    // by N (libjxl `tmp + N*SZ`). Total scratch up to base case (N=2):
    // N + N + N/2 + N/4 + ... < 3N. Allocate 4N to be safe.
    var tmp = new float[n * 4];
    _LibjxlIdct1DStep(from, fromOffset, 1, to, toOffset, 1, n, tmp, 0);
  }

  private static void _LibjxlIdct1DStep(
    float[] from, int fromOff, int fromStride,
    float[] to, int toOff, int toStride,
    int n, float[] tmpBuf, int tmpOff
  ) {
    if (n == 1) {
      to[toOff] = from[fromOff];
      return;
    }
    if (n == 2) {
      var a = from[fromOff];
      var b = from[fromOff + fromStride];
      to[toOff] = a + b;
      to[toOff + toStride] = a - b;
      return;
    }

    var half = n / 2;

    // Step 1: ForwardEvenOdd — split into evens (first half of tmp) and odds (second half).
    for (var i = 0; i < half; i++)
      tmpBuf[tmpOff + i] = from[fromOff + 2 * i * fromStride];
    for (var i = half; i < n; i++)
      tmpBuf[tmpOff + i] = from[fromOff + (2 * (i - half) + 1) * fromStride];

    // Step 2: Recursive IDCT on the even half (tmpBuf[tmpOff..tmpOff+half]).
    // Output goes back to the same range.
    _LibjxlIdct1DStep(tmpBuf, tmpOff, 1, tmpBuf, tmpOff, 1, half, tmpBuf, tmpOff + n);

    // Step 3: BTranspose on the odd half (tmpBuf[tmpOff+half..tmpOff+n]).
    //   for i = N-1 down to 1: coeff[i] += coeff[i-1]
    //   coeff[0] *= sqrt(2)
    var oddOff = tmpOff + half;
    for (var i = half - 1; i > 0; i--)
      tmpBuf[oddOff + i] += tmpBuf[oddOff + i - 1];
    tmpBuf[oddOff] *= _kSqrt2;

    // Step 4: Recursive IDCT on the odd half.
    _LibjxlIdct1DStep(tmpBuf, oddOff, 1, tmpBuf, oddOff, 1, half, tmpBuf, tmpOff + n);

    // Step 5: MultiplyAndAdd — combine evens (ev[i] = tmpBuf[tmpOff + i])
    // with odds (od[i] = tmpBuf[oddOff + i]) using W[i] = 1/(2*cos((i+0.5)*pi/N)).
    //   out[i]       = ev[i] + W[i] * od[i]
    //   out[N-1-i]   = ev[i] - W[i] * od[i]
    var multipliers = _GetWcMultipliers(n);
    for (var i = 0; i < half; i++) {
      var ev = tmpBuf[tmpOff + i];
      var od = tmpBuf[oddOff + i] * multipliers[i];
      to[toOff + i * toStride] = ev + od;
      to[toOff + (n - 1 - i) * toStride] = ev - od;
    }
  }

  private const float _kSqrt2 = 1.41421356237309504880f;

  /// <summary>libjxl <c>WcMultipliers&lt;N&gt;::kMultipliers[i] =
  /// 1/(2*cos((i+0.5)*pi/N))</c> for i in [0, N/2). Computed on demand and
  /// cached per N.</summary>
  private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, float[]> _wcMultipliersCache = new();
  private static float[] _GetWcMultipliers(int n) =>
    _wcMultipliersCache.GetOrAdd(n, static n => {
      var half = n / 2;
      var result = new float[half];
      for (var i = 0; i < half; i++)
        result[i] = (float)(1.0 / (2.0 * Math.Cos((i + 0.5) * Math.PI / n)));
      return result;
    });

  /// <summary>libjxl-compatible 1-D forward DCT (paired with <see cref="_LibjxlIdct1D"/>).
  /// Direct C# port of <c>DCT1DImpl&lt;N, SZ&gt;</c> + <c>StoreToBlockAndScale</c>
  /// (the 1/N scaling) from <c>lib/jxl/dct-inl.h</c>.
  ///
  /// <para>Algorithm:
  /// <list type="number">
  ///   <item><c>AddReverse</c> on first/second halves: <c>tmp[i] = in[i] + in[N-1-i]</c>.</item>
  ///   <item>Recursive forward DCT on the first half (in tmp).</item>
  ///   <item><c>SubReverse</c>: <c>tmp[N/2+i] = in[i] - in[N-1-i]</c>.</item>
  ///   <item><c>Multiply</c> the odd half by <c>WcMultipliers[i]</c>.</item>
  ///   <item>Recursive forward DCT on the odd half.</item>
  ///   <item><c>B</c> on the odd half: <c>coeff[0] = sqrt(2)·c[0] + c[1]</c>; for
  ///         <c>i ∈ [1, N-1)</c>: <c>coeff[i] += coeff[i+1]</c>.</item>
  ///   <item><c>InverseEvenOdd</c>: re-interleave (out[2i] = tmp[i], out[2i+1] = tmp[N/2+i]).</item>
  ///   <item>Final scale by 1/N (from <c>StoreToBlockAndScale</c>).</item>
  /// </list></para>
  /// </summary>
  internal static void _LibjxlDct1D(float[] from, int fromOff, float[] to, int toOff, int n) {
    if (n == 1) { to[toOff] = from[fromOff]; return; }
    var tmp = new float[n * 4];
    Array.Copy(from, fromOff, tmp, 0, n);
    _LibjxlDct1DStep(tmp, 0, 1, n, tmp, n);
    var inv = 1.0f / n;
    for (var i = 0; i < n; i++) to[toOff + i] = tmp[i] * inv;
  }

  private static void _LibjxlDct1DStep(
    float[] mem, int memOff, int memStride,
    int n, float[] tmpBuf, int tmpOff
  ) {
    if (n == 1) return;
    if (n == 2) {
      var a = mem[memOff];
      var b = mem[memOff + memStride];
      mem[memOff] = a + b;
      mem[memOff + memStride] = a - b;
      return;
    }
    var half = n / 2;

    // Step 1: AddReverse — tmp[i] = mem[i] + mem[N-1-i] for i in 0..half.
    for (var i = 0; i < half; i++)
      tmpBuf[tmpOff + i] = mem[memOff + i * memStride] + mem[memOff + (n - 1 - i) * memStride];

    // Step 2: forward DCT on the first half (in tmp), in-place.
    _LibjxlDct1DStep(tmpBuf, tmpOff, 1, half, tmpBuf, tmpOff + n);

    // Step 3: SubReverse — tmp[half+i] = mem[i] - mem[N-1-i].
    for (var i = 0; i < half; i++)
      tmpBuf[tmpOff + half + i] = mem[memOff + i * memStride] - mem[memOff + (n - 1 - i) * memStride];

    // Step 4: Multiply odd half by W[i].
    var multipliers = _GetWcMultipliers(n);
    for (var i = 0; i < half; i++)
      tmpBuf[tmpOff + half + i] *= multipliers[i];

    // Step 5: forward DCT on the odd half.
    _LibjxlDct1DStep(tmpBuf, tmpOff + half, 1, half, tmpBuf, tmpOff + n);

    // Step 6: B on odd half: tmp[half] = sqrt(2)*tmp[half] + tmp[half+1];
    //   for i in 1..half-1: tmp[half+i] += tmp[half+i+1].
    var oddOff = tmpOff + half;
    tmpBuf[oddOff] = _kSqrt2 * tmpBuf[oddOff] + tmpBuf[oddOff + 1];
    for (var i = 1; i < half - 1; i++)
      tmpBuf[oddOff + i] += tmpBuf[oddOff + i + 1];

    // Step 7: InverseEvenOdd — interleave evens (tmp[0..half]) and odds (tmp[half..n])
    // back into mem. out[2i] = even[i], out[2i+1] = odd[i].
    for (var i = 0; i < half; i++) {
      mem[memOff + (2 * i) * memStride] = tmpBuf[tmpOff + i];
      mem[memOff + (2 * i + 1) * memStride] = tmpBuf[tmpOff + half + i];
    }
  }

  /// <summary>Generic 2-D separable IDCT writing into a sub-rectangle of a
  /// destination buffer with arbitrary stride. Used by composite strategies
  /// (Dct4x4, Dct4x8, Dct8x4, Afv*) that tile multiple sub-block IDCTs into an
  /// 8x8 output. <paramref name="srcCoeffs"/> is a contiguous W*H buffer in
  /// row-major order; the result is written into
  /// <paramref name="dst"/>[dstY..dstY+H-1, dstX..dstX+W-1] using
  /// <paramref name="dstStride"/> as the row pitch.</summary>
  private static void _InverseDct2DToRect(
    float[] srcCoeffs, int width, int height,
    float[] dst, int dstX, int dstY, int dstStride
  ) {
    var tmp = new float[width * height];
    _InverseDct2D(srcCoeffs, tmp, width, height);
    for (var y = 0; y < height; y++)
      for (var x = 0; x < width; x++)
        dst[(dstY + y) * dstStride + (dstX + x)] = tmp[y * width + x];
  }

  // -------------------------------------------------------------------------
  // Composite strategies (libjxl `dec_transforms-inl.h` TransformToPixels)
  // -------------------------------------------------------------------------

  /// <summary>Dct4x4 — four independent 4x4 IDCTs filling four 4x4 quadrants of
  /// an 8x8 block. The four quadrant DC values are NOT stored directly; instead
  /// they are encoded as a 2x2 Hadamard transform at coeffs[0,0], [0,1], [1,0],
  /// [1,1]. Per-quadrant AC coefficients live at the 2-strided positions.
  /// Mirrors libjxl <c>TransformToPixels</c> case <c>DCT4X4</c>.</summary>
  private static void _InverseDct4x4QuadBlock(float[] coeffs, float[] outputPixels) {
    // Stride for the source 8x8 layout.
    const int stride = 8;

    // Recover the four quadrant DCs from the 2x2 Hadamard:
    //   block00 = coeffs[0]; block01 = coeffs[1];
    //   block10 = coeffs[8]; block11 = coeffs[9].
    var b00 = coeffs[0];
    var b01 = coeffs[1];
    var b10 = coeffs[stride];
    var b11 = coeffs[stride + 1];
    var dcs = new float[4];
    // libjxl indexing: dcs[qy*2 + qx]. Hadamard recovery (signs match libjxl).
    dcs[0] = b00 + b01 + b10 + b11; // quadrant (qy=0, qx=0) top-left
    dcs[1] = b00 + b01 - b10 - b11; // quadrant (qy=0, qx=1) top-right
    dcs[2] = b00 - b01 + b10 - b11; // quadrant (qy=1, qx=0) bottom-left
    dcs[3] = b00 - b01 - b10 + b11; // quadrant (qy=1, qx=1) bottom-right

    var subCoeffs = new float[16];
    for (var qy = 0; qy < 2; qy++) {
      for (var qx = 0; qx < 2; qx++) {
        // Build the 4x4 coefficient sub-block for this quadrant.
        Array.Clear(subCoeffs, 0, 16);
        subCoeffs[0] = dcs[qy * 2 + qx];
        for (var iy = 0; iy < 4; iy++) {
          for (var ix = 0; ix < 4; ix++) {
            if (ix == 0 && iy == 0) continue;
            // libjxl: block[iy*4 + ix] = coefficients[(qy + iy*2)*8 + qx + ix*2]
            subCoeffs[iy * 4 + ix] = coeffs[(qy + iy * 2) * stride + qx + ix * 2];
          }
        }
        _InverseDct2DToRect(subCoeffs, 4, 4, outputPixels, qx * 4, qy * 4, stride);
      }
    }
  }

  /// <summary>Dct4x8 — two independent 4x8 IDCTs stacked vertically (top half
  /// 0..3 rows, bottom half 4..7 rows) inside an 8x8 block. The two strip DCs
  /// are encoded as a 1x2 Hadamard at coeffs[0] / coeffs[8]; AC coefficients
  /// for the two strips are interleaved at row stride 2.
  /// Mirrors libjxl <c>TransformToPixels</c> case <c>DCT4X8</c>.</summary>
  private static void _InverseDct4x8DualBlock(float[] coeffs, float[] outputPixels) {
    const int stride = 8;
    var dcs = new float[2];
    var b0 = coeffs[0];
    var b1 = coeffs[stride];
    dcs[0] = b0 + b1; // top strip
    dcs[1] = b0 - b1; // bottom strip

    var subCoeffs = new float[4 * 8];
    for (var sy = 0; sy < 2; sy++) {
      Array.Clear(subCoeffs, 0, 32);
      subCoeffs[0] = dcs[sy];
      for (var iy = 0; iy < 4; iy++) {
        for (var ix = 0; ix < 8; ix++) {
          if (ix == 0 && iy == 0) continue;
          // libjxl: block[iy*8 + ix] = coefficients[(sy + iy*2)*8 + ix]
          subCoeffs[iy * 8 + ix] = coeffs[(sy + iy * 2) * stride + ix];
        }
      }
      // Each sub-block is 8 wide x 4 tall.
      _InverseDct2DToRect(subCoeffs, 8, 4, outputPixels, 0, sy * 4, stride);
    }
  }

  /// <summary>Dct8x4 — two independent 8x4 IDCTs stacked horizontally (left
  /// half 0..3 cols, right half 4..7 cols) inside an 8x8 block. Strip DCs at
  /// coeffs[0] / coeffs[8], same Hadamard recovery as Dct4x8 but rotated.
  /// libjxl writes the IDCT into <c>pixels + sx * 4</c>.
  /// libjxl source: <c>TransformToPixels</c> case <c>DCT8X4</c>, computes a
  /// <c>ComputeScaledIDCT&lt;8, 4&gt;()</c>, with COLS=4, ROWS=8 — i.e. width=4
  /// and height=8 in our axis convention.</summary>
  private static void _InverseDct8x4DualBlock(float[] coeffs, float[] outputPixels) {
    const int stride = 8;
    var dcs = new float[2];
    var b0 = coeffs[0];
    var b1 = coeffs[stride];
    dcs[0] = b0 + b1;
    dcs[1] = b0 - b1;

    // libjxl reads `coefficients[(sx + iy * 2) * 8 + ix]` with iy ranging 0..3
    // and ix 0..7 — i.e. the buffer is laid out logically as (4, 8) just like
    // DCT4X8. The IDCT is then ComputeScaledIDCT<8, 4>() = ROWS=8, COLS=4 (a
    // 4-wide, 8-tall result block), placed at column-offset `sx * 4`.
    var subCoeffs = new float[4 * 8];
    for (var sx = 0; sx < 2; sx++) {
      Array.Clear(subCoeffs, 0, 32);
      subCoeffs[0] = dcs[sx];
      for (var iy = 0; iy < 4; iy++) {
        for (var ix = 0; ix < 8; ix++) {
          if (ix == 0 && iy == 0) continue;
          subCoeffs[iy * 8 + ix] = coeffs[(sx + iy * 2) * stride + ix];
        }
      }
      // Reinterpret subCoeffs as a (4,8) tile but feed to a (W=4, H=8) IDCT.
      // libjxl's ComputeScaledIDCT<8, 4> takes ROWS=8, COLS=4 input — meaning
      // its input layout is 8 rows × 4 cols. We have to transpose subCoeffs
      // from logical (4 rows × 8 cols) to (8 rows × 4 cols) before IDCT.
      var trans = new float[4 * 8];
      for (var ty = 0; ty < 8; ty++)
        for (var tx = 0; tx < 4; tx++)
          trans[ty * 4 + tx] = subCoeffs[tx * 8 + ty];
      _InverseDct2DToRect(trans, 4, 8, outputPixels, sx * 4, 0, stride);
    }
  }

  /// <summary>Dct2x2 — three-level 2x2 Hadamard cascade (S=2, 4, 8) over the
  /// 8x8 coefficient block. Each level expands a S/2×S/2 already-reconstructed
  /// region into a S×S region by combining it with three S/2×S/2 patches of
  /// "residual" coefficients via a 2x2 Hadamard. After S=8 the entire 8x8
  /// region holds spatial pixel values.
  /// Mirrors libjxl <c>IDCT2TopBlock&lt;S&gt;</c> applied for S in {2, 4, 8}.</summary>
  private static void _InverseDct2x2Tower(float[] coeffs, float[] outputPixels) {
    const int dim = 8;

    // Operate in-place on a working buffer (libjxl uses a memcpy-then-mutate).
    var work = new float[dim * dim];
    Array.Copy(coeffs, work, dim * dim);

    _Idct2TopBlock(work, dim, 2);
    _Idct2TopBlock(work, dim, 4);
    _Idct2TopBlock(work, dim, 8);

    Array.Copy(work, outputPixels, dim * dim);
  }

  /// <summary>One level of the libjxl <c>IDCT2TopBlock&lt;S&gt;</c> Hadamard.
  /// Reads four S/2×S/2 sub-tiles from the top-left S×S area of
  /// <paramref name="block"/> (stride = <paramref name="stride"/>) and writes
  /// the 2×2 Hadamard-combined S×S top-left back. Other regions are
  /// untouched.</summary>
  private static void _Idct2TopBlock(float[] block, int stride, int s) {
    var num = s / 2;
    var temp = new float[stride * stride];
    for (var y = 0; y < num; y++) {
      for (var x = 0; x < num; x++) {
        var c00 = block[y * stride + x];
        var c01 = block[y * stride + num + x];
        var c10 = block[(y + num) * stride + x];
        var c11 = block[(y + num) * stride + num + x];
        var r00 = c00 + c01 + c10 + c11;
        var r01 = c00 + c01 - c10 - c11;
        var r10 = c00 - c01 + c10 - c11;
        var r11 = c00 - c01 - c10 + c11;
        temp[y * 2 * stride + x * 2] = r00;
        temp[y * 2 * stride + x * 2 + 1] = r01;
        temp[(y * 2 + 1) * stride + x * 2] = r10;
        temp[(y * 2 + 1) * stride + x * 2 + 1] = r11;
      }
    }
    // Copy back the S×S top-left.
    for (var y = 0; y < s; y++)
      for (var x = 0; x < s; x++)
        block[y * stride + x] = temp[y * stride + x];
  }

  /// <summary>Hornuss / IDENTITY — 8x8 block with DC pre-prediction. The four
  /// quadrant DCs are encoded as a 2x2 Hadamard at coeffs[0,0], [0,1], [1,0],
  /// [1,1]. Each 4x4 quadrant has 16 coefficient values (one per pixel) that
  /// reconstruct as `coeff + center_pixel`, where the center pixel
  /// (position (4y+1, 4x+1) of the 8x8 output) equals
  /// <c>quadrant_dc - sum_of_residuals * 1/16</c>.
  /// Mirrors libjxl <c>TransformToPixels</c> case <c>IDENTITY</c>.</summary>
  private static void _InverseHornuss(float[] coeffs, float[] outputPixels) {
    const int stride = 8;
    var b00 = coeffs[0];
    var b01 = coeffs[1];
    var b10 = coeffs[stride];
    var b11 = coeffs[stride + 1];
    Span<float> dcs = stackalloc float[4];
    dcs[0] = b00 + b01 + b10 + b11;
    dcs[1] = b00 + b01 - b10 - b11;
    dcs[2] = b00 - b01 + b10 - b11;
    dcs[3] = b00 - b01 - b10 + b11;

    for (var y = 0; y < 2; y++) {
      for (var x = 0; x < 2; x++) {
        var blockDc = dcs[y * 2 + x];
        // Sum residuals in this quadrant (skipping the (0,0) DC slot).
        var residualSum = 0.0f;
        for (var iy = 0; iy < 4; iy++) {
          for (var ix = 0; ix < 4; ix++) {
            if (ix == 0 && iy == 0) continue;
            residualSum += coeffs[(y + iy * 2) * stride + x + ix * 2];
          }
        }
        // Center pixel = quadrant DC minus residual mean.
        var centerY = 4 * y + 1;
        var centerX = 4 * x + 1;
        var centerVal = blockDc - residualSum * (1.0f / 16.0f);
        outputPixels[centerY * stride + centerX] = centerVal;
        // Other 15 pixels = coefficient + center.
        for (var iy = 0; iy < 4; iy++) {
          for (var ix = 0; ix < 4; ix++) {
            if (ix == 1 && iy == 1) continue;
            outputPixels[(y * 4 + iy) * stride + x * 4 + ix] =
              coeffs[(y + iy * 2) * stride + x + ix * 2] + centerVal;
          }
        }
        // Pixel at (y*4, x*4) is overwritten with coeff[(y+2)*8 + x+2] + center
        // (libjxl special case to handle the slot that aliased the DC residual).
        outputPixels[y * 4 * stride + x * 4] =
          coeffs[(y + 2) * stride + x + 2] + centerVal;
      }
    }
  }

  // -------------------------------------------------------------------------
  // AFV (Asymmetric / Fovea) variants — libjxl `AFVTransformToPixels`
  // -------------------------------------------------------------------------

  /// <summary>AFV0..3 inverse transform. The 8x8 block is partitioned into
  /// three regions that get three different transforms:
  /// <list type="bullet">
  ///   <item>4x4 quadrant at <c>(afv_y, afv_x)</c> — special "AFVIDCT4x4" basis.</item>
  ///   <item>4x4 quadrant at <c>(afv_y, 1 - afv_x)</c> — standard 4x4 IDCT.</item>
  ///   <item>4x8 strip at <c>(1 - afv_y, *)</c> — standard 4x8 IDCT.</item>
  /// </list>
  /// The three "DCs" are derived from a 1x3 Hadamard of coeffs[0,0],[0,1],[1,0].
  /// Mirrors libjxl <c>AFVTransformToPixels&lt;afv_kind&gt;</c>.</summary>
  private static void _InverseAfv(float[] coeffs, float[] outputPixels, int afvKind) {
    const int stride = 8;
    var afvX = afvKind & 1;
    var afvY = afvKind >> 1;

    // Recover the three "block" DCs from coeffs[0], coeffs[1], coeffs[8].
    var b00 = coeffs[0];
    var b01 = coeffs[1];
    var b10 = coeffs[stride];
    var dcAfv = (b00 + b10 + b01) * 4.0f;     // AFV4x4 DC
    var dcOther = (b00 + b10 - b01);          // 4x4 (other quadrant) DC
    var dcRow = b00 - b10;                    // 4x8 strip DC

    // ---- AFV 4x4 sub-block — special basis. ----
    var afvCoeff = new float[16];
    afvCoeff[0] = dcAfv;
    for (var iy = 0; iy < 4; iy++) {
      for (var ix = 0; ix < 4; ix++) {
        if (ix == 0 && iy == 0) continue;
        afvCoeff[iy * 4 + ix] = coeffs[iy * 2 * stride + ix * 2];
      }
    }
    var afvBlock = new float[16];
    _AfvIdct4x4(afvCoeff, afvBlock);
    for (var iy = 0; iy < 4; iy++) {
      for (var ix = 0; ix < 4; ix++) {
        var srcY = afvY == 1 ? 3 - iy : iy;
        var srcX = afvX == 1 ? 3 - ix : ix;
        outputPixels[(iy + afvY * 4) * stride + afvX * 4 + ix]
          = afvBlock[srcY * 4 + srcX];
      }
    }

    // ---- 4x4 IDCT in the (other-x, same-y) quadrant. ----
    var dct4x4Coeff = new float[16];
    dct4x4Coeff[0] = dcOther;
    for (var iy = 0; iy < 4; iy++) {
      for (var ix = 0; ix < 4; ix++) {
        if (ix == 0 && iy == 0) continue;
        dct4x4Coeff[iy * 4 + ix] = coeffs[iy * 2 * stride + ix * 2 + 1];
      }
    }
    // Output at row offset afvY*4, column offset (afvX==1 ? 0 : 4).
    var col4x4 = afvX == 1 ? 0 : 4;
    _InverseDct2DToRect(dct4x4Coeff, 4, 4, outputPixels, col4x4, afvY * 4, stride);

    // ---- 4x8 IDCT in the (full-row, opposite-y) strip. ----
    var dct4x8Coeff = new float[4 * 8];
    dct4x8Coeff[0] = dcRow;
    for (var iy = 0; iy < 4; iy++) {
      for (var ix = 0; ix < 8; ix++) {
        if (ix == 0 && iy == 0) continue;
        dct4x8Coeff[iy * 8 + ix] = coeffs[(1 + iy * 2) * stride + ix];
      }
    }
    var rowOff = afvY == 1 ? 0 : 4;
    _InverseDct2DToRect(dct4x8Coeff, 8, 4, outputPixels, 0, rowOff, stride);
  }

  /// <summary>The 4x4 AFV special basis (libjxl <c>AFVIDCT4x4</c>). Direct
  /// dot product of the 16 coefficient values against a 16x16 fixed
  /// orthonormal-ish basis.</summary>
  private static void _AfvIdct4x4(float[] coeffs, float[] pixels) {
    for (var i = 0; i < 16; i++) {
      var sum = 0.0f;
      for (var j = 0; j < 16; j++) sum += coeffs[j] * _Afv4x4Basis[j][i];
      pixels[i] = sum;
    }
  }

  // 4x4 AFV basis from libjxl `dec_transforms-inl.h` `k4x4AFVBasis`.
  // 16 basis functions, each 16 spatial samples (4x4 row-major).
  // BSD-3-Clause (c) JPEG XL Project Authors.
  private static readonly float[][] _Afv4x4Basis = new float[][] {
    new float[] {
      0.25f, 0.25f, 0.25f, 0.25f,
      0.25f, 0.25f, 0.25f, 0.25f,
      0.25f, 0.25f, 0.25f, 0.25f,
      0.25f, 0.25f, 0.25f, 0.25f,
    },
    new float[] {
      0.876902929799142f, 0.2206518106944235f, -0.10140050393753763f, -0.1014005039375375f,
      0.2206518106944236f, -0.10140050393753777f, -0.10140050393753772f, -0.10140050393753763f,
      -0.10140050393753758f, -0.10140050393753769f, -0.1014005039375375f, -0.10140050393753768f,
      -0.10140050393753768f, -0.10140050393753759f, -0.10140050393753763f, -0.10140050393753741f,
    },
    new float[] {
      0.0f, 0.0f, 0.40670075830260755f, 0.44444816619734445f,
      0.0f, 0.0f, 0.19574399372042936f, 0.2929100136981264f,
      -0.40670075830260716f, -0.19574399372042872f, 0.0f, 0.11379074460448091f,
      -0.44444816619734384f, -0.29291001369812636f, -0.1137907446044814f, 0.0f,
    },
    new float[] {
      0.0f, 0.0f, -0.21255748058288748f, 0.3085497062849767f,
      0.0f, 0.4706702258572536f, -0.1621205195722993f, 0.0f,
      -0.21255748058287047f, -0.16212051957228327f, -0.47067022585725277f, -0.1464291867126764f,
      0.3085497062849487f, 0.0f, -0.14642918671266536f, 0.4251149611657548f,
    },
    new float[] {
      0.0f, -0.7071067811865474f, 0.0f, 0.0f,
      0.7071067811865476f, 0.0f, 0.0f, 0.0f,
      0.0f, 0.0f, 0.0f, 0.0f,
      0.0f, 0.0f, 0.0f, 0.0f,
    },
    new float[] {
      -0.4105377591765233f, 0.6235485373547691f, -0.06435071657946274f, -0.06435071657946266f,
      0.6235485373547694f, -0.06435071657946284f, -0.0643507165794628f, -0.06435071657946274f,
      -0.06435071657946272f, -0.06435071657946279f, -0.06435071657946266f, -0.06435071657946277f,
      -0.06435071657946277f, -0.06435071657946273f, -0.06435071657946274f, -0.0643507165794626f,
    },
    new float[] {
      0.0f, 0.0f, -0.4517556589999482f, 0.15854503551840063f,
      0.0f, -0.04038515160822202f, 0.0074182263792423875f, 0.39351034269210167f,
      -0.45175565899994635f, 0.007418226379244351f, 0.1107416575309343f, 0.08298163094882051f,
      0.15854503551839705f, 0.3935103426921022f, 0.0829816309488214f, -0.45175565899994796f,
    },
    new float[] {
      0.0f, 0.0f, -0.304684750724869f, 0.5112616136591823f,
      0.0f, 0.0f, -0.290480129728998f, -0.06578701549142804f,
      0.304684750724884f, 0.2904801297290076f, 0.0f, -0.23889773523344604f,
      -0.5112616136592012f, 0.06578701549142545f, 0.23889773523345467f, 0.0f,
    },
    new float[] {
      0.0f, 0.0f, 0.3017929516615495f, 0.25792362796341184f,
      0.0f, 0.16272340142866204f, 0.09520022653475037f, 0.0f,
      0.3017929516615503f, 0.09520022653475055f, -0.16272340142866173f, -0.35312385449816297f,
      0.25792362796341295f, 0.0f, -0.3531238544981624f, -0.6035859033230976f,
    },
    new float[] {
      0.0f, 0.0f, 0.40824829046386274f, 0.0f,
      0.0f, 0.0f, 0.0f, -0.4082482904638628f,
      -0.4082482904638635f, 0.0f, 0.0f, -0.40824829046386296f,
      0.0f, 0.4082482904638634f, 0.408248290463863f, 0.0f,
    },
    new float[] {
      0.0f, 0.0f, 0.1747866975480809f, 0.0812611176717539f,
      0.0f, 0.0f, -0.3675398009862027f, -0.307882213957909f,
      -0.17478669754808135f, 0.3675398009862011f, 0.0f, 0.4826689115059883f,
      -0.08126111767175039f, 0.30788221395790305f, -0.48266891150598584f, 0.0f,
    },
    new float[] {
      0.0f, 0.0f, -0.21105601049335784f, 0.18567180916109802f,
      0.0f, 0.0f, 0.49215859013738733f, -0.38525013709251915f,
      0.21105601049335806f, -0.49215859013738905f, 0.0f, 0.17419412659916217f,
      -0.18567180916109904f, 0.3852501370925211f, -0.1741941265991621f, 0.0f,
    },
    new float[] {
      0.0f, 0.0f, -0.14266084808807264f, -0.3416446842253372f,
      0.0f, 0.7367497537172237f, 0.24627107722075148f, -0.08574019035519306f,
      -0.14266084808807344f, 0.24627107722075137f, 0.14883399227113567f, -0.04768680350229251f,
      -0.3416446842253373f, -0.08574019035519267f, -0.047686803502292804f, -0.14266084808807242f,
    },
    new float[] {
      0.0f, 0.0f, -0.13813540350758585f, 0.3302282550303788f,
      0.0f, 0.08755115000587084f, -0.07946706605909573f, -0.4613374887461511f,
      -0.13813540350758294f, -0.07946706605910261f, 0.49724647109535086f, 0.12538059448563663f,
      0.3302282550303805f, -0.4613374887461554f, 0.12538059448564315f, -0.13813540350758452f,
    },
    new float[] {
      0.0f, 0.0f, -0.17437602599651067f, 0.0702790691196284f,
      0.0f, -0.2921026642334881f, 0.3623817333531167f, 0.0f,
      -0.1743760259965108f, 0.36238173335311646f, 0.29210266423348785f, -0.4326608024727445f,
      0.07027906911962818f, 0.0f, -0.4326608024727457f, 0.34875205199302267f,
    },
    new float[] {
      0.0f, 0.0f, 0.11354987314994337f, -0.07417504595810355f,
      0.0f, 0.19402893032594343f, -0.435190496523228f, 0.21918684838857466f,
      0.11354987314994257f, -0.4351904965232251f, 0.5550443808910661f, -0.25468277124066463f,
      -0.07417504595810233f, 0.2191868483885728f, -0.25468277124066413f, 0.1135498731499429f,
    },
  };

  // -------------------------------------------------------------------------
  // 1-D IDCT
  // -------------------------------------------------------------------------

  /// <summary>1-D IDCT of length <paramref name="n"/> with libjxl scaling:
  /// <c>x[k] = X[0] + 2 * sum_{j=1..n-1} X[j] * cos((k + 0.5) * j * pi / n)</c>.
  /// Direct O(N^2) evaluation — n is small (max 8 for DCT8x8 inner loop, 32
  /// for DCT32x32, 256 for DCT256x256). For first-wave correctness we prefer
  /// this over a fast recursive variant; fast paths can be added later
  /// without API change.
  /// </summary>
  /// <param name="coeffs">Source coefficient buffer.</param>
  /// <param name="coeffOffset">Index of coeff[0] in source.</param>
  /// <param name="coeffStride">Stride between consecutive coeffs in source.</param>
  /// <param name="output">Destination pixel buffer.</param>
  /// <param name="outputOffset">Index of pixel[0] in destination.</param>
  /// <param name="outputStride">Stride between consecutive pixels in destination.</param>
  /// <param name="n">Transform length (must be >= 1).</param>
  private static void _Idct1D(
    float[] coeffs, int coeffOffset, int coeffStride,
    float[] output, int outputOffset, int outputStride,
    int n
  ) {
    if (n == 1) {
      // Length-1 IDCT is identity (DC only).
      output[outputOffset] = coeffs[coeffOffset];
      return;
    }
    if (n == 2) {
      // Length-2 IDCT: x[0] = X[0] + X[1]*cos(pi/4)*2 = X[0] + sqrt(2)*X[1] ...
      // Actually: x[k] = X[0] + 2*X[1]*cos((k+0.5)*pi/2)
      //   k=0: cos(pi/4) = sqrt(0.5);  x[0] = X[0] + 2*X[1]*sqrt(0.5) = X[0] + X[1]*sqrt(2)
      //   k=1: cos(3pi/4) = -sqrt(0.5); x[1] = X[0] - X[1]*sqrt(2)
      var c0 = coeffs[coeffOffset];
      var c1 = coeffs[coeffOffset + coeffStride];
      var s = (float)Math.Sqrt(2.0);
      output[outputOffset] = c0 + c1 * s;
      output[outputOffset + outputStride] = c0 - c1 * s;
      return;
    }

    // General case: direct evaluation. Pre-load coefficients to a contiguous
    // buffer for cache locality; n is small so this is cheap.
    // Stack-allocate for small n; heap-allocate for larger sizes (up to 256).
    Span<float> cs = n <= 64 ? stackalloc float[64] : new float[n];
    cs = cs[..n];
    for (var j = 0; j < n; j++) cs[j] = coeffs[coeffOffset + j * coeffStride];

    var pi = Math.PI;
    var invN = pi / n;
    for (var k = 0; k < n; k++) {
      // x[k] = c[0] + 2 * sum_{j=1..n-1} c[j] * cos((k + 0.5) * j * pi / n)
      var sum = (double)cs[0];
      var kPhase = (k + 0.5) * invN;
      for (var j = 1; j < n; j++)
        sum += 2.0 * cs[j] * Math.Cos(kPhase * j);
      output[outputOffset + k * outputStride] = (float)sum;
    }
  }

  // -------------------------------------------------------------------------
  // Forward DCT (test/verification helper — INTERNAL)
  // -------------------------------------------------------------------------

  /// <summary>Forward DCT companion to <see cref="_InverseDct2D"/>, using the
  /// same libjxl scaling convention. INTERNAL, intended for round-trip test
  /// validation only — production VarDCT encoding lives in a separate
  /// future encoder path.
  /// <para>X[k] = (1/N) * sum_{n=0..N-1} x[n] * cos((n + 0.5) * k * pi / N)</para>
  /// </summary>
  internal static void ForwardDct2D_Test(float[] pixels, float[] outputCoeffs, int width, int height) {
    if (pixels is null) throw new ArgumentNullException(nameof(pixels));
    if (outputCoeffs is null) throw new ArgumentNullException(nameof(outputCoeffs));
    var expected = width * height;
    if (pixels.Length != expected) throw new ArgumentException($"Forward DCT pixels expected length {expected}.", nameof(pixels));
    if (outputCoeffs.Length != expected) throw new ArgumentException($"Forward DCT outputCoeffs expected length {expected}.", nameof(outputCoeffs));

    // Pass 1: along rows.
    var tmp = new float[width * height];
    var rowBuf = new float[width];
    for (var y = 0; y < height; y++) {
      _LibjxlDct1D(pixels, y * width, rowBuf, 0, width);
      Array.Copy(rowBuf, 0, tmp, y * width, width);
    }
    // Pass 2: along columns.
    var colIn = new float[height];
    var colOut = new float[height];
    for (var x = 0; x < width; x++) {
      for (var y = 0; y < height; y++) colIn[y] = tmp[y * width + x];
      _LibjxlDct1D(colIn, 0, colOut, 0, height);
      for (var y = 0; y < height; y++) outputCoeffs[y * width + x] = colOut[y];
    }
  }

  /// <summary>1-D forward DCT with libjxl scaling (1/N).</summary>
  private static void _Dct1D(
    float[] pixels, int pixelOffset, int pixelStride,
    float[] output, int outputOffset, int outputStride,
    int n
  ) {
    if (n == 1) {
      output[outputOffset] = pixels[pixelOffset];
      return;
    }

    Span<float> xs = n <= 64 ? stackalloc float[64] : new float[n];
    xs = xs[..n];
    for (var i = 0; i < n; i++) xs[i] = pixels[pixelOffset + i * pixelStride];

    var pi = Math.PI;
    var invN = pi / n;
    var scale = 1.0f / n;
    for (var k = 0; k < n; k++) {
      // X[k] = (1/N) * sum_n x[n] * cos((n + 0.5) * k * pi / N)
      double sum = 0.0;
      for (var i = 0; i < n; i++)
        sum += xs[i] * Math.Cos((i + 0.5) * k * invN);
      output[outputOffset + k * outputStride] = (float)sum * scale;
    }
  }
}
