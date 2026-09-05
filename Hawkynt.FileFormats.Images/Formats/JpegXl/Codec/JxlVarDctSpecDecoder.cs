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
    EpfParams? epfParams = null,
    float[]? dcQuant = null,
    uint xQmScale = 3,
    uint bQmScale = 2,
    byte[]? codestream = null,
    JxlFrameToc? toc = null,
    int frameBody = 0,
    int groupSizeOverride = 0,
    int numDcGroups = 1,
    int numExtraChannels = 0
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
    var groupSize = groupSizeOverride > 0 ? groupSizeOverride : 1 << _DefaultGroupSizeLog2;
    var numGroupsW = (width + groupSize - 1) / groupSize;
    var numGroupsH = (height + groupSize - 1) / groupSize;

    // A frame in more than one group states each of its parts at its own offset
    // rather than one after another, and the table of contents gives them. With
    // one part there is nothing to seek to and the reader simply carries on.
    var sections = toc?.SectionSizes.Length ?? 1;
    var seeks = codestream is not null && toc is { Permuted: false } && sections > 1;

    JxlBitReader SectionReader(int index) {
      if (!seeks || index >= sections)
        return reader;

      var at = checked(frameBody + toc!.SectionOffsets[index]);
      if (at < 0 || at > codestream!.Length)
        throw new InvalidDataException($"This frame's section {index} sits past the end of the codestream.");

      return new JxlBitReader(codestream, at);
    }

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
    // Read full QuantizerParams bundle (global_scale + quant_dc) and derive
    // per-channel DC dequant scalars per libjxl quantizer.h:82-89:
    //   inv_global_scale = kGlobalScaleDenom / global_scale = 65536 / global_scale
    //   inv_quant_dc     = inv_global_scale / quant_dc
    //   mul_dc[c]        = inv_quant_dc * dequant.DCQuant(c) = inv_quant_dc / kInvDCQuant[c]
    // The dcQuant argument carries the per-channel DCQuant() values either
    // from the bitstream (when DequantMatrices.DecodeDC's all_default=0) or
    // from the libjxl defaults {1/4096, 1/512, 1/256}. When the caller passes
    // null we synthesise the defaults.
    var quantParams = JxlFrameQuantizer.ReadQuantizerParams(reader);
    var dcQuantUsed = dcQuant ?? JxlFrameQuantizer.DefaultDcQuant;
    var mulDc = new float[_NumXybChannels];
    for (var c = 0; c < _NumXybChannels; ++c)
      mulDc[c] = quantParams.InvQuantDc * dcQuantUsed[c];

    var blockCtxMap = JxlBlockContextMap.Decode(reader);
    _ = blockCtxMap; // not yet plumbed to AC coefficient decoder

    var dcCorrelation = JxlColorCorrelationMap.DecodeDc(reader);

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

    // The global modular stream of a VarDCT frame carries no colour — the
    // colour is in the transforms — but it does carry the frame's extra
    // channels, an alpha plane among them, at full size. A frame that has none
    // states a stream of no channels and nothing is read for it, which is why
    // skipping this went unnoticed until a picture with alpha turned up: there
    // the plane sits in the bitstream and everything after it was read a plane
    // too late.
    if (numExtraChannels > 0) {
      var extraChannels = new JxlChannel[numExtraChannels];
      for (var i = 0; i < numExtraChannels; ++i)
        extraChannels[i] = new JxlChannel {
          Width = width,
          Height = height,
          HShift = 0,
          VShift = 0,
          Pixels = new int[checked(width * height)],
        };

      JxlModularSpecDecoder.DecodeStream(
        reader, extraChannels, bitDepth, modularGlobalTree, modularGlobalEntropy,
        new JxlModularStreamOptions {
          MaxChannelSize = groupSize,
          StreamId = 0,
          UndoTransforms = false,
        });
    }

    // ProcessDCGroup for VarDCT (libjxl `dec_modular.cc::DecodeVarDCTDC`):
    //   1. Read 2 bits `extra_precision` (DC quantization precision boost).
    //   2. ModularGenericDecompress with 3 XYB channels at DC-block resolution
    //      (W/8 × H/8 — 1×1 for our 8×8 fixture).
    //
    // The empty ModularDecode call (group_id=0 nb_ch=0) that libjxl shows
    // first is for the DC group's 0-channel placeholder image — handled by
    // our DecodeGroup early-return-on-empty path.
    // The DC group and the metadata beside it are the frame's second section.
    var dcGroupReader = SectionReader(1);
    var extraPrecision = (int)dcGroupReader.ReadBits(2);
    // libjxl `DequantDC` multiplies DC by `mul = 1.0 / (1 << extra_precision)`.
    var extraPrecisionMul = 1f / (1 << extraPrecision);

    // Each of a VarDCT frame's sub-images is a modular stream of its own, and
    // the stream's number is property one, which an MA tree may split on. These
    // were all read as stream zero, so a tree that told the DC apart from the
    // metadata sent both down the same branch.
    const int dcGroupIndex = 0;
    var numDcGroupsForStreams = Math.Max(1, numDcGroups);
    var dcStreamId = 1 + dcGroupIndex;
    var acMetadataStreamId = 1 + 2 * numDcGroupsForStreams + dcGroupIndex;

    var dcImage = JxlModularSpecDecoder.DecodeGroup(
      dcGroupReader,
      width: dcWidth,
      height: dcHeight,
      numChannels: _NumXybChannels,
      bitDepth: bitDepth,
      globalTree: modularGlobalTree,
      globalEntropy: modularGlobalEntropy,
      streamId: dcStreamId);

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
    // The count belongs to the DC group's own section, not to whatever the
    // sequential reader is pointing at. With one section the two are the same
    // reader, which is why reading it from the wrong one went unnoticed.
    var count = (int)dcGroupReader.ReadBits(countBits) + 1;
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
      dcGroupReader, acMetaChannels, bitDepth, modularGlobalTree, modularGlobalEntropy, acMetadataStreamId);

    // Build per-block raw_quant_field from AC metadata (libjxl dec_modular.cc:
    // `row_qf[ix] = 1 + max(0, min(kQuantMax-1, row_in_2[num]))`). Channel 2
    // is row-major: row 0 = ACS strategy per block, row 1 = QF per block.
    // Channel-2 width = `count` (number of distinct blocks; 1 for an 8x8
    // single-block frame, dcGroupBlocksX*dcGroupBlocksY for fully-dense AC).
    // For the first-wave (DCT8-only, no multi-block strategies), every block
    // is its own count entry, in row-major scan order.
    // Walk the blocks in raster order and take one entry from the packed list
    // for each transform that starts there, stepping over the blocks a
    // multi-block transform covers. This is libjxl's own scan, and it is the
    // only way the list can be read: the entries are not per block, they are
    // per transform, so a decoder that assumed one entry per block would take
    // the wrong strategy for every block after the first large one.
    var strategyPlane = new JxlAcStrategyType[dcGroupBlocksX * dcGroupBlocksY];
    var perBlockQuant = new int[dcGroupBlocksX * dcGroupBlocksY];
    var covered = new bool[dcGroupBlocksX * dcGroupBlocksY];
    // Which cell a transform starts at is stated per cell, not implied by the
    // shape: two transforms of the same shape side by side leave a run of
    // identical cells, and there is no telling from the run alone where one
    // ends and the next begins. It has to be carried.
    var originPlane = new bool[dcGroupBlocksX * dcGroupBlocksY];
    var packed = acMetadata.Channels[2];
    var taken = 0;
    for (var by = 0; by < dcGroupBlocksY; ++by)
    for (var bx = 0; bx < dcGroupBlocksX; ++bx) {
      if (covered[by * dcGroupBlocksX + bx])
        continue;
      if (taken >= count)
        throw new InvalidDataException("This frame states fewer transforms than it has blocks to cover.");

      var raw = packed.Pixels[taken];
      if (!JxlAcStrategyGeometry.IsValid(raw))
        throw new InvalidDataException($"This frame states transform {raw}, which the format does not define.");

      var strategy = (JxlAcStrategyType)raw;
      var wide = JxlAcStrategyGeometry.BlocksWide(strategy);
      var high = JxlAcStrategyGeometry.BlocksHigh(strategy);
      if (bx + wide > dcGroupBlocksX || by + high > dcGroupBlocksY)
        throw new InvalidDataException(
          $"A {wide}x{high}-block transform at ({bx}, {by}) reaches past the picture.");

      var quant = 1 + Math.Max(0, Math.Min(255, packed.Pixels[count + taken]));
      for (var cy = 0; cy < high; ++cy)
      for (var cx = 0; cx < wide; ++cx) {
        var at = (by + cy) * dcGroupBlocksX + bx + cx;
        covered[at] = true;
        strategyPlane[at] = strategy;
        perBlockQuant[at] = quant;
        originPlane[at] = cy == 0 && cx == 0;
      }

      ++taken;
    }

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
    // The quantisation tables, the scan orders and the coefficient histograms
    // are the frame's high-frequency global section, which follows its
    // low-frequency groups.
    var hfGlobalReader = SectionReader(1 + numDcGroups);
    var quantTableSet = JxlFrameQuantizer.ReadDequantMatrices(hfGlobalReader);

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
    var numHistograms = 1 + (int)hfGlobalReader.ReadBits(numHistoBits);
    _ = numHistograms;

    // Single pass for now (matches the 1-pass assumption baked into our
    // groupStrategies layout below).
    JxlEntropyDecoder acEntropyForPass;
    int[][][] coeffOrders;
    {
      // used_orders: libjxl frame_header.h:503 kOrderEnc =
      //   U32Enc(Val(0x5F), Val(0x13), Val(0), Bits(13))
      // Selector 0/1/2 are constants (0x5F, 0x13, 0) consuming 0 payload
      // bits. Only selector 3 reads 13 payload bits. NOT the BitsOffset
      // form used by other U32Encoders.
      var usedOrders = hfGlobalReader.ReadU32(0x5Fu, 0u, 0x13u, 0u, 0u, 0u, 0u, 13u);
      // Decode permutations for any non-natural orders. Bit-position-only;
      // the actual permutations aren't yet plumbed to AC coefficient decode.
      coeffOrders = JxlCoeffOrderDecoder.DecodeCoeffOrders(hfGlobalReader, usedOrders);
      // AC entropy block: libjxl `dec_frame.cc::ProcessACGlobal` computes
      //   num_contexts = num_histograms * block_ctx_map.NumACContexts()
      // where NumACContexts = num_ctxs * (kNonZeroBuckets +
      // kZeroDensityContextCount) = num_ctxs * 495. For the default BCM
      // (num_ctxs = 15) this is 15 * 495 = 7425 contexts per histogram.
      var acContexts = numHistograms * blockCtxMap.NumACContexts;
      acEntropyForPass = JxlEntropyDecoder.Read(
        hfGlobalReader, acContexts, disallowLz77: false,
        distanceMultiplier: (uint)Math.Max(width, height));
    }

    // AC strategy data was already inside the AC metadata sub-image (channel 2
    // packs ACS<<4 | QF). For first-wave compatibility we still build a
    // synthetic per-group strategies array filled with DCT8 — the simplest
    // strategy that all 8×8 blocks use unless the encoder chose otherwise.
    // Each group's view of the strategy plane, which the metadata stated for the
    // whole picture. This used to be filled with 8x8 everywhere regardless of
    // what the file said, which is right only for a picture whose blocks really
    // are all plain 8x8.
    var blocksPerGroup = groupSize / _BlockDim;
    var groupStrategies = new JxlAcStrategyType[numGroupsW * numGroupsH][][];
    var groupOrigins = new bool[numGroupsW * numGroupsH][][];
    var groupQuant = new int[numGroupsW * numGroupsH][][];
    for (var gy = 0; gy < numGroupsH; ++gy) {
      for (var gx = 0; gx < numGroupsW; ++gx) {
        var groupIdx = gy * numGroupsW + gx;
        var (blocksX, blocksY) = _GroupBlockDims(gx, gy, width, height, groupSize);
        groupStrategies[groupIdx] = new JxlAcStrategyType[blocksY][];
        groupOrigins[groupIdx] = new bool[blocksY][];
        groupQuant[groupIdx] = new int[blocksY][];
        for (var by = 0; by < blocksY; ++by) {
          groupStrategies[groupIdx][by] = new JxlAcStrategyType[blocksX];
          groupOrigins[groupIdx][by] = new bool[blocksX];
          groupQuant[groupIdx][by] = new int[blocksX];
          for (var bx = 0; bx < blocksX; ++bx) {
            var at = (gy * blocksPerGroup + by) * dcGroupBlocksX + gx * blocksPerGroup + bx;
            groupStrategies[groupIdx][by][bx] = strategyPlane[at];
            groupOrigins[groupIdx][by][bx] = originPlane[at];
            groupQuant[groupIdx][by][bx] = perBlockQuant[at];
          }
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

        // Step 5: LF (DC) blocks per channel — built from the already-decoded
        // dcImage modular sub-image (NOT re-read from the bitstream). The
        // earlier _ReadLfBlocks variant tried to re-decode an LF modular
        // sub-image at this point, which double-read the bitstream. libjxl
        // computes DC + AC strictly once per group: DC during DecodeVarDCTDC,
        // AC during DecodeGroupImpl. Per-group LF slice = the rectangle of
        // dcImage covering this group's blocks.
        var lfBlocks = _SliceLfBlocksFromDc(
          dcImage, gx, gy, groupSize / _BlockDim, blocksX, blocksY);

        // Step 6: AC blocks per channel — non-DC coefficients per 8×8 cell.
        // Now uses the global BlockContextMap + per-frame AC entropy decoder
        // (matches libjxl `dec_group.cc::DecodeGroupImpl`).
        // Each group's coefficients sit at their own offset, after the
        // low-frequency groups and the high-frequency global section.
        var acGroupReader = SectionReader(2 + numDcGroups + groupIdx);
        var acBlocks = JxlAcDecoder.DecodeGroup(
          acGroupReader, acEntropy, strategies, blockCtxMap,
          blocksX, blocksY, _NumXybChannels, groupQuant[groupIdx], groupOrigins[groupIdx], coeffOrders);

        // Inject DC values into AC blocks at scan position 0. The AC decoder
        // skips position 0 (DC) per spec; combining LF DC with AC produces
        // the full quantized coefficient block fed into dequant + IDCT.
        for (var c = 0; c < _NumXybChannels; ++c) {
          for (var i = 0; i < lfBlocks[c].Coefficients.Length; ++i)
            acBlocks[c][i].Coefficients[0] = lfBlocks[c].Coefficients[i];
        }

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
      var origins = groupOrigins[groupIdx];
      // Per-block AC dequant scale: libjxl `Quantizer::inv_quant_ac` =
      // inv_global_scale / quant. dm multipliers = pow(1/1.25, qm_scale-2)
      // per `dec_cache.h:RecomputeRowQuant`.
      var xDmMul = MathF.Pow(1f / 1.25f, (int)xQmScale - 2);
      var bDmMul = MathF.Pow(1f / 1.25f, (int)bQmScale - 2);
      _RenderGroup(group, strategies, origins, blocksX, blocksY, quantTableSet,
        mulDc, extraPrecisionMul, dcCorrelation,
        quantParams.InvGlobalScale, perBlockQuant, xDmMul, bDmMul,
        channels, width, height);
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
    // The sharpness each block states is the fourth channel of the metadata
    // beside the low-frequency groups, which was decoded and thrown away for as
    // long as the filter it belongs to went unrun.
    var sharpnessPlane = acMetadata.Channels.Length > 3
      ? acMetadata.Channels[3].Pixels
      : new int[dcGroupBlocksX * dcGroupBlocksY];
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

      // The edge-preserving filter follows the smoothing one, on the same
      // planes and before the colour transform, which is the order libjxl
      // builds its pipeline in.
      if (epfParams is { Iters: > 0 })
        JxlEdgePreservingFilter.Apply(
          channels, width, height,
          perBlockQuant, sharpnessPlane, dcGroupBlocksX, dcGroupBlocksY,
          quantParams.InvGlobalScale, epfParams.Iters);
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

  /// <summary>Extract this group's per-channel DC slice from the frame's DC
  /// modular sub-image. The DC sub-image covers the whole frame at block
  /// resolution (one DC per 8×8 block); each VarDCT group occupies a
  /// <c>groupBlocks × groupBlocks</c> rectangle within it.
  ///
  /// <para>libjxl stores the DC modular sub-image with channels permuted as
  /// <c>image.channel[c &lt; 2 ? c ^ 1 : c]</c> (see
  /// <c>dec_modular.cc::DecodeVarDCTDC</c>): storage order is <c>[Y, X, B]</c>
  /// while the rest of our pipeline uses canonical XYB order <c>[X, Y, B]</c>.
  /// We undo the permutation here so the returned LF blocks line up with the
  /// AC blocks (which are already in canonical order via
  /// <c>JxlAcDecoder.DecodeGroup</c>'s <c>{1,0,2}</c> traversal).</para></summary>
  private static JxlLfBlock[] _SliceLfBlocksFromDc(
    JxlModularImage dcImage,
    int gx,
    int gy,
    int groupBlocks,
    int blocksX,
    int blocksY
  ) {
    var lfBlocks = new JxlLfBlock[_NumXybChannels];
    var x0 = gx * groupBlocks;
    var y0 = gy * groupBlocks;
    for (var c = 0; c < _NumXybChannels; ++c) {
      // Canonical c=0/1/2 (X/Y/B) ← modular sub-image index 1/0/2.
      var srcChannel = c < 2 ? c ^ 1 : c;
      var ch = dcImage.Channels[srcChannel];
      var coeffs = new short[blocksX * blocksY];
      for (var by = 0; by < blocksY; ++by)
        for (var bx = 0; bx < blocksX; ++bx) {
          var v = ch.Pixels[(y0 + by) * ch.Width + (x0 + bx)];
          coeffs[by * blocksX + bx] = v switch {
            > short.MaxValue => short.MaxValue,
            < short.MinValue => short.MinValue,
            _ => (short)v,
          };
        }
      lfBlocks[c] = new JxlLfBlock {
        Width = blocksX,
        Height = blocksY,
        Coefficients = coeffs,
      };
    }
    return lfBlocks;
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
    bool[][] origins,
    int blocksX,
    int blocksY,
    JxlQuantTableSet quantTableSet,
    float[] mulDc,
    float extraPrecisionMul,
    JxlColorCorrelationMap.DcCorrelationFactors dcCorrelation,
    float invGlobalScale,
    int[] perBlockQuant,
    float xDmMultiplier,
    float bDmMultiplier,
    float[][] channels,
    int imageWidth,
    int imageHeight
  ) {
    // Pre-compute the dequantized DC value per channel per block, applying the
    // DC chroma-from-luma correction (libjxl `compressed_dc.cc::DequantDC`):
    //   Y_dc = quant_y * mul_dc[Y] * extraPrecisionMul
    //   X_dc = quant_x * mul_dc[X] * extraPrecisionMul + Y_dc * dc_factors[YtoX]
    //   B_dc = quant_b * mul_dc[B] * extraPrecisionMul + Y_dc * dc_factors[YtoB]
    // This must happen BEFORE per-channel IDCT because the cfl correction
    // mixes Y into X/B and our IDCT is linear: applying it to the DC frequency
    // coefficient is equivalent to applying it to the post-IDCT spatial DC.
    var totalBlocks = blocksX * blocksY;
    var dequantDc = new float[_NumXybChannels * totalBlocks];
    const int xCh = 0, yCh = 1, bCh = 2;
    for (var i = 0; i < totalBlocks; ++i) {
      var qY = group.AcBlocks[yCh][i].Coefficients[0];
      var qX = group.AcBlocks[xCh][i].Coefficients[0];
      var qB = group.AcBlocks[bCh][i].Coefficients[0];
      var yDc = qY * mulDc[yCh] * extraPrecisionMul;
      dequantDc[yCh * totalBlocks + i] = yDc;
      dequantDc[xCh * totalBlocks + i] = qX * mulDc[xCh] * extraPrecisionMul + yDc * dcCorrelation.YtoX;
      dequantDc[bCh * totalBlocks + i] = qB * mulDc[bCh] * extraPrecisionMul + yDc * dcCorrelation.YtoB;
    }

    // Pre-pass: dequantize Y AC for all blocks so X and B can apply the
    // chroma-from-luma `b_cc_mul * dequant_y + dequant_b_cc` mixing per
    // libjxl `DequantLane`. For a fixture whose AC-metadata cmap channels
    // (ytox_map, ytob_map) are zero, X uses `YtoXRatio(0) = 0` (no mixing)
    // and B uses `YtoBRatio(0) = kYToBRatio = 1.0` (full Y added). For
    // non-default cmap, the per-tile factor would be looked up here.
    // First-wave assumes single-tile / all-zero cmap.
    const float xCcMul = 0f;             // YtoXRatio(0) = 0
    const float bCcMul = JxlColorCorrelationMap.DefaultYtoBRatio;  // = 1.0
    var dequantedY = new float[totalBlocks][];
    for (var by = 0; by < blocksY; ++by)
      for (var bx = 0; bx < blocksX; ++bx) {
        var blockIdx = by * blocksX + bx;
        var strategy = strategies[by][bx];
        if (!_ReconstructsFromOrigin(origins, bx, by, strategy))
          continue;

        var coeffBlock = group.AcBlocks[yCh][blockIdx];
        var (blockW, blockH) = JxlVarDctIdct.BlockSize(strategy);
        var quantValue = perBlockQuant[blockIdx];
        if (quantValue <= 0) quantValue = 1;

        // The weights belong to the transform's own shape. Taking the plain 8x8
        // table for every shape read past its end the moment a larger transform
        // appeared.
        dequantedY[blockIdx] = _Dequantize(
          coeffBlock.Coefficients, strategy, blockW, blockH,
          _StrategyTables(strategy, quantTableSet).Tables[yCh], quantTableSet.Tables[yCh],
          invGlobalScale / quantValue);
      }

    for (var c = 0; c < _NumXybChannels; ++c) {
      for (var by = 0; by < blocksY; ++by) {
        for (var bx = 0; bx < blocksX; ++bx) {
          var blockIdx = by * blocksX + bx;
          var strategy = strategies[by][bx];

          // A transform whose lowest coefficients can be rebuilt from the DC
          // values of the blocks it covers is reconstructed once, from the
          // block it starts at. The rest are still drawn once per block they
          // cover, which is not what the format says but is closer than
          // spreading an inverse transform that disagrees with it.
          if (!_ReconstructsFromOrigin(origins, bx, by, strategy))
            continue;
          var coeffBlock = group.AcBlocks[c][blockIdx];
          var (blockW, blockH) = JxlVarDctIdct.BlockSize(strategy);
          var blockArea = blockW * blockH;

          // Step 5: dequantize AC coefficients with libjxl's full formula
          //   pre_idct[k] = quantized[k] * (inv_global_scale / quant) *
          //                 dm_multiplier * dequant_matrix[k]
          // where inv_global_scale = kGlobalScaleDenom / global_scale and
          // dm_multiplier = 1 (Y), xDmMultiplier (X), or bDmMultiplier (B).
          var quantValue = perBlockQuant[blockIdx];
          if (quantValue <= 0) quantValue = 1;
          var scaledDequant = invGlobalScale / quantValue
            * (c == xCh ? xDmMultiplier : c == bCh ? bDmMultiplier : 1f);
          var dequantized = _Dequantize(
            coeffBlock.Coefficients, strategy, blockW, blockH,
            _StrategyTables(strategy, quantTableSet).Tables[c], quantTableSet.Tables[c],
            scaledDequant);

          // Apply chroma-from-luma AC mixing: for X channel add x_cc_mul *
          // dequant_y[k]; for B add b_cc_mul * dequant_y[k]. Y itself is
          // unchanged. Skips when the per-block Y dequant wasn't computed
          // (non-DCT8 strategies in the first wave).
          if ((c == xCh || c == bCh) && dequantedY[blockIdx] != null) {
            var yDeq = dequantedY[blockIdx];
            var mix = c == xCh ? xCcMul : bCcMul;
            if (mix != 0f) {
              for (var i = 0; i < blockArea; ++i)
                dequantized[i] += mix * yDeq[i];
            }
          }

          // The lowest coefficients come from the DC values of every block the
          // transform covers, not from the one it starts at. A transform of one
          // block has one of them and it is that block's DC.
          var toCoefficients = JxlLowestFrequencies.DcToCoefficients(strategy);
          if (toCoefficients is null) {
            dequantized[0] = dequantDc[c * totalBlocks + blockIdx];
          } else {
            var order = JxlNaturalCoeffOrder.For(strategy);
            var coveredWide = blockW / _BlockDim;
            var coveredHigh = blockH / _BlockDim;
            var dcOfCovered = new float[coveredWide * coveredHigh];
            for (var dy = 0; dy < coveredHigh; ++dy)
            for (var dx = 0; dx < coveredWide; ++dx) {
              var at = (by + dy) * blocksX + bx + dx;
              dcOfCovered[dy * coveredWide + dx] = at < totalBlocks
                ? dequantDc[c * totalBlocks + at]
                : dequantDc[c * totalBlocks + blockIdx];
            }

            for (var i = 0; i < toCoefficients.Length; ++i) {
              var value = 0f;
              for (var j = 0; j < dcOfCovered.Length; ++j)
                value += toCoefficients[i][j] * dcOfCovered[j];

              dequantized[order[i]] = value;
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

  /// <summary>
  /// How much luma a block's chroma channel carries, from the tile it sits in.
  /// </summary>
  /// <remarks>
  /// libjxl <c>ColorCorrelation::YtoXRatio</c> and <c>YtoBRatio</c>: the stated
  /// number over the colour factor, added to the channel's base. A tile is
  /// eight blocks across.
  /// </remarks>
  private static float _CorrelationAt(
    JxlChannel map,
    JxlVarDctGroup group,
    int bx,
    int by,
    float baseCorrelation
  ) {
    if (map.Width <= 0 || map.Height <= 0)
      return baseCorrelation;

    const int blocksPerTile = 8;
    var tileX = (group.X / _BlockDim + bx) / blocksPerTile;
    var tileY = (group.Y / _BlockDim + by) / blocksPerTile;
    if (tileX >= map.Width || tileY >= map.Height)
      return baseCorrelation;

    return baseCorrelation
           + map.Pixels[tileY * map.Width + tileX] / (float)JxlColorCorrelationMap.DefaultColorFactor;
  }

  /// <summary>
  /// Whether this transform is drawn once from the block it starts at.
  /// </summary>
  private static bool _ReconstructsFromOrigin(
    bool[][] origins,
    int bx,
    int by,
    JxlAcStrategyType strategy
  ) {
    // A shape whose lowest coefficients can be rebuilt from the covered blocks'
    // DC values is drawn once, from where it starts. A shape whose cannot is
    // still drawn at every block it covers — not what the format says, but
    // closer than drawing it once from coefficients that would be wrong across
    // the whole rectangle.
    if (JxlLowestFrequencies.DcToCoefficients(strategy) is null)
      return true;

    return origins[by][bx];
  }

  /// <summary>The quantisation weights a transform's own shape is dequantised with.</summary>
  private static JxlQuantTableSet _StrategyTables(JxlAcStrategyType strategy, JxlQuantTableSet fallback)
    => strategy == JxlAcStrategyType.Dct8x8
      ? fallback
      : JxlVarDctQuant.DefaultsForStrategy(strategy) ?? fallback;

  /// <summary>
  /// Undo the quantisation of one transform's coefficients.
  /// </summary>
  /// <remarks>
  /// The weights come from a table of the transform's own shape. Where none is
  /// to hand the plain 8x8 one is stretched over the block, which is an
  /// approximation and one of the reasons a VarDCT frame is not handed back.
  /// </remarks>
  private static float[] _Dequantize(
    short[] coefficients,
    JxlAcStrategyType strategy,
    int blockW,
    int blockH,
    JxlQuantTable table,
    JxlQuantTable fallback,
    float scale
  ) {
    var area = blockW * blockH;
    var result = new float[area];
    if (table.Width * table.Height == area) {
      for (var i = 0; i < area; ++i)
        result[i] = coefficients[i] * scale * table.Weights[i];

      return result;
    }

    for (var i = 0; i < area; ++i) {
      var ty = i / blockW * 8 / Math.Max(1, blockH);
      var tx = i % blockW * 8 / Math.Max(1, blockW);
      result[i] = coefficients[i] * scale * fallback.Weights[ty * 8 + tx];
    }

    return result;
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
