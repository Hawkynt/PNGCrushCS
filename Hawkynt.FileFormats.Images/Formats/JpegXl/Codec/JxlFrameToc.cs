using System;

namespace FileFormat.JpegXl.Codec;

// =====================================================================================
// Frame TOC (Table Of Contents) parser for JPEG XL VarDCT/Modular frames
// (ISO/IEC 18181-1 §G.5 / libjxl `lib/jxl/dec_frame.cc::ReadGroupOffsets` and
// `lib/jxl/coeff_order.cc::DecodePermutation`).
//
// The TOC sits immediately after the FrameHeader (and before the byte-aligned
// frame body). It tells the decoder where each independently-decodable section
// of the frame lives within the frame payload, which is what enables the
// permutation/parallel-decode property of the codestream.
//
// Section count rules (libjxl `NumTocEntries`):
//   - Single group, single pass:           1 section.
//   - Multi-group OR multi-pass:           2 + num_lf_groups + num_pass_groups
//                                          (LfGlobal + HfGlobal + N LF + M HF
//                                          where M = num_groups * num_passes).
//
// Wire format (libjxl `ReadGroupOffsets`):
//   1 bit               permuted flag
//   if permuted:        N permutation entries via Lehmer-code (per
//                       `DecodePermutation` in coeff_order.cc — uses entropy
//                       coding with kPermutationContexts contexts).
//   ZeroPadToByte()
//   for each section:   U32(0+u(10), 1024+u(14), 17408+u(22), Bits(30))
//                       — section size in bytes.
//   ZeroPadToByte()
//
// First-wave scope: simple 1-group / 1-pass case (single section, no
// permutation). The Lehmer-code permutation requires the full ANS entropy
// pipeline plus a context-map decoder, so we throw NotImplementedException
// when the permuted bit is set, with a precise message naming the missing
// piece. This is the same staged-implementation strategy used elsewhere in
// FileFormat.JpegXl (see JxlFrameQuantizer.ReadDequantMatrices).
//
// libjxl source links:
//   https://github.com/libjxl/libjxl/blob/main/lib/jxl/dec_frame.cc
//     (ReadGroupOffsets + the U32 size selector quoted above)
//   https://github.com/libjxl/libjxl/blob/main/lib/jxl/coeff_order.cc
//     (DecodePermutation — the Lehmer-code reader)
//   https://github.com/libjxl/libjxl/blob/main/lib/jxl/toc.cc
//     (NumTocEntries / TocPermutation helpers)
// =====================================================================================

/// <summary>
/// Parsed JPEG XL frame TOC (table of contents). Carries per-section byte
/// sizes, prefix-sum offsets, and the optional permutation map. The bit
/// reader is left byte-aligned and positioned at the first byte of the first
/// (in-canonical-order) frame section after a successful <see cref="Decode"/>.
/// </summary>
internal sealed class JxlFrameToc {

  /// <summary>Per-section byte offsets within the frame payload, computed as
  /// the prefix sum of <see cref="SectionSizes"/> in canonical (un-permuted)
  /// section order. Length matches <see cref="SectionSizes"/>; element 0 is
  /// always 0. Allows seek to LF/HF/group sections without re-walking the
  /// TOC.</summary>
  public required int[] SectionOffsets { get; init; }

  /// <summary>Per-section byte sizes in canonical (un-permuted) order — the
  /// `entry_sizes` array of libjxl's `ReadGroupOffsets`.</summary>
  public required int[] SectionSizes { get; init; }

  /// <summary>True if the encoder applied a section permutation; the
  /// on-disk order of section bytes does NOT match canonical order and the
  /// caller must consult <see cref="Permutation"/> when seeking.</summary>
  public required bool Permuted { get; init; }

  /// <summary>If <see cref="Permuted"/> is true, the permutation map: for
  /// canonical section index <c>i</c>, the on-disk order index is
  /// <c>Permutation[i]</c>. Empty array when not permuted.</summary>
  public required int[] Permutation { get; init; }

  /// <summary>
  /// Decode the TOC. The bit reader must be positioned at the first bit of
  /// the TOC (i.e. immediately after the FrameHeader). On return the reader
  /// is byte-aligned and positioned at the first byte of the frame body.
  /// </summary>
  /// <param name="reader">Bit reader positioned at the TOC.</param>
  /// <param name="numGroups">Number of pass groups in the frame (libjxl
  /// <c>num_groups</c>). For first-wave the only supported value is 1 (a
  /// single group); larger values trigger the multi-section TOC layout
  /// which is not yet implemented.</param>
  /// <param name="numPasses">Number of progressive passes in the frame
  /// (libjxl <c>num_passes</c>). For first-wave the only supported value is
  /// 1.</param>
  /// <returns>The parsed TOC.</returns>
  /// <exception cref="ArgumentNullException"><paramref name="reader"/> is null.</exception>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="numGroups"/>
  /// or <paramref name="numPasses"/> is less than 1.</exception>
  /// <exception cref="NotImplementedException">When the bitstream signals a
  /// permuted TOC, or when <paramref name="numGroups"/> &gt; 1 or
  /// <paramref name="numPasses"/> &gt; 1 (multi-section TOC).</exception>
  public static JxlFrameToc Decode(JxlBitReader reader, int numGroups, int numPasses)
    => Decode(reader, numGroups, numPasses, numDcGroups: 1);

  /// <summary>
  /// Decode the TOC with explicit DC group count. libjxl computes
  /// <c>NumTocEntries(num_groups, num_dc_groups, num_passes)</c> as
  /// <c>2 + num_dc_groups + num_groups * num_passes</c> when not in the
  /// single-section fast path. <paramref name="numDcGroups"/> is derived from
  /// frame_dim's xsize_dc_groups × ysize_dc_groups (one DC group per 2048-pixel
  /// tile by default); callers without that info should pass 1.
  /// </summary>
  public static JxlFrameToc Decode(JxlBitReader reader, int numGroups, int numPasses, int numDcGroups) {
    ArgumentNullException.ThrowIfNull(reader);
    if (numGroups < 1)
      throw new ArgumentOutOfRangeException(nameof(numGroups), "Must be >= 1.");
    if (numPasses < 1)
      throw new ArgumentOutOfRangeException(nameof(numPasses), "Must be >= 1.");
    if (numDcGroups < 1)
      throw new ArgumentOutOfRangeException(nameof(numDcGroups), "Must be >= 1.");

    // libjxl `ReadGroupOffsets`: the first bit is the permutation flag.
    var permuted = reader.ReadBool();

    if (permuted)
      throw new NotImplementedException(
        "JPEG XL frame TOC permutation decoding is not yet implemented. " +
        "The bitstream signals permuted = 1, which requires the full Lehmer-code " +
        "permutation reader (libjxl coeff_order.cc::DecodePermutation), itself " +
        "needing the ANS entropy decoder and a context map. First-wave scope " +
        "handles only the canonical (non-permuted) order. libjxl ref: " +
        "lib/jxl/dec_frame.cc::ReadGroupOffsets and lib/jxl/coeff_order.cc::DecodePermutation.");

    var numSections = _NumTocEntries(numGroups, numPasses, numDcGroups);

    // libjxl `ReadToc` byte-aligns AFTER the permutation flag (and
    // permutation block) before reading section sizes. Without this our
    // U32Coder reads land on the wrong bits.
    reader.ZeroPadToByte();

    var sizes = new int[numSections];
    var offsets = new int[numSections];
    var runningOffset = 0;
    for (var i = 0; i < numSections; i++) {
      // libjxl `ReadGroupOffsets` per-section size encoding:
      //   U32(Bits(10), 1024 + Bits(14), 17408 + Bits(22), Bits(30))
      // i.e.: selector 0 = 0 + u(10); selector 1 = 1024 + u(14);
      //       selector 2 = 17408 + u(22); selector 3 = 0 + u(30).
      var size = reader.ReadU32(
        c0: 0u, u0: 10u,        // Bits(10):              0 + read(10)
        c1: 1024u, u1: 14u,     // BitsOffset(14, 1024):  1024 + read(14)
        c2: 17408u, u2: 22u,    // BitsOffset(22, 17408): 17408 + read(22)
        c3: 0u, u3: 30u);       // Bits(30):              0 + read(30)
      sizes[i] = (int)size;
      offsets[i] = runningOffset;
      runningOffset += sizes[i];
    }

    // After reading all section sizes the TOC ends with a zero pad to the
    // next byte boundary (libjxl `ReadGroupOffsets` calls `JumpToByteBoundary`
    // after the loop).
    reader.ZeroPadToByte();

    return new JxlFrameToc {
      SectionOffsets = offsets,
      SectionSizes = sizes,
      Permuted = false,
      Permutation = Array.Empty<int>(),
    };
  }

  // ---------------------------------------------------------------------
  // Helpers
  // ---------------------------------------------------------------------

  /// <summary>libjxl <c>NumTocEntries</c> from <c>lib/jxl/toc.h</c>:
  /// <c>(num_groups == 1 &amp;&amp; num_passes == 1) ? 1 : 2 + num_dc_groups +
  /// num_groups * num_passes</c>. The "+2" accounts for the LfGlobal and
  /// HfGlobal sections.</summary>
  private static int _NumTocEntries(int numGroups, int numPasses, int numDcGroups) {
    if (numGroups == 1 && numPasses == 1)
      return 1;
    return 2 + numDcGroups + numGroups * numPasses;
  }
}
