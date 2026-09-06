// AV1 constant tables transcribed from the libaom reference implementation (BSD 2-Clause,
// Alliance for Open Media). See THIRD_PARTY_NOTICES.md. Generated values are copied verbatim:
// a re-derived probability table is simply a wrong one.

using System;

namespace FileFormat.Avif.Codec;

/// <summary>Tables for the AV1 in-loop post filters: CDEF direction offsets and taps from libaom's
/// <c>av1/common/cdef_block.c</c>, and the self-guided restoration parameter set from
/// <c>av1/common/restoration.c</c>.</summary>
internal static class Av1PostFilterTables {

  /// <summary>Cdef_Directions (AV1 7.15.3) as row/column offsets: [direction][tap][dy, dx]. libaom folds these into a fixed row stride in cdef_directions_padded; the row and column parts are separated here.</summary>
  internal static readonly int[] CdefDirections = [
    -1, 1, -2, 2, 0, 1, -1, 2,
    0, 1, 0, 2, 0, 1, 1, 2,
    1, 1, 2, 2, 1, 0, 2, 1,
    1, 0, 2, 0, 1, 0, 2, -1,
  ];
  /// <summary>cdef_pri_taps[2][2].</summary>
  internal static readonly int[] CdefPrimaryTaps = [
    4, 2, 3, 3,
  ];
  /// <summary>cdef_sec_taps[2].</summary>
  internal static readonly int[] CdefSecondaryTaps = [
    2, 1,
  ];
  /// <summary>av1_sgr_params[SGRPROJ_PARAMS=16], flattened as libaom's <c>sgr_params_type</c>
  /// stores it: r0, r1, s0, s1, where each s is the precomputed <c>(1 &lt;&lt; 20) / (n * n * eps)</c>
  /// scale rather than the coded eps. The specification's own Sgr_Params table interleaves the
  /// four values as r0, e0, r1, e1 instead, so a reader that assumes the specification's order
  /// picks up a scale where it wants a radius.</summary>
  internal static readonly int[] SgrParams = [
    2, 1, 140, 3236,
    2, 1, 112, 2158,
    2, 1, 93, 1618,
    2, 1, 80, 1438,
    2, 1, 70, 1295,
    2, 1, 58, 1177,
    2, 1, 47, 1079,
    2, 1, 37, 996,
    2, 1, 30, 925,
    2, 1, 25, 863,
    0, 1, -1, 2589,
    0, 1, -1, 1618,
    0, 1, -1, 1177,
    0, 1, -1, 925,
    2, 0, 56, -1,
    2, 0, 22, -1,
  ];
  /// <summary>av1_one_by_x[MAX_NELEM=25].</summary>
  internal static readonly int[] OneByX = [
    4096, 2048, 1365, 1024, 819, 683, 585, 512, 455, 410, 372, 341,
    315, 293, 273, 256, 241, 228, 216, 205, 195, 186, 178, 171,
    164,
  ];
  /// <summary>av1_x_by_xplus1[256].</summary>
  internal static readonly int[] XByXPlus1 = [
    1, 128, 171, 192, 205, 213, 219, 224, 228, 230, 233, 235, 236, 238, 239, 240,
    241, 242, 243, 243, 244, 244, 245, 245, 246, 246, 247, 247, 247, 247, 248, 248,
    248, 248, 249, 249, 249, 249, 249, 250, 250, 250, 250, 250, 250, 250, 251, 251,
    251, 251, 251, 251, 251, 251, 251, 251, 252, 252, 252, 252, 252, 252, 252, 252,
    252, 252, 252, 252, 252, 252, 252, 252, 252, 253, 253, 253, 253, 253, 253, 253,
    253, 253, 253, 253, 253, 253, 253, 253, 253, 253, 253, 253, 253, 253, 253, 253,
    253, 253, 253, 253, 253, 253, 254, 254, 254, 254, 254, 254, 254, 254, 254, 254,
    254, 254, 254, 254, 254, 254, 254, 254, 254, 254, 254, 254, 254, 254, 254, 254,
    254, 254, 254, 254, 254, 254, 254, 254, 254, 254, 254, 254, 254, 254, 254, 254,
    254, 254, 254, 254, 254, 254, 254, 254, 254, 254, 254, 254, 254, 254, 254, 254,
    254, 254, 254, 254, 254, 254, 254, 254, 254, 254, 255, 255, 255, 255, 255, 255,
    255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
    255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
    255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
    255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
    255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 256,
  ];
}
