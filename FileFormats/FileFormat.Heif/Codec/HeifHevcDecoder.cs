using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace FileFormat.Heif.Codec;

/// <summary>Parsed HEVC Sequence Parameter Set (SPS).</summary>
internal sealed class HevcSps {
  public int ProfileIdc { get; set; }
  public int LevelIdc { get; set; }
  public int ChromaFormatIdc { get; set; } = 1;     // 0=mono, 1=4:2:0, 2=4:2:2, 3=4:4:4
  public bool SeparateColourPlaneFlag { get; set; }
  public int PicWidthInLumaSamples { get; set; }
  public int PicHeightInLumaSamples { get; set; }
  public int BitDepth { get; set; } = 8;
  public int BitDepthC { get; set; } = 8;
  public int Log2MaxPicOrderCntLsb { get; set; } = 16;
  public int Log2MinCbSize { get; set; } = 3;   // log2_min_luma_coding_block_size_minus3 + 3
  public int Log2DiffMaxMinCbSize { get; set; } = 3;
  public int Log2MinTbSize { get; set; } = 2;   // log2_min_luma_transform_block_size_minus2 + 2
  public int Log2DiffMaxMinTbSize { get; set; } = 3;
  public int MaxTransformHierarchyDepthIntra { get; set; }
  public bool AmpEnabled { get; set; }
  public bool SampleAdaptiveOffsetEnabled { get; set; }
  public bool PcmEnabled { get; set; }
  public int NumShortTermRefPicSets { get; set; }
  public bool LongTermRefPicsPresent { get; set; }
  public bool SpsTemporalMvpEnabled { get; set; }
  public bool StrongIntraSmoothingEnabled { get; set; }
  public int MatrixCoeffs { get; set; } = 1; // BT.709 default
  public bool FullRange { get; set; }

  public int MinCbSize => 1 << Log2MinCbSize;
  public int MinCbSizeLog2 => Log2MinCbSize;
  public int CtbSizeLog2 => Log2MinCbSize + Log2DiffMaxMinCbSize;
  public int CtbSize => 1 << CtbSizeLog2;

  /// <summary>Parses SPS from RBSP data.</summary>
  public static HevcSps Parse(byte[] rbsp) {
    var reader = new HeifBitReader(rbsp);
    var sps = new HevcSps();

    // sps_video_parameter_set_id
    reader.ReadBits(4);
    // sps_max_sub_layers_minus1
    var maxSubLayers = (int)reader.ReadBits(3) + 1;
    // sps_temporal_id_nesting_flag
    reader.ReadBool();

    // profile_tier_level
    _SkipProfileTierLevel(reader, maxSubLayers);

    // sps_seq_parameter_set_id
    reader.ReadUe();

    sps.ChromaFormatIdc = (int)reader.ReadUe();
    if (sps.ChromaFormatIdc == 3)
      sps.SeparateColourPlaneFlag = reader.ReadBool();

    sps.PicWidthInLumaSamples = (int)reader.ReadUe();
    sps.PicHeightInLumaSamples = (int)reader.ReadUe();

    // conformance_window_flag
    if (reader.ReadBool()) {
      reader.ReadUe(); // conf_win_left_offset
      reader.ReadUe(); // conf_win_right_offset
      reader.ReadUe(); // conf_win_top_offset
      reader.ReadUe(); // conf_win_bottom_offset
    }

    sps.BitDepth = (int)reader.ReadUe() + 8;  // bit_depth_luma_minus8
    sps.BitDepthC = (int)reader.ReadUe() + 8; // bit_depth_chroma_minus8
    sps.Log2MaxPicOrderCntLsb = (int)reader.ReadUe() + 4;

    // sps_sub_layer_ordering_info_present_flag
    var subLayerOrderingPresent = reader.ReadBool();
    var startIdx = subLayerOrderingPresent ? 0 : maxSubLayers - 1;
    for (var i = startIdx; i < maxSubLayers; ++i) {
      reader.ReadUe(); // sps_max_dec_pic_buffering_minus1[i]
      reader.ReadUe(); // sps_max_num_reorder_pics[i]
      reader.ReadUe(); // sps_max_latency_increase_plus1[i]
    }

    sps.Log2MinCbSize = (int)reader.ReadUe() + 3;     // log2_min_luma_coding_block_size_minus3
    sps.Log2DiffMaxMinCbSize = (int)reader.ReadUe();
    sps.Log2MinTbSize = (int)reader.ReadUe() + 2;     // log2_min_luma_transform_block_size_minus2
    sps.Log2DiffMaxMinTbSize = (int)reader.ReadUe();
    sps.MaxTransformHierarchyDepthIntra = (int)reader.ReadUe();

    // max_transform_hierarchy_depth_inter
    reader.ReadUe();

    // scaling_list_enabled_flag
    if (reader.ReadBool()) {
      // sps_scaling_list_data_present_flag
      if (reader.ReadBool())
        _SkipScalingListData(reader);
    }

    sps.AmpEnabled = reader.ReadBool();
    sps.SampleAdaptiveOffsetEnabled = reader.ReadBool();

    sps.PcmEnabled = reader.ReadBool();
    if (sps.PcmEnabled) {
      reader.ReadBits(4); // pcm_sample_bit_depth_luma_minus1
      reader.ReadBits(4); // pcm_sample_bit_depth_chroma_minus1
      reader.ReadUe();    // log2_min_pcm_luma_coding_block_size_minus3
      reader.ReadUe();    // log2_diff_max_min_pcm_luma_coding_block_size
      reader.ReadBool();  // pcm_loop_filter_disabled_flag
    }

    sps.NumShortTermRefPicSets = (int)reader.ReadUe();
    // Skip short-term ref pic sets (complex, not needed for still images)

    return sps;
  }

  private static void _SkipProfileTierLevel(HeifBitReader reader, int maxSubLayers) {
    // general_profile_space(2) + general_tier_flag(1) + general_profile_idc(5)
    reader.ReadBits(8);
    // general_profile_compatibility_flag[32]
    reader.ReadBits(32);
    // general_progressive_source_flag ... general_reserved_zero_43bits
    reader.ReadBits(48);
    // general_level_idc
    reader.ReadBits(8);

    // sub_layer stuff
    for (var i = 0; i < maxSubLayers - 1; ++i) {
      reader.ReadBool(); // sub_layer_profile_present_flag
      reader.ReadBool(); // sub_layer_level_present_flag
    }
    if (maxSubLayers > 1) {
      for (var i = maxSubLayers - 1; i < 8; ++i)
        reader.ReadBits(2); // reserved_zero_2bits
    }
  }

  private static void _SkipScalingListData(HeifBitReader reader) {
    for (var sizeId = 0; sizeId < 4; ++sizeId) {
      var matrixCount = sizeId == 3 ? 2 : 6;
      for (var matrixId = 0; matrixId < matrixCount; ++matrixId) {
        if (!reader.ReadBool()) {
          reader.ReadUe(); // scaling_list_pred_matrix_id_delta
        } else {
          var coefNum = Math.Min(64, 1 << (4 + (sizeId << 1)));
          if (sizeId > 1)
            reader.ReadSe(); // scaling_list_dc_coef

          for (var i = 0; i < coefNum; ++i)
            reader.ReadSe(); // scaling_list_delta_coef
        }
      }
    }
  }
}

/// <summary>Parsed HEVC Picture Parameter Set (PPS).</summary>
internal sealed class HevcPps {
  public int PpsId { get; set; }
  public int SpsId { get; set; }
  public bool DependentSliceSegmentsEnabled { get; set; }
  public bool OutputFlagPresent { get; set; }
  public int NumExtraSliceHeaderBits { get; set; }
  public bool SignDataHidingEnabled { get; set; }
  public bool CabacInitPresent { get; set; }
  public int InitQpMinus26 { get; set; }
  public bool ConstrainedIntraPred { get; set; }
  public bool TransformSkipEnabled { get; set; }
  public bool CuQpDeltaEnabled { get; set; }
  public int DiffCuQpDeltaDepth { get; set; }
  public int CbQpOffset { get; set; }
  public int CrQpOffset { get; set; }
  public bool TilesEnabled { get; set; }
  public bool EntropyCodingSyncEnabled { get; set; }
  public bool LoopFilterAcrossSlicesEnabled { get; set; }
  public bool DeblockingFilterControlPresent { get; set; }
  public bool PpsDeblockingFilterDisabled { get; set; }

  /// <summary>Parses PPS from RBSP data.</summary>
  public static HevcPps Parse(byte[] rbsp) {
    var reader = new HeifBitReader(rbsp);
    var pps = new HevcPps();

    pps.PpsId = (int)reader.ReadUe();
    pps.SpsId = (int)reader.ReadUe();
    pps.DependentSliceSegmentsEnabled = reader.ReadBool();
    pps.OutputFlagPresent = reader.ReadBool();
    pps.NumExtraSliceHeaderBits = (int)reader.ReadBits(3);
    pps.SignDataHidingEnabled = reader.ReadBool();
    pps.CabacInitPresent = reader.ReadBool();

    reader.ReadUe(); // num_ref_idx_l0_default_active_minus1
    reader.ReadUe(); // num_ref_idx_l1_default_active_minus1

    pps.InitQpMinus26 = reader.ReadSe();
    pps.ConstrainedIntraPred = reader.ReadBool();
    pps.TransformSkipEnabled = reader.ReadBool();
    pps.CuQpDeltaEnabled = reader.ReadBool();
    if (pps.CuQpDeltaEnabled)
      pps.DiffCuQpDeltaDepth = (int)reader.ReadUe();

    pps.CbQpOffset = reader.ReadSe();
    pps.CrQpOffset = reader.ReadSe();

    reader.ReadBool(); // pps_slice_chroma_qp_offsets_present_flag
    reader.ReadBool(); // weighted_pred_flag
    reader.ReadBool(); // weighted_bipred_flag
    reader.ReadBool(); // transquant_bypass_enabled_flag

    pps.TilesEnabled = reader.ReadBool();
    pps.EntropyCodingSyncEnabled = reader.ReadBool();

    if (pps.TilesEnabled) {
      var numTileCols = (int)reader.ReadUe() + 1;
      var numTileRows = (int)reader.ReadUe() + 1;
      var uniformSpacing = reader.ReadBool();
      if (!uniformSpacing) {
        for (var i = 0; i < numTileCols - 1; ++i)
          reader.ReadUe();
        for (var i = 0; i < numTileRows - 1; ++i)
          reader.ReadUe();
      }
      reader.ReadBool(); // loop_filter_across_tiles_enabled_flag
    }

    pps.LoopFilterAcrossSlicesEnabled = reader.ReadBool();
    pps.DeblockingFilterControlPresent = reader.ReadBool();
    if (pps.DeblockingFilterControlPresent) {
      reader.ReadBool(); // deblocking_filter_override_enabled_flag
      pps.PpsDeblockingFilterDisabled = reader.ReadBool();
      if (!pps.PpsDeblockingFilterDisabled) {
        reader.ReadSe(); // pps_beta_offset_div2
        reader.ReadSe(); // pps_tc_offset_div2
      }
    }

    return pps;
  }
}

/// <summary>Top-level HEVC I-frame decoder for HEIF images.
/// Parses hvcC configuration box, NAL units (VPS, SPS, PPS, IDR slice), and orchestrates decoding.</summary>
internal static class HeifHevcDecoder {

  /// <summary>Decodes HEVC data from a HEIF file into RGB24 pixel data.</summary>
  /// <param name="hvcCData">Optional hvcC configuration box data (contains VPS/SPS/PPS).</param>
  /// <param name="mdatData">Raw mdat payload (HEVC NAL units).</param>
  /// <returns>Tuple of (width, height, rgbPixelData).</returns>
  public static (int Width, int Height, byte[] RgbData) Decode(byte[]? hvcCData, byte[] mdatData) {
    ArgumentNullException.ThrowIfNull(mdatData);
    if (mdatData.Length == 0)
      throw new InvalidOperationException("HEVC: empty mdat data.");

    // Step 1: Parse parameter sets from hvcC (if available)
    var nalLengthSize = 4;
    HevcSps? sps = null;
    HevcPps? pps = null;

    if (hvcCData != null && hvcCData.Length >= 23)
      _ParseHvcC(hvcCData, out nalLengthSize, out sps, out pps);

    // Step 2: Parse NAL units from mdat
    List<HevcNalUnit> nalUnits;
    if (_LooksLikeLengthPrefixed(mdatData, nalLengthSize))
      nalUnits = HeifNalParser.ParseLengthPrefixed(mdatData, 0, mdatData.Length, nalLengthSize);
    else
      nalUnits = HeifNalParser.ParseAnnexB(mdatData, 0, mdatData.Length);

    // Process NAL units: find SPS/PPS/IDR
    byte[]? sliceData = null;
    var sliceOffset = 0;
    var sliceLength = 0;

    foreach (var nal in nalUnits) {
      var rbsp = HeifNalParser.RemoveEmulationPrevention(mdatData, nal.PayloadOffset, nal.PayloadSize);
      switch (nal.Type) {
        case HevcNalUnitType.SpsNut:
          sps = HevcSps.Parse(rbsp);
          break;
        case HevcNalUnitType.PpsNut:
          pps = HevcPps.Parse(rbsp);
          break;
        case HevcNalUnitType.IdrWRadl:
        case HevcNalUnitType.IdrNLp:
        case HevcNalUnitType.CraNut:
        case HevcNalUnitType.BlaWLp:
        case HevcNalUnitType.BlaWRadl:
        case HevcNalUnitType.BlaNLp:
          sliceData = rbsp;
          sliceOffset = 0;
          sliceLength = rbsp.Length;
          break;
      }
    }

    if (sps == null)
      throw new InvalidOperationException("HEVC: no SPS found.");
    if (pps == null)
      throw new InvalidOperationException("HEVC: no PPS found.");

    // Step 3: Allocate frame buffers
    var width = sps.PicWidthInLumaSamples;
    var height = sps.PicHeightInLumaSamples;
    var numPlanes = sps.ChromaFormatIdc > 0 ? 3 : 1;

    var planes = new short[numPlanes][];
    var planeWidths = new int[numPlanes];
    var planeHeights = new int[numPlanes];
    var planeStrides = new int[numPlanes];

    for (var p = 0; p < numPlanes; ++p) {
      var subX = p > 0 && sps.ChromaFormatIdc == 1 ? 1 : 0;
      var subY = p > 0 && sps.ChromaFormatIdc == 1 ? 1 : 0;
      planeWidths[p] = (width + subX) >> subX;
      planeHeights[p] = (height + subY) >> subY;
      planeStrides[p] = planeWidths[p];
      planes[p] = new short[planeStrides[p] * planeHeights[p]];

      var mid = (short)(1 << (sps.BitDepth - 1));
      Array.Fill(planes[p], mid);
    }

    // Step 4: Decode slice data
    if (sliceData != null && sliceLength > 0) {
      // Skip slice header (simplified: skip first few bytes to get to CABAC data)
      var headerLen = _EstimateSliceHeaderLength(sliceData, sliceOffset, sliceLength, sps, pps);
      var cabacOffset = sliceOffset + headerLen;
      var cabacLength = sliceLength - headerLen;

      if (cabacLength > 0) {
        var sliceDecoder = new HeifSliceDecoder(sps, pps, planes, planeWidths, planeHeights, planeStrides);
        sliceDecoder.DecodeSlice(sliceData, cabacOffset, cabacLength);
      }
    }

    // Step 5: Convert to RGB
    byte[] rgbData;
    if (numPlanes == 1) {
      rgbData = HeifYuvToRgb.ConvertMonoToRgb(planes[0], planeStrides[0], width, height, sps.BitDepth, sps.FullRange);
    } else if (sps.ChromaFormatIdc == 1) {
      rgbData = HeifYuvToRgb.ConvertYuv420ToRgb(
        planes[0], planeStrides[0],
        planes[1], planeStrides[1],
        planes[2], planeStrides[2],
        width, height, sps.BitDepth, sps.MatrixCoeffs, sps.FullRange);
    } else {
      rgbData = HeifYuvToRgb.ConvertYuv444ToRgb(
        planes[0], planeStrides[0],
        planes[1], planeStrides[1],
        planes[2], planeStrides[2],
        width, height, sps.BitDepth, sps.MatrixCoeffs, sps.FullRange);
    }

    return (width, height, rgbData);
  }

  private static void _ParseHvcC(byte[] data, out int nalLengthSize, out HevcSps? sps, out HevcPps? pps) {
    sps = null;
    pps = null;

    // HEVCDecoderConfigurationRecord (ISO/IEC 14496-15 section 8.3.3.1.2)
    // configurationVersion(1) + profile_indication fields(22) + ...
    if (data.Length < 23) {
      nalLengthSize = 4;
      return;
    }

    nalLengthSize = (data[21] & 0x03) + 1;
    var numArrays = data[22];
    var offset = 23;

    for (var i = 0; i < numArrays && offset < data.Length; ++i) {
      if (offset >= data.Length)
        break;

      var nalType = (HevcNalUnitType)(data[offset] & 0x3F);
      ++offset;

      if (offset + 2 > data.Length)
        break;

      var numNalus = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
      offset += 2;

      for (var j = 0; j < numNalus && offset + 2 <= data.Length; ++j) {
        var naluLength = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
        offset += 2;

        if (offset + naluLength > data.Length)
          break;

        // Skip NAL header (2 bytes for HEVC)
        if (naluLength >= 2) {
          var rbsp = HeifNalParser.RemoveEmulationPrevention(data, offset + 2, naluLength - 2);
          switch (nalType) {
            case HevcNalUnitType.SpsNut:
              sps = HevcSps.Parse(rbsp);
              break;
            case HevcNalUnitType.PpsNut:
              pps = HevcPps.Parse(rbsp);
              break;
          }
        }

        offset += naluLength;
      }
    }
  }

  private static bool _LooksLikeLengthPrefixed(byte[] data, int nalLengthSize) {
    if (data.Length < nalLengthSize)
      return false;

    // Check if first N bytes as big-endian length makes sense
    var length = 0;
    for (var i = 0; i < nalLengthSize; ++i)
      length = (length << 8) | data[i];

    return length > 0 && length < data.Length;
  }

  private static int _EstimateSliceHeaderLength(byte[] data, int offset, int length, HevcSps sps, HevcPps pps) {
    // Parse minimal slice header to find where CABAC data begins
    if (length < 3)
      return Math.Min(length, 3);

    var reader = new HeifBitReader(data, offset, length);

    try {
      // first_slice_segment_in_pic_flag
      reader.ReadBool();

      // no_output_of_prior_pics_flag (for IDR/BLA)
      reader.ReadBool();

      // slice_pic_parameter_set_id
      reader.ReadUe();

      // slice_type
      reader.ReadUe();

      // For I-slices, skip to alignment
      reader.ByteAlign();

      var headerBits = reader.BitsRemaining;
      var totalBits = length * 8;
      return (totalBits - headerBits + 7) / 8;
    } catch {
      return Math.Min(length, 5);
    }
  }
}
