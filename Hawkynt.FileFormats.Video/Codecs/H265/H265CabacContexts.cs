using System;

namespace FileFormat.Codecs.H265;

/// <summary>
/// Where each syntax element's context variables live, and the values they start at — ITU-T H.265,
/// clause 9.3.2.2 and Tables 9-5 to 9-37.
/// </summary>
/// <remarks>
/// <b>These tables are the decoder.</b> A context variable is a running estimate of how probable the
/// next bin of one particular syntax element is, given what the neighbours did, and every one of them
/// starts from a number the standard fixes. Start them all at state zero instead — which is what
/// happens when the initialisation is written and never called — and the arithmetic decoder still
/// runs, still consumes exactly as many bits as it is given, and still produces coefficients. They
/// are simply not the coefficients that were encoded. There is no error, no mismatch and no
/// truncated stream to notice; there is a picture, and it is wrong. So the values are entered here
/// from the standard's tables with the clause beside each, and the only thing that can prove them
/// right is decoding a real stream to the sample.
/// <para/>
/// Each entry is one byte holding two numbers: a slope in the top four bits and an intercept in the
/// bottom four. The initial state is that line evaluated at the slice's quantiser and clipped, which
/// is how one table serves every quantiser — a coarsely quantised slice has fewer significant
/// coefficients, so the same context should start out expecting fewer, and the slope is how much
/// fewer.
/// <para/>
/// Three columns, because a slice's statistics depend on what it is allowed to do. An intra slice
/// codes no motion at all and its residuals are large; a predicted slice codes mostly small
/// residuals and a great deal of motion. A bidirectional slice may use either of the two predicted
/// columns, its choice stated in the slice header, because a B slice at the top of a prediction
/// pyramid behaves like a P slice and one at the bottom does not.
/// <para/>
/// Where a syntax element cannot occur in a slice of some kind the standard writes "na" and this
/// writes <see cref="_UNUSED"/>. Nothing reads those entries; giving them a value rather than leaving
/// a hole keeps the table rectangular, which is how it can be checked against the standard by eye.
/// </remarks>
internal static class H265CabacContexts {

  /// <summary>The value the standard's tables leave blank, for a context that slice type cannot reach.</summary>
  private const byte _UNUSED = 154;

  // The offset of each syntax element's block of contexts within the one flat state array. Laid out
  // in the order the standard's tables are, so that a reader checking one against the other reads
  // both top to bottom.

  internal const int SAO_MERGE = 0;
  internal const int SAO_TYPE_IDX = SAO_MERGE + 1;
  internal const int SPLIT_CU_FLAG = SAO_TYPE_IDX + 1;
  internal const int CU_TRANSQUANT_BYPASS_FLAG = SPLIT_CU_FLAG + 3;
  internal const int CU_SKIP_FLAG = CU_TRANSQUANT_BYPASS_FLAG + 1;
  internal const int PRED_MODE_FLAG = CU_SKIP_FLAG + 3;
  internal const int PART_MODE = PRED_MODE_FLAG + 1;
  internal const int PREV_INTRA_LUMA_PRED_FLAG = PART_MODE + 4;
  internal const int INTRA_CHROMA_PRED_MODE = PREV_INTRA_LUMA_PRED_FLAG + 1;
  internal const int RQT_ROOT_CBF = INTRA_CHROMA_PRED_MODE + 1;
  internal const int MERGE_FLAG = RQT_ROOT_CBF + 1;
  internal const int MERGE_IDX = MERGE_FLAG + 1;
  internal const int INTER_PRED_IDC = MERGE_IDX + 1;
  internal const int REF_IDX = INTER_PRED_IDC + 5;
  internal const int MVP_FLAG = REF_IDX + 2;
  internal const int SPLIT_TRANSFORM_FLAG = MVP_FLAG + 1;
  internal const int CBF_LUMA = SPLIT_TRANSFORM_FLAG + 3;
  internal const int CBF_CHROMA = CBF_LUMA + 2;
  internal const int ABS_MVD_GREATER0_FLAG = CBF_CHROMA + 4;
  internal const int ABS_MVD_GREATER1_FLAG = ABS_MVD_GREATER0_FLAG + 1;
  internal const int CU_QP_DELTA_ABS = ABS_MVD_GREATER1_FLAG + 1;
  internal const int TRANSFORM_SKIP_FLAG_LUMA = CU_QP_DELTA_ABS + 2;
  internal const int TRANSFORM_SKIP_FLAG_CHROMA = TRANSFORM_SKIP_FLAG_LUMA + 1;
  internal const int LAST_SIG_COEFF_X_PREFIX = TRANSFORM_SKIP_FLAG_CHROMA + 1;
  internal const int LAST_SIG_COEFF_Y_PREFIX = LAST_SIG_COEFF_X_PREFIX + 18;
  internal const int CODED_SUB_BLOCK_FLAG = LAST_SIG_COEFF_Y_PREFIX + 18;
  internal const int SIG_COEFF_FLAG = CODED_SUB_BLOCK_FLAG + 4;
  internal const int COEFF_ABS_LEVEL_GREATER1_FLAG = SIG_COEFF_FLAG + 42;
  internal const int COEFF_ABS_LEVEL_GREATER2_FLAG = COEFF_ABS_LEVEL_GREATER1_FLAG + 24;

  /// <summary>How many context variables one entropy coder holds.</summary>
  internal const int COUNT = COEFF_ABS_LEVEL_GREATER2_FLAG + 6;

  /// <summary>
  /// The initialisation values, one row per initialisation type: intra, predicted, bidirectional.
  /// </summary>
  /// <remarks>
  /// Assembled once at type load rather than written out as three flat arrays of a hundred and
  /// fifty-four numbers, so that each syntax element's row sits beside its own name and its own
  /// clause. A flat array would be shorter and would make a transposition between two neighbouring
  /// elements invisible.
  /// </remarks>
  private static readonly byte[][] _InitialValues = _Build();

  /// <summary>
  /// Sets every context to the state clause 9.3.2.2 gives it for this slice.
  /// </summary>
  /// <param name="states">
  /// The context states, packed one byte each: the probability state in the upper seven bits and the
  /// most probable symbol in the lowest.
  /// </param>
  /// <param name="initType">0 for an intra slice, 1 and 2 for the two predicted flavours.</param>
  /// <param name="sliceQpY">The slice's quantiser, which is what the tabulated line is evaluated at.</param>
  internal static void Initialize(byte[] states, int initType, int sliceQpY) {
    var values = _InitialValues[initType];
    var qp = Math.Clamp(sliceQpY, 0, 51);

    for (var i = 0; i < COUNT; ++i) {
      var initValue = values[i];

      // Equation 9-5: a line through the quantiser, with the slope in the top nibble and the
      // intercept in the bottom one, clipped to the 126 states either side of even odds.
      var slope = (initValue >> 4) * 5 - 45;
      var intercept = ((initValue & 15) << 3) - 16;
      var preState = Math.Clamp(((slope * qp) >> 4) + intercept, 1, 126);

      // Below the midpoint the more probable symbol is zero and the state counts down from it;
      // above, it is one and the state counts up.
      var mps = preState <= 63 ? 0 : 1;
      var stateIdx = mps != 0 ? preState - 64 : 63 - preState;
      states[i] = (byte)((stateIdx << 1) | mps);
    }
  }

  private static byte[][] _Build() {
    var tables = new byte[3][];
    for (var initType = 0; initType < 3; ++initType)
      tables[initType] = new byte[COUNT];

    // Table 9-5: sao_merge_left_flag and sao_merge_up_flag share one context.
    _Set(tables, SAO_MERGE, [153], [153], [153]);

    // Table 9-6: sao_type_idx_luma and sao_type_idx_chroma share one context.
    _Set(tables, SAO_TYPE_IDX, [200], [185], [160]);

    // Table 9-7.
    _Set(tables, SPLIT_CU_FLAG, [139, 141, 157], [107, 139, 126], [107, 139, 126]);

    // Table 9-8.
    _Set(tables, CU_TRANSQUANT_BYPASS_FLAG, [154], [154], [154]);

    // Table 9-9: no coding unit is skipped in an intra slice, so the intra column is blank.
    _Set(tables, CU_SKIP_FLAG, [_UNUSED, _UNUSED, _UNUSED], [197, 185, 201], [197, 185, 201]);

    // Table 9-10.
    _Set(tables, PRED_MODE_FLAG, [_UNUSED], [149], [134]);

    // Table 9-11: an intra slice reaches only the first bin, which says square or quartered.
    _Set(tables, PART_MODE,
      [184, _UNUSED, _UNUSED, _UNUSED], [154, 139, 154, 154], [154, 139, 154, 154]);

    // Table 9-12.
    _Set(tables, PREV_INTRA_LUMA_PRED_FLAG, [184], [154], [183]);

    // Table 9-13.
    _Set(tables, INTRA_CHROMA_PRED_MODE, [63], [152], [152]);

    // Table 9-14.
    _Set(tables, RQT_ROOT_CBF, [_UNUSED], [79], [79]);

    // Table 9-15.
    _Set(tables, MERGE_FLAG, [_UNUSED], [110], [154]);

    // Table 9-16.
    _Set(tables, MERGE_IDX, [_UNUSED], [122], [137]);

    // Table 9-17.
    _Set(tables, INTER_PRED_IDC,
      [_UNUSED, _UNUSED, _UNUSED, _UNUSED, _UNUSED], [95, 79, 63, 31, 31], [95, 79, 63, 31, 31]);

    // Table 9-18: ref_idx_l0 and ref_idx_l1 share these two.
    _Set(tables, REF_IDX, [_UNUSED, _UNUSED], [153, 153], [153, 153]);

    // Table 9-19: mvp_l0_flag and mvp_l1_flag share one context.
    _Set(tables, MVP_FLAG, [_UNUSED], [168], [168]);

    // Table 9-20.
    _Set(tables, SPLIT_TRANSFORM_FLAG, [153, 138, 138], [124, 138, 94], [224, 167, 122]);

    // Table 9-21.
    _Set(tables, CBF_LUMA, [111, 141], [153, 111], [153, 111]);

    // Table 9-22: cbf_cb and cbf_cr share these four.
    _Set(tables, CBF_CHROMA, [94, 138, 182, 154], [149, 107, 167, 154], [149, 92, 167, 154]);

    // Table 9-23.
    _Set(tables, ABS_MVD_GREATER0_FLAG, [_UNUSED], [140], [169]);
    _Set(tables, ABS_MVD_GREATER1_FLAG, [_UNUSED], [198], [198]);

    // Table 9-24.
    _Set(tables, CU_QP_DELTA_ABS, [154, 154], [154, 154], [154, 154]);

    // Table 9-25.
    _Set(tables, TRANSFORM_SKIP_FLAG_LUMA, [139], [139], [139]);
    _Set(tables, TRANSFORM_SKIP_FLAG_CHROMA, [139], [139], [139]);

    // Tables 9-26 and 9-27. The last significant coefficient's column and row are coded with the
    // same numbers in two separate sets of contexts, because in most blocks they are not alike.
    ReadOnlySpan<byte> lastPrefixIntra = [
      110, 110, 124, 125, 140, 153, 125, 127, 140, 109, 111, 143, 127, 111, 79, 108, 123, 63,
    ];
    ReadOnlySpan<byte> lastPrefixPredicted = [
      125, 110, 94, 110, 95, 79, 125, 111, 110, 78, 110, 111, 111, 95, 94, 108, 123, 108,
    ];
    ReadOnlySpan<byte> lastPrefixBidirectional = [
      125, 110, 124, 110, 95, 94, 125, 111, 111, 79, 125, 126, 111, 111, 79, 108, 123, 93,
    ];

    _Set(tables, LAST_SIG_COEFF_X_PREFIX, lastPrefixIntra, lastPrefixPredicted, lastPrefixBidirectional);
    _Set(tables, LAST_SIG_COEFF_Y_PREFIX, lastPrefixIntra, lastPrefixPredicted, lastPrefixBidirectional);

    // Table 9-28.
    _Set(tables, CODED_SUB_BLOCK_FLAG, [91, 171, 134, 141], [121, 140, 61, 154], [121, 140, 61, 154]);

    // Table 9-29: twenty-seven luma contexts then fifteen chroma ones.
    _Set(tables, SIG_COEFF_FLAG,
      [
        111, 111, 125, 110, 110, 94, 124, 108, 124, 107, 125, 141, 179, 153, 125, 107, 125, 141, 179, 153, 125,
        107, 125, 141, 179, 153, 125,
        140, 139, 182, 182, 152, 136, 152, 136, 153, 136, 139, 111, 136, 139, 111,
      ],
      [
        155, 154, 139, 153, 139, 123, 123, 63, 153, 166, 183, 140, 136, 153, 154, 166, 183, 140, 136, 153, 154,
        166, 183, 140, 136, 153, 154,
        170, 153, 123, 123, 107, 121, 107, 121, 167, 151, 183, 140, 151, 183, 140,
      ],
      [
        170, 154, 139, 153, 139, 123, 123, 63, 124, 166, 183, 140, 136, 153, 154, 166, 183, 140, 136, 153, 154,
        166, 183, 140, 136, 153, 154,
        170, 153, 138, 138, 122, 121, 122, 121, 167, 151, 183, 140, 151, 183, 140,
      ]);

    // Table 9-30: sixteen luma contexts, four sets of four, then eight chroma ones.
    _Set(tables, COEFF_ABS_LEVEL_GREATER1_FLAG,
      [
        140, 92, 137, 138, 140, 152, 138, 139, 153, 74, 149, 92, 139, 107, 122, 152,
        140, 179, 166, 182, 140, 227, 122, 197,
      ],
      [
        154, 196, 196, 167, 154, 152, 167, 182, 182, 134, 149, 136, 153, 121, 136, 137,
        169, 194, 166, 167, 154, 167, 137, 182,
      ],
      [
        154, 196, 167, 167, 154, 152, 167, 182, 182, 134, 149, 136, 153, 121, 136, 137,
        169, 208, 166, 167, 154, 167, 137, 182,
      ]);

    // Table 9-31: four luma contexts and two chroma ones.
    _Set(tables, COEFF_ABS_LEVEL_GREATER2_FLAG,
      [138, 153, 136, 167, 152, 152], [107, 167, 91, 122, 107, 167], [107, 167, 91, 107, 107, 167]);

    return tables;
  }

  private static void _Set(
    byte[][] tables, int offset, ReadOnlySpan<byte> intra, ReadOnlySpan<byte> predicted,
    ReadOnlySpan<byte> bidirectional) {
    intra.CopyTo(tables[0].AsSpan(offset));
    predicted.CopyTo(tables[1].AsSpan(offset));
    bidirectional.CopyTo(tables[2].AsSpan(offset));
  }
}
