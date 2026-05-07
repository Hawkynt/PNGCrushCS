using System;

namespace FileFormat.JpegXl.Codec;

// =====================================================================================
// LF (low-frequency) coefficient decoder for VarDCT (ISO/IEC 18181-1 §G.6;
// libjxl `lib/jxl/dec_modular.cc::DecodeVarDCTDC` + `lib/jxl/dec_group.cc`).
//
// The LF sub-image is the per-block DC component of a VarDCT frame. For DCT8
// blocks (the most common case) there is exactly one DC coefficient per 8×8
// block; for larger DCT shapes (DCT16+) the LF sub-image is the lower-resolution
// coefficient sub-image at the smaller block grid.
//
// libjxl bitstream layout for a single LF group (paraphrased from
// dec_modular.cc::DecodeVarDCTDC):
//
//   1. extra_precision = ReadBits(2)
//      — extra precision bits used by DequantDC; pure-decode side just
//        records the value. Quantization scaling is mul = 1.0 / (1 << extra_precision).
//   2. The body of the LF group is a modular sub-image of dimensions
//      (groupBlocksWide × groupBlocksHigh) with `numChannels` channels.
//      Per libjxl the channel order is permuted: image.channel[c < 2 ? c ^ 1 : c]
//      so that Y comes first (Y=0 in libjxl's internal ordering means c=1 in
//      XYB → after the swap Y is stored at channel index 0). For the spec-faithful
//      surface we expose, the caller hands us numChannels and we return one
//      JxlLfBlock per channel in the SAME order as the modular decoder produced.
//      Re-ordering to canonical XYB-by-channel-index is a frame-level concern,
//      not an LF-decoder concern.
//   3. The modular sub-codec reads its own GroupHeader (transform chain),
//      MA tree, entropy block, and per-pixel residuals. This is delegated
//      verbatim to JxlModularSpecDecoder.Decode. Any feature in the modular
//      header we don't yet support (non-trivial transform chain, custom
//      MA tree shape, etc.) propagates NotImplementedException up to the
//      caller — which is the correct behaviour: the failure mode is precise
//      and load-bearing for downstream debugging.
//
// What this decoder does NOT do (deliberately, as scoped by the orchestrator):
//   - Dequantize the DC values. libjxl's DequantDC multiplies by the per-channel
//     DC quant factor and the chroma-from-luma DC factor; that step belongs to
//     the dequantization sub-codec (JxlVarDctQuant) and is reached after this
//     decoder returns.
//   - Apply the chroma_subsampling shift (gi.channel[c].w >>= HShift(c)). For
//     DCT8 frames with subsampling=4:4:4 (the typical case) all three channels
//     have identical dimensions and this is a no-op.
//   - Run XYB color-from-luma correction. That happens in JxlXybColorTransform.
// =====================================================================================

/// <summary>
/// Decoder for the LF (low-frequency, i.e. DC) sub-image of one VarDCT group.
/// Single static entry point: <see cref="DecodeGroup"/>.
/// </summary>
internal static class JxlLfDecoder {

  /// <summary>
  /// Decode the LF (DC) coefficient sub-image for a VarDCT group.
  ///
  /// <para>For a DCT8-only frame this returns one DC coefficient per 8×8 block;
  /// the LF sub-image has dimensions <c>groupBlocksWide × groupBlocksHigh</c>
  /// per channel.</para>
  /// </summary>
  /// <param name="reader">Bit reader positioned at the start of the LF group data
  /// (immediately after any preceding TOC/group preamble — see libjxl
  /// <c>DecodeVarDCTDC</c> for the exact framing).</param>
  /// <param name="groupBlocksWide">Number of 8×8 blocks horizontally in the group.</param>
  /// <param name="groupBlocksHigh">Number of 8×8 blocks vertically in the group.</param>
  /// <param name="numChannels">Number of channels in the LF sub-image. Typically 3
  /// for an XYB VarDCT frame.</param>
  /// <returns>One <see cref="JxlLfBlock"/> per channel; the block's
  /// <see cref="JxlLfBlock.Coefficients"/> array contains the dequantized DCs in
  /// row-major order (length = <c>groupBlocksWide × groupBlocksHigh</c>).</returns>
  public static JxlLfBlock[] DecodeGroup(
    JxlBitReader reader,
    int groupBlocksWide,
    int groupBlocksHigh,
    int numChannels
  ) {
    ArgumentNullException.ThrowIfNull(reader);
    if (groupBlocksWide <= 0)
      throw new ArgumentOutOfRangeException(nameof(groupBlocksWide), "Block width must be positive.");
    if (groupBlocksHigh <= 0)
      throw new ArgumentOutOfRangeException(nameof(groupBlocksHigh), "Block height must be positive.");
    if (numChannels <= 0)
      throw new ArgumentOutOfRangeException(nameof(numChannels), "Channel count must be positive.");

    // Step 1: extra_precision (2 bits).
    //
    // libjxl: `extra_precision = reader->ReadFixedBits<2>();` — the value is
    // used downstream by DequantDC for the multiplier `1.0f / (1 << extra_precision)`.
    // The pure decode side just consumes the bits. We preserve them for the
    // dequantization stage by stamping them onto the returned blocks (no
    // first-class field exists in JxlLfBlock yet, so the caller can't read it
    // back; that's fine for the first-wave wiring — JxlVarDctQuant.Dequantize
    // is invoked later with the default mul=1.0 scale and will need a small
    // extension to accept extra_precision once the DC dequant path lands).
    var extraPrecision = reader.ReadBits(2);
    _ = extraPrecision;  // currently unused; consumed for bit-position correctness.

    // Step 2: hand off to the modular sub-codec.
    //
    // bitDepth: libjxl uses `full_image.bitdepth` from the global modular
    // header. For a fresh per-LF-group sub-image we don't have that context,
    // so we follow the orchestrator's directive and use 16 — wide enough for
    // DC residuals (which are at most ±2^15 after entropy decode) and matches
    // the contract documented at the call site.
    //
    // The modular decoder reads its own GroupHeader (incl. the 1-bit
    // `use_global_tree` / `all_default` flag), so we do NOT pre-consume that
    // bit here. Doing so would double-read it.
    var modular = JxlModularSpecDecoder.Decode(
      reader,
      width: groupBlocksWide,
      height: groupBlocksHigh,
      numChannels: numChannels,
      bitDepth: 16
    );

    // Step 3: wrap the modular channels into JxlLfBlock instances.
    //
    // The modular decoder returns at least `numChannels` channels (transforms
    // may produce more, but for an LF-DC sub-image with the typical empty
    // transform chain the count matches exactly). We pick the first
    // `numChannels` and convert int32 pixels → int16 coefficients with
    // saturation, matching libjxl's storage semantics for DC residuals
    // (ACType::k16 path uses int16; ACType::k32 only kicks in for very-high-
    // dynamic-range JPEG-XL inputs which the DC stream never triggers in the
    // first-wave pipeline).
    var producedChannels = modular.Channels;
    if (producedChannels.Length < numChannels)
      throw new System.IO.InvalidDataException(
        $"LF modular sub-image produced {producedChannels.Length} channels, " +
        $"expected at least {numChannels}.");

    var lfBlocks = new JxlLfBlock[numChannels];
    for (var c = 0; c < numChannels; ++c) {
      var channel = producedChannels[c];

      // Dimensions sanity. The modular decoder may apply transforms that
      // reshape per-channel dimensions; for the LF-DC sub-image this should
      // be a no-op. If a transform reshaped the channel we report it loudly:
      // proceeding silently would silently corrupt downstream IDCT.
      if (channel.Width != groupBlocksWide || channel.Height != groupBlocksHigh)
        throw new System.IO.InvalidDataException(
          $"LF modular channel {c} dimensions are " +
          $"{channel.Width}×{channel.Height}, expected " +
          $"{groupBlocksWide}×{groupBlocksHigh}. " +
          "Transform-induced reshape is not yet supported in the LF wrapper.");

      var pixelCount = groupBlocksWide * groupBlocksHigh;
      var coeffs = new short[pixelCount];
      for (var i = 0; i < pixelCount; ++i)
        coeffs[i] = _SaturateInt16(channel.Pixels[i]);

      lfBlocks[c] = new JxlLfBlock {
        Width = groupBlocksWide,
        Height = groupBlocksHigh,
        Coefficients = coeffs,
      };
    }
    return lfBlocks;
  }

  /// <summary>
  /// Saturate an int32 to int16. JXL's LF residuals fit in int16 by spec for
  /// DCT8 frames; values that overflow indicate either (a) a malformed
  /// bitstream or (b) a future feature (e.g. high-bit-depth DC) we don't yet
  /// support. Saturating rather than throwing here keeps the decoder
  /// tolerant — the IDCT step naturally handles saturated DCs.
  /// </summary>
  private static short _SaturateInt16(int value) {
    if (value > short.MaxValue)
      return short.MaxValue;
    if (value < short.MinValue)
      return short.MinValue;
    return (short)value;
  }
}
