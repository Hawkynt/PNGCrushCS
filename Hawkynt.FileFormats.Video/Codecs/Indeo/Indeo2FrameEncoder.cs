using System;

namespace FileFormat.Codecs.Indeo;

/// <summary>
/// Encodes one Indeo 2 intra frame from its three native planes.
/// </summary>
/// <remarks>
/// The format has no transmitted codebooks: one frame picks one of four fixed delta tables for
/// luminance and one for both chrominance planes, then writes Huffman-coded sample pairs. This writer
/// tries all four choices and keeps the one with the least squared reconstruction error, using the
/// shorter bitstream to break ties.
/// <para/>
/// Only intra frames are emitted. Inter coding is a compression decision rather than a requirement of
/// the bitstream, and an all-intra stream is independently decodable at every packet while still using
/// exactly the same sample-pair coding as an ordinary Indeo 2 key frame.
/// </remarks>
internal sealed class Indeo2FrameEncoder {

  private const int _FRAME_HEADER_LENGTH = 48;
  private const int _INTRA_FLAG_OFFSET = 18;
  private const byte _INTRA_FRAME_FLAG = 0x04;
  private const int _TABLE_SELECTOR_OFFSET = 0x22;
  private const byte _NEUTRAL = 0x80;
  private const int _RUN_BASE = 0x7F;
  private const int _LONGEST_RUN_PAIRS = 16;
  private const int _LAST_DELTA_SYMBOL = 0x7F;

  private readonly int _width;
  private readonly int _height;
  private readonly int _chromaWidth;
  private readonly int _chromaHeight;

  internal Indeo2FrameEncoder(int width, int height) {
    this._width = width;
    this._height = height;
    this._chromaWidth = width >> 2;
    this._chromaHeight = height >> 2;
  }

  /// <summary>
  /// Encodes one key frame. The stream order is Y, Cr, Cb even though the public arguments follow the
  /// conventional Y, Cb, Cr order.
  /// </summary>
  internal byte[] EncodeIntra(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> cb, ReadOnlySpan<byte> cr) {
    var lumaSamples = checked(this._width * this._height);
    var chromaSamples = checked(this._chromaWidth * this._chromaHeight);
    if (luma.Length < lumaSamples || cb.Length < chromaSamples || cr.Length < chromaSamples)
      throw new ArgumentException("The supplied planes are shorter than the Indeo 2 picture geometry requires.");

    luma = luma[..lumaSamples];
    cb = cb[..chromaSamples];
    cr = cr[..chromaSamples];

    var (lumaTable, lumaPlan) = _BestPlan(luma, this._width, this._height);
    var (chromaTable, crPlan, cbPlan) = _BestChromaPlans(cr, cb, this._chromaWidth, this._chromaHeight);

    var bits = new Indeo2BitWriter();
    _WritePlan(bits, lumaPlan, this._width);
    _WritePlan(bits, crPlan, this._chromaWidth);
    _WritePlan(bits, cbPlan, this._chromaWidth);

    var body = bits.ToArray();
    var result = new byte[_FRAME_HEADER_LENGTH + body.Length];
    // Real RT21 key frames in the fixture corpus use bit 2 here. FFmpeg treats any non-zero value as
    // intra, but writing the observed value costs nothing and is friendlier to older decoders that may
    // interpret this as a flag byte rather than as a Boolean.
    result[_INTRA_FLAG_OFFSET] = _INTRA_FRAME_FLAG;
    result[_TABLE_SELECTOR_OFFSET] = (byte)(lumaTable | (chromaTable << 2));
    body.CopyTo(result.AsSpan(_FRAME_HEADER_LENGTH));
    return result;
  }

  private static (int Table, PlanePlan Plan) _BestPlan(ReadOnlySpan<byte> source, int width, int height) {
    var bestTable = 0;
    var best = _PlanPlane(source, width, height, Indeo2Tables.Deltas[0]);

    for (var table = 1; table < Indeo2Tables.Deltas.Length; ++table) {
      var candidate = _PlanPlane(source, width, height, Indeo2Tables.Deltas[table]);
      if (_IsBetter(candidate.Error, candidate.Bits, best.Error, best.Bits)) {
        bestTable = table;
        best = candidate;
      }
    }

    return (bestTable, best);
  }

  private static (int Table, PlanePlan Cr, PlanePlan Cb) _BestChromaPlans(
    ReadOnlySpan<byte> cr, ReadOnlySpan<byte> cb, int width, int height) {
    var bestTable = 0;
    var bestCr = _PlanPlane(cr, width, height, Indeo2Tables.Deltas[0]);
    var bestCb = _PlanPlane(cb, width, height, Indeo2Tables.Deltas[0]);
    var bestError = bestCr.Error + bestCb.Error;
    var bestBits = bestCr.Bits + bestCb.Bits;

    for (var table = 1; table < Indeo2Tables.Deltas.Length; ++table) {
      var candidateCr = _PlanPlane(cr, width, height, Indeo2Tables.Deltas[table]);
      var candidateCb = _PlanPlane(cb, width, height, Indeo2Tables.Deltas[table]);
      var error = candidateCr.Error + candidateCb.Error;
      var bits = candidateCr.Bits + candidateCb.Bits;
      if (!_IsBetter(error, bits, bestError, bestBits))
        continue;

      bestTable = table;
      bestCr = candidateCr;
      bestCb = candidateCb;
      bestError = error;
      bestBits = bits;
    }

    return (bestTable, bestCr, bestCb);
  }

  /// <summary>
  /// Greedily chooses the pair that best reconstructs each two samples under the already reconstructed
  /// line above it. Symbol zero in the plan means a run/skip rather than a Huffman symbol zero — the
  /// latter does not exist in Indeo 2.
  /// </summary>
  private static PlanePlan _PlanPlane(ReadOnlySpan<byte> source, int width, int height, byte[] table) {
    var samples = checked(width * height);
    var reconstructed = new byte[samples];
    var symbols = new byte[samples >> 1];
    long error = 0;
    var bits = 0;

    for (var y = 0; y < height; ++y) {
      var row = y * width;
      var pendingSkipPairs = 0;

      for (var x = 0; x < width; x += 2) {
        var at = row + x;
        var predictor0 = y == 0 ? _NEUTRAL : reconstructed[at - width];
        var predictor1 = y == 0 ? _NEUTRAL : reconstructed[at - width + 1];
        var best0 = predictor0;
        var best1 = predictor1;
        var bestSymbol = 0;
        var bestError = _PairError(source[at], source[at + 1], best0, best1);

        for (var symbol = 1; symbol <= _LAST_DELTA_SYMBOL; ++symbol) {
          byte candidate0;
          byte candidate1;
          if (y == 0) {
            candidate0 = table[symbol * 2];
            candidate1 = table[symbol * 2 + 1];
          } else {
            candidate0 = _Clamp(predictor0 + table[symbol * 2] - 128);
            candidate1 = _Clamp(predictor1 + table[symbol * 2 + 1] - 128);
          }

          var candidateError = _PairError(source[at], source[at + 1], candidate0, candidate1);
          if (candidateError >= bestError)
            continue;

          bestError = candidateError;
          best0 = candidate0;
          best1 = candidate1;
          bestSymbol = symbol;
        }

        reconstructed[at] = best0;
        reconstructed[at + 1] = best1;
        symbols[at >> 1] = (byte)bestSymbol;
        error += bestError;

        if (bestSymbol == 0) {
          ++pendingSkipPairs;
          continue;
        }

        bits += _RunBitCount(pendingSkipPairs);
        pendingSkipPairs = 0;
        bits += Indeo2BitWriter.CodeLength((byte)bestSymbol);
      }

      bits += _RunBitCount(pendingSkipPairs);
    }

    return new(symbols, error, bits);
  }

  private static void _WritePlan(Indeo2BitWriter bits, PlanePlan plan, int width) {
    var pairsPerLine = width >> 1;
    for (var first = 0; first < plan.Symbols.Length; first += pairsPerLine) {
      var pendingSkipPairs = 0;
      var line = plan.Symbols.AsSpan(first, pairsPerLine);

      foreach (var symbol in line) {
        if (symbol == 0) {
          ++pendingSkipPairs;
          continue;
        }

        _WriteRun(bits, pendingSkipPairs);
        pendingSkipPairs = 0;
        bits.WriteSymbol(symbol);
      }

      _WriteRun(bits, pendingSkipPairs);
    }
  }

  private static void _WriteRun(Indeo2BitWriter bits, int pairCount) {
    while (pairCount > 0) {
      var chunk = Math.Min(pairCount, _LONGEST_RUN_PAIRS);
      bits.WriteSymbol((byte)(_RUN_BASE + chunk));
      pairCount -= chunk;
    }
  }

  private static int _RunBitCount(int pairCount) {
    var bits = 0;
    while (pairCount > 0) {
      var chunk = Math.Min(pairCount, _LONGEST_RUN_PAIRS);
      bits += Indeo2BitWriter.CodeLength((byte)(_RUN_BASE + chunk));
      pairCount -= chunk;
    }

    return bits;
  }

  private static bool _IsBetter(long error, int bits, long otherError, int otherBits)
    => error < otherError || error == otherError && bits < otherBits;

  private static long _PairError(byte source0, byte source1, byte reconstructed0, byte reconstructed1) {
    var delta0 = source0 - reconstructed0;
    var delta1 = source1 - reconstructed1;
    return (long)delta0 * delta0 + (long)delta1 * delta1;
  }

  private static byte _Clamp(int value) => value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;

  private readonly record struct PlanePlan(byte[] Symbols, long Error, int Bits);
}
