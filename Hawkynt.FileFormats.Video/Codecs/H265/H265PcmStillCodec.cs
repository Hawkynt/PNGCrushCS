using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.H265;

/// <summary>
/// A deliberately small HEVC Main-Still-Picture encoder for lossless 8-bit 4:2:0 still images.
/// </summary>
/// <remarks>
/// The coded picture is made exclusively from 64 by 64 intra PCM coding units. HEVC's PCM mode is
/// part of the ordinary Main profile syntax: a coding unit terminates CABAC, byte-aligns, carries its
/// Y/Cb/Cr samples verbatim and then restarts CABAC without resetting the probability contexts.
/// This makes a useful first encoder because it is completely interoperable while requiring none of
/// the transform, quantisation, motion-search or rate-distortion machinery of a compressed encoder.
/// <para/>
/// There are only three arithmetic decisions between two PCM payloads. Instead of carrying a second,
/// subtly different implementation of the CABAC arithmetic engine, this encoder finds a short code
/// point inside the interval for those decisions by running the normative decoder already used by the
/// H.265 reader. This is interval coding by construction, not a private escape: the resulting bytes
/// are ordinary CABAC and an unrelated HEVC decoder sees the same bins.
/// </remarks>
internal static class H265PcmStillCodec {

  internal readonly record struct EncodedImage(
    byte[] DecoderConfiguration,
    byte[] Sample,
    int HevcDisplayWidth,
    int HevcDisplayHeight
  );

  private const int _CTB_LOG2 = 6;
  private const int _CTB_SIZE = 1 << _CTB_LOG2;
  private const int _PCM_BYTES_PER_CTB = _CTB_SIZE * _CTB_SIZE + 2 * (_CTB_SIZE / 2) * (_CTB_SIZE / 2);

  internal static EncodedImage Encode(RawImage source) {
    ArgumentNullException.ThrowIfNull(source);
    if (source.Width <= 0 || source.Height <= 0)
      throw new ArgumentOutOfRangeException(nameof(source), "HEVC requires a positive image size.");

    // A 4:2:0 conformance window has two-luma-sample crop units. Preserve an odd final row/column
    // by duplicating it into an even HEVC display picture; the HEIF clean-aperture property removes
    // that duplicate again at the container layer.
    var displayWidth = (source.Width + 1) & ~1;
    var displayHeight = (source.Height + 1) & ~1;
    var codedWidth = _RoundUp(displayWidth, _CTB_SIZE);
    var codedHeight = _RoundUp(displayHeight, _CTB_SIZE);

    var evenRgb = _PadRgbToEven(source, displayWidth, displayHeight);
    var yuv = FastRawImageConverter.Convert(evenRgb, PixelFormat.Yuv420P8);
    var planes = _PadYuvToCodedSize(yuv, codedWidth, codedHeight);

    var vps = _MakeNal(H265NalUnitType.VideoParameterSet, _BuildVps());
    var sps = _MakeNal(H265NalUnitType.SequenceParameterSet,
      _BuildSps(codedWidth, codedHeight, displayWidth, displayHeight));
    var pps = _MakeNal(H265NalUnitType.PictureParameterSet, _BuildPps());
    var slice = _MakeNal(H265NalUnitType.IdrWithNoLeadingPictures,
      _BuildSlice(planes.Y, planes.Cb, planes.Cr, codedWidth, codedHeight));

    var configuration = _BuildDecoderConfiguration(vps, sps, pps);
    var sample = new byte[4 + slice.Length];
    BinaryPrimitives.WriteUInt32BigEndian(sample, (uint)slice.Length);
    slice.CopyTo(sample, 4);

    return new(configuration, sample, displayWidth, displayHeight);
  }

  /// <summary>
  /// Decodes the constrained PCM profile written above. This is deliberately not a replacement for
  /// <see cref="H265FrameDecoder"/>; it lets the still-image path consume PCM until the general
  /// decoder grows the CABAC-to-raw handoff needed by arbitrary PCM-bearing video streams.
  /// </summary>
  internal static bool TryDecode(
    ReadOnlyMemory<byte> sample,
    ReadOnlyMemory<byte> configurationRecord,
    out RawImage image
  ) {
    image = null!;
    var configuration = H265DecoderConfiguration.TryParse(configurationRecord);
    if (configuration == null || configuration.LengthSize != 4)
      return false;

    H265SequenceParameterSet? sps = null;
    H265PictureParameterSet? pps = null;
    foreach (var bytes in configuration.ParameterSets) {
      var nal = H265NalReader.Parse(bytes);
      if (nal.Type == H265NalUnitType.SequenceParameterSet)
        sps = H265SequenceParameterSet.Parse(nal.Payload);
      else if (nal.Type == H265NalUnitType.PictureParameterSet)
        pps = H265PictureParameterSet.Parse(nal.Payload);
    }

    if (sps == null || pps == null
        || !sps.PcmEnabled
        || sps.ChromaFormatIdc != 1
        || sps.BitDepthLuma != 8 || sps.BitDepthChroma != 8
        || sps.PcmBitDepthLuma != 8 || sps.PcmBitDepthChroma != 8
        || sps.CtbLog2SizeY != _CTB_LOG2
        || sps.Log2MaxPcmCbSizeY < _CTB_LOG2
        || sps.Width % _CTB_SIZE != 0 || sps.Height % _CTB_SIZE != 0)
      return false;

    H265NalUnit? coded = null;
    foreach (var nal in H265NalReader.SplitLengthPrefixed(sample, 4))
      if (nal.Type == H265NalUnitType.IdrWithNoLeadingPictures) {
        coded = nal;
        break;
      }

    if (coded == null)
      return false;

    var sequenceSets = new Dictionary<int, H265SequenceParameterSet> { [sps.Id] = sps };
    var pictureSets = new Dictionary<int, H265PictureParameterSet> { [pps.Id] = pps };
    var header = H265SliceHeader.Parse(coded, sequenceSets, pictureSets);
    if (!header.FirstSliceSegmentInPicture || !header.IsIntra || header.Sps != sps || header.Pps != pps)
      return false;

    var y = new byte[sps.Width * sps.Height];
    var cw = sps.Width >> 1;
    var ch = sps.Height >> 1;
    var cb = new byte[cw * ch];
    var cr = new byte[cw * ch];

    var contexts = new byte[H265CabacContexts.COUNT];
    H265CabacContexts.Initialize(contexts, 0, header.SliceQpY);

    var engine = new H265CabacEngine(coded.Payload, contexts);
    engine.Start(header.DataOffset);

    var ctbsAcross = sps.Width >> _CTB_LOG2;
    var ctbsDown = sps.Height >> _CTB_LOG2;
    var total = ctbsAcross * ctbsDown;

    for (var index = 0; index < total; ++index) {
      // Every CTB is one unsplit coding unit. All neighbours have depth zero, hence context zero.
      if (engine.DecodeBin(H265CabacContexts.SPLIT_CU_FLAG) != 0)
        return false;
      if (engine.DecodeTerminate() == 0) // pcm_flag
        return false;

      var rawByte = (engine.BitPosition + 7) >> 3;
      if (rawByte > coded.Payload.Length - _PCM_BYTES_PER_CTB)
        return false;

      var ctbX = (index % ctbsAcross) << _CTB_LOG2;
      var ctbY = (index / ctbsAcross) << _CTB_LOG2;
      _ReadPcmCtb(coded.Payload, rawByte, y, cb, cr, sps.Width, cw, ctbX, ctbY);
      rawByte += _PCM_BYTES_PER_CTB;

      // pcm() initializes the arithmetic registers again but does not initialize the contexts.
      engine = new H265CabacEngine(coded.Payload, contexts);
      engine.Start(rawByte);

      var end = engine.DecodeTerminate();
      if (index + 1 == total) {
        if (end == 0)
          return false;
      } else if (end != 0)
        return false;
    }

    image = _CropAndConvert(y, cb, cr, sps);
    return true;
  }

  private static RawImage _PadRgbToEven(RawImage source, int width, int height) {
    var rgb = source.ToRgb24();
    if (source.Width == width && source.Height == height)
      return new RawImage { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };

    var result = new byte[width * height * 3];
    for (var y = 0; y < height; ++y) {
      var sy = Math.Min(y, source.Height - 1);
      for (var x = 0; x < width; ++x) {
        var sx = Math.Min(x, source.Width - 1);
        var from = (sy * source.Width + sx) * 3;
        var to = (y * width + x) * 3;
        result[to] = rgb[from];
        result[to + 1] = rgb[from + 1];
        result[to + 2] = rgb[from + 2];
      }
    }

    return new RawImage { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = result };
  }

  private static (byte[] Y, byte[] Cb, byte[] Cr) _PadYuvToCodedSize(RawImage yuv, int width, int height) {
    var sourceY = yuv.GetPlaneData(0);
    var sourceCb = yuv.GetPlaneData(1);
    var sourceCr = yuv.GetPlaneData(2);
    var (sourceCw, sourceCh) = yuv.GetPlaneDimensions(1);

    var y = _PadPlane(sourceY, yuv.Width, yuv.Height, width, height);
    var cb = _PadPlane(sourceCb, sourceCw, sourceCh, width >> 1, height >> 1);
    var cr = _PadPlane(sourceCr, sourceCw, sourceCh, width >> 1, height >> 1);
    return (y, cb, cr);
  }

  private static byte[] _PadPlane(ReadOnlySpan<byte> source, int sw, int sh, int dw, int dh) {
    var result = new byte[dw * dh];
    for (var y = 0; y < dh; ++y) {
      var sy = Math.Min(y, sh - 1);
      var row = result.AsSpan(y * dw, dw);
      source.Slice(sy * sw, sw).CopyTo(row);
      row[sw..].Fill(source[sy * sw + sw - 1]);
    }
    return result;
  }

  private static byte[] _BuildVps() {
    var w = new Bits();
    w.WriteBits(0, 4); // vps_video_parameter_set_id
    w.WriteBit(1); // vps_base_layer_internal_flag
    w.WriteBit(1); // vps_base_layer_available_flag
    w.WriteBits(0, 6); // vps_max_layers_minus1
    w.WriteBits(0, 3); // vps_max_sub_layers_minus1
    w.WriteBit(1); // vps_temporal_id_nesting_flag
    w.WriteBits(0xffff, 16);
    _WriteProfileTierLevel(w);
    w.WriteBit(0); // vps_sub_layer_ordering_info_present_flag
    w.WriteUe(0); // max_dec_pic_buffering_minus1
    w.WriteUe(0); // max_num_reorder_pics
    w.WriteUe(0); // max_latency_increase_plus1
    w.WriteBits(0, 6); // vps_max_layer_id
    w.WriteUe(0); // vps_num_layer_sets_minus1
    w.WriteBit(0); // vps_timing_info_present_flag
    w.WriteBit(0); // vps_extension_flag
    w.WriteRbspTrailingBits();
    return w.ToArray();
  }

  private static byte[] _BuildSps(int width, int height, int displayWidth, int displayHeight) {
    var w = new Bits();
    w.WriteBits(0, 4); // sps_video_parameter_set_id
    w.WriteBits(0, 3); // sps_max_sub_layers_minus1
    w.WriteBit(1); // temporal nesting
    _WriteProfileTierLevel(w);
    w.WriteUe(0); // sps_seq_parameter_set_id
    w.WriteUe(1); // chroma_format_idc = 4:2:0
    w.WriteUe((uint)width);
    w.WriteUe((uint)height);

    var cropRight = (width - displayWidth) >> 1;
    var cropBottom = (height - displayHeight) >> 1;
    w.WriteBit(cropRight != 0 || cropBottom != 0 ? 1 : 0);
    if (cropRight != 0 || cropBottom != 0) {
      w.WriteUe(0);
      w.WriteUe((uint)cropRight);
      w.WriteUe(0);
      w.WriteUe((uint)cropBottom);
    }

    w.WriteUe(0); // bit_depth_luma_minus8
    w.WriteUe(0); // bit_depth_chroma_minus8
    w.WriteUe(0); // log2_max_pic_order_cnt_lsb_minus4
    w.WriteBit(0); // sub_layer_ordering_info_present_flag
    w.WriteUe(0); // max_dec_pic_buffering_minus1
    w.WriteUe(0); // max_num_reorder_pics
    w.WriteUe(0); // max_latency_increase_plus1
    w.WriteUe(0); // log2_min_luma_coding_block_size_minus3 => 8
    w.WriteUe(3); // log2_diff_max_min_luma_coding_block_size => 64 CTB
    w.WriteUe(0); // log2_min_luma_transform_block_size_minus2 => 4
    w.WriteUe(3); // log2_diff_max_min_luma_transform_block_size => 32
    w.WriteUe(0); // max_transform_hierarchy_depth_inter
    w.WriteUe(0); // max_transform_hierarchy_depth_intra
    w.WriteBit(0); // scaling_list_enabled_flag
    w.WriteBit(0); // amp_enabled_flag
    w.WriteBit(0); // sample_adaptive_offset_enabled_flag
    w.WriteBit(1); // pcm_enabled_flag
    w.WriteBits(7, 4); // pcm_sample_bit_depth_luma_minus1
    w.WriteBits(7, 4); // pcm_sample_bit_depth_chroma_minus1
    w.WriteUe(0); // log2_min_pcm_luma_coding_block_size_minus3 => 8
    w.WriteUe(3); // log2_diff_max_min_pcm_luma_coding_block_size => 64
    w.WriteBit(1); // pcm_loop_filter_disabled_flag
    w.WriteUe(0); // num_short_term_ref_pic_sets
    w.WriteBit(0); // long_term_ref_pics_present_flag
    w.WriteBit(0); // sps_temporal_mvp_enabled_flag
    w.WriteBit(0); // strong_intra_smoothing_enabled_flag
    w.WriteBit(0); // vui_parameters_present_flag
    w.WriteBit(0); // sps_extension_present_flag
    w.WriteRbspTrailingBits();
    return w.ToArray();
  }

  private static byte[] _BuildPps() {
    var w = new Bits();
    w.WriteUe(0); // pps_pic_parameter_set_id
    w.WriteUe(0); // pps_seq_parameter_set_id
    w.WriteBit(0); // dependent_slice_segments_enabled_flag
    w.WriteBit(0); // output_flag_present_flag
    w.WriteBits(0, 3); // num_extra_slice_header_bits
    w.WriteBit(0); // sign_data_hiding_enabled_flag
    w.WriteBit(0); // cabac_init_present_flag
    w.WriteUe(0); // num_ref_idx_l0_default_active_minus1
    w.WriteUe(0); // num_ref_idx_l1_default_active_minus1
    w.WriteSe(0); // init_qp_minus26
    w.WriteBit(0); // constrained_intra_pred_flag
    w.WriteBit(0); // transform_skip_enabled_flag
    w.WriteBit(0); // cu_qp_delta_enabled_flag
    w.WriteSe(0); // pps_cb_qp_offset
    w.WriteSe(0); // pps_cr_qp_offset
    w.WriteBit(0); // pps_slice_chroma_qp_offsets_present_flag
    w.WriteBit(0); // weighted_pred_flag
    w.WriteBit(0); // weighted_bipred_flag
    w.WriteBit(0); // transquant_bypass_enabled_flag
    w.WriteBit(0); // tiles_enabled_flag
    w.WriteBit(0); // entropy_coding_sync_enabled_flag
    w.WriteBit(0); // pps_loop_filter_across_slices_enabled_flag
    w.WriteBit(1); // deblocking_filter_control_present_flag
    w.WriteBit(0); // deblocking_filter_override_enabled_flag
    w.WriteBit(1); // pps_deblocking_filter_disabled_flag
    w.WriteBit(0); // pps_scaling_list_data_present_flag
    w.WriteBit(0); // lists_modification_present_flag
    w.WriteUe(0); // log2_parallel_merge_level_minus2
    w.WriteBit(0); // slice_segment_header_extension_present_flag
    w.WriteBit(0); // pps_extension_present_flag
    w.WriteRbspTrailingBits();
    return w.ToArray();
  }

  private static byte[] _BuildSlice(byte[] y, byte[] cb, byte[] cr, int width, int height) {
    var header = new Bits();
    header.WriteBit(1); // first_slice_segment_in_pic_flag
    header.WriteBit(0); // no_output_of_prior_pics_flag
    header.WriteUe(0); // slice_pic_parameter_set_id
    header.WriteUe(2); // slice_type = I
    header.WriteSe(0); // slice_qp_delta
    header.WriteByteAlignment();

    var result = new List<byte>(header.ByteLength + width * height * 3 / 2 + 128);
    result.AddRange(header.ToArray());

    var contexts = new byte[H265CabacContexts.COUNT];
    H265CabacContexts.Initialize(contexts, 0, 26);

    var across = width >> _CTB_LOG2;
    var down = height >> _CTB_LOG2;
    var total = across * down;

    // The first entropy-coded subset starts directly after the slice header.
    result.AddRange(_FindCabacPrefix(contexts, leadingEndFlag: null));

    for (var index = 0; index < total; ++index) {
      var ctbX = (index % across) << _CTB_LOG2;
      var ctbY = (index / across) << _CTB_LOG2;
      _WritePcmCtb(result, y, cb, cr, width, width >> 1, ctbX, ctbY);

      if (index + 1 == total) {
        result.AddRange(_FindCabacSuffix(contexts, final: true));
        break;
      }

      // After pcm(), CABAC is restarted. First code end_of_slice_segment_flag=0, then the next
      // unsplit CU and its pcm_flag=1. Finishing that tiny interval lands exactly on the next PCM.
      result.AddRange(_FindCabacSuffix(contexts, final: false));
    }

    // rbsp_slice_segment_trailing_bits(): stop bit followed by alignment zeroes. The CABAC prefixes
    // above are whole bytes already, so this is the canonical one-byte spelling.
    result.Add(0x80);
    return result.ToArray();
  }

  /// <summary>
  /// Finds a two-byte code point that decodes as split_cu_flag=0, pcm_flag=1.
  /// </summary>
  private static byte[] _FindCabacPrefix(byte[] contexts, bool? leadingEndFlag) {
    return _SearchCabac(contexts, candidateStates => {
      var engine = candidateStates.Engine;
      if (leadingEndFlag.HasValue && engine.DecodeTerminate() != (leadingEndFlag.Value ? 1 : 0))
        return false;
      if (engine.DecodeBin(H265CabacContexts.SPLIT_CU_FLAG) != 0)
        return false;
      return engine.DecodeTerminate() != 0;
    });
  }

  private static byte[] _FindCabacSuffix(byte[] contexts, bool final) {
    if (final)
      return _SearchCabac(contexts, candidateStates => candidateStates.Engine.DecodeTerminate() != 0);
    return _FindCabacPrefix(contexts, leadingEndFlag: false);
  }

  private readonly ref struct CabacCandidate(H265CabacEngine engine) {
    internal H265CabacEngine Engine { get; } = engine;
  }

  private delegate bool CabacProbe(CabacCandidate candidate);

  private static byte[] _SearchCabac(byte[] contexts, CabacProbe probe) {
    var baseline = (byte[])contexts.Clone();

    for (var value = 0; value <= ushort.MaxValue; ++value) {
      Array.Copy(baseline, contexts, contexts.Length);
      byte[] bytes = [(byte)(value >> 8), (byte)value];
      var engine = new H265CabacEngine(bytes, contexts);
      try {
        engine.Start(0);
      } catch (InvalidDataException) {
        continue;
      }

      var candidate = new CabacCandidate(engine);
      if (!probe(candidate))
        continue;

      // The arithmetic decoder deliberately reads ahead. A two-byte interval is useful for PCM only
      // when its lookahead has not crossed the next byte boundary, because that boundary is where
      // pcm_sample_luma/chroma begin.
      if (((candidate.Engine.BitPosition + 7) >> 3) != 2)
        continue;

      return bytes;
    }

    Array.Copy(baseline, contexts, contexts.Length);
    throw new InvalidOperationException("No two-byte HEVC CABAC interval represented the required PCM syntax.");
  }

  private static void _WritePcmCtb(
    List<byte> output, byte[] y, byte[] cb, byte[] cr, int yStride, int cStride, int x, int yy) {
    for (var row = 0; row < _CTB_SIZE; ++row)
      for (var col = 0; col < _CTB_SIZE; ++col)
        output.Add(y[(yy + row) * yStride + x + col]);

    var cx = x >> 1;
    var cy = yy >> 1;
    var cs = _CTB_SIZE >> 1;
    for (var row = 0; row < cs; ++row)
      for (var col = 0; col < cs; ++col)
        output.Add(cb[(cy + row) * cStride + cx + col]);
    for (var row = 0; row < cs; ++row)
      for (var col = 0; col < cs; ++col)
        output.Add(cr[(cy + row) * cStride + cx + col]);
  }

  private static void _ReadPcmCtb(
    byte[] input, int at, byte[] y, byte[] cb, byte[] cr, int yStride, int cStride, int x, int yy) {
    for (var row = 0; row < _CTB_SIZE; ++row)
      for (var col = 0; col < _CTB_SIZE; ++col)
        y[(yy + row) * yStride + x + col] = input[at++];

    var cx = x >> 1;
    var cy = yy >> 1;
    var cs = _CTB_SIZE >> 1;
    for (var row = 0; row < cs; ++row)
      for (var col = 0; col < cs; ++col)
        cb[(cy + row) * cStride + cx + col] = input[at++];
    for (var row = 0; row < cs; ++row)
      for (var col = 0; col < cs; ++col)
        cr[(cy + row) * cStride + cx + col] = input[at++];
  }

  private static RawImage _CropAndConvert(byte[] y, byte[] cb, byte[] cr, H265SequenceParameterSet sps) {
    var width = sps.DisplayWidth;
    var height = sps.DisplayHeight;
    var cx0 = sps.CropOffsetX >> 1;
    var cy0 = sps.CropOffsetY >> 1;
    var cw = width >> 1;
    var ch = height >> 1;
    var codedCw = sps.Width >> 1;

    var packed = new byte[width * height + 2 * cw * ch];
    var at = 0;
    for (var row = 0; row < height; ++row) {
      y.AsSpan((sps.CropOffsetY + row) * sps.Width + sps.CropOffsetX, width).CopyTo(packed.AsSpan(at));
      at += width;
    }
    for (var row = 0; row < ch; ++row) {
      cb.AsSpan((cy0 + row) * codedCw + cx0, cw).CopyTo(packed.AsSpan(at));
      at += cw;
    }
    for (var row = 0; row < ch; ++row) {
      cr.AsSpan((cy0 + row) * codedCw + cx0, cw).CopyTo(packed.AsSpan(at));
      at += cw;
    }

    var yuv = new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Yuv420P8,
      PixelData = packed,
    };
    return FastRawImageConverter.Convert(yuv, PixelFormat.Rgb24);
  }

  private static byte[] _BuildDecoderConfiguration(byte[] vps, byte[] sps, byte[] pps) {
    var size = 23 + (3 + 2 + vps.Length) + (3 + 2 + sps.Length) + (3 + 2 + pps.Length);
    var result = new byte[size];
    result[0] = 1;
    result[1] = H265ProfileTierLevel.MAIN_STILL_PICTURE;
    // general_profile_compatibility_flags: advertise Main and Main Still Picture.
    result[2] = 0x50;
    result[12] = 120; // level 4.0; deliberately generous for still-image dimensions.
    result[13] = 0xF0;
    result[14] = 0x00;
    result[15] = 0xFC;
    result[16] = 0xFD; // chromaFormat = 1
    result[17] = 0xF8; // bitDepthLumaMinus8 = 0
    result[18] = 0xF8; // bitDepthChromaMinus8 = 0
    result[21] = 0x0F; // one temporal layer, nested, four-byte NAL lengths
    result[22] = 3;

    var at = 23;
    at = _WriteParameterArray(result, at, H265NalUnitType.VideoParameterSet, vps);
    at = _WriteParameterArray(result, at, H265NalUnitType.SequenceParameterSet, sps);
    _WriteParameterArray(result, at, H265NalUnitType.PictureParameterSet, pps);
    return result;
  }

  private static int _WriteParameterArray(byte[] destination, int at, H265NalUnitType type, byte[] nal) {
    destination[at++] = (byte)(0x80 | (int)type);
    destination[at++] = 0;
    destination[at++] = 1;
    BinaryPrimitives.WriteUInt16BigEndian(destination.AsSpan(at, 2), checked((ushort)nal.Length));
    at += 2;
    nal.CopyTo(destination, at);
    return at + nal.Length;
  }

  private static byte[] _MakeNal(H265NalUnitType type, byte[] rbsp) {
    var escaped = _EscapeRbsp(rbsp);
    var result = new byte[2 + escaped.Length];
    result[0] = (byte)((int)type << 1);
    result[1] = 1; // nuh_layer_id=0, nuh_temporal_id_plus1=1
    escaped.CopyTo(result, 2);
    return result;
  }

  private static byte[] _EscapeRbsp(ReadOnlySpan<byte> rbsp) {
    var output = new List<byte>(rbsp.Length + 16);
    var zeroes = 0;
    foreach (var value in rbsp) {
      if (zeroes >= 2 && value <= 3) {
        output.Add(3);
        zeroes = 0;
      }
      output.Add(value);
      zeroes = value == 0 ? zeroes + 1 : 0;
    }
    return output.ToArray();
  }

  private static void _WriteProfileTierLevel(Bits w) {
    w.WriteBits(0, 2); // general_profile_space
    w.WriteBit(0); // general_tier_flag
    w.WriteBits(H265ProfileTierLevel.MAIN_STILL_PICTURE, 5);
    for (var i = 0; i < 32; ++i)
      w.WriteBit(i is H265ProfileTierLevel.MAIN or H265ProfileTierLevel.MAIN_STILL_PICTURE ? 1 : 0);
    w.WriteBit(1); // general_progressive_source_flag
    w.WriteBit(0); // general_interlaced_source_flag
    w.WriteBit(1); // general_non_packed_constraint_flag
    w.WriteBit(1); // general_frame_only_constraint_flag
    w.WriteBits(0, 32);
    w.WriteBits(0, 12); // remainder of the 48-bit constraint field
    w.WriteBits(120, 8); // general_level_idc
  }

  private static int _RoundUp(int value, int multiple)
    => checked((value + multiple - 1) / multiple * multiple);

  private sealed class Bits {
    private readonly List<byte> _bytes = [];
    private int _current;
    private int _used;

    internal int ByteLength => this._bytes.Count + (this._used == 0 ? 0 : 1);

    internal void WriteBit(int bit) {
      this._current = (this._current << 1) | (bit & 1);
      if (++this._used != 8)
        return;
      this._bytes.Add((byte)this._current);
      this._current = 0;
      this._used = 0;
    }

    internal void WriteBits(uint value, int count) {
      for (var bit = count - 1; bit >= 0; --bit)
        this.WriteBit((int)(value >> bit));
    }

    internal void WriteUe(uint value) {
      var codeNum = value + 1;
      var bits = 0;
      for (var copy = codeNum; copy != 0; copy >>= 1)
        ++bits;
      for (var i = 1; i < bits; ++i)
        this.WriteBit(0);
      this.WriteBits(codeNum, bits);
    }

    internal void WriteSe(int value) {
      var codeNum = value <= 0 ? (uint)(-value * 2) : (uint)(value * 2 - 1);
      this.WriteUe(codeNum);
    }

    internal void WriteByteAlignment() {
      this.WriteBit(1);
      while (this._used != 0)
        this.WriteBit(0);
    }

    internal void WriteRbspTrailingBits() => this.WriteByteAlignment();

    internal byte[] ToArray() {
      if (this._used == 0)
        return this._bytes.ToArray();
      var result = new byte[this._bytes.Count + 1];
      this._bytes.CopyTo(result);
      result[^1] = (byte)(this._current << (8 - this._used));
      return result;
    }
  }
}
