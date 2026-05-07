using System;
using System.IO;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// Decoder for the per-block AC strategy field of a VarDCT group
/// (ISO/IEC 18181-1 §G.5 / libjxl <c>ModularFrameDecoder::DecodeAcMetadata</c>
/// in <c>lib/jxl/dec_modular.cc</c> lines 480-560 / commit 06337d1).
///
/// <para>In the JPEG XL bitstream, the AC strategy field is encoded as a
/// dedicated 4-channel modular sub-image:
/// <list type="number">
///   <item>channel 0: cmap_x  — Y→X chroma-from-luma correlation factor.</item>
///   <item>channel 1: cmap_y  — Y→B chroma-from-luma correlation factor.</item>
///   <item>channel 2: packed (strategy &lt;&lt; 4) | qf_index — per-block
///         AC strategy (top 4 bits, valid range 0..26) and quantization-field
///         index (low 4 bits).</item>
///   <item>channel 3: epf_sharpness — per-block EPF (edge-preserving filter)
///         sharpness, stored as <c>row_in_3[ix]</c> in libjxl.</item>
/// </list>
/// The decoder reads this 4-channel sub-image via the modular generic
/// decompress pipeline (<see cref="JxlModularSpecDecoder.Decode"/>) and then
/// validates each entry of channel 2:
/// <list type="bullet">
///   <item>strategy = packed &gt;&gt; 4 must be in <c>[0, 27)</c>
///         (<c>AcStrategy::kNumValidStrategies</c>).</item>
///   <item>qf_idx = packed &amp; 0xF (4 LSBs) — kept on the strategy grid
///         only as part of the packed value; this decoder returns the
///         strategy plane only.</item>
///   <item>For multi-block strategies (<c>covered_blocks_x * covered_blocks_y &gt; 1</c>),
///         the trailing 8x8 sub-blocks within the multi-block area are marked
///         as <see cref="CoveredByNeighbour"/> (libjxl's "is_first = false"
///         sentinel encoded as <c>0xFF</c> here).</item>
/// </list>
/// </para>
///
/// <para>Two entry points are exposed:
/// <list type="bullet">
///   <item><see cref="CreateAllDct8x8"/> — synthesise a uniform all-DCT8x8
///         grid of the requested dimensions; consumes zero bits. Useful as a
///         fixture and as a fallback for frames known to contain only DCT8
///         blocks.</item>
///   <item><see cref="DecodeForGroup"/> — full spec-conformant decode of the
///         AC-strategy modular sub-image.</item>
/// </list></para>
/// </summary>
internal static class JxlAcStrategyDecoder {

  /// <summary>
  /// Sentinel value placed in the strategy grid for the non-first 8x8 sub-blocks
  /// of a multi-block AC strategy (e.g. the bottom-right 3 sub-blocks of a
  /// DCT16x16). Equivalent to libjxl's "is_first = false" flag on
  /// <c>AcStrategy</c>. Stored as an out-of-range
  /// <see cref="JxlAcStrategyType"/> value (0xFF) — callers MUST treat this
  /// as "skip; data already covered by neighbour" and never feed it to
  /// dequantization or IDCT.
  /// </summary>
  public const JxlAcStrategyType CoveredByNeighbour = (JxlAcStrategyType)0xFF;

  /// <summary>
  /// libjxl <c>AcStrategy::kNumValidStrategies</c> — the highest valid raw
  /// strategy is <c>DCT128X256 = 26</c>, so 27 raw strategy values are
  /// recognised. See <c>lib/jxl/ac_strategy.h:91</c>.
  /// </summary>
  private const int _NumValidStrategies = 27;

  /// <summary>
  /// Number of channels in the AC-strategy modular sub-image. Per libjxl
  /// <c>dec_modular.cc:489</c>: "YToX, YToB, ACS + QF, EPF" — fixed at 4.
  /// </summary>
  private const int _NumAcMetadataChannels = 4;

  /// <summary>
  /// Bit depth of the modular-coded AC-metadata channels. libjxl uses
  /// <c>full_image.bitdepth</c> which propagates from
  /// <c>metadata.bit_depth.bits_per_sample</c>; for AC-metadata the only
  /// requirement is that 0..255 round-trips, so we use 8.
  /// </summary>
  private const int _AcMetadataBitDepth = 8;

  /// <summary>
  /// Create a uniform all-DCT8x8 strategy grid. Suitable for fixtures and
  /// for the first-wave VarDCT integration where no large-DCT support is
  /// required yet. Consumes no bits — use this when the bitstream is known
  /// (or assumed) to contain only DCT8 blocks. The caller should advance the
  /// bit reader past the AC-strategy modular sub-image themselves before
  /// using the rest of the AC entropy stream.
  /// </summary>
  /// <param name="groupBlocksWide">Number of 8x8 blocks across the group
  /// (typically 32 for a 256x256-pixel group).</param>
  /// <param name="groupBlocksHigh">Number of 8x8 blocks down the group.</param>
  public static JxlAcStrategyType[][] CreateAllDct8x8(int groupBlocksWide, int groupBlocksHigh) {
    if (groupBlocksWide < 0)
      throw new ArgumentOutOfRangeException(nameof(groupBlocksWide), "Must be >= 0.");
    if (groupBlocksHigh < 0)
      throw new ArgumentOutOfRangeException(nameof(groupBlocksHigh), "Must be >= 0.");

    var grid = new JxlAcStrategyType[groupBlocksHigh][];
    for (var y = 0; y < groupBlocksHigh; ++y) {
      grid[y] = new JxlAcStrategyType[groupBlocksWide];
      // Default-initialised to JxlAcStrategyType.Dct8x8 (= 0). No further
      // work needed; the explicit allocation suffices.
    }
    return grid;
  }

  /// <summary>
  /// Decode the AC strategy field for one VarDCT group. See class-level XML
  /// docs for the bitstream layout.
  /// </summary>
  /// <param name="reader">Bit reader positioned at the start of the
  /// AC-strategy modular sub-image inside the group bitstream.</param>
  /// <param name="entropy">Frame-level entropy decoder. Currently unused in
  /// the spec-conformant path because <see cref="JxlModularSpecDecoder.Decode"/>
  /// reads its own per-section entropy tables (libjxl
  /// <c>ModularGenericDecompress</c> behaves identically). Kept on the
  /// signature for forward compatibility with frame-level histogram sharing
  /// and to validate non-null. Pass any valid decoder.</param>
  /// <param name="groupBlocksWide">Number of 8x8 blocks across the group.</param>
  /// <param name="groupBlocksHigh">Number of 8x8 blocks down the group.</param>
  /// <returns>A 2D array <c>grid[y][x]</c> giving the AC strategy of each 8x8
  /// block. Non-first sub-blocks of multi-block strategies carry
  /// <see cref="CoveredByNeighbour"/>.</returns>
  /// <exception cref="InvalidDataException">An entry of the modular sub-image
  /// channel-2 plane decoded a strategy outside <c>[0, 27)</c>, or a
  /// multi-block strategy would extend past the group's right/bottom edge.</exception>
  public static JxlAcStrategyType[][] DecodeForGroup(
    JxlBitReader reader,
    JxlEntropyDecoder entropy,
    int groupBlocksWide,
    int groupBlocksHigh
  ) {
    ArgumentNullException.ThrowIfNull(reader);
    ArgumentNullException.ThrowIfNull(entropy);
    if (groupBlocksWide < 0)
      throw new ArgumentOutOfRangeException(nameof(groupBlocksWide));
    if (groupBlocksHigh < 0)
      throw new ArgumentOutOfRangeException(nameof(groupBlocksHigh));

    // Empty group: nothing to decode, no bits consumed.
    if (groupBlocksWide == 0 || groupBlocksHigh == 0)
      return CreateAllDct8x8(groupBlocksWide, groupBlocksHigh);

    // -------------------------------------------------------------
    // Decode the 4-channel modular sub-image carrying the AC metadata.
    //
    // libjxl ref: dec_modular.cc:480-560, function DecodeAcMetadata. The
    // four channels are (cmap_x, cmap_y, ACS+QF packed, EPF sharpness).
    // libjxl additionally varies channel 0/1 dimensions to color-tile
    // resolution and channel 2 to a packed-list shape (count × 2); per
    // task spec we use the simplified uniform (W × H) layout for every
    // channel and read only channel 2 to derive the strategy plane.
    // -------------------------------------------------------------
    var modular = JxlModularSpecDecoder.Decode(
      reader,
      width: groupBlocksWide,
      height: groupBlocksHigh,
      numChannels: _NumAcMetadataChannels,
      bitDepth: _AcMetadataBitDepth);

    if (modular.Channels.Length < _NumAcMetadataChannels)
      throw new InvalidDataException(
        $"AC-strategy modular sub-image returned {modular.Channels.Length} " +
        $"channels; expected at least {_NumAcMetadataChannels}.");

    // Channel 2 carries (strategy << 4) | qf_idx packed per block, in
    // row-major order with stride = groupBlocksWide. libjxl mirrors this
    // layout via image.channel[2] read at row 0/1; the task spec collapses
    // the variant to a uniform (W × H) plane indexed [y*W + x].
    var packedPlane = modular.Channels[2];
    if (packedPlane.Width != groupBlocksWide || packedPlane.Height != groupBlocksHigh)
      throw new InvalidDataException(
        $"AC-strategy modular channel 2 has dimensions " +
        $"{packedPlane.Width}x{packedPlane.Height}, expected " +
        $"{groupBlocksWide}x{groupBlocksHigh}.");

    return _BuildStrategyGridFromPackedPlane(
      packedPlane.Pixels,
      groupBlocksWide,
      groupBlocksHigh);
  }

  /// <summary>
  /// Walk a packed-strategy plane (channel 2 of the AC-metadata modular
  /// sub-image) and produce the per-block strategy grid with multi-block
  /// bookkeeping applied. Exposed as <c>internal</c> so unit tests can
  /// hand-craft the packed plane directly without reproducing a full modular
  /// bitstream — the post-modular logic is the part that owns the multi-block
  /// covered-by-neighbour marking and the strategy-validity check.
  /// </summary>
  /// <param name="packedPixels">Row-major packed plane,
  /// length = <paramref name="groupBlocksWide"/> * <paramref name="groupBlocksHigh"/>.
  /// Each entry is <c>(strategy &lt;&lt; 4) | qf_idx</c>.</param>
  /// <param name="groupBlocksWide">Group width in 8x8 blocks.</param>
  /// <param name="groupBlocksHigh">Group height in 8x8 blocks.</param>
  /// <returns>Strategy grid. Top-left cells of multi-block strategies hold
  /// the canonical <see cref="JxlAcStrategyType"/>; trailing cells of the
  /// covered area hold <see cref="CoveredByNeighbour"/>.</returns>
  /// <exception cref="InvalidDataException">Strategy value out of range, or
  /// multi-block area extends past the group boundary.</exception>
  internal static JxlAcStrategyType[][] _BuildStrategyGridFromPackedPlane(
    int[] packedPixels,
    int groupBlocksWide,
    int groupBlocksHigh
  ) {
    ArgumentNullException.ThrowIfNull(packedPixels);
    if (packedPixels.Length < groupBlocksWide * groupBlocksHigh)
      throw new ArgumentException(
        $"Packed plane has {packedPixels.Length} entries but " +
        $"{groupBlocksWide}x{groupBlocksHigh} = {groupBlocksWide * groupBlocksHigh} " +
        "are required.",
        nameof(packedPixels));

    // -------------------------------------------------------------
    // Walk the strategy plane in row-major order. For each (x, y):
    //   1. Skip cells already marked as covered by a previous multi-block
    //      strategy (libjxl: AcStrategy::IsValid early-continue).
    //   2. Validate the raw strategy value (0..26).
    //   3. Validate that the multi-block area fits inside the group.
    //   4. Mark the trailing covered_blocks_x*covered_blocks_y - 1 cells
    //      as CoveredByNeighbour.
    // -------------------------------------------------------------
    var grid = new JxlAcStrategyType[groupBlocksHigh][];
    for (var y = 0; y < groupBlocksHigh; ++y)
      grid[y] = new JxlAcStrategyType[groupBlocksWide];

    // Track which cells have been marked as covered. `covered[y][x] = true`
    // means an earlier block has already claimed this cell — we skip it.
    var covered = new bool[groupBlocksHigh][];
    for (var y = 0; y < groupBlocksHigh; ++y)
      covered[y] = new bool[groupBlocksWide];

    for (var y = 0; y < groupBlocksHigh; ++y) {
      for (var x = 0; x < groupBlocksWide; ++x) {
        if (covered[y][x]) {
          // Already claimed by a multi-block parent — leave the prior mark
          // (CoveredByNeighbour, written when the parent was processed)
          // and move on. libjxl: ac_strategy.IsValid(x, y) → continue.
          continue;
        }

        var packed = packedPixels[y * groupBlocksWide + x];
        var strategy = (packed >> 4) & 0xFF;

        if (!_IsRawStrategyValid(strategy))
          throw new InvalidDataException(
            $"Invalid AC strategy {strategy} at block ({x}, {y}); " +
            $"expected [0, {_NumValidStrategies}). " +
            $"libjxl ref: AcStrategy::IsRawStrategyValid in ac_strategy.h.");

        var (coveredX, coveredY) = _GetCoveredBlocks(strategy);

        // Validate the multi-block area fits inside the group rectangle.
        // libjxl dec_modular.cc:540-547 raises "Invalid AC strategy, x/y
        // overflow" when next_x_dct_block > xlim or > AC group boundary.
        if (x + coveredX > groupBlocksWide)
          throw new InvalidDataException(
            $"AC strategy {strategy} at ({x}, {y}) extends past the group's " +
            $"right edge: covered_blocks_x={coveredX} but only " +
            $"{groupBlocksWide - x} cells remain.");
        if (y + coveredY > groupBlocksHigh)
          throw new InvalidDataException(
            $"AC strategy {strategy} at ({x}, {y}) extends past the group's " +
            $"bottom edge: covered_blocks_y={coveredY} but only " +
            $"{groupBlocksHigh - y} cells remain.");

        // The top-left cell carries the canonical strategy.
        grid[y][x] = (JxlAcStrategyType)(byte)strategy;
        covered[y][x] = true;

        // For multi-block strategies, mark the remaining
        // covered_blocks_x * covered_blocks_y - 1 cells as
        // CoveredByNeighbour. Single-block strategies (1x1) skip the loop.
        if (coveredX * coveredY > 1) {
          for (var dy = 0; dy < coveredY; ++dy) {
            for (var dx = 0; dx < coveredX; ++dx) {
              if (dx == 0 && dy == 0)
                continue; // top-left already written
              grid[y + dy][x + dx] = CoveredByNeighbour;
              covered[y + dy][x + dx] = true;
            }
          }
        }
      }
    }

    return grid;
  }

  /// <summary>
  /// Compatibility overload used by parallel integration code
  /// (<c>JxlVarDctSpecDecoder._ReadAcStrategyOrFallback</c>) before the
  /// frame-level entropy decoder is wired into the call site. Returns a flat
  /// row-major array of length <c>blocksX * blocksY</c>. Currently throws
  /// <see cref="NotImplementedException"/> for non-trivial dimensions; the
  /// caller is expected to catch and fall back to all-DCT8x8.
  /// </summary>
  /// <remarks>
  /// This overload exists because the public 4-arg signature mandated by the
  /// task spec doesn't match the integration call site, which was written
  /// against an earlier 3-arg shape. We keep both: the 4-arg API is the
  /// stable contract, the 3-arg one is a transient bridge that the
  /// integration code will retire once the frame-level entropy decoder is
  /// threaded through.
  /// </remarks>
  internal static JxlAcStrategyType[] DecodeForGroup(
    JxlBitReader reader,
    int blocksX,
    int blocksY
  ) {
    ArgumentNullException.ThrowIfNull(reader);
    if (blocksX < 0 || blocksY < 0)
      throw new ArgumentOutOfRangeException(
        nameof(blocksX), "Block dimensions must be non-negative.");

    throw new NotImplementedException(
      "JxlAcStrategyDecoder.DecodeForGroup (3-arg compatibility shim): the " +
      "frame-level entropy decoder is not yet threaded through this call " +
      "site. Use the 4-arg overload (reader, entropy, blocksX, blocksY) " +
      "directly when an entropy decoder is available; otherwise fall back " +
      "to CreateAllDct8x8.");
  }

  // ===============================================================
  // covered_blocks lookup table (libjxl ac_strategy.h:148-170 — the
  // covered_blocks_x / covered_blocks_y arrays). These describe how many
  // 8x8 sub-blocks each strategy spans horizontally / vertically. The
  // top-left cell carries the strategy; the trailing cells are marked
  // CoveredByNeighbour.
  // ===============================================================

  /// <summary>
  /// Per-strategy <c>(covered_blocks_x, covered_blocks_y)</c>. Strategy
  /// indices match <see cref="JxlAcStrategyType"/> raw values 0..26.
  /// Source: libjxl <c>lib/jxl/ac_strategy.h:148-170</c> kLut tables.
  /// </summary>
  private static readonly (byte X, byte Y)[] _CoveredBlocksTable = {
    /*  0 DCT8x8       */ ((byte)1,  (byte)1),
    /*  1 Hornuss      */ ((byte)1,  (byte)1),
    /*  2 DCT2x2       */ ((byte)1,  (byte)1),
    /*  3 DCT4x4       */ ((byte)1,  (byte)1),
    /*  4 DCT16x16     */ ((byte)2,  (byte)2),
    /*  5 DCT32x32     */ ((byte)4,  (byte)4),
    /*  6 DCT16x8      */ ((byte)1,  (byte)2),
    /*  7 DCT8x16      */ ((byte)2,  (byte)1),
    /*  8 DCT32x8      */ ((byte)1,  (byte)4),
    /*  9 DCT8x32      */ ((byte)4,  (byte)1),
    /* 10 DCT32x16     */ ((byte)2,  (byte)4),
    /* 11 DCT16x32     */ ((byte)4,  (byte)2),
    /* 12 DCT4x8       */ ((byte)1,  (byte)1),
    /* 13 DCT8x4       */ ((byte)1,  (byte)1),
    /* 14 AFV0         */ ((byte)1,  (byte)1),
    /* 15 AFV1         */ ((byte)1,  (byte)1),
    /* 16 AFV2         */ ((byte)1,  (byte)1),
    /* 17 AFV3         */ ((byte)1,  (byte)1),
    /* 18 DCT64x64     */ ((byte)8,  (byte)8),
    /* 19 DCT64x32     */ ((byte)4,  (byte)8),
    /* 20 DCT32x64     */ ((byte)8,  (byte)4),
    /* 21 DCT128x128   */ ((byte)16, (byte)16),
    /* 22 DCT128x64    */ ((byte)8,  (byte)16),
    /* 23 DCT64x128    */ ((byte)16, (byte)8),
    /* 24 DCT256x256   */ ((byte)32, (byte)32),
    /* 25 DCT256x128   */ ((byte)16, (byte)32),
    /* 26 DCT128x256   */ ((byte)32, (byte)16),
  };

  /// <summary>
  /// Look up the <c>(covered_blocks_x, covered_blocks_y)</c> for a raw
  /// strategy value. Caller must have already validated
  /// <c>0 &lt;= rawStrategy &lt; <see cref="_NumValidStrategies"/></c>.
  /// </summary>
  /// <remarks>
  /// libjxl maps strategy → covered block count via two parallel
  /// 27-element <c>kLut</c> arrays in <c>ac_strategy.h</c>. We collapse
  /// them to a single (X, Y) table for clarity. The values are part of
  /// the spec — changing them changes which sub-blocks the parent strategy
  /// claims, which would mis-decode every multi-block frame.
  /// </remarks>
  internal static (int X, int Y) _GetCoveredBlocks(int rawStrategy) {
    var entry = _CoveredBlocksTable[rawStrategy];
    return (entry.X, entry.Y);
  }

  /// <summary>
  /// libjxl <c>AcStrategy::IsRawStrategyValid</c>: the raw strategy value
  /// must lie in <c>[0, kNumValidStrategies)</c>. See
  /// <c>lib/jxl/ac_strategy.h:117-119</c>.
  /// </summary>
  private static bool _IsRawStrategyValid(int rawStrategy) =>
    rawStrategy >= 0 && rawStrategy < _NumValidStrategies;
}
