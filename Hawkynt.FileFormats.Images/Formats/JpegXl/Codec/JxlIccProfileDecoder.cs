using System;
using System.IO;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// Decoder for the JPEG XL ICC profile blob (ISO/IEC 18181-1 §3.7;
/// libjxl <c>lib/jxl/icc_codec.cc::ICCReader::Init/Process</c> +
/// <c>icc_codec_common.cc</c>).
///
/// <para>When <see cref="JxlColorEncoding.WantIcc"/> is true on the
/// ImageMetadata, an ICC profile is encoded directly after the metadata
/// bundle and BEFORE the first FrameHeader. The encoding is a custom
/// ANS-coded byte stream of length <c>enc_size</c>, with per-byte context
/// selection based on byte position and the two previous decoded bytes.
/// Bitstream layout:
/// <list type="number">
///   <item>U64 <c>enc_size</c> — the encoded byte count (output ICC profile
///         size after <c>UnpredictICC</c> expansion).</item>
///   <item>ANS code with 41 contexts (<c>kNumICCContexts</c>).</item>
///   <item>Exactly <c>enc_size</c> bytes decoded via the ANS reader, with
///         context = <see cref="_IccAnsContext"/>(i, byte[i-1], byte[i-2]).</item>
///   <item>Final ANS-state validation.</item>
/// </list>
/// </para>
///
/// <para>The decoded byte stream represents a compressed/predicted form of
/// the ICC profile (commands + data). Reconstructing the actual ICC profile
/// bytes requires <c>UnpredictICC</c> (Insert / Shuffle / Predict / XYZ /
/// TypeStrings command interpretation + linear predictor + tag table
/// expansion) — that's a separate workstream and not yet implemented. For
/// callers needing only bit-position alignment (i.e. "advance the reader
/// past the ICC blob"), this decoder is sufficient.</para>
/// </summary>
internal static class JxlIccProfileDecoder {

  /// <summary>libjxl <c>kNumICCContexts</c> from <c>icc_codec_common.h</c>.</summary>
  internal const int NumIccContexts = 41;

  /// <summary>libjxl maximum encoded ICC profile size (256 MiB).</summary>
  private const ulong _MaxEncSize = 268435456;

  /// <summary>Read the ICC profile blob, advancing the bit reader past it.
  /// Returns the encoded byte stream (NOT the final ICC profile — that
  /// requires <c>UnpredictICC</c> expansion). Bit-position alignment for
  /// downstream readers IS correct after this call.</summary>
  public static byte[] Read(JxlBitReader reader) {
    ArgumentNullException.ThrowIfNull(reader);

    // Phase 1: read encoded size.
    var encSize = reader.ReadU64();
    if (encSize > _MaxEncSize)
      throw new InvalidDataException($"ICC profile size {encSize} exceeds libjxl cap of {_MaxEncSize}.");

    if (encSize == 0)
      return [];

    // Phase 2: ANS code histograms with 41 contexts.
    var entropy = JxlEntropyDecoder.Read(reader, NumIccContexts);

    // Phase 3: decode exactly enc_size bytes via the ANS reader. Per libjxl
    // `ICCReader::Process`, the loop runs `for (; i_ < enc_size_; i_++)`
    // and decodes each byte using `ICCANSContext(i, prev1, prev2)` where
    // prev1/prev2 default to 0 for i < 2.
    var output = new byte[encSize];
    for (long i = 0; i < (long)encSize; ++i) {
      byte b1 = i > 0 ? output[i - 1] : (byte)0;
      byte b2 = i > 1 ? output[i - 2] : (byte)0;
      var ctx = _IccAnsContext(i, b1, b2);
      var symbol = entropy.ReadInt(ctx);
      if (symbol < 0 || symbol > 255)
        throw new InvalidDataException($"ICC ANS symbol {symbol} out of range [0,256).");
      output[i] = (byte)symbol;
    }

    // Phase 4: ANS final state validation.
    if (!entropy.CheckFinalState())
      throw new InvalidDataException("ICC ANS stream ended in invalid final state.");

    return output;
  }

  /// <summary>libjxl <c>ICCANSContext</c> from <c>icc_codec_common.cc</c>:
  /// position-and-byte-kind context selection. Returns 0 for i &lt;= 128;
  /// otherwise <c>1 + ByteKind1(prev1) + ByteKind2(prev2) * 8</c>.</summary>
  private static int _IccAnsContext(long i, byte b1, byte b2) {
    if (i <= 128)
      return 0;
    return 1 + _ByteKind1(b1) + _ByteKind2(b2) * 8;
  }

  /// <summary>libjxl <c>ByteKind1</c>: 8 byte categories used for the
  /// previous-byte context dimension.</summary>
  private static int _ByteKind1(byte b) {
    if ((b >= 'a' && b <= 'z') || (b >= 'A' && b <= 'Z')) return 0;
    if ((b >= '0' && b <= '9') || b == '.' || b == ',') return 1;
    if (b == 0) return 2;
    if (b == 1) return 3;
    if (b < 16) return 4;
    if (b == 255) return 6;
    if (b > 240) return 5;
    return 7;
  }

  /// <summary>libjxl <c>ByteKind2</c>: 5 byte categories used for the
  /// previous-previous-byte context dimension.</summary>
  private static int _ByteKind2(byte b) {
    if ((b >= 'a' && b <= 'z') || (b >= 'A' && b <= 'Z')) return 0;
    if ((b >= '0' && b <= '9') || b == '.' || b == ',') return 1;
    if (b < 16) return 2;
    if (b > 240) return 3;
    return 4;
  }
}
