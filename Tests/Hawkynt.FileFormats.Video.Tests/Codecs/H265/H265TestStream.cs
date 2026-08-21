using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.H265.Tests;

/// <summary>
/// Builds H.265 byte streams a bit at a time, so that a test can state exactly what a stream says.
/// </summary>
/// <remarks>
/// Only the parameter sets are written here, and that is the point. What these tests check is the
/// boundary of what the decoder accepts, and every one of those decisions is made from a parameter
/// set before a single coding tree block is read — so a stream that carries the parameter sets and
/// stops is enough to ask the question, and does not need a slice whose every bin would have to be
/// arithmetic coded by hand.
/// <para/>
/// The arithmetic of a decoded picture is not checked from streams built here and could not be: a
/// test that writes its own bitstream can only show that the decoder and the test agree with each
/// other. That was measured against a reference decoder over a corpus of encoded streams, sample by
/// sample, which is the only comparison that can tell agreement from correctness.
/// </remarks>
internal sealed class H265TestStream {

  private int _pending;
  private int _pendingBits;

  /// <summary>The video parameter set as its own NAL unit, which every stream opens with.</summary>
  internal H265TestStream VideoParameterSet() {
    this._Begin();
    this.Bits(4, 0);   // vps_video_parameter_set_id
    this.Bits(1, 1);   // vps_base_layer_internal_flag
    this.Bits(1, 1);   // vps_base_layer_available_flag
    this.Bits(6, 0);   // vps_max_layers_minus1
    this.Bits(3, 0);   // vps_max_sub_layers_minus1
    this.Bits(1, 1);   // vps_temporal_id_nesting_flag
    this.Bits(16, 0xFFFF);
    this._ProfileTierLevel();
    this.Flag(false);  // vps_sub_layer_ordering_info_present_flag
    this.Unsigned(1);  // vps_max_dec_pic_buffering_minus1
    this.Unsigned(0);  // vps_max_num_reorder_pics
    this.Unsigned(0);  // vps_max_latency_increase_plus1
    this.Bits(6, 0);   // vps_max_layer_id
    this.Unsigned(0);  // vps_num_layer_sets_minus1
    this.Flag(false);  // vps_timing_info_present_flag
    this.Flag(false);  // vps_extension_flag
    return this.EndNal(32);
  }

  /// <summary>
  /// A sequence parameter set, with every field a refusal turns on exposed.
  /// </summary>
  internal H265TestStream SequenceParameterSet(
    int chromaFormatIdc = 1,
    bool separateColourPlanes = false,
    int width = 64,
    int height = 64,
    int bitDepthLuma = 8,
    int bitDepthChroma = 8,
    bool pcmEnabled = false,
    bool rangeExtension = false,
    bool multilayerExtension = false,
    bool screenContentExtension = false,
    int profileIdc = 1) {
    this._Begin();
    this.Bits(4, 0);   // sps_video_parameter_set_id
    this.Bits(3, 0);   // sps_max_sub_layers_minus1
    this.Bits(1, 1);   // sps_temporal_id_nesting_flag
    this._ProfileTierLevel(profileIdc);

    this.Unsigned(0);  // sps_seq_parameter_set_id
    this.Unsigned(chromaFormatIdc);
    if (chromaFormatIdc == 3)
      this.Flag(separateColourPlanes);

    this.Unsigned(width);
    this.Unsigned(height);
    this.Flag(false);  // conformance_window_flag
    this.Unsigned(bitDepthLuma - 8);
    this.Unsigned(bitDepthChroma - 8);
    this.Unsigned(4);  // log2_max_pic_order_cnt_lsb_minus4
    this.Flag(false);  // sps_sub_layer_ordering_info_present_flag
    this.Unsigned(1);  // sps_max_dec_pic_buffering_minus1
    this.Unsigned(0);  // sps_max_num_reorder_pics
    this.Unsigned(0);  // sps_max_latency_increase_plus1
    this.Unsigned(0);  // log2_min_luma_coding_block_size_minus3
    this.Unsigned(3);  // log2_diff_max_min_luma_coding_block_size
    this.Unsigned(0);  // log2_min_luma_transform_block_size_minus2
    this.Unsigned(3);  // log2_diff_max_min_luma_transform_block_size
    this.Unsigned(0);  // max_transform_hierarchy_depth_inter
    this.Unsigned(0);  // max_transform_hierarchy_depth_intra
    this.Flag(false);  // scaling_list_enabled_flag
    this.Flag(false);  // amp_enabled_flag
    this.Flag(true);   // sample_adaptive_offset_enabled_flag

    this.Flag(pcmEnabled);
    if (pcmEnabled) {
      this.Bits(4, 7);  // pcm_sample_bit_depth_luma_minus1
      this.Bits(4, 7);  // pcm_sample_bit_depth_chroma_minus1
      this.Unsigned(0); // log2_min_pcm_luma_coding_block_size_minus3
      this.Unsigned(0); // log2_diff_max_min_pcm_luma_coding_block_size
      this.Flag(true);  // pcm_loop_filter_disabled_flag
    }

    this.Unsigned(0);  // num_short_term_ref_pic_sets
    this.Flag(false);  // long_term_ref_pics_present_flag
    this.Flag(true);   // sps_temporal_mvp_enabled_flag
    this.Flag(true);   // strong_intra_smoothing_enabled_flag
    this.Flag(false);  // vui_parameters_present_flag

    var extension = rangeExtension || multilayerExtension || screenContentExtension;
    this.Flag(extension);
    if (extension) {
      this.Flag(rangeExtension);
      this.Flag(multilayerExtension);
      this.Flag(false); // sps_3d_extension_flag
      this.Flag(screenContentExtension);
      this.Bits(4, 0);  // sps_extension_4bits

      if (rangeExtension) {
        // The first of the nine flags, which is enough to be refused by name.
        this.Flag(true);
        for (var i = 1; i < 9; ++i)
          this.Flag(false);
      }
    }

    return this.EndNal(33);
  }

  /// <summary>A picture parameter set, with the fields a refusal turns on exposed.</summary>
  internal H265TestStream PictureParameterSet(
    bool tiles = false,
    bool dependentSliceSegments = false,
    bool rangeExtension = false,
    bool crossComponentPrediction = false,
    int saoOffsetScale = 0) {
    this._Begin();
    this.Unsigned(0);  // pps_pic_parameter_set_id
    this.Unsigned(0);  // pps_seq_parameter_set_id
    this.Flag(dependentSliceSegments);
    this.Flag(false);  // output_flag_present_flag
    this.Bits(3, 0);   // num_extra_slice_header_bits
    this.Flag(true);   // sign_data_hiding_enabled_flag
    this.Flag(false);  // cabac_init_present_flag
    this.Unsigned(0);  // num_ref_idx_l0_default_active_minus1
    this.Unsigned(0);  // num_ref_idx_l1_default_active_minus1
    this.Signed(0);    // init_qp_minus26
    this.Flag(false);  // constrained_intra_pred_flag
    this.Flag(false);  // transform_skip_enabled_flag
    this.Flag(false);  // cu_qp_delta_enabled_flag
    this.Signed(0);    // pps_cb_qp_offset
    this.Signed(0);    // pps_cr_qp_offset
    this.Flag(false);  // pps_slice_chroma_qp_offsets_present_flag
    this.Flag(false);  // weighted_pred_flag
    this.Flag(false);  // weighted_bipred_flag
    this.Flag(false);  // transquant_bypass_enabled_flag

    this.Flag(tiles);
    this.Flag(false);  // entropy_coding_sync_enabled_flag
    if (tiles) {
      this.Unsigned(1); // num_tile_columns_minus1
      this.Unsigned(1); // num_tile_rows_minus1
      this.Flag(true);  // uniform_spacing_flag
      this.Flag(true);  // loop_filter_across_tiles_enabled_flag
    }

    this.Flag(true);   // pps_loop_filter_across_slices_enabled_flag
    this.Flag(false);  // deblocking_filter_control_present_flag
    this.Flag(false);  // pps_scaling_list_data_present_flag
    this.Flag(false);  // lists_modification_present_flag
    this.Unsigned(0);  // log2_parallel_merge_level_minus2
    this.Flag(false);  // slice_segment_header_extension_present_flag

    var extension = rangeExtension;
    this.Flag(extension);
    if (extension) {
      this.Flag(true);  // pps_range_extension_flag
      this.Flag(false); // pps_multilayer_extension_flag
      this.Flag(false); // pps_3d_extension_flag
      this.Flag(false); // pps_scc_extension_flag
      this.Bits(4, 0);  // pps_extension_4bits

      // log2_max_transform_skip_block_size_minus2 is absent: transform_skip_enabled_flag is 0.
      this.Flag(crossComponentPrediction);
      this.Flag(false); // chroma_qp_offset_list_enabled_flag
      this.Unsigned(saoOffsetScale);
      this.Unsigned(0);
    }

    return this.EndNal(34);
  }

  /// <summary>
  /// A slice segment header for an intra refresh picture, and nothing after it.
  /// </summary>
  /// <remarks>
  /// Enough to reach the refusals that live in the header — a dependent segment, a tiled picture —
  /// and no further. The slice data that would follow is arithmetic coded and is not written here.
  /// </remarks>
  internal H265TestStream IntraSliceHeader(bool firstSegment = true, bool dependent = false, int sliceType = 2) {
    this._Begin();
    this.Flag(firstSegment);
    this.Flag(false);  // no_output_of_prior_pics_flag
    this.Unsigned(0);  // slice_pic_parameter_set_id

    if (!firstSegment) {
      this.Flag(dependent);
      this.Bits(1, 1); // slice_segment_address, one bit for a picture of two coding tree blocks
    }

    if (!dependent) {
      this.Unsigned(sliceType); // slice_type: 2 is I
      this.Flag(true);  // slice_sao_luma_flag
      this.Flag(true);  // slice_sao_chroma_flag
      this.Signed(0);   // slice_qp_delta
    }

    this.Bits(1, 1);   // byte_alignment
    this._AlignToByte();
    return this.EndNal(20);
  }

  /// <summary>
  /// The opening of a slice segment header for a picture predicted from other pictures.
  /// </summary>
  /// <remarks>
  /// It stops at <c>slice_type</c>, which is as far as such a picture is read: everything after that
  /// field is only meaningful once the reference pictures it names exist, and the refusal is raised
  /// there. The NAL unit type is a trailing picture, because a random access point may not carry a
  /// predicted slice at all and is rejected earlier for a different reason.
  /// </remarks>
  internal H265TestStream InterSliceHeader(int sliceType) {
    this._Begin();
    this.Flag(true);        // first_slice_segment_in_pic_flag
    this.Unsigned(0);       // slice_pic_parameter_set_id
    this.Unsigned(sliceType);
    this.Bits(1, 1);        // byte_alignment
    this._AlignToByte();
    return this.EndNal(1);  // TRAIL_R
  }

  internal H265TestStream Bits(int count, int value) {
    for (var i = count - 1; i >= 0; --i)
      this._Bit((value >> i) & 1);

    return this;
  }

  internal H265TestStream Flag(bool value) => this.Bits(1, value ? 1 : 0);

  /// <summary>An unsigned Exp-Golomb code — <c>ue(v)</c>.</summary>
  internal H265TestStream Unsigned(int value) {
    var length = 0;
    while (1 << (length + 1) <= value + 1)
      ++length;

    this.Bits(length, 0);
    return this.Bits(length + 1, value + 1);
  }

  /// <summary>A signed Exp-Golomb code — <c>se(v)</c>.</summary>
  internal H265TestStream Signed(int value)
    => this.Unsigned(value <= 0 ? -2 * value : 2 * value - 1);

  /// <summary>Closes the payload and writes it as a NAL unit of the given type.</summary>
  internal H265TestStream EndNal(int type) {
    if (this._pendingBits != 0 || this._payload.Count == 0 || !this._trailed)
      this._Trailing();

    this._output.Add(0);
    this._output.Add(0);
    this._output.Add(0);
    this._output.Add(1);
    this._output.Add((byte)((type << 1) & 0x7E));
    this._output.Add(1);

    // The emulation prevention byte, which the decoder undoes: any pair of zeroes followed by a byte
    // below four would otherwise read as a start code.
    var zeroes = 0;
    foreach (var octet in this._payload) {
      if (zeroes >= 2 && octet <= 3) {
        this._output.Add(3);
        zeroes = 0;
      }

      this._output.Add(octet);
      zeroes = octet == 0 ? zeroes + 1 : 0;
    }

    this._payload.Clear();
    this._trailed = false;
    return this;
  }

  internal byte[] ToArray() => [.. this._output];

  private readonly List<byte> _output = [];
  private readonly List<byte> _payload = [];
  private bool _trailed;

  private void _Begin() {
    this._payload.Clear();
    this._pending = 0;
    this._pendingBits = 0;
    this._trailed = false;
  }

  private void _Bit(int value) {
    this._pending = (this._pending << 1) | (value & 1);
    if (++this._pendingBits != 8)
      return;

    this._payload.Add((byte)this._pending);
    this._pending = 0;
    this._pendingBits = 0;
  }

  private void _AlignToByte() {
    while (this._pendingBits != 0)
      this._Bit(0);
  }

  /// <summary>The stop bit and the zeroes to the byte boundary — <c>rbsp_trailing_bits()</c>.</summary>
  private void _Trailing() {
    this._Bit(1);
    this._AlignToByte();
    this._trailed = true;
  }

  private void _ProfileTierLevel(int profileIdc = 1) {
    this.Bits(2, 0);   // general_profile_space
    this.Bits(1, 0);   // general_tier_flag
    this.Bits(5, profileIdc);

    for (var i = 0; i < 32; ++i)
      this.Bits(1, i == profileIdc ? 1 : 0);

    // The forty-eight bits of source-scan, packing and constraint flags.
    this.Bits(1, 1);   // general_progressive_source_flag
    this.Bits(1, 0);   // general_interlaced_source_flag
    this.Bits(1, 1);   // general_non_packed_constraint_flag
    this.Bits(1, 1);   // general_frame_only_constraint_flag
    for (var i = 0; i < 44; ++i)
      this.Bits(1, 0);

    this.Bits(8, 30);  // general_level_idc: level 1
  }
}
