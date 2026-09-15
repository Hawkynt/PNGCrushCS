using System;

namespace FileFormat.JpegXl.Codec;

// =====================================================================================
// Frame TOC (Table Of Contents) parser for JPEG XL VarDCT/Modular frames
// (ISO/IEC 18181-1 §G.5 / libjxl `lib/jxl/toc.cc::ReadToc` and
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
// Wire format (libjxl `ReadToc`):
//   1 bit               permuted flag
//   if permuted:        N permutation entries via entropy-coded Lehmer code
//                       (`DecodePermutation` with 8 permutation contexts)
//   ZeroPadToByte()
//   for each section:   U32(0+u(10), 1024+u(14), 17408+u(22), 4211712+u(30))
//                       — physical section size in bytes.
//   ZeroPadToByte()
//
// A decoded permutation maps canonical section index to physical section index.
// The size list itself is physical-order. libjxl therefore first computes
// physical prefix offsets and then remaps offsets/sizes through the permutation;
// this class exposes those already-remapped canonical arrays so callers never
// need a special seek path for permuted TOCs.
// =====================================================================================

/// <summary>
/// Parsed JPEG XL frame TOC (table of contents). Carries per-section byte
/// sizes, prefix-sum offsets, and the optional permutation map. The bit
/// reader is left byte-aligned and positioned at the first byte of the first
/// physically stored frame section after a successful <see cref="Decode"/>.
/// </summary>
internal sealed class JxlFrameToc {

  /// <summary>Per-section byte offsets within the frame payload, indexed by
  /// canonical section id. For a permuted TOC these point into the physically
  /// reordered section byte stream.</summary>
  public required int[] SectionOffsets { get; init; }

  /// <summary>Per-section byte sizes indexed by canonical section id.</summary>
  public required int[] SectionSizes { get; init; }

  /// <summary>True if the encoder applied a section permutation.</summary>
  public required bool Permuted { get; init; }

  /// <summary>If <see cref="Permuted"/> is true, the permutation map: for
  /// canonical section index <c>i</c>, the physical on-disk section index is
  /// <c>Permutation[i]</c>. Empty array when not permuted.</summary>
  public required int[] Permutation { get; init; }

  /// <summary>
  /// Decode the TOC. The bit reader must be positioned at the first bit of
  /// the TOC (i.e. immediately after the FrameHeader). On return the reader
  /// is byte-aligned and positioned at the first byte of the frame body.
  /// </summary>
  public static JxlFrameToc Decode(JxlBitReader reader, int numGroups, int numPasses)
    => Decode(reader, numGroups, numPasses, numDcGroups: 1);

  /// <summary>
  /// Decode the TOC with explicit DC group count. libjxl computes
  /// <c>NumTocEntries(num_groups, num_dc_groups, num_passes)</c> as
  /// <c>2 + num_dc_groups + num_groups * num_passes</c> when not in the
  /// single-section fast path.
  /// </summary>
  public static JxlFrameToc Decode(JxlBitReader reader, int numGroups, int numPasses, int numDcGroups) {
    ArgumentNullException.ThrowIfNull(reader);
    if (numGroups < 1)
      throw new ArgumentOutOfRangeException(nameof(numGroups), "Must be >= 1.");
    if (numPasses < 1)
      throw new ArgumentOutOfRangeException(nameof(numPasses), "Must be >= 1.");
    if (numDcGroups < 1)
      throw new ArgumentOutOfRangeException(nameof(numDcGroups), "Must be >= 1.");

    var numSections = _NumTocEntries(numGroups, numPasses, numDcGroups);

    // libjxl `ReadToc`: permutation flag, optional entropy-coded Lehmer
    // permutation, then byte-align before the size list.
    var permuted = reader.ReadBool();
    var permutation = permuted ? _DecodePermutation(reader, numSections) : Array.Empty<int>();
    reader.ZeroPadToByte();

    var physicalSizes = new int[numSections];
    var physicalOffsets = new int[numSections];
    var runningOffset = 0;
    for (var i = 0; i < numSections; ++i) {
      // libjxl `kTocDist`:
      //   U32(Bits(10), BitsOffset(14, 1024), BitsOffset(22, 17408),
      //       BitsOffset(30, 4211712))
      var size = reader.ReadU32(
        c0: 0u, u0: 10u,
        c1: 1024u, u1: 14u,
        c2: 17408u, u2: 22u,
        c3: 4211712u, u3: 30u);
      if (size > int.MaxValue)
        throw new System.IO.InvalidDataException($"A JPEG XL frame section is too large ({size} bytes).");

      physicalSizes[i] = (int)size;
      physicalOffsets[i] = runningOffset;
      runningOffset = checked(runningOffset + physicalSizes[i]);
    }

    reader.ZeroPadToByte();

    if (!permuted) {
      return new JxlFrameToc {
        SectionOffsets = physicalOffsets,
        SectionSizes = physicalSizes,
        Permuted = false,
        Permutation = Array.Empty<int>(),
      };
    }

    // libjxl `ReadGroupOffsets`: the decoded permutation maps canonical entry
    // i to the physical entry whose bytes belong to it. Remap both arrays so
    // every downstream caller can keep addressing canonical section ids.
    var offsets = new int[numSections];
    var sizes = new int[numSections];
    for (var i = 0; i < numSections; ++i) {
      var physical = permutation[i];
      if ((uint)physical >= (uint)numSections)
        throw new System.IO.InvalidDataException(
          $"JPEG XL TOC permutation maps section {i} to invalid physical index {physical}.");
      offsets[i] = physicalOffsets[physical];
      sizes[i] = physicalSizes[physical];
    }

    return new JxlFrameToc {
      SectionOffsets = offsets,
      SectionSizes = sizes,
      Permuted = true,
      Permutation = permutation,
    };
  }

  /// <summary>
  /// libjxl <c>DecodePermutation(memory_manager, skip=0, size, ...)</c>. The
  /// permutation syntax is an entropy-coded Lehmer code with eight contexts.
  /// Existing JPEG XL entropy and Lehmer helpers are reused rather than adding
  /// a second implementation of either primitive.
  /// </summary>
  private static int[] _DecodePermutation(JxlBitReader reader, int size) {
    var entropy = JxlEntropyDecoder.Read(
      reader,
      numContexts: JxlCoeffOrderDecoder.PermutationContexts,
      disallowLz77: false);

    var lehmer = new int[size];
    var end = entropy.ReadInt(JxlCoeffOrderDecoder.CoeffOrderContext((uint)size));
    if (end < 0 || end > size)
      throw new System.IO.InvalidDataException(
        $"JPEG XL TOC permutation ends at {end}, outside 0..{size}.");

    uint last = 0;
    for (var i = 0; i < end; ++i) {
      var value = entropy.ReadInt(JxlCoeffOrderDecoder.CoeffOrderContext(last));
      if (value < 0 || value >= size - i)
        throw new System.IO.InvalidDataException(
          $"JPEG XL TOC Lehmer digit {value} at position {i} is outside 0..{size - i - 1}.");
      lehmer[i] = value;
      last = (uint)value;
    }

    if (!entropy.CheckFinalState())
      throw new System.IO.InvalidDataException(
        "JPEG XL TOC permutation did not end in the entropy decoder's initial state.");

    return JxlLehmerCode.Decode(lehmer, size);
  }

  /// <summary>libjxl <c>NumTocEntries</c> from <c>lib/jxl/toc.h</c>.</summary>
  private static int _NumTocEntries(int numGroups, int numPasses, int numDcGroups) {
    if (numGroups == 1 && numPasses == 1)
      return 1;
    return checked(2 + numDcGroups + numGroups * numPasses);
  }
}
