using System;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// JPEG XL Weighted Predictor (WP) for the modular sub-codec
/// (ISO/IEC 18181-1 §H.3 / §H.4; libjxl <c>weighted::State::Predict</c> in
/// <c>lib/jxl/modular/encoding/context_predict.h</c>).
///
/// <para>
/// The WP combines four sub-predictions with weights derived from each
/// sub-predictor's recent absolute error. State is kept per-channel as two
/// rolling rows of (per-pixel) errors and per-sub-predictor (per-pixel)
/// absolute errors. After every <see cref="Predict"/> the caller MUST call
/// <see cref="Update"/> with the actual decoded pixel value so that the next
/// pixel sees the updated rolling errors.
/// </para>
///
/// <para>
/// Sub-predictors (from libjxl <c>State::Predict</c>):
///   <list type="bullet">
///     <item>p0 = W + NE - N</item>
///     <item>p1 = N - ((teN + teW + teNE) * p1C) &gt;&gt; 5</item>
///     <item>p2 = W - ((teN + teW + teNW) * p2C) &gt;&gt; 5</item>
///     <item>p3 = N - (teNW*p3Ca + teN*p3Cb + teNE*p3Cc + (NN-N)*p3Cd + (NW-W)*p3Ce) &gt;&gt; 5</item>
///   </list>
/// Default header weights are <c>{0xd, 0xc, 0xc, 0xc}</c> and default
/// <c>p1C..p3Ce</c> = <c>{16, 10, 7, 7, 7, 0, 0}</c> (see <c>weighted::Header</c>).
/// We use those defaults; alternative parameter modes ("PredictorMode" 0..4)
/// are encoder-side presets that the decoder receives via the bitstream's
/// WP header — wiring that into the larger modular decoder is left to the
/// caller and tracked as a TODO for the bitstream-driven path.
/// </para>
///
/// <para>
/// WP also exposes one entropy-context "property" for the MA tree's split-on-
/// max-error feature (libjxl <c>kWPProp</c>): <see cref="GetProperties"/>
/// returns a single-element array carrying that property value.
/// </para>
/// </summary>
internal sealed class JxlWeightedPredictor {

  /// <summary>libjxl <c>weighted::kNumPredictors</c>.</summary>
  internal const int NumSubPredictors = 4;

  /// <summary>libjxl <c>weighted::kPredExtraBits</c>: 3 fractional bits added
  /// to the integer pixel domain so the weighted average has subpixel
  /// precision before being rounded back. <c>1 &lt;&lt; 3 = 8</c>.</summary>
  internal const int PredExtraBits = 3;

  /// <summary>libjxl <c>weighted::kPredictionRound</c> = (1 &lt;&lt; 3) &gt;&gt; 1 - 1 = 3.</summary>
  internal const int PredictionRound = ((1 << PredExtraBits) >> 1) - 1;

  // libjxl `weighted::Header` defaults from `Header::VisitFields`:
  private const int DefaultP1C = 16;
  private const int DefaultP2C = 10;
  private const int DefaultP3Ca = 7;
  private const int DefaultP3Cb = 7;
  private const int DefaultP3Cc = 7;
  private const int DefaultP3Cd = 0;
  private const int DefaultP3Ce = 0;
  private static readonly int[] _DefaultMaxWeights = { 0xd, 0xc, 0xc, 0xc };

  // libjxl `divlookup`: (1 << 24) / (i + 1) for i in 0..63. Used by
  // ErrorWeight / WeightedAverage to approximate division.
  private static readonly uint[] _DivLookup = {
    16777216, 8388608, 5592405, 4194304, 3355443, 2796202, 2396745, 2097152,
    1864135,  1677721, 1525201, 1398101, 1290555, 1198372, 1118481, 1048576,
    986895,   932067,  883011,  838860,  798915,  762600,  729444,  699050,
    671088,   645277,  621378,  599186,  578524,  559240,  541200,  524288,
    508400,   493447,  479349,  466033,  453438,  441505,  430185,  419430,
    409200,   399457,  390167,  381300,  372827,  364722,  356962,  349525,
    342392,   335544,  328965,  322638,  316551,  310689,  305040,  299593,
    294337,   289262,  284359,  279620,  275036,  270600,  266305,  262144,
  };

  private readonly int _width;
  private readonly int _maxError; // currently unused; reserved for future error-clamp
  private readonly int _p1C;
  private readonly int _p2C;
  private readonly int _p3Ca;
  private readonly int _p3Cb;
  private readonly int _p3Cc;
  private readonly int _p3Cd;
  private readonly int _p3Ce;
  private readonly uint[] _maxWeights;

  // Two rolling rows of state. Indexing follows libjxl: at row y, cur_row =
  // (y & 1 == 1) ? 0 : (xsize + 2); prev_row swaps. Using (xsize + 2) per
  // row (2 columns of margin) avoids out-of-bounds writes for x=xsize-1's
  // NE access and the off-by-one update at prev_row + x + 1.
  private readonly long[] _error;        // signed; size = 2*(xsize+2)
  private readonly uint[][] _predErrors; // per-sub-predictor abs errors; size = 2*(xsize+2)

  // Last prediction's per-sub-predictor outputs (in <<3 domain), retained
  // across Predict/Update so Update can compute |p_i - actual|.
  private readonly long[] _prediction;
  private long _pred; // weighted average in <<3 domain (before rounding)
  private long _maxErrorProp; // libjxl property exposed at kWPProp

  /// <summary>
  /// Construct a WP with the requested image width and a (currently advisory)
  /// max-error cap. The header parameters use libjxl's default preset (the
  /// <see cref="JxlWeightedPredictor"/> assumes the bitstream said
  /// <c>all_default = true</c>).
  /// </summary>
  /// <param name="width">Channel width in pixels. Must be positive.</param>
  /// <param name="maxError">Reserved: max allowed absolute error per the
  /// spec's safety bounds. Currently retained but not enforced — added to
  /// the constructor signature to match the caller's contract.</param>
  public JxlWeightedPredictor(int width, int maxError) {
    if (width <= 0)
      throw new ArgumentOutOfRangeException(nameof(width), "Must be positive.");
    _width = width;
    _maxError = maxError;
    _p1C = DefaultP1C;
    _p2C = DefaultP2C;
    _p3Ca = DefaultP3Ca;
    _p3Cb = DefaultP3Cb;
    _p3Cc = DefaultP3Cc;
    _p3Cd = DefaultP3Cd;
    _p3Ce = DefaultP3Ce;
    _maxWeights = new uint[NumSubPredictors];
    for (var i = 0; i < NumSubPredictors; ++i)
      _maxWeights[i] = (uint)_DefaultMaxWeights[i];

    var rowLen = (width + 2) * 2;
    _error = new long[rowLen];
    _predErrors = new uint[NumSubPredictors][];
    for (var i = 0; i < NumSubPredictors; ++i)
      _predErrors[i] = new uint[rowLen];

    _prediction = new long[NumSubPredictors];
  }

  /// <summary>
  /// Predict the value at <paramref name="x"/>, <paramref name="y"/> in
  /// <paramref name="channel"/>. Uses already-decoded neighbours W, N, NE,
  /// NW, NN (with the libjxl edge-handling: NE/NW clamp to N at the borders;
  /// NN falls back to N in the first two rows).
  /// </summary>
  /// <remarks>
  /// libjxl's hot loop holds N/W/NE/NW/NN in registers from a row-pointer
  /// scan; here we read them out of <see cref="JxlChannel"/> on demand to
  /// match the requested public API. That's slightly less cache-friendly but
  /// correct — the integration point for the modular decoder can switch to
  /// pointer-following once the full pipeline is wired.
  /// </remarks>
  public int Predict(int x, int y, JxlChannel channel) {
    ArgumentNullException.ThrowIfNull(channel);
    if ((uint)x >= (uint)channel.Width)
      throw new ArgumentOutOfRangeException(nameof(x));
    if ((uint)y >= (uint)channel.Height)
      throw new ArgumentOutOfRangeException(nameof(y));
    if (channel.Width != _width)
      throw new ArgumentException(
        $"Channel width {channel.Width} does not match WP width {_width}.",
        nameof(channel));

    var xsize = _width;
    var n = y > 0 ? channel.Get(x, y - 1) : 0;
    var w = x > 0 ? channel.Get(x - 1, y) : (y > 0 ? n : 0);
    var ne = (x < xsize - 1 && y > 0) ? channel.Get(x + 1, y - 1) : n;
    var nw = (x > 0 && y > 0) ? channel.Get(x - 1, y - 1) : w;
    var nn = y > 1 ? channel.Get(x, y - 2) : n;

    return _PredictCore(x, y, n, w, ne, nw, nn);
  }

  /// <summary>
  /// libjxl <c>State::Predict&lt;true&gt;</c> property output: a single
  /// integer property at the WP slot (libjxl <c>kWPProp</c>). The MA tree
  /// can split on it.
  /// </summary>
  public int[] GetProperties(int x, int y, JxlChannel channel) {
    ArgumentNullException.ThrowIfNull(channel);
    // Make sure the prediction state is up-to-date for this pixel: libjxl
    // computes the property as part of Predict<true>. We re-run the predict
    // (which is idempotent: it doesn't mutate _error / _predErrors — those
    // only change in Update) and then return the recorded property.
    _ = Predict(x, y, channel);
    return new[] { (int)_maxErrorProp };
  }

  /// <summary>
  /// Update the WP state with the actual decoded value at
  /// <paramref name="x"/>, <paramref name="y"/>. Must be called once per
  /// pixel, after <see cref="Predict"/>, in raster order.
  /// </summary>
  public void Update(int x, int y, int actualValue, JxlChannel channel) {
    ArgumentNullException.ThrowIfNull(channel);
    if (channel.Width != _width)
      throw new ArgumentException(
        $"Channel width {channel.Width} does not match WP width {_width}.",
        nameof(channel));

    var xsize = _width;
    var rowStride = xsize + 2;
    var curRow = (y & 1) == 1 ? 0 : rowStride;
    var prevRow = (y & 1) == 1 ? rowStride : 0;

    var valShifted = (long)actualValue << PredExtraBits;
    _error[curRow + x] = _pred - valShifted;

    for (var i = 0; i < NumSubPredictors; ++i) {
      var diff = _prediction[i] - valShifted;
      if (diff < 0)
        diff = -diff;
      var err = (uint)((diff + PredictionRound) >> PredExtraBits);
      _predErrors[i][curRow + x] = err;
      // libjxl: also accumulate this pixel's error into the NE slot of the
      // *previous* row, so subsequent pixels see it as part of their N/NE
      // error sum. Bounds: prev_row + x + 1 fits in the (xsize+2) margin.
      _predErrors[i][prevRow + x + 1] += err;
    }
  }

  /// <summary>Predict using already-known neighbour values. Useful when the
  /// caller is already reading them out of an int buffer with custom edge
  /// handling (e.g. JxlModularSpecDecoder). Sets the max-error property so
  /// <see cref="MaxErrorProperty"/> reflects this prediction. Equivalent to
  /// libjxl <c>State::Predict&lt;true&gt;</c> with explicit neighbour
  /// arguments.</summary>
  public int PredictWithNeighbors(int x, int y, int n, int w, int ne, int nw, int nn)
    => _PredictCore(x, y, n, w, ne, nw, nn);

  /// <summary>Update WP state with the actual decoded pixel value at
  /// <paramref name="x"/>, <paramref name="y"/>. Same as
  /// <see cref="Update(int, int, int, JxlChannel)"/> but doesn't require a
  /// JxlChannel — only width is needed (taken from constructor).</summary>
  public void UpdateWithValue(int x, int y, int actualValue) {
    var rowStride = _width + 2;
    var curRow = (y & 1) == 1 ? 0 : rowStride;
    var prevRow = (y & 1) == 1 ? rowStride : 0;

    var valShifted = (long)actualValue << PredExtraBits;
    _error[curRow + x] = _pred - valShifted;

    for (var i = 0; i < NumSubPredictors; ++i) {
      var diff = _prediction[i] - valShifted;
      if (diff < 0)
        diff = -diff;
      var err = (uint)((diff + PredictionRound) >> PredExtraBits);
      _predErrors[i][curRow + x] = err;
      _predErrors[i][prevRow + x + 1] += err;
    }
  }

  /// <summary>libjxl <c>kWPProp</c> property — the signed-max-magnitude of
  /// the recent errors at W, N, NW, NE positions. Set during the most
  /// recent <see cref="PredictWithNeighbors"/> or <see cref="Predict"/>.
  /// Used by the MA tree's split-on-WP-error feature.</summary>
  public int MaxErrorProperty => (int)_maxErrorProp;

  private int _PredictCore(int x, int y, int nIn, int wIn, int neIn, int nwIn, int nnIn) {
    var xsize = _width;
    var rowStride = xsize + 2;
    var curRow = (y & 1) == 1 ? 0 : rowStride;
    var prevRow = (y & 1) == 1 ? rowStride : 0;

    var posN = prevRow + x;
    var posNE = x < xsize - 1 ? posN + 1 : posN;
    var posNW = x > 0 ? posN - 1 : posN;

    // Compute weights from accumulated absolute errors at N/NE/NW.
    Span<uint> weights = stackalloc uint[NumSubPredictors];
    for (var i = 0; i < NumSubPredictors; ++i) {
      var sum = (ulong)_predErrors[i][posN] + _predErrors[i][posNE] + _predErrors[i][posNW];
      weights[i] = _ErrorWeight(sum, _maxWeights[i]);
    }

    // Add fractional bits.
    var n = (long)nIn << PredExtraBits;
    var wv = (long)wIn << PredExtraBits;
    var ne = (long)neIn << PredExtraBits;
    var nw = (long)nwIn << PredExtraBits;
    var nn = (long)nnIn << PredExtraBits;

    var teW = x == 0 ? 0L : _error[curRow + x - 1];
    var teN = _error[posN];
    var teNW = _error[posNW];
    var teNE = _error[posNE];
    var sumWN = teN + teW;

    // Property: signed-max of |teW|, |teN|, |teNW|, |teNE|.
    var prop = teW;
    if (Math.Abs(teN) > Math.Abs(prop))
      prop = teN;
    if (Math.Abs(teNW) > Math.Abs(prop))
      prop = teNW;
    if (Math.Abs(teNE) > Math.Abs(prop))
      prop = teNE;
    _maxErrorProp = prop;

    // Sub-predictions in <<3 domain.
    _prediction[0] = wv + ne - n;
    _prediction[1] = n - (((sumWN + teNE) * _p1C) >> 5);
    _prediction[2] = wv - (((sumWN + teNW) * _p2C) >> 5);
    _prediction[3] = n - (
      (teNW * _p3Ca + teN * _p3Cb + teNE * _p3Cc +
       (nn - n) * _p3Cd + (nw - wv) * _p3Ce) >> 5);

    var pred = _WeightedAverage(_prediction, weights);

    // Conditional clamp: if the three error signs do NOT all match, skip clamp.
    // libjxl: `((teN ^ teW) | (teN ^ teNW)) > 0` -> skip clamp.
    if (((teN ^ teW) | (teN ^ teNW)) > 0) {
      _pred = pred;
      return (int)((pred + PredictionRound) >> PredExtraBits);
    }

    var mx = Math.Max(wv, Math.Max(ne, n));
    var mn = Math.Min(wv, Math.Min(ne, n));
    if (pred > mx)
      pred = mx;
    else if (pred < mn)
      pred = mn;
    _pred = pred;
    return (int)((pred + PredictionRound) >> PredExtraBits);
  }

  /// <summary>libjxl <c>State::ErrorWeight</c>: approximates
  /// <c>4 + (maxweight &lt;&lt; 24) / (x + 1)</c> using <see cref="_DivLookup"/>
  /// over a normalised range.</summary>
  private static uint _ErrorWeight(ulong x, uint maxweight) {
    var shift = _FloorLog2NonzeroULong(x + 1) - 5;
    if (shift < 0)
      shift = 0;
    var idx = (int)((x >> shift) & 63);
    return 4u + (uint)((maxweight * (ulong)_DivLookup[idx]) >> shift);
  }

  /// <summary>libjxl <c>State::WeightedAverage</c>: weight-normalises so the
  /// log of the weight sum is exactly 4 (sum in [16,31]), then computes
  /// <c>(sum_of_p_i * w_i) * (1 &lt;&lt; 24) / weight_sum</c> via the divlookup.</summary>
  private static long _WeightedAverage(long[] p, ReadOnlySpan<uint> weightsIn) {
    Span<uint> weights = stackalloc uint[NumSubPredictors];
    weightsIn.CopyTo(weights);

    uint weightSum = 0;
    for (var i = 0; i < NumSubPredictors; ++i)
      weightSum += weights[i];
    // Shift down so log2(weight_sum) becomes 4, i.e. weight_sum in [16,31].
    var logWeight = _FloorLog2Nonzero(weightSum);
    if (logWeight < 4)
      logWeight = 4; // guard for tiny error sums; libjxl's DASSERT > 15 holds
                      // because each ErrorWeight returns >= 4 and there are 4 of them.
    weightSum = 0;
    for (var i = 0; i < NumSubPredictors; ++i) {
      weights[i] >>= logWeight - 4;
      weightSum += weights[i];
    }
    if (weightSum == 0)
      return 0;

    long sum = (weightSum >> 1) - 1;
    for (var i = 0; i < NumSubPredictors; ++i)
      sum += p[i] * (long)weights[i];

    var divIdx = (int)Math.Min((uint)(weightSum - 1), 63u);
    return (sum * _DivLookup[divIdx]) >> 24;
  }

  private static int _FloorLog2Nonzero(uint v) {
    if (v == 0)
      return 0;
    var r = 0;
    while (v > 1) {
      ++r;
      v >>= 1;
    }
    return r;
  }

  private static int _FloorLog2NonzeroULong(ulong v) {
    if (v == 0)
      return 0;
    var r = 0;
    while (v > 1) {
      ++r;
      v >>= 1;
    }
    return r;
  }
}
