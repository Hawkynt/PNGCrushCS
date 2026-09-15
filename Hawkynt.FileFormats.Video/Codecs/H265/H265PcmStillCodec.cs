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
/// The coded picture is made exclusively from 32 by 32 intra PCM coding units. HEVC's PCM mode is
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

  // 32 by 32, not 64 by 64: H.265 7.4.3.2 caps Log2MaxIpcmCbSizeY at Min(CtbLog2SizeY, 5), so a
  // coding block carrying PCM samples can never be larger than 32 by 32. Making the coding tree
  // block the same size keeps every tree block exactly one unsplit PCM coding unit.
  internal const int _CTB_LOG2 = 5;
  internal const int _CTB_SIZE = 1 << _CTB_LOG2;
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

    var level = _SmallestLevelFor(codedWidth, codedHeight);
    var vps = _MakeNal(H265NalUnitType.VideoParameterSet, _BuildVps(level));
    var sps = _MakeNal(H265NalUnitType.SequenceParameterSet,
      _BuildSps(codedWidth, codedHeight, displayWidth, displayHeight, level));
    var pps = _MakeNal(H265NalUnitType.PictureParameterSet, _BuildPps());
    var slice = _MakeNal(H265NalUnitType.IdrWithNoLeadingPictures,
      _BuildSlice(planes.Y, planes.Cb, planes.Cr, codedWidth, codedHeight));

    var configuration = _BuildDecoderConfiguration(vps, sps, pps, level);
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

  /// <summary>
  /// A still picture in the shape BPG's container carries one: the handful of sequence parameter
  /// set fields BPG keeps in a header of its own, and the NALs that follow them.
  /// </summary>
  /// <remarks>
  /// This is not the stream <see cref="Encode"/> writes with two NALs deleted. A BPG file carries no
  /// video or sequence parameter set at all; its header states the fields a decoder cannot infer and
  /// the format's specification fixes every other one, so the picture has to be coded to those fixed
  /// choices rather than to any this encoder would otherwise prefer.
  /// </remarks>
  internal readonly record struct BpgEncodedImage(byte[] SequenceHeader, byte[] Data);

  /// <summary>The number of component planes a BPG picture this codec writes or reads is made of.</summary>
  /// <remarks>
  /// Three, always. A BPG picture may state one plane instead, and this codec does not write or read
  /// that one: <c>libbpg</c>'s own PCM path puts a coding unit's chroma blocks into planes a
  /// monochrome frame has not allocated, so a monochrome PCM picture is one the reference decoder
  /// cannot read — and this package will not write a file it can only check against itself.
  /// </remarks>
  private const int _BPG_PLANES = 3;

  /// <summary>The bytes of PCM samples one 4:4:4 coding unit carries at eight bits a sample.</summary>
  private const int _BPG_PCM_BYTES_PER_CTB = _BPG_PLANES * _CTB_SIZE * _CTB_SIZE;

  /// <summary>
  /// Encodes one still picture for a BPG container: 4:4:4 with the three planes holding G, B and R,
  /// at eight bits a sample and losing nothing.
  /// </summary>
  /// <remarks>
  /// The geometry is forced by what BPG leaves unsaid. Its header states
  /// log2_min_luma_coding_block_size but never the coded picture size, which a decoder derives as
  /// ceil(dimension / MinCbSizeY) * MinCbSizeY — so making the smallest coding block the same 32 by
  /// 32 as the coding tree block rounds the coded picture to whole tree blocks, and every tree block
  /// is then one unsplit coding unit whose samples all lie inside the picture. Clause 7.3.8.5 codes
  /// split_cu_flag only while a coding block is larger than the smallest the sequence allows and
  /// part_mode only once it is not, so that choice also decides which element precedes pcm_flag.
  /// </remarks>
  internal static BpgEncodedImage EncodeBpgStill(RawImage source) {
    ArgumentNullException.ThrowIfNull(source);
    if (source.Width <= 0 || source.Height <= 0)
      throw new ArgumentOutOfRangeException(nameof(source), "HEVC requires a positive image size.");

    var codedWidth = _RoundUp(source.Width, _CTB_SIZE);
    var codedHeight = _RoundUp(source.Height, _CTB_SIZE);
    _SmallestLevelFor(codedWidth, codedHeight); // refuses a picture past the largest defined level
    var planes = _BuildBpgPlanes(source, codedWidth, codedHeight);

    var pps = _MakeNal(H265NalUnitType.PictureParameterSet, _BuildPps());
    var slice = _MakeNal(H265NalUnitType.IdrWithNoLeadingPictures,
      _BuildBpgSlice(planes, codedWidth, codedHeight));

    // BPG omits the start code in front of the first NAL only, and keeps it in front of every other.
    var data = new byte[pps.Length + 3 + slice.Length];
    pps.CopyTo(data, 0);
    data[pps.Length + 2] = 1;
    slice.CopyTo(data, pps.Length + 3);

    return new(_BuildBpgSequenceHeader(), data);
  }

  /// <summary>
  /// Decodes the picture <see cref="EncodeBpgStill"/> writes, at the coded size BPG's own header
  /// implies. Answers false for anything outside that shape rather than returning invented samples.
  /// </summary>
  internal static bool TryDecodeBpgStill(
    ReadOnlySpan<byte> hevcData,
    int codedWidth,
    int codedHeight,
    out byte[][] planes
  ) {
    planes = [];
    if (codedWidth <= 0 || codedHeight <= 0
        || codedWidth % _CTB_SIZE != 0 || codedHeight % _CTB_SIZE != 0)
      return false;

    var annexB = new byte[3 + hevcData.Length];
    annexB[2] = 1;
    hevcData.CopyTo(annexB.AsSpan(3));

    H265PictureParameterSet? pps = null;
    H265NalUnit? coded = null;
    foreach (var nal in H265NalReader.SplitAnnexB(annexB))
      switch (nal.Type) {
        case H265NalUnitType.PictureParameterSet:
          pps = H265PictureParameterSet.Parse(nal.Payload);
          break;
        case H265NalUnitType.IdrWithNoLeadingPictures:
        case H265NalUnitType.IdrWithRandomAccessDecodableLeading:
          coded ??= nal;
          break;
      }

    if (pps == null || coded == null
        || !_BpgSliceHeaderIsUniformPcm(pps, coded, out var sliceQpY, out var dataOffset))
      return false;

    var samples = new byte[_BPG_PLANES][];
    for (var plane = 0; plane < _BPG_PLANES; ++plane)
      samples[plane] = new byte[codedWidth * codedHeight];

    var contexts = new byte[H265CabacContexts.COUNT];
    H265CabacContexts.Initialize(contexts, 0, sliceQpY);

    var engine = new H265CabacEngine(coded.Payload, contexts);
    try {
      engine.Start(dataOffset);
    } catch (InvalidDataException) {
      return false;
    }

    var across = codedWidth >> _CTB_LOG2;
    var total = across * (codedHeight >> _CTB_LOG2);

    for (var index = 0; index < total; ++index) {
      if (engine.DecodeBin(H265CabacContexts.PART_MODE) != 1) // part_mode: PART_2Nx2N
        return false;
      if (engine.DecodeTerminate() == 0) // pcm_flag
        return false;

      var at = (engine.BitPosition + 7) >> 3;
      if (at > coded.Payload.Length - _BPG_PCM_BYTES_PER_CTB)
        return false;

      var x = (index % across) << _CTB_LOG2;
      var y = (index / across) << _CTB_LOG2;
      foreach (var plane in samples)
        for (var row = 0; row < _CTB_SIZE; ++row) {
          var to = (y + row) * codedWidth + x;
          for (var column = 0; column < _CTB_SIZE; ++column)
            plane[to + column] = coded.Payload[at++];
        }

      // pcm() initializes the arithmetic registers again but does not initialize the contexts.
      engine = new H265CabacEngine(coded.Payload, contexts);
      try {
        engine.Start(at);
      } catch (InvalidDataException) {
        return false;
      }

      var end = engine.DecodeTerminate();
      if (index + 1 == total) {
        if (end == 0)
          return false;
      } else if (end != 0)
        return false;
    }

    planes = samples;
    return true;
  }

  /// <summary>
  /// Reads the slice segment header of a candidate BPG picture and says whether its coding units are
  /// the unsplit PCM ones this codec writes, leaving the byte the entropy-coded data starts at.
  /// </summary>
  private static bool _BpgSliceHeaderIsUniformPcm(
    H265PictureParameterSet pps,
    H265NalUnit coded,
    out int sliceQpY,
    out int dataOffset
  ) {
    sliceQpY = 0;
    dataOffset = 0;
    if (pps.DependentSliceSegmentsEnabled || pps.OutputFlagPresent || pps.ExtraSliceHeaderBits != 0
        || pps.CabacInitPresent || pps.SliceChromaQpOffsetsPresent || pps.TilesEnabled
        || pps.EntropyCodingSyncEnabled || pps.LoopFilterAcrossSlicesEnabled
        || pps.DeblockingFilterOverrideEnabled || pps.SliceSegmentHeaderExtensionPresent
        || pps.CuQpDeltaEnabled || pps.TransquantBypassEnabled)
      return false;

    var reader = new H265BitReader(coded.Payload);
    if (reader.ReadBit() != 1) // first_slice_segment_in_pic_flag
      return false;

    reader.Skip(1); // no_output_of_prior_pics_flag: this NAL type is always an IRAP one
    if (reader.ReadUnsignedExpGolomb() != pps.Id) // slice_pic_parameter_set_id
      return false;
    if (reader.ReadUnsignedExpGolomb() != 2) // slice_type: I
      return false;

    sliceQpY = pps.InitQp + reader.ReadSignedExpGolomb(); // slice_qp_delta
    reader.Skip(1); // alignment_bit_equal_to_one
    reader.AlignToByte();
    dataOffset = reader.BytePosition;
    return true;
  }

  /// <summary>The sequence parameter set fields BPG keeps in its own header, in its own order.</summary>
  private static byte[] _BuildBpgSequenceHeader() {
    var w = new Bits();
    w.WriteUe(_CTB_LOG2 - 3); // log2_min_luma_coding_block_size_minus3 => 32
    w.WriteUe(0); // log2_diff_max_min_luma_coding_block_size => the tree block is that same 32
    w.WriteUe(0); // log2_min_transform_block_size_minus2 => 4
    w.WriteUe(3); // log2_diff_max_min_transform_block_size => 32
    w.WriteUe(0); // max_transform_hierarchy_depth_intra
    w.WriteBit(0); // sample_adaptive_offset_enabled_flag
    w.WriteBit(1); // pcm_enabled_flag
    w.WriteBits(7, 4); // pcm_sample_bit_depth_luma_minus1
    w.WriteBits(7, 4); // pcm_sample_bit_depth_chroma_minus1
    w.WriteUe(_CTB_LOG2 - 3); // log2_min_pcm_luma_coding_block_size_minus3 => 32
    w.WriteUe(0); // log2_diff_max_min_pcm_luma_coding_block_size
    w.WriteBit(1); // pcm_loop_filter_disabled_flag
    w.WriteBit(0); // strong_intra_smoothing_enabled_flag
    w.WriteBit(0); // sps_extension_present_flag

    // BPG's trailing_bits are zeroes to the next byte boundary. They are not rbsp_trailing_bits:
    // there is no stop bit here, because this header is not a NAL unit and nothing scans back for
    // the end of it — the length in front of it already said where it stops.
    w.WriteZeroAlignment();
    return w.ToArray();
  }

  private static byte[][] _BuildBpgPlanes(RawImage source, int width, int height) {
    // BPG's RGB colour space is the absence of a colour transform, and its component order is
    // HEVC's own: G in the luma plane, B in the first chroma plane and R in the second.
    var rgb = source.ToRgb24();
    var count = source.Width * source.Height;
    var g = new byte[count];
    var b = new byte[count];
    var r = new byte[count];
    for (var i = 0; i < count; ++i) {
      r[i] = rgb[i * 3];
      g[i] = rgb[i * 3 + 1];
      b[i] = rgb[i * 3 + 2];
    }

    return [
      _PadPlane(g, source.Width, source.Height, width, height),
      _PadPlane(b, source.Width, source.Height, width, height),
      _PadPlane(r, source.Width, source.Height, width, height),
    ];
  }

  private static byte[] _BuildBpgSlice(byte[][] planes, int width, int height) {
    var header = new Bits();
    header.WriteBit(1); // first_slice_segment_in_pic_flag
    header.WriteBit(0); // no_output_of_prior_pics_flag
    header.WriteUe(0); // slice_pic_parameter_set_id
    header.WriteUe(2); // slice_type = I
    header.WriteSe(0); // slice_qp_delta
    header.WriteByteAlignment();

    var result = new List<byte>(header.ByteLength + planes.Length * width * height + 128);
    result.AddRange(header.ToArray());

    var contexts = new byte[H265CabacContexts.COUNT];
    H265CabacContexts.Initialize(contexts, 0, 26);

    var across = width >> _CTB_LOG2;
    var total = across * (height >> _CTB_LOG2);

    result.AddRange(_FindUniformCuPrefix(contexts, leadingEndFlag: null));

    for (var index = 0; index < total; ++index) {
      var x = (index % across) << _CTB_LOG2;
      var y = (index / across) << _CTB_LOG2;
      foreach (var plane in planes)
        for (var row = 0; row < _CTB_SIZE; ++row) {
          var at = (y + row) * width + x;
          for (var column = 0; column < _CTB_SIZE; ++column)
            result.Add(plane[at + column]);
        }

      result.AddRange(index + 1 == total
        ? _SearchCabac(contexts, (ref H265CabacEngine engine) => engine.DecodeTerminate() != 0)
        : _FindUniformCuPrefix(contexts, leadingEndFlag: false));
    }

    result.Add(0x80); // rbsp_slice_segment_trailing_bits()
    return result.ToArray();
  }

  /// <summary>
  /// Finds a code point that decodes as one unsplit PCM coding unit, optionally preceded by the
  /// end-of-slice bin that separates it from the coding unit before it.
  /// </summary>
  /// <remarks>
  /// The element in front of pcm_flag is not the one <see cref="_FindCabacPrefix"/> codes. A coding
  /// block the size of the smallest the sequence allows has no split_cu_flag and does have
  /// part_mode, whose first bin says PART_2Nx2N for an intra unit.
  /// </remarks>
  private static byte[] _FindUniformCuPrefix(byte[] contexts, bool? leadingEndFlag)
    => _SearchCabac(contexts, (ref H265CabacEngine engine) => {
      if (leadingEndFlag.HasValue && engine.DecodeTerminate() != (leadingEndFlag.Value ? 1 : 0))
        return false;
      if (engine.DecodeBin(H265CabacContexts.PART_MODE) != 1)
        return false;
      return engine.DecodeTerminate() != 0; // pcm_flag
    });

  internal static RawImage _PadRgbToEven(RawImage source, int width, int height) {
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

  internal static (byte[] Y, byte[] Cb, byte[] Cr) _PadYuvToCodedSize(RawImage yuv, int width, int height) {
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

  /// <summary>
  /// Picks the lowest general_level_idc whose picture-size limit covers this image. A level a
  /// picture does not fit into makes the stream non-conformant, and a decoder that allocates from
  /// the level is entitled to refuse it, so the number cannot be a constant.
  /// </summary>
  internal static byte _SmallestLevelFor(int width, int height) {
    // H.265 Table A.8, MaxLumaPs per level, plus A.4.1's dimension bound of Sqrt(MaxLumaPs * 8).
    (byte Level, long MaxLumaPs)[] levels = [
      (30, 36864), (60, 122880), (63, 245760), (90, 552960), (93, 983040),
      (120, 2228224), (150, 8912896), (180, 35651584),
    ];

    var samples = (long)width * height;
    foreach (var (level, maxLumaPs) in levels) {
      var maxSide = (long)Math.Sqrt(maxLumaPs * 8d);
      if (samples <= maxLumaPs && width <= maxSide && height <= maxSide)
        return level;
    }

    throw new NotSupportedException(
      $"HEVC: a {width} by {height} picture exceeds the largest defined level's picture size.");
  }

  internal static byte[] _BuildVps(byte level) {
    var w = new Bits();
    w.WriteBits(0, 4); // vps_video_parameter_set_id
    w.WriteBit(1); // vps_base_layer_internal_flag
    w.WriteBit(1); // vps_base_layer_available_flag
    w.WriteBits(0, 6); // vps_max_layers_minus1
    w.WriteBits(0, 3); // vps_max_sub_layers_minus1
    w.WriteBit(1); // vps_temporal_id_nesting_flag
    w.WriteBits(0xffff, 16);
    _WriteProfileTierLevel(w, level);
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

  internal static byte[] _BuildSps(
    int width, int height, int displayWidth, int displayHeight, byte level,
    int maxDecPicBufferingMinus1 = 0, int log2MaxPocLsbMinus4 = 0, int maxNumReorderPics = 0) {
    var w = new Bits();
    w.WriteBits(0, 4); // sps_video_parameter_set_id
    w.WriteBits(0, 3); // sps_max_sub_layers_minus1
    w.WriteBit(1); // temporal nesting
    _WriteProfileTierLevel(w, level);
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
    w.WriteUe((uint)log2MaxPocLsbMinus4);
    w.WriteBit(0); // sub_layer_ordering_info_present_flag
    w.WriteUe((uint)maxDecPicBufferingMinus1);
    w.WriteUe((uint)maxNumReorderPics);
    w.WriteUe(0); // max_latency_increase_plus1
    w.WriteUe(0); // log2_min_luma_coding_block_size_minus3 => 8
    w.WriteUe((uint)(_CTB_LOG2 - 3)); // log2_diff_max_min_luma_coding_block_size
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
    w.WriteUe((uint)(_CTB_LOG2 - 3)); // log2_diff_max_min_pcm_luma_coding_block_size
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

  internal static byte[] _BuildPps(bool cabacInitPresent = false) {
    var w = new Bits();
    w.WriteUe(0); // pps_pic_parameter_set_id
    w.WriteUe(0); // pps_seq_parameter_set_id
    w.WriteBit(0); // dependent_slice_segments_enabled_flag
    w.WriteBit(0); // output_flag_present_flag
    w.WriteBits(0, 3); // num_extra_slice_header_bits
    w.WriteBit(0); // sign_data_hiding_enabled_flag
    w.WriteBit(cabacInitPresent ? 1 : 0); // cabac_init_present_flag
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

  internal static byte[] _BuildSlice(byte[] y, byte[] cb, byte[] cr, int width, int height) {
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
  /// Finds a code point that decodes as split_cu_flag=0, pcm_flag=1.
  /// </summary>
  private static byte[] _FindCabacPrefix(byte[] contexts, bool? leadingEndFlag)
    => _SearchCabac(contexts, (ref H265CabacEngine engine) => {
      if (leadingEndFlag.HasValue && engine.DecodeTerminate() != (leadingEndFlag.Value ? 1 : 0))
        return false;
      if (engine.DecodeBin(H265CabacContexts.SPLIT_CU_FLAG) != 0)
        return false;
      return engine.DecodeTerminate() != 0;
    });

  private static byte[] _FindCabacSuffix(byte[] contexts, bool final) {
    if (final)
      return _SearchCabac(contexts, (ref H265CabacEngine engine) => engine.DecodeTerminate() != 0);
    return _FindCabacPrefix(contexts, leadingEndFlag: false);
  }

  private delegate bool CabacProbe(ref H265CabacEngine engine);

  /// <summary>
  /// Finds the shortest whole number of bytes that decodes as the bins <paramref name="probe"/>
  /// asks for and is spent exactly by them.
  /// </summary>
  /// <remarks>
  /// Two conditions, and the second carries as much weight as the first. The arithmetic decoder
  /// holds nine bits of lookahead, so a code point can decode the right bins and still leave the
  /// decoder's read pointer inside the byte the samples start in — and clause 9.3.4.3.5 puts
  /// pcm_sample_luma at the first byte boundary at or after the last bit the arithmetic decoder
  /// drew. A candidate whose lookahead crossed that boundary is one this encoder and a conforming
  /// decoder would place the samples differently for, so it is rejected; when no two-byte code point
  /// survives both conditions the search widens to three.
  /// </remarks>
  private static byte[] _SearchCabac(byte[] contexts, CabacProbe probe) {
    var baseline = (byte[])contexts.Clone();

    for (var length = 2; length <= 3; ++length) {
      var limit = 1L << (length << 3);
      for (var value = 0L; value < limit; ++value) {
        Array.Copy(baseline, contexts, contexts.Length);

        var bytes = new byte[length];
        for (var i = 0; i < length; ++i)
          bytes[i] = (byte)(value >> ((length - 1 - i) << 3));

        var engine = new H265CabacEngine(bytes, contexts);
        try {
          engine.Start(0);
        } catch (InvalidDataException) {
          continue;
        }

        if (!probe(ref engine) || ((engine.BitPosition + 7) >> 3) != length)
          continue;

        return bytes;
      }
    }

    Array.Copy(baseline, contexts, contexts.Length);
    throw new InvalidOperationException("No short HEVC CABAC interval represented the required PCM syntax.");
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

  internal static byte[] _BuildDecoderConfiguration(byte[] vps, byte[] sps, byte[] pps, byte level) {
    var size = 23 + (3 + 2 + vps.Length) + (3 + 2 + sps.Length) + (3 + 2 + pps.Length);
    var result = new byte[size];
    result[0] = 1;
    result[1] = H265ProfileTierLevel.MAIN_STILL_PICTURE;
    // general_profile_compatibility_flags: advertise Main and Main Still Picture.
    result[2] = 0x50;
    result[6] = _CONSTRAINT_FLAGS_FIRST_BYTE;
    result[12] = level;
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

  internal static byte[] _MakeNal(H265NalUnitType type, byte[] rbsp) {
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

  private static void _WriteProfileTierLevel(Bits w, byte level) {
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
    w.WriteBits(level, 8); // general_level_idc
  }

  /// <summary>
  /// The first six bytes of general_constraint_indicator_flags as
  /// <see cref="_WriteProfileTierLevel"/> spells them. ISO/IEC 14496-15 requires the hvcC record to
  /// repeat the sequence parameter set's values rather than invent its own.
  /// </summary>
  private const byte _CONSTRAINT_FLAGS_FIRST_BYTE = 0b1011_0000;

  internal static int _RoundUp(int value, int multiple)
    => checked((value + multiple - 1) / multiple * multiple);

  internal sealed class Bits {
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

    /// <summary>Pads to the next byte boundary with zeroes, with no stop bit in front of them.</summary>
    internal void WriteZeroAlignment() {
      while (this._used != 0)
        this.WriteBit(0);
    }

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
