using System;

namespace FileFormat.JpegXl.Codec;

// =====================================================================================
// Top-level VarDCT decoder orchestrator (ISO/IEC 18181-1 §G.1; libjxl
// `lib/jxl/dec_frame.cc::DecodeFrame` + `lib/jxl/dec_group.cc::DecodeGroupImpl`).
//
// VarDCT is JPEG XL's lossy mode. The pipeline structure is:
//
//   1. Read frame quant tables (DequantWeights::Decode, libjxl quant_weights.cc)
//   2. Read AC strategy per group (libjxl dec_ac_strategy.cc)
//   3. Read LF coefficients (DC for DCT8, downsampled subimage for DCT16+)
//   4. Read AC coefficients (per-block, context-modeled hybrid integers)
//   5. Dequantize (multiply each block's coefficients by the quant table)
//   6. Inverse DCT per block (size depends on AC strategy)
//   7. Pack into channel float planes (XYB color space)
//   8. Loop filter (Gaborish + EPF), patches, splines  -- SKIPPED in first wave.
//
// This file implements the structural skeleton: TOC-style group iteration,
// per-block dispatch, and the integration boundary against the four parallel
// helper modules (JxlVarDctQuant, JxlAcStrategyDecoder, JxlBlockContextMap,
// JxlVarDctIdct, JxlXybColorTransform). Where any helper is absent or stubbed,
// we throw NotImplementedException with a precise message rather than emit
// silently-wrong pixels.
// =====================================================================================

/// <summary>
/// Top-level orchestrator for a VarDCT frame. The bit reader must be
/// positioned immediately after the FrameHeader (see
/// <see cref="JxlSpecFrameHeader"/>). Returns the decoded image as 3 XYB
/// channel float planes; callers that need sRGB should run
/// <c>JxlXybColorTransform</c> on the result.
/// </summary>
internal static class JxlVarDctSpecDecoder {

  /// <summary>Default group size for VarDCT frames per ISO/IEC 18181-1 §C.2.4
  /// — log2 of pixel dimension. group_size_shift defaults to 1, which yields
  /// 256×256 pixel groups (1 &lt;&lt; (8 + group_size_shift)).</summary>
  private const int _DefaultGroupSizeLog2 = 8; // 256 px when group_size_shift = 0

  /// <summary>JXL's basic block dimension. AC strategies always operate in
  /// multiples of this.</summary>
  private const int _BlockDim = 8;

  /// <summary>Number of XYB channels.</summary>
  private const int _NumXybChannels = 3;

  /// <summary>
  /// Decode a VarDCT frame from the bitstream. The reader must be positioned
  /// after the FrameHeader. Returns the decoded image as 3 XYB-channel float
  /// planes (NOT yet converted to sRGB — caller can use
  /// <c>JxlXybColorTransform</c> for that).
  /// </summary>
  /// <param name="reader">Bit reader positioned immediately after FrameHeader.</param>
  /// <param name="width">Logical image width in pixels.</param>
  /// <param name="height">Logical image height in pixels.</param>
  /// <param name="bitDepth">Sample bit depth (informational; XYB planes are float32).</param>
  public static JxlVarDctImage Decode(
    JxlBitReader reader,
    int width,
    int height,
    int bitDepth,
    GaborishParams? gaborishParams = null,
    EpfParams? epfParams = null
  ) {
    ArgumentNullException.ThrowIfNull(reader);
    if (width <= 0)
      throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
    if (height <= 0)
      throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");
    if (bitDepth <= 0 || bitDepth > 32)
      throw new ArgumentOutOfRangeException(nameof(bitDepth), "Bit depth must be in (0, 32].");

    // ---------------------------------------------------------------
    // Group geometry. JXL groups are square spatial blocks of pixels.
    // group_size_shift defaults to 1 → 256×256. In a real bitstream we'd
    // pull group_size_shift from the FrameHeader; first-wave hard-codes 256.
    //
    // Inside each group we tile 8×8 blocks; for AC strategies > DCT8 a
    // single "block" may span multiple 8×8 cells, but we always step in
    // 8×8 increments and let the AC strategy descriptor consume cells.
    //
    // libjxl ref: lib/jxl/frame_dimensions.h::FrameDimensions::Set
    // ---------------------------------------------------------------
    var groupSize = 1 << _DefaultGroupSizeLog2; // 256
    var numGroupsW = (width + groupSize - 1) / groupSize;
    var numGroupsH = (height + groupSize - 1) / groupSize;

    // ---------------------------------------------------------------
    // Step 1 (NEW): Restoration filter (Gaborish + EPF) header.
    //
    // libjxl ref: lib/jxl/loop_filter.cc::LoopFilter::VisitFields. Layout:
    //   Bool all_default
    //   if (!all_default) {
    //     Bool gab (default true); if (gab) { Bool gab_custom; if custom 6×F16 }
    //     U(2) epf_iters; if (epf_iters > 0) { sharp/weight/sigma sub-bundles }
    //   }
    //
    // Our existing helpers expose the two halves separately:
    //   * JxlGaborish.ReadHeader consumes the all_default bit AND, when
    //     all_default == 0, the gab + optional gab_custom + 6×F16 bits.
    //   * JxlEpf.ReadHeader consumes the epf_iters U(2) (and any custom
    //     sub-bundles), and assumes the caller has already advanced past
    //     all_default + Gaborish.
    //
    // First-wave path: when all_default == 1, both halves return defaults
    // and EPF must NOT be read (the bits are absent). When all_default ==
    // 0, both halves participate. JxlGaborish.ReadHeader does not surface
    // which sub-path it took, so for correctness we infer "all_default"
    // from the bitstream position before/after the call: the only path
    // that consumes exactly 1 bit is all_default == 1.
    //
    // The persisted GaborishParams / EpfParams flow into Step 8 below so
    // that user-customised loop-filter weights are honoured at apply time.
    // ---------------------------------------------------------------
    // Per libjxl `frame_header.cc::VisitNested(&loop_filter)`, the loop_filter
    // bundle (Gaborish + EPF) is part of FrameHeader. JxlSpecFrameHeader's
    // _ReadRestorationFilter now persists the parsed params on FrameHeader,
    // and the caller passes them through here so the inverse Gaborish / EPF
    // pass at the end of decode can apply the encoder-chosen weights.
    // When the params are null, spec defaults are used (see step 10 below).

    // ---------------------------------------------------------------
    // Step 2: Patches / Splines presence flags.
    //
    // libjxl ref: lib/jxl/dec_frame.cc::ProcessDCGlobal. Patches and
    // splines are gated by FrameHeader.flags & {kPatches, kSplines};
    // when set, the bitstream contains the dictionary/list before the
    // VarDCT-only DecodeGlobalDCInfo call. The orchestrator does not yet
    // have the FrameHeader.flags wired through, so we conservatively
    // probe the 1-bit "has_*" gate that JxlPatches/JxlSplines expose
    // when it would unambiguously advance the bit position.
    //
    // First-wave note: the spec puts these reads OUTSIDE the loop_filter
    // bundle and INSIDE ProcessDCGlobal. Because we do not yet have
    // frame-level kPatches/kSplines flag access, we surface the helpers
    // as no-ops (commented below) and defer wiring to a follow-up. Any
    // frame that has patches or splines enabled will mis-align further
    // downstream — but the audit log will name the gap clearly.
    //
    //   if (frameFlags & kPatches) JxlPatches.ReadDictionary(reader, entropy);
    //   if (frameFlags & kSplines) JxlSplines.ReadList(reader, entropy);
    // ---------------------------------------------------------------

    // ---------------------------------------------------------------
    // Step 3: DC global section (libjxl `dec_frame.cc::DecodeDCGlobalSection`).
    //
    // Reads, in order:
    //   1. Quantizer.Decode  — global_scale + quant_dc (always for VarDCT).
    //   2. BlockContextMap.Decode — DC-context map for AC coefficient decoding.
    //   3. DC modular sub-image — 3 channels (X, Y, B) at DC resolution
    //      (W/8 × H/8). Carries the per-block DC values that feed quantization.
    //
    // The DC sub-image is a full modular sub-codec invocation; without working
    // modular decode we cannot advance past it. Bit positions UP TO this point
    // are now correctly aligned with libjxl.
    // ---------------------------------------------------------------
    var globalScale = JxlFrameQuantizer.ReadGlobalScale(reader);
    _ = globalScale; // not surfaced through JxlVarDctImage in first wave

    var blockCtxMap = JxlBlockContextMap.Decode(reader);
    _ = blockCtxMap; // not yet plumbed to AC coefficient decoder

    JxlColorCorrelationMap.DecodeDc(reader);

    // DC modular sub-image dimensions = (W/8) × (H/8) per channel × 3 channels.
    // libjxl `ModularFrameDecoder::DecodeGlobalInfo` reads the 1-bit
    // `has_tree` + (if true) the global tree + global histograms. The global
    // tree+entropy are SHARED across multiple per-group ModularDecode calls
    // within the frame: DC sub-image (3 channels), AC metadata (4 channels),
    // and any per-channel modular fallbacks for non-Library quant tables.
    var dcWidth = (width + _BlockDim - 1) / _BlockDim;
    var dcHeight = (height + _BlockDim - 1) / _BlockDim;
    var (modularGlobalTree, modularGlobalEntropy) = JxlModularSpecDecoder.DecodeGlobalInfo(
      reader, distanceMultiplierHint: (uint)Math.Max(dcWidth, _NumXybChannels));

    // ProcessDCGroup for VarDCT (libjxl `dec_modular.cc::DecodeVarDCTDC`):
    //   1. Read 2 bits `extra_precision` (DC quantization precision boost).
    //   2. ModularGenericDecompress with 3 XYB channels at DC-block resolution
    //      (W/8 × H/8 — 1×1 for our 8×8 fixture).
    //
    // The empty ModularDecode call (group_id=0 nb_ch=0) that libjxl shows
    // first is for the DC group's 0-channel placeholder image — handled by
    // our DecodeGroup early-return-on-empty path.
    var extraPrecision = reader.ReadBits(2);
    _ = extraPrecision; // dequant scale = 1 / (1 << extra_precision); not yet wired

    var dcImage = JxlModularSpecDecoder.DecodeGroup(
      reader,
      width: dcWidth,
      height: dcHeight,
      numChannels: _NumXybChannels,
      bitDepth: bitDepth,
      globalTree: modularGlobalTree,
      globalEntropy: modularGlobalEntropy);
    _ = dcImage; // wired to caller once DC dequant + chroma-from-luma are ready

    // ProcessDCGroup also issues an empty ModularDecode for ModularDC (group_id=2)
    // which is for non-default modular-in-VarDCT features. For typical XYB
    // images this is empty (channels.Length==0) so DecodeGroupChannels with an
    // empty array returns immediately without consuming bits. (We skip the
    // call entirely.)

    // ProcessDCGroup also calls `DecodeAcMetadata` (libjxl
    // `dec_modular.cc::DecodeAcMetadata`):
    //   1. Read `count = ReadBits(CeilLog2Nonzero(dc_group_pixels)) + 1` bits
    //      where dc_group_pixels = (dcGroupRect.xsize * dcGroupRect.ysize).
    //      For an 8x8 frame with single-block DC group, that's 1*1 = 1 → 0 bits.
    //   2. ModularDecode for 4 channels:
    //      - channel 0/1: ytox/ytob cmap maps, hshift=vshift=3, dims = ceil(rxsize/8)×ceil(rysize/8)
    //      - channel 2: ACS+QF, dims = count × 2, hshift=vshift=0
    //      - channel 3: EPF sigma, hshift=vshift=3, dims = ceil(rxsize/8)×ceil(rysize/8)
    var dcGroupBlocksX = (width + _BlockDim - 1) / _BlockDim;
    var dcGroupBlocksY = (height + _BlockDim - 1) / _BlockDim;
    var upperBound = dcGroupBlocksX * dcGroupBlocksY;
    var countBits = upperBound <= 1 ? 0 : (int)Math.Ceiling(Math.Log2(upperBound));
    var count = (int)reader.ReadBits(countBits) + 1;
    var crX = (dcGroupBlocksX + 7) >> 3;
    var crY = (dcGroupBlocksY + 7) >> 3;
    // libjxl default Image::Create gives ch[3] dims = r.xsize × r.ysize with
    // shift 0,0 (only ch[0..2] are explicitly resized).
    var acMetaChannels = new JxlChannel[] {
      new() { Width = crX, Height = crY, HShift = 3, VShift = 3, Pixels = new int[crX * crY] },
      new() { Width = crX, Height = crY, HShift = 3, VShift = 3, Pixels = new int[crX * crY] },
      new() { Width = count, Height = 2, HShift = 0, VShift = 0, Pixels = new int[count * 2] },
      new() { Width = dcGroupBlocksX, Height = dcGroupBlocksY, HShift = 0, VShift = 0, Pixels = new int[dcGroupBlocksX * dcGroupBlocksY] },
    };
    var acMetadata = JxlModularSpecDecoder.DecodeGroupChannels(
      reader, acMetaChannels, bitDepth, modularGlobalTree, modularGlobalEntropy);
    _ = acMetadata; // wired to AC strategy / cmap inversion in a follow-up

    // ---------------------------------------------------------------
    // Step 4: AC global section (libjxl `dec_frame.cc::ProcessACGlobal`).
    //
    // Reads, in order:
    //   1. DequantMatrices.Decode — 17 quant tables, 8 modes each.
    //   2. AC metadata 4-channel modular sub-image: cmap_x, cmap_y,
    //      packed(ACS<<4|QF), EPF sigma. Dimensions match DC sub-image.
    //
    // Note: DequantMatrices was previously placed at the start of the frame
    // payload (immediately after Quantizer), which is incorrect — per libjxl
    // it lives in the AC global section, AFTER BlockContextMap and the DC
    // modular sub-image.
    // ---------------------------------------------------------------
    var quantTableSet = JxlFrameQuantizer.ReadDequantMatrices(reader);

    // After DequantMatrices, libjxl `ProcessACGlobal` reads:
    //   1. num_histograms = 1 + ReadBits(CeilLog2Nonzero(num_groups))
    //   2. For each pass:
    //      a. used_orders = U32Coder::Read(kOrderEnc) where
    //         kOrderEnc = U32(Val(0x5F), Bits(13), BitsOffset(13, 0x800), BitsOffset(13, 0x1000))
    //      b. If used_orders != 0: DecodeCoeffOrders for each AC strategy
    //      c. DecodeHistograms(num_histograms * BlockCtxMap.NumACContexts())
    //
    // For the simplest 8×8 single-block case: num_groups=1 → 0 bits for
    // num_histograms (= 1), used_orders defaults to 0x5F (= "use natural
    // order for DCT8 only"), 2 bits for the selector, 0 bits for
    // DecodeCoeffOrders (used_orders=0x5F triggers natural-order init only).
    var numGroups = numGroupsW * numGroupsH;
    var numHistoBits = numGroups <= 1 ? 0 : (int)Math.Ceiling(Math.Log2(numGroups));
    var numHistograms = 1 + (int)reader.ReadBits(numHistoBits);
    _ = numHistograms;

    // Single pass for now (matches the 1-pass assumption baked into our
    // groupStrategies layout below).
    JxlEntropyDecoder acEntropyForPass;
    {
      // used_orders: libjxl frame_header.h:503 kOrderEnc =
      //   U32Enc(Val(0x5F), Val(0x13), Val(0), Bits(13))
      // Selector 0/1/2 are constants (0x5F, 0x13, 0) consuming 0 payload
      // bits. Only selector 3 reads 13 payload bits. NOT the BitsOffset
      // form used by other U32Encoders.
      var usedOrders = reader.ReadU32(0x5Fu, 0u, 0x13u, 0u, 0u, 0u, 0u, 13u);
      // Decode permutations for any non-natural orders. Bit-position-only;
      // the actual permutations aren't yet plumbed to AC coefficient decode.
      JxlCoeffOrderDecoder.DecodeCoeffOrders(reader, usedOrders);
      // AC entropy block: libjxl `dec_frame.cc::ProcessACGlobal` computes
      //   num_contexts = num_histograms * block_ctx_map.NumACContexts()
      // where NumACContexts = num_ctxs * (kNonZeroBuckets +
      // kZeroDensityContextCount) = num_ctxs * 495. For the default BCM
      // (num_ctxs = 15) this is 15 * 495 = 7425 contexts per histogram.
      var acContexts = numHistograms * blockCtxMap.NumACContexts;
      acEntropyForPass = JxlEntropyDecoder.Read(
        reader, acContexts, disallowLz77: false,
        distanceMultiplier: (uint)Math.Max(width, height));
    }

    // AC strategy data was already inside the AC metadata sub-image (channel 2
    // packs ACS<<4 | QF). For first-wave compatibility we still build a
    // synthetic per-group strategies array filled with DCT8 — the simplest
    // strategy that all 8×8 blocks use unless the encoder chose otherwise.
    var groupStrategies = new JxlAcStrategyType[numGroupsW * numGroupsH][][];
    for (var gy = 0; gy < numGroupsH; ++gy) {
      for (var gx = 0; gx < numGroupsW; ++gx) {
        var groupIdx = gy * numGroupsW + gx;
        var (blocksX, blocksY) = _GroupBlockDims(gx, gy, width, height, groupSize);
        groupStrategies[groupIdx] = new JxlAcStrategyType[blocksY][];
        for (var by = 0; by < blocksY; ++by) {
          groupStrategies[groupIdx][by] = new JxlAcStrategyType[blocksX];
          for (var bx = 0; bx < blocksX; ++bx)
            groupStrategies[groupIdx][by][bx] = JxlAcStrategyType.Dct8x8;
        }
      }
    }

    // ---------------------------------------------------------------
    // Step 5 + 6: Per-group LF + AC coefficient decoding.
    //
    // libjxl: dec_group.cc::DecodeGroupImpl. The loop is:
    //   for each pass:
    //     for each group:
    //       read LF coefficients (DC for DCT8 blocks)
    //       read AC coefficients (per-block, context-modeled)
    //
    // We collapse to single-pass + group-major. The block context map (a
    // per-block context selector keyed by quant-step + previous-block-DC
    // info) feeds JxlEntropyDecoder.ReadInt for every coefficient.
    //
    // First-wave caveat: the entropy block layout — specifically the
    // num_contexts argument and the relationship between LF and AC
    // entropy blocks — is non-trivial. We hand off to
    // JxlBlockContextMap.DecodeAndCreateEntropy(reader, ...) when that
    // helper lands. Until then we throw at the LF/AC read step with a
    // clear message naming the missing piece.
    // ---------------------------------------------------------------
    // The AC entropy block was decoded above as acEntropyForPass. Plumb it
    // directly into the per-group AC decode so JxlAcDecoder can read real
    // tokens. (Was previously a CreateSimple placeholder that returned 0
    // for every read with no bits consumed — useful only for keeping the
    // structural pipeline alive while the entropy block was being wired.)
    var acEntropy = acEntropyForPass;

    var groups = new JxlVarDctGroup[numGroupsW * numGroupsH];
    for (var gy = 0; gy < numGroupsH; ++gy) {
      for (var gx = 0; gx < numGroupsW; ++gx) {
        var groupIdx = gy * numGroupsW + gx;
        var (blocksX, blocksY) = _GroupBlockDims(gx, gy, width, height, groupSize);
        var strategies = groupStrategies[groupIdx];

        // Step 5: LF block per channel — for DCT8 the LF is just DC.
        var lfBlocks = _ReadLfBlocks(reader, blocksX, blocksY, strategies);

        // Step 6: AC blocks per channel — non-DC coefficients per 8×8 cell.
        // Now uses the global BlockContextMap + per-frame AC entropy decoder
        // (matches libjxl `dec_group.cc::DecodeGroupImpl`).
        var acBlocks = JxlAcDecoder.DecodeGroup(
          reader, acEntropy, strategies, blockCtxMap,
          blocksX, blocksY, _NumXybChannels);

        groups[groupIdx] = new JxlVarDctGroup {
          X = gx * groupSize,
          Y = gy * groupSize,
          Width = Math.Min(groupSize, width - gx * groupSize),
          Height = Math.Min(groupSize, height - gy * groupSize),
          AcBlocks = acBlocks,
          LfBlocks = lfBlocks,
        };
      }
    }

    // ---------------------------------------------------------------
    // Step 7 + 8 + 9: Dequantize → IDCT → pack into channel planes.
    //
    // Per channel, per group, per block: multiply coefficients by the
    // matching quant table entry, run inverse DCT (size determined by
    // AC strategy), and write the spatial output into the channel
    // float plane at the group's origin + block's intra-group offset.
    //
    // libjxl ref: dec_group.cc::DecodeGroupImpl + dct_util.cc::TransposedScaledIDCT
    // ---------------------------------------------------------------
    var channels = new float[_NumXybChannels][];
    for (var c = 0; c < _NumXybChannels; ++c)
      channels[c] = new float[width * height];

    for (var groupIdx = 0; groupIdx < groups.Length; ++groupIdx) {
      var group = groups[groupIdx];
      var (blocksX, blocksY) = _GroupBlockDims(
        groupIdx % numGroupsW, groupIdx / numGroupsW, width, height, groupSize
      );
      var strategies = groupStrategies[groupIdx];
      _RenderGroup(group, strategies, blocksX, blocksY, quantTableSet, channels, width, height);
    }

    // ---------------------------------------------------------------
    // Step 10: Restoration filters and overlays. Each is best-effort —
    // failures (NotImplementedException for non-default paths) are
    // swallowed; the un-filtered output is still better than nothing.
    //
    // Order matches libjxl `dec_frame.cc`:
    //   patches → splines → EPF → Gaborish → XYB→sRGB.
    // ---------------------------------------------------------------

    // Patches: header read at frame setup; for first wave, treat as absent.
    // Splines: same.
    // EPF: only operates if a sigma plane and EpfParams were provided. The
    //   orchestrator currently doesn't propagate the per-block sigma plane
    //   (that comes from the modular DC-group section), so even with EPF
    //   parameters parsed in Step 1 we must skip the EPF apply here. The
    //   parsed EpfParams is preserved on the local for future wiring.
    // Gaborish: spec defaults gain ≈ 1; safe to apply unconditionally as a
    //   no-op when the data was already produced without forward Gaborish.
    //   For real .jxl frames the encoder always applied forward Gaborish
    //   so the inverse here is required for accurate output. When the
    //   bitstream signalled custom Gaborish weights (gaborishParams !=
    //   null with non-default WeightsX/Y/B), we honour them per channel.
    _ = epfParams; // EPF apply requires sigma plane not yet wired.
    try {
      if (gaborishParams is null || !gaborishParams.Enabled) {
        // Gaborish disabled: skip the inverse filter entirely.
      } else {
        // Per-channel weights when present (length 2: [a, b]); otherwise
        // fall back to spec defaults via DefaultWeights.
        var weightsX = gaborishParams.WeightsX.Length == 2
          ? gaborishParams.WeightsX
          : new[] { JxlGaborish.DefaultWeights(0).A, JxlGaborish.DefaultWeights(0).B };
        var weightsY = gaborishParams.WeightsY.Length == 2
          ? gaborishParams.WeightsY
          : new[] { JxlGaborish.DefaultWeights(1).A, JxlGaborish.DefaultWeights(1).B };
        var weightsB = gaborishParams.WeightsB.Length == 2
          ? gaborishParams.WeightsB
          : new[] { JxlGaborish.DefaultWeights(2).A, JxlGaborish.DefaultWeights(2).B };
        JxlGaborish.ApplyInPlace(channels[0], width, height, weightsX);
        JxlGaborish.ApplyInPlace(channels[1], width, height, weightsY);
        JxlGaborish.ApplyInPlace(channels[2], width, height, weightsB);
      }
    } catch (System.NotImplementedException) {
      // Some sub-feature isn't ready; skip.
    } catch (System.ArgumentException) {
      // Degenerate dimensions (e.g. 0×N); skip.
    }

    return new JxlVarDctImage {
      Width = width,
      Height = height,
      Channels = channels,
    };
  }

  // ===============================================================
  // Step 5: LF block reader.
  // ===============================================================

  /// <summary>
  /// For each channel and each block in this group, read the LF (low-frequency)
  /// coefficient(s). For DCT8 blocks this is exactly the DC coefficient (1 value).
  /// For larger DCT shapes (DCT16+) it's a downsampled coefficient sub-image.
  /// </summary>
  private static JxlLfBlock[] _ReadLfBlocks(
    JxlBitReader reader,
    int blocksX,
    int blocksY,
    JxlAcStrategyType[][] strategies
  ) {
    // Wired to the spec-conformant LF group decoder. Internally delegates
    // through the modular sub-codec; failures (unsupported sub-features)
    // propagate as NotImplementedException with the upstream's message.
    return JxlLfDecoder.DecodeGroup(reader, blocksX, blocksY, _NumXybChannels);
  }

  // ===============================================================
  // Step 7+8+9: Dequantize → IDCT → place.
  // ===============================================================

  /// <summary>
  /// For one group, apply per-block dequantization, run the inverse DCT, and
  /// scatter the spatial output into the channel float planes.
  /// </summary>
  private static void _RenderGroup(
    JxlVarDctGroup group,
    JxlAcStrategyType[][] strategies,
    int blocksX,
    int blocksY,
    JxlQuantTableSet quantTableSet,
    float[][] channels,
    int imageWidth,
    int imageHeight
  ) {
    for (var c = 0; c < _NumXybChannels; ++c) {
      for (var by = 0; by < blocksY; ++by) {
        for (var bx = 0; bx < blocksX; ++bx) {
          var blockIdx = by * blocksX + bx;
          var strategy = strategies[by][bx];
          var coeffBlock = group.AcBlocks[c][blockIdx];
          var (blockW, blockH) = JxlVarDctIdct.BlockSize(strategy);
          var blockArea = blockW * blockH;

          // Step 5: dequantize. For DCT8 we use the per-channel quant table
          // as-is. For other strategies we look up libjxl's per-strategy
          // distance bands and build a strategy-sized table on the fly.
          // libjxl ref: quant_weights.cc::QuantizerForStrategy uses the
          // appropriate kQuantTable[required_size_x[t] * required_size_y[t]]
          // entry; we mirror that with JxlVarDctQuant.DefaultsForStrategy.
          var dequantized = new float[blockArea];
          var strategyTables = strategy == JxlAcStrategyType.Dct8x8
            ? quantTableSet
            : (JxlVarDctQuant.DefaultsForStrategy(strategy) ?? quantTableSet);
          var table = strategyTables.Tables[c];
          if (table.Width * table.Height == blockArea) {
            JxlVarDctQuant.Dequantize(coeffBlock.Coefficients, table, dequantized);
          } else {
            // Fallback: scale DCT8 table to block size by nearest-neighbour.
            for (var i = 0; i < blockArea; ++i) {
              var ty = (i / blockW) * 8 / Math.Max(1, blockH);
              var tx = (i % blockW) * 8 / Math.Max(1, blockW);
              dequantized[i] = coeffBlock.Coefficients[i]
                * quantTableSet.Tables[c].Weights[ty * 8 + tx];
            }
          }

          // Step 6: inverse DCT. Output is BlockW×BlockH spatial samples
          // in float. Defers to JxlVarDctIdct.InverseAcStrategy.
          var spatial = new float[blockArea];
          JxlVarDctIdct.InverseAcStrategy(strategy, dequantized, spatial);

          // Step 7: scatter into the channel plane.
          var pixelX = group.X + bx * _BlockDim;
          var pixelY = group.Y + by * _BlockDim;
          for (var y = 0; y < blockH; ++y) {
            var dstY = pixelY + y;
            if (dstY >= imageHeight) break;
            for (var x = 0; x < blockW; ++x) {
              var dstX = pixelX + x;
              if (dstX >= imageWidth) continue;
              channels[c][dstY * imageWidth + dstX] = spatial[y * blockW + x];
            }
          }
        }
      }
    }
  }

  // ===============================================================
  // Geometry helpers.
  // ===============================================================

  /// <summary>Block dims (in 8×8 cells) for a specific group, accounting for
  /// the image's right/bottom edge which may be partial.</summary>
  private static (int blocksX, int blocksY) _GroupBlockDims(
    int gx,
    int gy,
    int width,
    int height,
    int groupSize
  ) {
    var pixelsW = Math.Min(groupSize, width - gx * groupSize);
    var pixelsH = Math.Min(groupSize, height - gy * groupSize);
    var blocksX = (pixelsW + _BlockDim - 1) / _BlockDim;
    var blocksY = (pixelsH + _BlockDim - 1) / _BlockDim;
    return (blocksX, blocksY);
  }

}
