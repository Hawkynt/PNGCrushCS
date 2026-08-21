using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.H264.Tests;

/// <summary>
/// Writes H.264 byte streams a NAL unit and a bit at a time, so a test can state exactly which syntax
/// it is exercising.
/// </summary>
/// <remarks>
/// Every stream in this library's tests is built rather than checked in, and for a codec that matters
/// more than usual: the paths worth testing are the ones a real encoder never produces. x264 emits no
/// <c>I_PCM</c> macroblock, no macroblock whose quantiser wraps past 51, and no slice whose
/// <c>first_mb_in_slice</c> is anything but zero unless asked in ways that also change everything
/// else — so comparing against it, which is how the decoder's arithmetic was checked, cannot reach
/// any of them. These can.
/// <para/>
/// Emulation prevention is applied on the way out rather than assumed absent, so that a test which
/// happens to produce two zero bytes gets a stream a decoder can actually read.
/// </remarks>
internal sealed class H264TestStream {

  private readonly List<byte> _stream = [];
  private readonly List<byte> _payload = [];
  private int _partial;
  private int _partialBits;

  /// <summary>Appends the low <paramref name="count"/> bits of a value, most significant first.</summary>
  internal H264TestStream Bits(int value, int count) {
    for (var i = count - 1; i >= 0; --i)
      this._Bit((value >> i) & 1);

    return this;
  }

  /// <summary>Appends a code written the way the standard prints it; spaces are grouping.</summary>
  internal H264TestStream Code(string code) {
    foreach (var character in code)
      switch (character) {
        case '0': this._Bit(0); break;
        case '1': this._Bit(1); break;
        case ' ': break;
        default: throw new ArgumentException($"'{character}' is not a bit.", nameof(code));
      }

    return this;
  }

  /// <summary>An unsigned Exp-Golomb code — <c>ue(v)</c>, H.264 clause 9.1.</summary>
  internal H264TestStream Unsigned(int value) {
    if (value < 0)
      throw new ArgumentOutOfRangeException(nameof(value), "ue(v) codes non-negative numbers only.");

    var codeNum = value + 1;
    var length = 0;
    while (codeNum >> (length + 1) != 0)
      ++length;

    for (var i = 0; i < length; ++i)
      this._Bit(0);

    return this.Bits(codeNum, length + 1);
  }

  /// <summary>A signed Exp-Golomb code — <c>se(v)</c>, clause 9.1.1.</summary>
  internal H264TestStream Signed(int value) => this.Unsigned(value > 0 ? 2 * value - 1 : -2 * value);

  /// <summary>Ends the payload with <c>rbsp_trailing_bits()</c> and emits it as a NAL unit.</summary>
  /// <param name="type">The <c>nal_unit_type</c>.</param>
  /// <param name="refIdc">The <c>nal_ref_idc</c>: zero for a unit nothing refers to.</param>
  internal H264TestStream EndNal(int type, int refIdc) {
    this._Bit(1);
    while (this._partialBits != 0)
      this._Bit(0);

    this._stream.Add(0x00);
    this._stream.Add(0x00);
    this._stream.Add(0x00);
    this._stream.Add(0x01);
    this._stream.Add((byte)((refIdc << 5) | type));

    // The escape the encoder is required to insert, so that no start code can occur inside a unit
    // (clause 7.4.1.1). Applied even where the payload has no zero pair, because a test that happened
    // to produce one would otherwise write a stream no decoder could read.
    var zeroes = 0;
    foreach (var octet in this._payload) {
      if (zeroes == 2 && octet <= 3) {
        this._stream.Add(0x03);
        zeroes = 0;
      }

      this._stream.Add(octet);
      zeroes = octet == 0 ? zeroes + 1 : 0;
    }

    this._payload.Clear();
    return this;
  }

  /// <summary>
  /// The payload written so far, padded to a byte, without a NAL unit around it.
  /// </summary>
  /// <remarks>
  /// For the tests that read one syntax element rather than a stream. Padded with ones because a
  /// reader that took too many bits then decodes something rather than running off the end, which is
  /// the failure worth seeing.
  /// </remarks>
  internal byte[] RawPayload() {
    while (this._partialBits != 0)
      this._Bit(1);

    return [.. this._payload];
  }

  /// <summary>The finished byte stream.</summary>
  internal byte[] ToArray() {
    if (this._partialBits != 0 || this._payload.Count > 0)
      throw new InvalidOperationException("A NAL unit was left unfinished; call EndNal before ToArray.");

    return [.. this._stream];
  }

  // ==============================================================================================
  // The parameter sets and slices the tests are built out of
  // ==============================================================================================

  /// <summary>
  /// A Baseline sequence parameter set of <paramref name="widthInMbs"/> by
  /// <paramref name="heightInMbs"/> macroblocks.
  /// </summary>
  internal H264TestStream SequenceParameterSet(
    int widthInMbs = 1, int heightInMbs = 1, int maxRefFrames = 1, int profileIdc = 66) {
    this.Bits(profileIdc, 8);
    this.Bits(0, 8); // the six constraint_set flags and reserved_zero_2bits
    this.Bits(10, 8); // level_idc
    this.Unsigned(0); // seq_parameter_set_id
    this.Unsigned(0); // log2_max_frame_num_minus4
    this.Unsigned(2); // pic_order_cnt_type: 2, which has no further fields and no reordering
    this.Unsigned(maxRefFrames); // max_num_ref_frames
    this.Bits(0, 1); // gaps_in_frame_num_value_allowed_flag
    this.Unsigned(widthInMbs - 1);
    this.Unsigned(heightInMbs - 1);
    this.Bits(1, 1); // frame_mbs_only_flag
    this.Bits(1, 1); // direct_8x8_inference_flag
    this.Bits(0, 1); // frame_cropping_flag
    this.Bits(0, 1); // vui_parameters_present_flag
    return this.EndNal(7, 3);
  }

  /// <summary>
  /// A High profile sequence parameter set, which is the only kind that states its chroma format and
  /// sample depth rather than having them fixed by the profile (clause 7.3.2.1.1).
  /// </summary>
  internal H264TestStream HighSequenceParameterSet(
    int chromaFormatIdc = 1, int bitDepth = 8, bool scalingMatrices = false, bool transformBypass = false) {
    this.Bits(100, 8); // profile_idc: High
    this.Bits(0, 8);
    this.Bits(30, 8); // level_idc
    this.Unsigned(0); // seq_parameter_set_id
    this.Unsigned(chromaFormatIdc);
    if (chromaFormatIdc == 3)
      this.Bits(0, 1); // separate_colour_plane_flag

    this.Unsigned(bitDepth - 8); // bit_depth_luma_minus8
    this.Unsigned(bitDepth - 8); // bit_depth_chroma_minus8
    this.Bits(transformBypass ? 1 : 0, 1); // qpprime_y_zero_transform_bypass_flag

    this.Bits(scalingMatrices ? 1 : 0, 1); // seq_scaling_matrix_present_flag
    if (scalingMatrices)
      for (var list = 0; list < (chromaFormatIdc != 3 ? 8 : 12); ++list)
        this.Bits(0, 1); // seq_scaling_list_present_flag: present but every list defaulted

    this.Unsigned(0); // log2_max_frame_num_minus4
    this.Unsigned(2); // pic_order_cnt_type
    this.Unsigned(1); // max_num_ref_frames
    this.Bits(0, 1);
    this.Unsigned(0); // pic_width_in_mbs_minus1
    this.Unsigned(0); // pic_height_in_map_units_minus1
    this.Bits(1, 1); // frame_mbs_only_flag
    this.Bits(1, 1); // direct_8x8_inference_flag
    this.Bits(0, 1); // frame_cropping_flag
    this.Bits(0, 1); // vui_parameters_present_flag
    return this.EndNal(7, 3);
  }

  /// <summary>An interlaced sequence parameter set, which codes fields or macroblock pairs.</summary>
  internal H264TestStream InterlacedSequenceParameterSet() {
    this.Bits(77, 8); // profile_idc: Main
    this.Bits(0, 8);
    this.Bits(30, 8);
    this.Unsigned(0);
    this.Unsigned(0); // log2_max_frame_num_minus4
    this.Unsigned(2); // pic_order_cnt_type
    this.Unsigned(1); // max_num_ref_frames
    this.Bits(0, 1);
    this.Unsigned(0); // pic_width_in_mbs_minus1
    this.Unsigned(0); // pic_height_in_map_units_minus1
    this.Bits(0, 1); // frame_mbs_only_flag: fields or macroblock pairs
    this.Bits(0, 1); // mb_adaptive_frame_field_flag
    this.Bits(1, 1); // direct_8x8_inference_flag
    this.Bits(0, 1); // frame_cropping_flag
    this.Bits(0, 1); // vui_parameters_present_flag
    return this.EndNal(7, 3);
  }

  /// <summary>A Baseline picture parameter set with the deblocking filter control absent.</summary>
  internal H264TestStream PictureParameterSet(bool cabac = false, int sliceGroups = 1, bool weightedPrediction = false) {
    this.Unsigned(0); // pic_parameter_set_id
    this.Unsigned(0); // seq_parameter_set_id
    this.Bits(cabac ? 1 : 0, 1);
    this.Bits(0, 1); // bottom_field_pic_order_in_frame_present_flag
    this.Unsigned(sliceGroups - 1);

    if (sliceGroups > 1) {
      this.Unsigned(0); // slice_group_map_type: interleaved
      for (var group = 0; group < sliceGroups; ++group)
        this.Unsigned(0); // run_length_minus1
    }

    this.Unsigned(0); // num_ref_idx_l0_default_active_minus1
    this.Unsigned(0); // num_ref_idx_l1_default_active_minus1
    this.Bits(weightedPrediction ? 1 : 0, 1);
    this.Bits(0, 2); // weighted_bipred_idc
    this.Signed(0); // pic_init_qp_minus26
    this.Signed(0); // pic_init_qs_minus26
    this.Signed(0); // chroma_qp_index_offset
    this.Bits(0, 1); // deblocking_filter_control_present_flag
    this.Bits(0, 1); // constrained_intra_pred_flag
    this.Bits(0, 1); // redundant_pic_cnt_present_flag
    return this.EndNal(8, 3);
  }

  /// <summary>
  /// The header of an IDR slice covering a whole picture, leaving the payload open for slice data.
  /// </summary>
  internal H264TestStream BeginIdrSliceHeader(int sliceType = 7) {
    this.Unsigned(0); // first_mb_in_slice
    this.Unsigned(sliceType);
    this.Unsigned(0); // pic_parameter_set_id
    this.Bits(0, 4); // frame_num
    this.Unsigned(0); // idr_pic_id
    this.Bits(0, 1); // no_output_of_prior_pics_flag
    this.Bits(0, 1); // long_term_reference_flag
    this.Signed(0); // slice_qp_delta
    return this;
  }

  /// <summary>The header of a non-IDR slice, likewise.</summary>
  /// <param name="frameNum">Which reference frame this picture is.</param>
  /// <param name="sliceType">A value of Table 7-6; 5 is a P slice whose picture is all P.</param>
  /// <param name="activeRefs">
  /// How many entries of reference picture list 0 the slice may index. Anything but the parameter
  /// set's default is written as an override.
  /// </param>
  /// <param name="reorderBy">
  /// When given, a <c>ref_pic_list_modification</c> subtracting this much from the current picture
  /// number, which moves that reference to the front of the list.
  /// </param>
  /// <param name="markUnusedAt">
  /// When given, a <c>memory_management_control_operation</c> of 1 marking the picture this far
  /// below the current one as no longer a reference.
  /// </param>
  internal H264TestStream BeginSliceHeader(
    int frameNum, int sliceType = 5, int activeRefs = 1, int? reorderBy = null, int? markUnusedAt = null) {
    this.Unsigned(0); // first_mb_in_slice
    this.Unsigned(sliceType);
    this.Unsigned(0); // pic_parameter_set_id
    this.Bits(frameNum, 4);

    if (sliceType % 5 is 0 or 1 or 3) {
      var override_ = activeRefs != 1;
      this.Bits(override_ ? 1 : 0, 1); // num_ref_idx_active_override_flag
      if (override_)
        this.Unsigned(activeRefs - 1);

      this.Bits(reorderBy.HasValue ? 1 : 0, 1); // ref_pic_list_modification_flag_l0
      if (reorderBy.HasValue) {
        this.Unsigned(0); // modification_of_pic_nums_idc: subtract from the predicted picture number
        this.Unsigned(reorderBy.Value - 1); // abs_diff_pic_num_minus1
        this.Unsigned(3); // and end the list
      }
    }

    this.Bits(markUnusedAt.HasValue ? 1 : 0, 1); // adaptive_ref_pic_marking_mode_flag
    if (markUnusedAt.HasValue) {
      this.Unsigned(1); // memory_management_control_operation: mark a short-term picture unused
      this.Unsigned(markUnusedAt.Value - 1); // difference_of_pic_nums_minus1
      this.Unsigned(0); // and end the operations
    }

    this.Signed(0); // slice_qp_delta
    return this;
  }

  /// <summary>
  /// <c>mb_skip_run</c>: how many macroblocks are skipped before the next coded one.
  /// </summary>
  /// <remarks>
  /// Every macroblock of a P slice is preceded by one of these, even when nothing is skipped — the
  /// count is part of the slice data rather than something a coded macroblock implies (clause 7.3.4).
  /// </remarks>
  internal H264TestStream SkipRun(int macroblocks) => this.Unsigned(macroblocks);

  /// <summary>
  /// One <c>P_L0_16x16</c> macroblock naming a reference index, with a zero vector and no residual.
  /// </summary>
  /// <remarks>
  /// A macroblock that copies its reference exactly, which is what makes it useful for saying
  /// <em>which</em> reference the list actually put at an index. A skipped macroblock cannot: it is
  /// always index zero.
  /// </remarks>
  internal H264TestStream InterMacroblockCopying(int refIdx, int activeRefs) {
    this.Unsigned(0); // mb_type: P_L0_16x16

    // ref_idx_l0 is te(v): one inverted bit when only two entries can be named, ue(v) beyond that.
    if (activeRefs == 2)
      this.Bits(refIdx == 0 ? 1 : 0, 1);
    else if (activeRefs > 2)
      this.Unsigned(refIdx);

    this.Signed(0); // mvd_l0 horizontal
    this.Signed(0); // mvd_l0 vertical
    this.Unsigned(0); // coded_block_pattern: code number 0, which Table 9-4's inter column reads as 0
    return this;
  }

  /// <summary>
  /// One <c>I_PCM</c> macroblock: the samples themselves, byte aligned and uncompressed.
  /// </summary>
  /// <remarks>
  /// The one macroblock type whose reconstruction is stated rather than computed, which makes it the
  /// only way a test can put a chosen picture into the decoded picture buffer. No encoder in ordinary
  /// use emits one, so it is also the only way this path is reached at all. Its quantisation parameter
  /// is zero by definition (clause 7.4.5), which sets the deblocking filter's thresholds to zero and
  /// leaves the samples exactly as written.
  /// </remarks>
  /// <param name="mbType">25 in an I slice; 30 in a P slice, where intra types are shifted up by five.</param>
  /// <param name="luma">The luminance sample at each position of the macroblock.</param>
  /// <param name="chroma">The value both chrominance planes are filled with.</param>
  internal H264TestStream PcmMacroblock(int mbType, Func<int, int, byte> luma, byte chroma) {
    ArgumentNullException.ThrowIfNull(luma);

    this.Unsigned(mbType);

    while (this._partialBits != 0)
      this._Bit(0); // pcm_alignment_zero_bit

    for (var y = 0; y < 16; ++y)
      for (var x = 0; x < 16; ++x)
        this.Bits(luma(x, y), 8);

    for (var sample = 0; sample < 2 * 8 * 8; ++sample)
      this.Bits(chroma, 8);

    return this;
  }

  /// <summary>
  /// One <c>I_16x16_2_0_0</c> macroblock: DC prediction, no coded block pattern and no residual.
  /// </summary>
  /// <remarks>
  /// The one macroblock whose reconstruction can be worked out by hand. With no neighbours the DC
  /// prediction of clause 8.3.3 is the mid-grey <c>1 &lt;&lt; (BitDepth − 1)</c>, the coded block
  /// pattern is zero so there is no residual to add, and the luma DC block still has to be read
  /// because an Intra_16x16 macroblock always carries one — which is the empty <c>coeff_token</c>.
  /// </remarks>
  internal H264TestStream FlatIntra16x16Macroblock() {
    this.Unsigned(3); // mb_type: I_16x16_2_0_0 — Table 7-11 index 2, DC, no coded blocks
    this.Unsigned(0); // intra_chroma_pred_mode: DC
    this.Signed(0); // mb_qp_delta
    this.Code("1"); // coeff_token for the luma DC block: TotalCoeff 0, in the 0 <= nC < 2 column
    return this;
  }

  private void _Bit(int bit) {
    this._partial = (this._partial << 1) | (bit & 1);
    if (++this._partialBits != 8)
      return;

    this._payload.Add((byte)this._partial);
    this._partial = 0;
    this._partialBits = 0;
  }
}
