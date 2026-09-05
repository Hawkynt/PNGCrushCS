using System;

namespace FileFormat.Jpeg2000.Codec;

/// <summary>EBCOT bit-plane coder for a single code-block (ITU-T T.800 Annex D).</summary>
/// <remarks>
/// Three things here are easy to get wrong in a way that still round-trips against itself, and all
/// three were wrong before: the scan is by stripes of four rows and then by column, not raster; a
/// coefficient coded in the significance-propagation pass is marked visited so the cleanup pass of
/// the same bit-plane leaves it alone; and the zero-coding context table depends on which way the
/// subband is high-pass, so HL is the LL table with the horizontal and vertical neighbour counts
/// exchanged and HH has a table of its own.
/// <para/>
/// The number of magnitude bit-planes is not derived from the pass count either. It is Mb minus the
/// zero bit-planes the packet header signalled, where Mb comes from the quantization marker; a
/// decoder that counts passes instead only works on streams whose every code-block was coded to
/// completion.
/// </remarks>
internal static class Tier1Coder {

  /// <summary>Zero-coding contexts 0..8, sign 9..13, magnitude refinement 14..16, run-length 17, uniform 18.</summary>
  internal const int CONTEXT_COUNT = 19;

  private const int _CX_SIGN = 9;
  private const int _CX_MAG = 14;
  private const int _CX_RUN_LENGTH = 17;
  private const int _CX_UNIFORM = 18;

  /// <summary>Scod code-block style: vertically causal context formation.</summary>
  internal const int STYLE_VERTICALLY_CAUSAL = 0x08;

  /// <summary>Scod code-block style: a segmentation symbol closes every cleanup pass.</summary>
  internal const int STYLE_SEGMENTATION_SYMBOLS = 0x20;

  private const int _SIGMA = 1;   // coefficient is significant
  private const int _VISITED = 2; // coded in this bit-plane's significance-propagation pass
  private const int _REFINED = 4; // has had at least one magnitude refinement bit
  private const int _NEGATIVE = 8;

  /// <summary>Decodes one code-block into signed integer coefficients.</summary>
  /// <param name="data">Concatenated MQ codeword segments for the block.</param>
  /// <param name="width">Code-block width in coefficients.</param>
  /// <param name="height">Code-block height in coefficients.</param>
  /// <param name="numPasses">Total coding passes signalled across all layers.</param>
  /// <param name="magnitudeBits">Mb minus the signalled zero bit-planes.</param>
  /// <param name="orientation">0 = LL, 1 = HL, 2 = LH, 3 = HH.</param>
  /// <param name="codeBlockStyle">SPcod/SPcoc code-block style flags.</param>
  /// <param name="halve">
  /// Whether to drop the extra low bit the reconstruction interval is carried in. The reversible
  /// path wants plain integers; the irreversible path keeps the bit and folds the halving into the
  /// quantization step instead, so nothing is thrown away before the coefficient is scaled.
  /// </param>
  public static int[] Decode(
    byte[] data,
    int width,
    int height,
    int numPasses,
    int magnitudeBits,
    int orientation,
    int codeBlockStyle,
    bool halve
  ) {
    ArgumentNullException.ThrowIfNull(data);

    var magnitudes = new int[width * height];
    if (width <= 0 || height <= 0 || numPasses <= 0 || magnitudeBits <= 0)
      return magnitudes;
    if (magnitudeBits > 30)
      throw new InvalidDataException($"A JPEG 2000 code-block claims {magnitudeBits} magnitude bit-planes, which does not fit an Int32 coefficient.");

    var flags = new byte[width * height];
    var mq = new MqDecoder(data, 0, data.Length, CONTEXT_COUNT);
    _ResetContexts(mq.SetContext);

    // Magnitudes are carried at twice their value while decoding. A coding pass narrows a
    // coefficient to an interval rather than to a number, and the extra bit is what lets the
    // reconstruction sit at the middle of that interval; a stream that stops early — every lossy
    // stream — is otherwise reconstructed at the bottom of it and comes out systematically dark.
    var state = new BlockState(width, height, orientation, codeBlockStyle, flags, magnitudes);
    var plane = magnitudeBits;
    var passType = 2;

    for (var pass = 0; pass < numPasses && plane >= 1; ++pass) {
      var one = 1 << plane;
      var half = one >> 1;
      switch (passType) {
        case 0:
          _DecodeSignificancePropagation(mq, state, one | half);
          break;
        case 1:
          _DecodeMagnitudeRefinement(mq, state, half);
          break;
        default:
          _DecodeCleanup(mq, state, one | half);
          if ((codeBlockStyle & STYLE_SEGMENTATION_SYMBOLS) != 0)
            _DecodeSegmentationSymbol(mq);
          break;
      }

      if (passType == 2) {
        --plane;
        passType = 0;
        for (var i = 0; i < flags.Length; ++i)
          flags[i] &= unchecked((byte)~_VISITED);
      } else
        ++passType;
    }

    var result = magnitudes;
    for (var i = 0; i < result.Length; ++i) {
      if ((flags[i] & _SIGMA) == 0) {
        result[i] = 0;
        continue;
      }

      var value = halve ? result[i] / 2 : result[i];
      result[i] = (flags[i] & _NEGATIVE) != 0 ? -value : value;
    }

    return result;
  }

  /// <summary>
  /// Encodes one code-block to completion and reports the passes it took and how many magnitude
  /// bit-planes it needed.
  /// </summary>
  public static byte[] Encode(
    int[] coefficients,
    int width,
    int height,
    int orientation,
    int codeBlockStyle,
    out int numPasses,
    out int magnitudeBits
  ) {
    ArgumentNullException.ThrowIfNull(coefficients);
    numPasses = 0;
    magnitudeBits = 0;

    if (width <= 0 || height <= 0)
      return [];

    var maximum = 0;
    for (var i = 0; i < width * height; ++i)
      maximum = Math.Max(maximum, Math.Abs(coefficients[i]));

    if (maximum == 0)
      return [];

    magnitudeBits = 32 - System.Numerics.BitOperations.LeadingZeroCount((uint)maximum);

    var flags = new byte[width * height];
    var magnitudes = new int[width * height];
    for (var i = 0; i < magnitudes.Length; ++i) {
      magnitudes[i] = Math.Abs(coefficients[i]);
      if (coefficients[i] < 0)
        flags[i] |= _NEGATIVE;
    }

    var mq = new MqEncoder(CONTEXT_COUNT);
    _ResetContexts(mq.SetContext);

    var state = new BlockState(width, height, orientation, codeBlockStyle, flags, magnitudes);
    var plane = magnitudeBits - 1;
    var passType = 2;

    while (plane >= 0) {
      var bitValue = 1 << plane;
      switch (passType) {
        case 0:
          _EncodeSignificancePropagation(mq, state, bitValue);
          break;
        case 1:
          _EncodeMagnitudeRefinement(mq, state, bitValue);
          break;
        default:
          _EncodeCleanup(mq, state, bitValue);
          if ((codeBlockStyle & STYLE_SEGMENTATION_SYMBOLS) != 0)
            _EncodeSegmentationSymbol(mq);
          break;
      }

      ++numPasses;

      if (passType == 2) {
        --plane;
        passType = 0;
        for (var i = 0; i < flags.Length; ++i)
          flags[i] &= unchecked((byte)~_VISITED);
      } else
        ++passType;
    }

    return mq.Flush();
  }

  /// <summary>Table D.7: every context starts in state zero bar these three.</summary>
  private static void _ResetContexts(Action<int, int, int> setContext) {
    setContext(0, 4, 0);
    setContext(_CX_RUN_LENGTH, 3, 0);
    setContext(_CX_UNIFORM, 46, 0);
  }

  private sealed class BlockState(
    int width,
    int height,
    int orientation,
    int codeBlockStyle,
    byte[] flags,
    int[] magnitudes
  ) {
    internal int Width { get; } = width;
    internal int Height { get; } = height;
    internal int Orientation { get; } = orientation;
    internal bool VerticallyCausal { get; } = (codeBlockStyle & STYLE_VERTICALLY_CAUSAL) != 0;
    internal byte[] Flags { get; } = flags;
    internal int[] Magnitudes { get; } = magnitudes;

    internal bool IsSignificant(int x, int y) {
      if ((uint)x >= (uint)this.Width || (uint)y >= (uint)this.Height)
        return false;
      return (this.Flags[y * this.Width + x] & _SIGMA) != 0;
    }

    /// <summary>
    /// Significance as the context formation may see it. With vertically causal contexts a
    /// coefficient in the stripe below the current one counts as insignificant.
    /// </summary>
    internal bool IsSignificantForContext(int x, int y, int stripeTop) {
      if (this.VerticallyCausal && y >= stripeTop + 4)
        return false;
      return this.IsSignificant(x, y);
    }

    internal int SignContribution(int x, int y, int stripeTop) {
      if (!this.IsSignificantForContext(x, y, stripeTop))
        return 0;
      return (this.Flags[y * this.Width + x] & _NEGATIVE) != 0 ? -1 : 1;
    }
  }

  private static int _ZeroCodingContext(BlockState state, int x, int y, int stripeTop) {
    var h = (state.IsSignificantForContext(x - 1, y, stripeTop) ? 1 : 0)
          + (state.IsSignificantForContext(x + 1, y, stripeTop) ? 1 : 0);
    var v = (state.IsSignificantForContext(x, y - 1, stripeTop) ? 1 : 0)
          + (state.IsSignificantForContext(x, y + 1, stripeTop) ? 1 : 0);
    var d = (state.IsSignificantForContext(x - 1, y - 1, stripeTop) ? 1 : 0)
          + (state.IsSignificantForContext(x + 1, y - 1, stripeTop) ? 1 : 0)
          + (state.IsSignificantForContext(x - 1, y + 1, stripeTop) ? 1 : 0)
          + (state.IsSignificantForContext(x + 1, y + 1, stripeTop) ? 1 : 0);

    // Table D.1. HL is the LL/LH table read with the two axes exchanged, HH has its own.
    switch (state.Orientation) {
      case 3:
        var hv = h + v;
        if (d >= 3) return 8;
        if (d == 2) return hv >= 1 ? 7 : 6;
        if (d == 1) return hv >= 2 ? 5 : hv == 1 ? 4 : 3;
        return hv >= 2 ? 2 : hv;
      case 1:
        (h, v) = (v, h);
        break;
    }

    if (h == 2) return 8;
    if (h == 1) return v >= 1 ? 7 : d >= 1 ? 6 : 5;
    if (v == 2) return 4;
    if (v == 1) return 3;
    return d >= 2 ? 2 : d;
  }

  /// <summary>Table D.3, returning the context and the bit the coded sign is exclusive-ored with.</summary>
  private static void _SignContext(BlockState state, int x, int y, int stripeTop, out int context, out int xorBit) {
    var h = Math.Clamp(
      state.SignContribution(x - 1, y, stripeTop) + state.SignContribution(x + 1, y, stripeTop), -1, 1);
    var v = Math.Clamp(
      state.SignContribution(x, y - 1, stripeTop) + state.SignContribution(x, y + 1, stripeTop), -1, 1);

    if (h == 0) {
      context = v == 0 ? _CX_SIGN : _CX_SIGN + 1;
      xorBit = v < 0 ? 1 : 0;
      return;
    }

    if (h > 0) {
      context = _CX_SIGN + (v > 0 ? 4 : v == 0 ? 3 : 2);
      xorBit = 0;
      return;
    }

    context = _CX_SIGN + (v < 0 ? 4 : v == 0 ? 3 : 2);
    xorBit = 1;
  }

  /// <summary>Table D.4.</summary>
  private static int _MagnitudeContext(BlockState state, int x, int y, int stripeTop) {
    if ((state.Flags[y * state.Width + x] & _REFINED) != 0)
      return _CX_MAG + 2;

    var hasNeighbour = state.IsSignificantForContext(x - 1, y, stripeTop)
                    || state.IsSignificantForContext(x + 1, y, stripeTop)
                    || state.IsSignificantForContext(x, y - 1, stripeTop)
                    || state.IsSignificantForContext(x, y + 1, stripeTop)
                    || state.IsSignificantForContext(x - 1, y - 1, stripeTop)
                    || state.IsSignificantForContext(x + 1, y - 1, stripeTop)
                    || state.IsSignificantForContext(x - 1, y + 1, stripeTop)
                    || state.IsSignificantForContext(x + 1, y + 1, stripeTop);

    return _CX_MAG + (hasNeighbour ? 1 : 0);
  }

  private static void _DecodeSignificancePropagation(MqDecoder mq, BlockState state, int onePlusHalf) {
    for (var stripeTop = 0; stripeTop < state.Height; stripeTop += 4)
      for (var x = 0; x < state.Width; ++x) {
        var rows = Math.Min(4, state.Height - stripeTop);
        for (var row = 0; row < rows; ++row) {
          var y = stripeTop + row;
          var index = y * state.Width + x;
          if ((state.Flags[index] & _SIGMA) != 0)
            continue;

          var context = _ZeroCodingContext(state, x, y, stripeTop);
          if (context == 0)
            continue;

          state.Flags[index] |= _VISITED;
          if (mq.DecodeBit(context) == 0)
            continue;

          _SignContext(state, x, y, stripeTop, out var signContext, out var xorBit);
          var negative = (mq.DecodeBit(signContext) ^ xorBit) != 0;
          state.Flags[index] |= (byte)(_SIGMA | (negative ? _NEGATIVE : 0));
          state.Magnitudes[index] = onePlusHalf;
        }
      }
  }

  private static void _EncodeSignificancePropagation(MqEncoder mq, BlockState state, int bitValue) {
    for (var stripeTop = 0; stripeTop < state.Height; stripeTop += 4)
      for (var x = 0; x < state.Width; ++x) {
        var rows = Math.Min(4, state.Height - stripeTop);
        for (var row = 0; row < rows; ++row) {
          var y = stripeTop + row;
          var index = y * state.Width + x;
          if ((state.Flags[index] & _SIGMA) != 0)
            continue;

          var context = _ZeroCodingContext(state, x, y, stripeTop);
          if (context == 0)
            continue;

          state.Flags[index] |= _VISITED;
          var symbol = (state.Magnitudes[index] & bitValue) != 0 ? 1 : 0;
          mq.EncodeBit(context, symbol);
          if (symbol == 0)
            continue;

          _SignContext(state, x, y, stripeTop, out var signContext, out var xorBit);
          mq.EncodeBit(signContext, ((state.Flags[index] & _NEGATIVE) != 0 ? 1 : 0) ^ xorBit);
          state.Flags[index] |= _SIGMA;
        }
      }
  }

  private static void _DecodeMagnitudeRefinement(MqDecoder mq, BlockState state, int half) {
    for (var stripeTop = 0; stripeTop < state.Height; stripeTop += 4)
      for (var x = 0; x < state.Width; ++x) {
        var rows = Math.Min(4, state.Height - stripeTop);
        for (var row = 0; row < rows; ++row) {
          var y = stripeTop + row;
          var index = y * state.Width + x;
          if ((state.Flags[index] & (_SIGMA | _VISITED)) != _SIGMA)
            continue;

          var context = _MagnitudeContext(state, x, y, stripeTop);

          // The refinement bit halves the interval; the reconstruction moves to the middle of
          // whichever half it names, which is a step of half the current plane either way.
          state.Magnitudes[index] += mq.DecodeBit(context) != 0 ? half : -half;
          state.Flags[index] |= _REFINED;
        }
      }
  }

  private static void _EncodeMagnitudeRefinement(MqEncoder mq, BlockState state, int bitValue) {
    for (var stripeTop = 0; stripeTop < state.Height; stripeTop += 4)
      for (var x = 0; x < state.Width; ++x) {
        var rows = Math.Min(4, state.Height - stripeTop);
        for (var row = 0; row < rows; ++row) {
          var y = stripeTop + row;
          var index = y * state.Width + x;
          if ((state.Flags[index] & (_SIGMA | _VISITED)) != _SIGMA)
            continue;

          var context = _MagnitudeContext(state, x, y, stripeTop);
          mq.EncodeBit(context, (state.Magnitudes[index] & bitValue) != 0 ? 1 : 0);
          state.Flags[index] |= _REFINED;
        }
      }
  }

  private static void _DecodeCleanup(MqDecoder mq, BlockState state, int onePlusHalf) {
    for (var stripeTop = 0; stripeTop < state.Height; stripeTop += 4)
      for (var x = 0; x < state.Width; ++x) {
        var rows = Math.Min(4, state.Height - stripeTop);
        var row = 0;

        if (_CanRunLengthCode(state, x, stripeTop, rows)) {
          if (mq.DecodeBit(_CX_RUN_LENGTH) == 0)
            continue;

          row = (mq.DecodeBit(_CX_UNIFORM) << 1) | mq.DecodeBit(_CX_UNIFORM);
          var y = stripeTop + row;
          var index = y * state.Width + x;
          _SignContext(state, x, y, stripeTop, out var signContext, out var xorBit);
          var negative = (mq.DecodeBit(signContext) ^ xorBit) != 0;
          state.Flags[index] |= (byte)(_SIGMA | (negative ? _NEGATIVE : 0));
          state.Magnitudes[index] = onePlusHalf;
          ++row;
        }

        for (; row < rows; ++row) {
          var y = stripeTop + row;
          var index = y * state.Width + x;
          if ((state.Flags[index] & (_SIGMA | _VISITED)) != 0)
            continue;

          var context = _ZeroCodingContext(state, x, y, stripeTop);
          if (mq.DecodeBit(context) == 0)
            continue;

          _SignContext(state, x, y, stripeTop, out var signContext, out var xorBit);
          var negative = (mq.DecodeBit(signContext) ^ xorBit) != 0;
          state.Flags[index] |= (byte)(_SIGMA | (negative ? _NEGATIVE : 0));
          state.Magnitudes[index] = onePlusHalf;
        }
      }
  }

  private static void _EncodeCleanup(MqEncoder mq, BlockState state, int bitValue) {
    for (var stripeTop = 0; stripeTop < state.Height; stripeTop += 4)
      for (var x = 0; x < state.Width; ++x) {
        var rows = Math.Min(4, state.Height - stripeTop);
        var row = 0;

        if (_CanRunLengthCode(state, x, stripeTop, rows)) {
          var first = -1;
          for (var candidate = 0; candidate < 4; ++candidate)
            if ((state.Magnitudes[(stripeTop + candidate) * state.Width + x] & bitValue) != 0) {
              first = candidate;
              break;
            }

          if (first < 0) {
            mq.EncodeBit(_CX_RUN_LENGTH, 0);
            continue;
          }

          mq.EncodeBit(_CX_RUN_LENGTH, 1);
          mq.EncodeBit(_CX_UNIFORM, (first >> 1) & 1);
          mq.EncodeBit(_CX_UNIFORM, first & 1);

          var y = stripeTop + first;
          var index = y * state.Width + x;
          _SignContext(state, x, y, stripeTop, out var signContext, out var xorBit);
          mq.EncodeBit(signContext, ((state.Flags[index] & _NEGATIVE) != 0 ? 1 : 0) ^ xorBit);
          state.Flags[index] |= _SIGMA;
          row = first + 1;
        }

        for (; row < rows; ++row) {
          var y = stripeTop + row;
          var index = y * state.Width + x;
          if ((state.Flags[index] & (_SIGMA | _VISITED)) != 0)
            continue;

          var context = _ZeroCodingContext(state, x, y, stripeTop);
          var symbol = (state.Magnitudes[index] & bitValue) != 0 ? 1 : 0;
          mq.EncodeBit(context, symbol);
          if (symbol == 0)
            continue;

          _SignContext(state, x, y, stripeTop, out var signContext, out var xorBit);
          mq.EncodeBit(signContext, ((state.Flags[index] & _NEGATIVE) != 0 ? 1 : 0) ^ xorBit);
          state.Flags[index] |= _SIGMA;
        }
      }
  }

  /// <summary>
  /// The run-length primitive applies only to a complete stripe column in which every coefficient is
  /// still insignificant, none was coded in this plane's earlier passes, and all four have an
  /// all-zero neighbourhood.
  /// </summary>
  private static bool _CanRunLengthCode(BlockState state, int x, int stripeTop, int rows) {
    if (rows != 4)
      return false;

    for (var row = 0; row < 4; ++row) {
      var y = stripeTop + row;
      if ((state.Flags[y * state.Width + x] & (_SIGMA | _VISITED)) != 0)
        return false;
      if (_ZeroCodingContext(state, x, y, stripeTop) != 0)
        return false;
    }

    return true;
  }

  private static void _DecodeSegmentationSymbol(MqDecoder mq) {
    var symbol = 0;
    for (var i = 0; i < 4; ++i)
      symbol = (symbol << 1) | mq.DecodeBit(_CX_UNIFORM);

    if (symbol != 0xA)
      throw new InvalidOperationException("JPEG 2000 cleanup pass ended on a segmentation symbol other than 0xA.");
  }

  private static void _EncodeSegmentationSymbol(MqEncoder mq) {
    for (var bit = 3; bit >= 0; --bit)
      mq.EncodeBit(_CX_UNIFORM, (0xA >> bit) & 1);
  }
}
