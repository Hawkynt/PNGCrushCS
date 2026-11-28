using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.H265;

namespace FileFormat.Bpg.Codec;

/// <summary>
/// Decodes the HEVC streams of a BPG file — the colour stream and, where there is one, the alpha
/// stream — into pictures, every frame of an animation included.
/// </summary>
/// <remarks>
/// A BPG file carries neither a video nor a sequence parameter set. In their place each stream has
/// a short header holding the fields an encoder chooses, and the specification fixes the rest: ids
/// of zero, one sub-layer, the coded size rounded up to whole minimum coding blocks, both depths
/// from the container's own <c>bit_depth_minus_8</c>, <c>chroma_format_idc</c> from its pixel format
/// (zero for alpha), a picture order count of eight bits, no scaling lists, asymmetric motion
/// partitions and temporal motion vector prediction enabled, and no reference picture sets of the
/// sequence's own. Those two halves are put back together here into a real sequence parameter set,
/// so the picture data — which does carry its picture parameter sets — goes through the general
/// H.265 decoder unchanged. The alpha stream's NAL units are the ones with <c>nuh_layer_id</c> one;
/// they are separated out and decoded as a stream of their own.
/// <para/>
/// An animation's later frames are P pictures referring to the one before, and how many times a
/// frame repeats is a prefix SEI message of payload type 257 ahead of its first slice.
/// <para/>
/// Converted from Fabrice Bellard's libbpg (<c>libbpg.c</c>, MIT licence), whose header handling
/// and modified sequence parameter set this follows.
/// </remarks>
internal static class BpgStreamDecoder {

  /// <summary>The fields of one <c>hevc_header()</c>, in the order the SPS states them.</summary>
  private sealed record Header(
    int Log2MinCbMinus3, int Log2DiffMaxMinCb, int Log2MinTbMinus2, int Log2DiffMaxMinTb, int MaxTransformHierarchyDepthIntra,
    bool Sao, bool Pcm, int PcmLumaMinus1, int PcmChromaMinus1, int Log2MinPcmMinus3, int Log2DiffMaxMinPcm, bool PcmLoopFilterDisabled,
    bool StrongIntraSmoothing, bool ExtensionPresent, bool RangeExtension, int Extension7Bits, int RangeFlags);

  /// <summary>One decoded frame: the colour picture, the alpha picture if any, and how many periods it lasts.</summary>
  internal sealed record Frame(H265Picture Colour, H265Picture? Alpha, int Duration);

  internal static IReadOnlyList<Frame> Decode(byte[] pictureData, int width, int height, int chromaFormatIdc, int bitDepth, bool hasAlpha) {
    var offset = 0;
    Header? alphaHeader = null;
    if (hasAlpha)
      alphaHeader = _ReadHeader(pictureData, ref offset);
    var colourHeader = _ReadHeader(pictureData, ref offset);
    if (offset >= pictureData.Length)
      throw new InvalidDataException("A BPG file ends after its HEVC headers, before any picture data.");

    var (colourStream, alphaStream, durations) = _Split(pictureData.AsSpan(offset), hasAlpha);

    var colour = _DecodeStream(colourHeader, colourStream, width, height, chromaFormatIdc, bitDepth);
    var alpha = alphaHeader is null ? null : _DecodeStream(alphaHeader, alphaStream, width, height, 0, bitDepth);
    if (colour.Count == 0)
      throw new InvalidDataException("A BPG file's colour stream decodes to no picture.");
    if (alpha is not null && alpha.Count != colour.Count)
      throw new InvalidDataException($"A BPG file's alpha stream holds {alpha.Count} pictures and its colour stream {colour.Count}.");

    var frames = new Frame[colour.Count];
    for (var i = 0; i < frames.Length; ++i)
      frames[i] = new(colour[i], alpha?[i], i < durations.Count ? durations[i] : 1);
    return frames;
  }

  private static Header _ReadHeader(byte[] data, ref int offset) {
    var length = BpgUe7.Read(data, ref offset);
    if (length <= 0 || offset + length > data.Length)
      throw new InvalidDataException("A BPG file's HEVC header states a length its picture data does not hold.");

    var reader = new H265BitReader(data.AsSpan(offset, length));
    offset += length;
    var log2MinCb = reader.ReadUnsignedExpGolomb();
    var log2DiffCb = reader.ReadUnsignedExpGolomb();
    var log2MinTb = reader.ReadUnsignedExpGolomb();
    var log2DiffTb = reader.ReadUnsignedExpGolomb();
    var depthIntra = reader.ReadUnsignedExpGolomb();
    var sao = reader.ReadFlag();
    var pcm = reader.ReadFlag();
    int pcmLuma = 0, pcmChroma = 0, log2MinPcm = 0, log2DiffPcm = 0;
    var pcmLoop = false;
    if (pcm) {
      pcmLuma = reader.ReadBits(4);
      pcmChroma = reader.ReadBits(4);
      log2MinPcm = reader.ReadUnsignedExpGolomb();
      log2DiffPcm = reader.ReadUnsignedExpGolomb();
      pcmLoop = reader.ReadFlag();
    }

    var strong = reader.ReadFlag();
    var extension = reader.ReadFlag();
    var range = false;
    var seven = 0;
    var rangeFlags = 0;
    if (extension) {
      range = reader.ReadFlag();
      seven = reader.ReadBits(7);
    }

    if (range)
      rangeFlags = reader.ReadBits(9);

    if (log2MinCb > 3 || log2DiffCb > 3 || log2MinTb > 3 || log2DiffTb > 3)
      throw new InvalidDataException("A BPG file's HEVC header states block sizes H.265 does not allow.");
    return new(log2MinCb, log2DiffCb, log2MinTb, log2DiffTb, depthIntra, sao, pcm, pcmLuma, pcmChroma, log2MinPcm, log2DiffPcm, pcmLoop,
      strong, extension, range, seven, rangeFlags);
  }

  /// <summary>
  /// Splits <c>hevc_data()</c> — NAL units in the byte stream format, the first without its start
  /// code — into the colour stream and the alpha stream, and collects the frame durations.
  /// </summary>
  private static (List<H265NalUnit> Colour, List<H265NalUnit> Alpha, List<int> Durations) _Split(ReadOnlySpan<byte> data, bool hasAlpha) {
    var annexB = new byte[data.Length + 3];
    annexB[2] = 1;
    data.CopyTo(annexB.AsSpan(3));

    var colour = new List<H265NalUnit>();
    var alpha = new List<H265NalUnit>();
    var durations = new List<int>();
    var pendingDuration = 1;
    foreach (var range in _NalRanges(annexB)) {
      var unit = annexB.AsSpan(range.Start, range.Length);
      if (unit.Length < 2)
        throw new InvalidDataException("A BPG file's picture data holds a NAL unit too short for its header.");

      var layer = ((unit[0] & 1) << 5) | (unit[1] >> 3);
      if (layer > 1 || (layer == 1 && !hasAlpha))
        throw new InvalidDataException($"A BPG file's picture data holds a NAL unit of layer {layer}.");

      // The alpha stream is an ordinary single-layer stream once its layer id is put back to zero.
      var copy = unit.ToArray();
      copy[0] &= 0xFE;
      copy[1] &= 0x07;
      var nal = H265NalReader.Parse(copy);
      if (layer == 1) {
        alpha.Add(nal);
        continue;
      }

      if (nal.Type == H265NalUnitType.PrefixSupplementalEnhancementInformation && _FrameDuration(nal.Payload) is { } stated)
        pendingDuration = stated;
      else if (nal.IsSlice && nal.Payload.Length > 0 && (nal.Payload[0] & 0x80) != 0) {
        durations.Add(pendingDuration);
        pendingDuration = 1;
      }

      colour.Add(nal);
    }

    return (colour, alpha, durations);
  }

  private static IEnumerable<(int Start, int Length)> _NalRanges(byte[] data) {
    var starts = new List<int>();
    for (var i = 0; i + 2 < data.Length; ++i)
      if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 1) {
        starts.Add(i + 3);
        i += 2;
      }

    for (var k = 0; k < starts.Count; ++k) {
      var end = k + 1 < starts.Count ? starts[k + 1] - 3 : data.Length;
      while (end > starts[k] && data[end - 1] == 0)
        --end;
      if (end > starts[k])
        yield return (starts[k], end - starts[k]);
    }
  }

  /// <summary>The <c>frame_duration</c> of a payload-type-257 SEI message, if the unit carries one.</summary>
  private static int? _FrameDuration(byte[] payload) {
    var at = 0;
    while (at < payload.Length && payload[at] != 0x80) {
      var type = 0;
      while (at < payload.Length && payload[at] == 0xFF) {
        type += 255;
        ++at;
      }

      if (at >= payload.Length)
        return null;
      type += payload[at++];
      var size = 0;
      while (at < payload.Length && payload[at] == 0xFF) {
        size += 255;
        ++at;
      }

      if (at >= payload.Length)
        return null;
      size += payload[at++];
      if (at + size > payload.Length)
        return null;
      if (type == 257 && size >= 2) {
        var duration = (payload[at] << 8) | payload[at + 1];
        if (duration == 0)
          throw new InvalidDataException("A BPG frame states a duration of zero periods.");
        return duration;
      }

      at += size;
    }

    return null;
  }

  private static List<H265Picture> _DecodeStream(Header header, List<H265NalUnit> nals, int width, int height, int chromaFormatIdc, int bitDepth) {
    var decoder = new H265StreamDecoder();
    var sps = new H265NalUnit(H265NalUnitType.SequenceParameterSet, 0, 0, _BuildSps(header, width, height, chromaFormatIdc, bitDepth), []);
    var units = new List<H265NalUnit>(nals.Count + 1) { sps };
    units.AddRange(nals);
    decoder.Decode(units);
    decoder.Flush();
    var pictures = new List<H265Picture>();
    while (decoder.TryTakePicture(out var picture, out _))
      pictures.Add(picture);
    return pictures;
  }

  /// <summary>The sequence parameter set a BPG header stands for (BPG specification 3.2, libbpg's modified SPS).</summary>
  private static byte[] _BuildSps(Header h, int width, int height, int chromaFormatIdc, int bitDepth) {
    var w = new H265PcmStillCodec.Bits();
    w.WriteBits(0, 4); // sps_video_parameter_set_id
    w.WriteBits(0, 3); // sps_max_sub_layers_minus1
    w.WriteBit(1); // sps_temporal_id_nesting_flag

    // profile_tier_level: format range extensions (Main 4:4:4 16 Still Picture is one of them), level 8.5.
    w.WriteBits(0, 2);
    w.WriteBit(0);
    w.WriteBits(4, 5);
    for (var i = 0; i < 32; ++i)
      w.WriteBit(i == 4 ? 1 : 0);
    w.WriteBit(1); // progressive_source
    w.WriteBit(0); // interlaced_source
    w.WriteBit(1); // non_packed_constraint
    w.WriteBit(1); // frame_only_constraint
    w.WriteBits(0, 32);
    w.WriteBits(0, 12);
    w.WriteBits(255, 8); // general_level_idc

    w.WriteUe(0); // sps_seq_parameter_set_id
    w.WriteUe((uint)chromaFormatIdc);
    if (chromaFormatIdc == 3)
      w.WriteBit(0); // separate_colour_plane_flag
    var cb = 1 << (h.Log2MinCbMinus3 + 3);
    w.WriteUe((uint)((width + cb - 1) / cb * cb));
    w.WriteUe((uint)((height + cb - 1) / cb * cb));
    w.WriteBit(0); // conformance_window_flag: the picture is cropped to the BPG header's size instead
    w.WriteUe((uint)(bitDepth - 8));
    w.WriteUe((uint)(bitDepth - 8));
    w.WriteUe(4); // log2_max_pic_order_cnt_lsb_minus4
    w.WriteBit(1); // sps_sub_layer_ordering_info_present_flag
    w.WriteUe(1); // sps_max_dec_pic_buffering_minus1: the picture before is the only reference
    w.WriteUe(0); // sps_max_num_reorder_pics
    w.WriteUe(0); // sps_max_latency_increase_plus1
    w.WriteUe((uint)h.Log2MinCbMinus3);
    w.WriteUe((uint)h.Log2DiffMaxMinCb);
    w.WriteUe((uint)h.Log2MinTbMinus2);
    w.WriteUe((uint)h.Log2DiffMaxMinTb);
    w.WriteUe((uint)h.MaxTransformHierarchyDepthIntra); // inter: the same as intra
    w.WriteUe((uint)h.MaxTransformHierarchyDepthIntra);
    w.WriteBit(0); // scaling_list_enabled_flag
    w.WriteBit(1); // amp_enabled_flag
    w.WriteBit(h.Sao ? 1 : 0);
    w.WriteBit(h.Pcm ? 1 : 0);
    if (h.Pcm) {
      w.WriteBits((uint)h.PcmLumaMinus1, 4);
      w.WriteBits((uint)h.PcmChromaMinus1, 4);
      w.WriteUe((uint)h.Log2MinPcmMinus3);
      w.WriteUe((uint)h.Log2DiffMaxMinPcm);
      w.WriteBit(h.PcmLoopFilterDisabled ? 1 : 0);
    }

    w.WriteUe(0); // num_short_term_ref_pic_sets
    w.WriteBit(0); // long_term_ref_pics_present_flag
    w.WriteBit(1); // sps_temporal_mvp_enabled_flag
    w.WriteBit(h.StrongIntraSmoothing ? 1 : 0);
    w.WriteBit(0); // vui_parameters_present_flag
    w.WriteBit(h.ExtensionPresent ? 1 : 0);
    if (h.ExtensionPresent) {
      w.WriteBit(h.RangeExtension ? 1 : 0);
      w.WriteBits((uint)h.Extension7Bits, 7);
    }

    if (h.RangeExtension)
      w.WriteBits((uint)h.RangeFlags, 9);
    w.WriteRbspTrailingBits();
    return w.ToArray();
  }
}
