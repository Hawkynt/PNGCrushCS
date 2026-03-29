namespace FileFormat.JpegLs;

/// <summary>
/// Adaptive Golomb-Rice entropy coder for JPEG-LS (ITU-T T.87).
/// Handles mapping of signed errors to non-negative values, and encoding/decoding
/// using unary+binary Golomb-Rice codes with a limited-length fallback.
/// </summary>
internal static class JpegLsGolombCoder {

  /// <summary>
  /// Maps a signed error value to a non-negative Golomb-Rice symbol.
  /// Uses the error-mapping formula from ITU-T T.87 Table A.4:
  /// Normal:   error &gt;= 0 => 2*error, error &lt; 0 => -2*error - 1
  /// Inverted: error &gt;= 0 => 2*error + 1, error &lt; 0 => -2*error
  /// </summary>
  /// <param name="error">Signed prediction error (after modulo reduction).</param>
  /// <param name="inverted">Whether to use the inverted mapping (k==0 and bias condition).</param>
  /// <returns>Non-negative mapped error value.</returns>
  internal static int MapError(int error, bool inverted) {
    if (inverted)
      return error >= 0 ? 2 * error + 1 : -2 * error;
    return error >= 0 ? 2 * error : -2 * error - 1;
  }

  /// <summary>
  /// Inverse maps a non-negative Golomb-Rice symbol back to a signed error value.
  /// </summary>
  /// <param name="mapped">Non-negative mapped value.</param>
  /// <param name="inverted">Whether the inverted mapping was used.</param>
  /// <returns>Signed prediction error.</returns>
  internal static int UnmapError(int mapped, bool inverted) {
    if (inverted)
      return mapped % 2 == 0 ? -(mapped / 2) : (mapped + 1) / 2;
    return mapped % 2 == 0 ? mapped / 2 : -((mapped + 1) / 2);
  }

  /// <summary>
  /// Encodes a non-negative mapped value using Golomb-Rice coding.
  /// Normal: writes (quotient) zeros + 1 + (k) remainder bits.
  /// Limited-length fallback: writes (limit - qbpp - 1) zeros + 1 + (qbpp) bits of (mapped - 1).
  /// </summary>
  /// <param name="writer">Bit-level output writer (MSB-first).</param>
  /// <param name="mapped">Non-negative mapped error value.</param>
  /// <param name="k">Golomb parameter (number of remainder bits).</param>
  /// <param name="limit">Maximum code length limit: 2 * (BPP + max(8, BPP)).</param>
  /// <param name="qbpp">Bits per quantized sample: ceil(log2(RANGE)).</param>
  internal static void Encode(BitWriter writer, int mapped, int k, int limit, int qbpp) {
    var quotient = mapped >> k;

    if (quotient < limit - qbpp - 1) {
      // Normal Golomb-Rice: unary zeros + 1 separator + k remainder bits
      for (var i = 0; i < quotient; ++i)
        writer.WriteBit(0);
      writer.WriteBit(1);
      if (k > 0) {
        var remainder = mapped & ((1 << k) - 1);
        writer.WriteBits(remainder, k);
      }
    } else {
      // Limited-length fallback: (limit - qbpp - 1) zeros + 1 + qbpp bits
      for (var i = 0; i < limit - qbpp - 1; ++i)
        writer.WriteBit(0);
      writer.WriteBit(1);
      writer.WriteBits(mapped - 1, qbpp);
    }
  }

  /// <summary>
  /// Decodes a non-negative mapped value using Golomb-Rice coding.
  /// Reads unary zeros for the quotient, then k remainder bits.
  /// Falls back to limited-length decoding when quotient reaches (limit - qbpp - 1).
  /// </summary>
  /// <param name="reader">Bit-level input reader (MSB-first).</param>
  /// <param name="k">Golomb parameter (number of remainder bits).</param>
  /// <param name="limit">Maximum code length limit.</param>
  /// <param name="qbpp">Bits per quantized sample.</param>
  /// <returns>Non-negative mapped error value.</returns>
  internal static int Decode(BitReader reader, int k, int limit, int qbpp) {
    var quotient = 0;
    while (reader.ReadBit() == 0) {
      ++quotient;
      if (quotient >= limit - qbpp - 1) {
        // Limited-length fallback: consume the '1' terminator and read qbpp raw bits
        reader.ReadBit();
        return reader.ReadBits(qbpp) + 1;
      }
    }

    // Normal case: quotient zeros followed by a 1 (already consumed by the while condition)
    if (k > 0) {
      var remainder = reader.ReadBits(k);
      return (quotient << k) | remainder;
    }

    return quotient;
  }
}
