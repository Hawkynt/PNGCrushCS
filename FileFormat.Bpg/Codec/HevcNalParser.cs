using System;
using System.Collections.Generic;

namespace FileFormat.Bpg.Codec;

/// <summary>NAL unit parser for HEVC: VPS, SPS, PPS, and slice header parsing.</summary>
internal static class HevcNalParser {

  /// <summary>HEVC NAL unit types relevant for BPG decoding.</summary>
  public enum NalUnitType {
    TrailN = 0,
    TrailR = 1,
    IdrWRadl = 19,
    IdrNLp = 20,
    CraNut = 21,
    VpsNut = 32,
    SpsNut = 33,
    PpsNut = 34,
  }

  /// <summary>Parsed Video Parameter Set.</summary>
  public sealed class Vps {
    public int Id { get; init; }
    public int MaxSubLayersMinus1 { get; init; }
  }

  /// <summary>Parsed Sequence Parameter Set.</summary>
  public sealed class Sps {
    public int Id { get; init; }
    public int VpsId { get; init; }
    public int MaxSubLayersMinus1 { get; init; }
    public int ChromaFormatIdc { get; init; }
    public bool SeparateColourPlane { get; init; }
    public int PicWidthInLumaSamples { get; init; }
    public int PicHeightInLumaSamples { get; init; }
    public int BitDepthLumaMinus8 { get; init; }
    public int BitDepthChromaMinus8 { get; init; }
    public int Log2MaxPicOrderCntLsbMinus4 { get; init; }
    public int Log2MinLumaCodingBlockSizeMinus3 { get; init; }
    public int Log2DiffMaxMinLumaCodingBlockSize { get; init; }
    public int Log2MinTransformBlockSizeMinus2 { get; init; }
    public int Log2DiffMaxMinTransformBlockSize { get; init; }
    public int MaxTransformHierarchyDepthInter { get; init; }
    public int MaxTransformHierarchyDepthIntra { get; init; }
    public bool ScalingListEnabled { get; init; }
    public bool AmpEnabled { get; init; }
    public bool SaoEnabled { get; init; }
    public bool PcmEnabled { get; init; }
    public int PcmSampleBitDepthLumaMinus1 { get; init; }
    public int PcmSampleBitDepthChromaMinus1 { get; init; }
    public int Log2MinPcmLumaCodingBlockSizeMinus3 { get; init; }
    public int Log2DiffMaxMinPcmLumaCodingBlockSize { get; init; }
    public bool PcmLoopFilterDisabled { get; init; }
    public bool StrongIntraSmoothingEnabled { get; init; }
    public int CtbLog2SizeY { get; init; }
    public int CtbSizeY { get; init; }
    public int MinCbLog2SizeY { get; init; }
    public int MinCbSizeY { get; init; }
    public int PicWidthInCtbs { get; init; }
    public int PicHeightInCtbs { get; init; }

    // Conformance window
    public bool ConformanceWindowPresent { get; init; }
    public int ConfWinLeftOffset { get; init; }
    public int ConfWinRightOffset { get; init; }
    public int ConfWinTopOffset { get; init; }
    public int ConfWinBottomOffset { get; init; }
  }

  /// <summary>Parsed Picture Parameter Set.</summary>
  public sealed class Pps {
    public int Id { get; init; }
    public int SpsId { get; init; }
    public bool DependentSliceSegmentsEnabled { get; init; }
    public bool OutputFlagPresent { get; init; }
    public int NumExtraSliceHeaderBits { get; init; }
    public bool SignDataHidingEnabled { get; init; }
    public bool CabacInitPresent { get; init; }
    public int NumRefIdxL0DefaultActiveMinus1 { get; init; }
    public int NumRefIdxL1DefaultActiveMinus1 { get; init; }
    public int InitQpMinus26 { get; init; }
    public bool ConstrainedIntraPred { get; init; }
    public bool TransformSkipEnabled { get; init; }
    public bool CuQpDeltaEnabled { get; init; }
    public int DiffCuQpDeltaDepth { get; init; }
    public int CbQpOffset { get; init; }
    public int CrQpOffset { get; init; }
    public bool SliceChromaQpOffsetsPresent { get; init; }
    public bool WeightedPred { get; init; }
    public bool WeightedBiPred { get; init; }
    public bool TransquantBypassEnabled { get; init; }
    public bool TilesEnabled { get; init; }
    public bool EntropyCodingSyncEnabled { get; init; }
    public bool LoopFilterAcrossSlicesEnabled { get; init; }
    public bool DeblockingFilterControlPresent { get; init; }
    public bool DeblockingFilterOverrideEnabled { get; init; }
    public bool DeblockingFilterDisabled { get; init; }
    public int BetaOffset { get; init; }
    public int TcOffset { get; init; }
    public bool ScalingListDataPresent { get; init; }
    public bool ListsModificationPresent { get; init; }
    public int Log2ParallelMergeLevelMinus2 { get; init; }
    public bool SliceSegmentHeaderExtensionPresent { get; init; }
  }

  /// <summary>Parsed slice header.</summary>
  public sealed class SliceHeader {
    public bool FirstSliceSegmentInPic { get; init; }
    public NalUnitType NalType { get; init; }
    public int PpsId { get; init; }
    public int SliceType { get; init; } // 0=B, 1=P, 2=I
    public int SliceQp { get; init; }
    public bool SaoLumaFlag { get; init; }
    public bool SaoChromaFlag { get; init; }
    public bool DeblockingFilterDisabled { get; init; }
    public int BetaOffset { get; init; }
    public int TcOffset { get; init; }
    public int CabacInitByteOffset { get; init; } // Byte offset where CABAC data starts
  }

  /// <summary>Parses BPG-embedded HEVC NAL units and returns decoded parameter sets and slice data.</summary>
  public static (Vps? vps, Sps sps, Pps pps, SliceHeader sliceHeader, byte[] sliceData) ParseBpgHevcData(byte[] hevcData) {
    var nalUnits = _ExtractNalUnits(hevcData);

    Vps? vps = null;
    Sps? sps = null;
    Pps? pps = null;
    SliceHeader? sliceHeader = null;
    byte[]? sliceData = null;

    foreach (var (nalType, rbspData) in nalUnits) {
      switch (nalType) {
        case NalUnitType.VpsNut:
          vps = _ParseVps(rbspData);
          break;
        case NalUnitType.SpsNut:
          sps = _ParseSps(rbspData);
          break;
        case NalUnitType.PpsNut:
          pps = _ParsePps(rbspData);
          break;
        case NalUnitType.IdrWRadl:
        case NalUnitType.IdrNLp:
        case NalUnitType.CraNut:
        case NalUnitType.TrailN:
        case NalUnitType.TrailR:
          if (sps != null && pps != null) {
            sliceHeader = _ParseSliceHeader(rbspData, nalType, sps, pps);
            sliceData = rbspData;
          }
          break;
      }
    }

    if (sps == null)
      throw new InvalidOperationException("No SPS found in BPG HEVC data.");
    if (pps == null)
      throw new InvalidOperationException("No PPS found in BPG HEVC data.");
    if (sliceHeader == null || sliceData == null)
      throw new InvalidOperationException("No slice data found in BPG HEVC data.");

    return (vps, sps, pps, sliceHeader, sliceData);
  }

  /// <summary>Extracts NAL units from BPG-format HEVC data (length-prefixed, no start codes).</summary>
  private static List<(NalUnitType type, byte[] rbsp)> _ExtractNalUnits(byte[] data) {
    var units = new List<(NalUnitType, byte[])>();
    var offset = 0;

    while (offset + 4 < data.Length) {
      // BPG uses 4-byte big-endian length prefix
      var nalLength = (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
      offset += 4;

      if (nalLength <= 0 || offset + nalLength > data.Length)
        break;

      // Parse NAL unit header (2 bytes)
      if (nalLength < 2) {
        offset += nalLength;
        continue;
      }

      var nalHeader0 = data[offset];
      var nalHeader1 = data[offset + 1];

      // forbidden_zero_bit(1), nal_unit_type(6), nuh_layer_id(6), nuh_temporal_id_plus1(3)
      var nalType = (NalUnitType)((nalHeader0 >> 1) & 0x3F);

      // Extract RBSP (remove emulation prevention bytes 00 00 03 -> 00 00)
      var rbsp = _RemoveEmulationPrevention(data, offset + 2, nalLength - 2);

      units.Add((nalType, rbsp));
      offset += nalLength;
    }

    return units;
  }

  private static byte[] _RemoveEmulationPrevention(byte[] data, int offset, int length) {
    var result = new List<byte>(length);
    for (var i = 0; i < length; ++i) {
      if (i + 2 < length && data[offset + i] == 0 && data[offset + i + 1] == 0 && data[offset + i + 2] == 3) {
        result.Add(0);
        result.Add(0);
        i += 2; // Skip the 0x03 emulation prevention byte
      } else {
        result.Add(data[offset + i]);
      }
    }
    return result.ToArray();
  }

  private static Vps _ParseVps(byte[] rbsp) {
    var br = new HevcBitReader(rbsp);
    var vpsId = (int)br.ReadBits(4);
    br.SkipBits(2); // vps_reserved_three_2bits
    var maxSubLayersMinus1 = (int)br.ReadBits(3);
    br.ReadFlag(); // vps_temporal_id_nesting_flag

    br.SkipBits(16); // vps_reserved_0xffff_16bits

    // profile_tier_level
    _SkipProfileTierLevel(br, maxSubLayersMinus1);

    return new() {
      Id = vpsId,
      MaxSubLayersMinus1 = maxSubLayersMinus1,
    };
  }

  private static Sps _ParseSps(byte[] rbsp) {
    var br = new HevcBitReader(rbsp);

    var vpsId = (int)br.ReadBits(4);
    var maxSubLayersMinus1 = (int)br.ReadBits(3);
    br.ReadFlag(); // sps_temporal_id_nesting_flag

    _SkipProfileTierLevel(br, maxSubLayersMinus1);

    var spsId = (int)br.ReadUe();
    var chromaFormatIdc = (int)br.ReadUe();
    var separateColourPlane = false;
    if (chromaFormatIdc == 3)
      separateColourPlane = br.ReadFlag();

    var picWidth = (int)br.ReadUe();
    var picHeight = (int)br.ReadUe();

    // Conformance window
    var confWindowPresent = br.ReadFlag();
    int confWinLeft = 0, confWinRight = 0, confWinTop = 0, confWinBottom = 0;
    if (confWindowPresent) {
      confWinLeft = (int)br.ReadUe();
      confWinRight = (int)br.ReadUe();
      confWinTop = (int)br.ReadUe();
      confWinBottom = (int)br.ReadUe();
    }

    var bitDepthLumaMinus8 = (int)br.ReadUe();
    var bitDepthChromaMinus8 = (int)br.ReadUe();
    var log2MaxPicOrderCntLsbMinus4 = (int)br.ReadUe();

    // sub_layer_ordering_info_present_flag
    var subLayerOrderingInfoPresent = br.ReadFlag();
    var startIdx = subLayerOrderingInfoPresent ? 0 : maxSubLayersMinus1;
    for (var i = startIdx; i <= maxSubLayersMinus1; ++i) {
      br.ReadUe(); // sps_max_dec_pic_buffering_minus1
      br.ReadUe(); // sps_max_num_reorder_pics
      br.ReadUe(); // sps_max_latency_increase_plus1
    }

    var log2MinLumaCodingBlockSizeMinus3 = (int)br.ReadUe();
    var log2DiffMaxMinLumaCodingBlockSize = (int)br.ReadUe();
    var log2MinTransformBlockSizeMinus2 = (int)br.ReadUe();
    var log2DiffMaxMinTransformBlockSize = (int)br.ReadUe();
    var maxTransformHierarchyDepthInter = (int)br.ReadUe();
    var maxTransformHierarchyDepthIntra = (int)br.ReadUe();

    var scalingListEnabled = br.ReadFlag();
    if (scalingListEnabled) {
      var scalingListDataPresent = br.ReadFlag();
      if (scalingListDataPresent)
        _SkipScalingListData(br);
    }

    var ampEnabled = br.ReadFlag();
    var saoEnabled = br.ReadFlag();
    var pcmEnabled = br.ReadFlag();

    var pcmSampleBitDepthLumaMinus1 = 0;
    var pcmSampleBitDepthChromaMinus1 = 0;
    var log2MinPcmLumaCodingBlockSizeMinus3 = 0;
    var log2DiffMaxMinPcmLumaCodingBlockSize = 0;
    var pcmLoopFilterDisabled = false;

    if (pcmEnabled) {
      pcmSampleBitDepthLumaMinus1 = (int)br.ReadBits(4);
      pcmSampleBitDepthChromaMinus1 = (int)br.ReadBits(4);
      log2MinPcmLumaCodingBlockSizeMinus3 = (int)br.ReadUe();
      log2DiffMaxMinPcmLumaCodingBlockSize = (int)br.ReadUe();
      pcmLoopFilterDisabled = br.ReadFlag();
    }

    // Short-term reference picture sets
    var numShortTermRefPicSets = (int)br.ReadUe();
    for (var i = 0; i < numShortTermRefPicSets; ++i)
      _SkipShortTermRefPicSet(br, i, numShortTermRefPicSets);

    // Long-term reference pictures
    var longTermRefPicsPresent = br.ReadFlag();
    if (longTermRefPicsPresent) {
      var numLongTermRefPics = (int)br.ReadUe();
      for (var i = 0; i < numLongTermRefPics; ++i) {
        br.SkipBits(log2MaxPicOrderCntLsbMinus4 + 4); // lt_ref_pic_poc_lsb_sps
        br.ReadFlag(); // used_by_curr_pic_lt_sps_flag
      }
    }

    br.ReadFlag(); // sps_temporal_mvp_enabled_flag
    var strongIntraSmoothingEnabled = br.ReadFlag();

    // Compute derived values
    var minCbLog2SizeY = log2MinLumaCodingBlockSizeMinus3 + 3;
    var ctbLog2SizeY = minCbLog2SizeY + log2DiffMaxMinLumaCodingBlockSize;
    var ctbSizeY = 1 << ctbLog2SizeY;
    var minCbSizeY = 1 << minCbLog2SizeY;
    var picWidthInCtbs = (picWidth + ctbSizeY - 1) / ctbSizeY;
    var picHeightInCtbs = (picHeight + ctbSizeY - 1) / ctbSizeY;

    return new() {
      Id = spsId,
      VpsId = vpsId,
      MaxSubLayersMinus1 = maxSubLayersMinus1,
      ChromaFormatIdc = chromaFormatIdc,
      SeparateColourPlane = separateColourPlane,
      PicWidthInLumaSamples = picWidth,
      PicHeightInLumaSamples = picHeight,
      BitDepthLumaMinus8 = bitDepthLumaMinus8,
      BitDepthChromaMinus8 = bitDepthChromaMinus8,
      Log2MaxPicOrderCntLsbMinus4 = log2MaxPicOrderCntLsbMinus4,
      Log2MinLumaCodingBlockSizeMinus3 = log2MinLumaCodingBlockSizeMinus3,
      Log2DiffMaxMinLumaCodingBlockSize = log2DiffMaxMinLumaCodingBlockSize,
      Log2MinTransformBlockSizeMinus2 = log2MinTransformBlockSizeMinus2,
      Log2DiffMaxMinTransformBlockSize = log2DiffMaxMinTransformBlockSize,
      MaxTransformHierarchyDepthInter = maxTransformHierarchyDepthInter,
      MaxTransformHierarchyDepthIntra = maxTransformHierarchyDepthIntra,
      ScalingListEnabled = scalingListEnabled,
      AmpEnabled = ampEnabled,
      SaoEnabled = saoEnabled,
      PcmEnabled = pcmEnabled,
      PcmSampleBitDepthLumaMinus1 = pcmSampleBitDepthLumaMinus1,
      PcmSampleBitDepthChromaMinus1 = pcmSampleBitDepthChromaMinus1,
      Log2MinPcmLumaCodingBlockSizeMinus3 = log2MinPcmLumaCodingBlockSizeMinus3,
      Log2DiffMaxMinPcmLumaCodingBlockSize = log2DiffMaxMinPcmLumaCodingBlockSize,
      PcmLoopFilterDisabled = pcmLoopFilterDisabled,
      StrongIntraSmoothingEnabled = strongIntraSmoothingEnabled,
      CtbLog2SizeY = ctbLog2SizeY,
      CtbSizeY = ctbSizeY,
      MinCbLog2SizeY = minCbLog2SizeY,
      MinCbSizeY = minCbSizeY,
      PicWidthInCtbs = picWidthInCtbs,
      PicHeightInCtbs = picHeightInCtbs,
      ConformanceWindowPresent = confWindowPresent,
      ConfWinLeftOffset = confWinLeft,
      ConfWinRightOffset = confWinRight,
      ConfWinTopOffset = confWinTop,
      ConfWinBottomOffset = confWinBottom,
    };
  }

  private static Pps _ParsePps(byte[] rbsp) {
    var br = new HevcBitReader(rbsp);

    var ppsId = (int)br.ReadUe();
    var spsId = (int)br.ReadUe();
    var dependentSliceSegmentsEnabled = br.ReadFlag();
    var outputFlagPresent = br.ReadFlag();
    var numExtraSliceHeaderBits = (int)br.ReadBits(3);
    var signDataHidingEnabled = br.ReadFlag();
    var cabacInitPresent = br.ReadFlag();
    var numRefIdxL0DefaultActiveMinus1 = (int)br.ReadUe();
    var numRefIdxL1DefaultActiveMinus1 = (int)br.ReadUe();
    var initQpMinus26 = br.ReadSe();
    var constrainedIntraPred = br.ReadFlag();
    var transformSkipEnabled = br.ReadFlag();
    var cuQpDeltaEnabled = br.ReadFlag();

    var diffCuQpDeltaDepth = 0;
    if (cuQpDeltaEnabled)
      diffCuQpDeltaDepth = (int)br.ReadUe();

    var cbQpOffset = br.ReadSe();
    var crQpOffset = br.ReadSe();
    var sliceChromaQpOffsetsPresent = br.ReadFlag();
    var weightedPred = br.ReadFlag();
    var weightedBiPred = br.ReadFlag();
    var transquantBypassEnabled = br.ReadFlag();
    var tilesEnabled = br.ReadFlag();
    var entropyCodingSyncEnabled = br.ReadFlag();

    if (tilesEnabled) {
      var numTileColumnsMinus1 = (int)br.ReadUe();
      var numTileRowsMinus1 = (int)br.ReadUe();
      var uniformSpacingFlag = br.ReadFlag();
      if (!uniformSpacingFlag) {
        for (var i = 0; i < numTileColumnsMinus1; ++i)
          br.ReadUe();
        for (var i = 0; i < numTileRowsMinus1; ++i)
          br.ReadUe();
      }
      if (numTileColumnsMinus1 > 0 || numTileRowsMinus1 > 0)
        br.ReadFlag(); // loop_filter_across_tiles_enabled_flag
    }

    var loopFilterAcrossSlicesEnabled = br.ReadFlag();
    var deblockingFilterControlPresent = br.ReadFlag();
    var deblockingFilterOverrideEnabled = false;
    var deblockingFilterDisabled = false;
    var betaOffset = 0;
    var tcOffset = 0;

    if (deblockingFilterControlPresent) {
      deblockingFilterOverrideEnabled = br.ReadFlag();
      deblockingFilterDisabled = br.ReadFlag();
      if (!deblockingFilterDisabled) {
        betaOffset = br.ReadSe() * 2;
        tcOffset = br.ReadSe() * 2;
      }
    }

    var scalingListDataPresent = br.ReadFlag();
    if (scalingListDataPresent)
      _SkipScalingListData(br);

    var listsModificationPresent = br.ReadFlag();
    var log2ParallelMergeLevelMinus2 = (int)br.ReadUe();
    var sliceSegmentHeaderExtensionPresent = br.ReadFlag();

    return new() {
      Id = ppsId,
      SpsId = spsId,
      DependentSliceSegmentsEnabled = dependentSliceSegmentsEnabled,
      OutputFlagPresent = outputFlagPresent,
      NumExtraSliceHeaderBits = numExtraSliceHeaderBits,
      SignDataHidingEnabled = signDataHidingEnabled,
      CabacInitPresent = cabacInitPresent,
      NumRefIdxL0DefaultActiveMinus1 = numRefIdxL0DefaultActiveMinus1,
      NumRefIdxL1DefaultActiveMinus1 = numRefIdxL1DefaultActiveMinus1,
      InitQpMinus26 = initQpMinus26,
      ConstrainedIntraPred = constrainedIntraPred,
      TransformSkipEnabled = transformSkipEnabled,
      CuQpDeltaEnabled = cuQpDeltaEnabled,
      DiffCuQpDeltaDepth = diffCuQpDeltaDepth,
      CbQpOffset = cbQpOffset,
      CrQpOffset = crQpOffset,
      SliceChromaQpOffsetsPresent = sliceChromaQpOffsetsPresent,
      WeightedPred = weightedPred,
      WeightedBiPred = weightedBiPred,
      TransquantBypassEnabled = transquantBypassEnabled,
      TilesEnabled = tilesEnabled,
      EntropyCodingSyncEnabled = entropyCodingSyncEnabled,
      LoopFilterAcrossSlicesEnabled = loopFilterAcrossSlicesEnabled,
      DeblockingFilterControlPresent = deblockingFilterControlPresent,
      DeblockingFilterOverrideEnabled = deblockingFilterOverrideEnabled,
      DeblockingFilterDisabled = deblockingFilterDisabled,
      BetaOffset = betaOffset,
      TcOffset = tcOffset,
      ScalingListDataPresent = scalingListDataPresent,
      ListsModificationPresent = listsModificationPresent,
      Log2ParallelMergeLevelMinus2 = log2ParallelMergeLevelMinus2,
      SliceSegmentHeaderExtensionPresent = sliceSegmentHeaderExtensionPresent,
    };
  }

  private static SliceHeader _ParseSliceHeader(byte[] rbsp, NalUnitType nalType, Sps sps, Pps pps) {
    var br = new HevcBitReader(rbsp);

    var firstSliceSegmentInPic = br.ReadFlag();

    // For IDR/CRA: no_output_of_prior_pics_flag
    if (nalType is NalUnitType.IdrWRadl or NalUnitType.IdrNLp or NalUnitType.CraNut)
      br.ReadFlag(); // no_output_of_prior_pics_flag

    var ppsId = (int)br.ReadUe();

    if (!firstSliceSegmentInPic && pps.DependentSliceSegmentsEnabled)
      br.ReadFlag(); // dependent_slice_segment_flag

    if (!firstSliceSegmentInPic) {
      // slice_segment_address
      var ctbCount = sps.PicWidthInCtbs * sps.PicHeightInCtbs;
      var addrBits = _CeilLog2(ctbCount);
      if (addrBits > 0)
        br.ReadBits(addrBits);
    }

    // Skip extra slice header bits
    for (var i = 0; i < pps.NumExtraSliceHeaderBits; ++i)
      br.ReadFlag();

    var sliceType = (int)br.ReadUe(); // 0=B, 1=P, 2=I

    if (pps.OutputFlagPresent)
      br.ReadFlag(); // pic_output_flag

    if (sps.SeparateColourPlane)
      br.ReadBits(2); // colour_plane_id

    // For non-IDR: pic_order_cnt_lsb and short_term_ref_pic_set
    var isIdr = nalType is NalUnitType.IdrWRadl or NalUnitType.IdrNLp;
    if (!isIdr) {
      // Skip picture order count and reference picture set parsing for BPG (always I-frame)
      br.ReadBits(sps.Log2MaxPicOrderCntLsbMinus4 + 4); // pic_order_cnt_lsb
      var shortTermRefPicSetSpsFlag = br.ReadFlag();
      if (!shortTermRefPicSetSpsFlag) {
        // Skip inline short_term_ref_pic_set - we don't need it for I-only decoding
        // This is a simplification; full parsing would be needed for non-I frames
      }
    }

    // SAO flags
    var saoLumaFlag = false;
    var saoChromaFlag = false;
    if (sps.SaoEnabled) {
      saoLumaFlag = br.ReadFlag();
      if (sps.ChromaFormatIdc != 0)
        saoChromaFlag = br.ReadFlag();
    }

    // QP delta
    var sliceQpDelta = br.ReadSe();
    var sliceQp = pps.InitQpMinus26 + 26 + sliceQpDelta;

    // Chroma QP offsets
    if (pps.SliceChromaQpOffsetsPresent) {
      br.ReadSe(); // slice_cb_qp_offset
      br.ReadSe(); // slice_cr_qp_offset
    }

    // Deblocking filter override
    var deblockingFilterDisabled = pps.DeblockingFilterDisabled;
    var betaOffset = pps.BetaOffset;
    var tcOffset = pps.TcOffset;

    if (pps.DeblockingFilterOverrideEnabled) {
      var deblockingFilterOverrideFlag = br.ReadFlag();
      if (deblockingFilterOverrideFlag) {
        deblockingFilterDisabled = br.ReadFlag();
        if (!deblockingFilterDisabled) {
          betaOffset = br.ReadSe() * 2;
          tcOffset = br.ReadSe() * 2;
        }
      }
    }

    // Align to byte boundary for CABAC data
    br.AlignToByte();

    return new() {
      FirstSliceSegmentInPic = firstSliceSegmentInPic,
      NalType = nalType,
      PpsId = ppsId,
      SliceType = sliceType,
      SliceQp = sliceQp,
      SaoLumaFlag = saoLumaFlag,
      SaoChromaFlag = saoChromaFlag,
      DeblockingFilterDisabled = deblockingFilterDisabled,
      BetaOffset = betaOffset,
      TcOffset = tcOffset,
      CabacInitByteOffset = br.ByteOffset,
    };
  }

  private static void _SkipProfileTierLevel(HevcBitReader br, int maxSubLayersMinus1) {
    br.ReadBits(2);  // general_profile_space
    br.ReadFlag();    // general_tier_flag
    br.ReadBits(5);  // general_profile_idc
    br.SkipBits(32); // general_profile_compatibility_flag[32]
    br.ReadFlag();    // general_progressive_source_flag
    br.ReadFlag();    // general_interlaced_source_flag
    br.ReadFlag();    // general_non_packed_constraint_flag
    br.ReadFlag();    // general_frame_only_constraint_flag
    br.SkipBits(44); // reserved_zero_44bits
    br.ReadBits(8);  // general_level_idc

    // sub_layer flags
    var subLayerProfilePresent = new bool[maxSubLayersMinus1];
    var subLayerLevelPresent = new bool[maxSubLayersMinus1];

    for (var i = 0; i < maxSubLayersMinus1; ++i) {
      subLayerProfilePresent[i] = br.ReadFlag();
      subLayerLevelPresent[i] = br.ReadFlag();
    }

    if (maxSubLayersMinus1 > 0)
      for (var i = maxSubLayersMinus1; i < 8; ++i)
        br.ReadBits(2); // reserved_zero_2bits

    for (var i = 0; i < maxSubLayersMinus1; ++i) {
      if (subLayerProfilePresent[i]) {
        br.ReadBits(2);  // sub_layer_profile_space
        br.ReadFlag();    // sub_layer_tier_flag
        br.ReadBits(5);  // sub_layer_profile_idc
        br.SkipBits(32); // sub_layer_profile_compatibility_flag
        br.SkipBits(48); // constraint flags
      }
      if (subLayerLevelPresent[i])
        br.ReadBits(8); // sub_layer_level_idc
    }
  }

  private static void _SkipScalingListData(HevcBitReader br) {
    for (var sizeId = 0; sizeId < 4; ++sizeId) {
      var matrixCount = sizeId == 3 ? 2 : 6;
      for (var matrixId = 0; matrixId < matrixCount; ++matrixId) {
        var scalingListPredModeFlag = br.ReadFlag();
        if (!scalingListPredModeFlag) {
          br.ReadUe(); // scaling_list_pred_matrix_id_delta
        } else {
          var coefNum = Math.Min(64, 1 << (4 + (sizeId << 1)));
          if (sizeId > 1)
            br.ReadSe(); // scaling_list_dc_coef_minus8
          for (var i = 0; i < coefNum; ++i)
            br.ReadSe(); // scaling_list_delta_coef
        }
      }
    }
  }

  private static void _SkipShortTermRefPicSet(HevcBitReader br, int stRpsIdx, int numShortTermRefPicSets) {
    var interRefPicSetPredictionFlag = false;
    if (stRpsIdx != 0)
      interRefPicSetPredictionFlag = br.ReadFlag();

    if (interRefPicSetPredictionFlag) {
      if (stRpsIdx == numShortTermRefPicSets)
        br.ReadUe(); // delta_idx_minus1
      br.ReadFlag(); // delta_rps_sign
      br.ReadUe();   // abs_delta_rps_minus1

      // We need the previous RPS to know the count - use a reasonable upper bound
      // For BPG I-frames this path is typically not taken
      var numDeltaPocs = 0; // Conservative: skip if we can't determine count
      for (var j = 0; j <= numDeltaPocs; ++j) {
        var usedByCurrPicFlag = br.ReadFlag();
        if (!usedByCurrPicFlag)
          br.ReadFlag(); // use_delta_flag
      }
    } else {
      var numNegativePics = (int)br.ReadUe();
      var numPositivePics = (int)br.ReadUe();
      for (var i = 0; i < numNegativePics; ++i) {
        br.ReadUe();   // delta_poc_s0_minus1
        br.ReadFlag(); // used_by_curr_pic_s0_flag
      }
      for (var i = 0; i < numPositivePics; ++i) {
        br.ReadUe();   // delta_poc_s1_minus1
        br.ReadFlag(); // used_by_curr_pic_s1_flag
      }
    }
  }

  private static int _CeilLog2(int x) {
    var bits = 0;
    var v = x - 1;
    while (v > 0) {
      v >>= 1;
      ++bits;
    }
    return Math.Max(bits, 0);
  }
}
