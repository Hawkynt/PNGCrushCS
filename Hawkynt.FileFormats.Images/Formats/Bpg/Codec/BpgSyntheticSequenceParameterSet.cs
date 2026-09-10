using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.H265;

namespace FileFormat.Bpg.Codec;

/// <summary>Reconstructs the standard SPS state that BPG deliberately removes from its HEVC stream.</summary>
/// <remarks>
/// BPG 0.9.5 section 3.2 fixes every SPS field not carried by <see cref="BpgSequenceHeader"/>. Building
/// an ordinary RBSP from those values lets the shared H.265 parser and decoder remain the authority
/// for HEVC syntax instead of maintaining a second BPG-only decoder.
/// </remarks>
internal static class BpgSyntheticSequenceParameterSet {

  internal static H265SequenceParameterSet Create(BpgFile bpg, BpgSequenceHeader header) {
    ArgumentNullException.ThrowIfNull(bpg);
    ArgumentNullException.ThrowIfNull(header);

    var chromaFormat = bpg.PixelFormat switch {
      BpgPixelFormat.Grayscale => 0,
      BpgPixelFormat.YCbCr420 or BpgPixelFormat.YCbCr420Mpeg2 => 1,
      BpgPixelFormat.YCbCr422 or BpgPixelFormat.YCbCr422Mpeg2 => 2,
      BpgPixelFormat.YCbCr444 => 3,
      _ => throw new InvalidDataException($"BPG pixel_format {(int)bpg.PixelFormat} cannot be represented by HEVC."),
    };

    _ValidateBlockGeometry(header);
    var minBlock = 1 << header.Log2MinLumaCodingBlockSize;
    var codedWidth = _RoundUp(bpg.Width, minBlock);
    var codedHeight = _RoundUp(bpg.Height, minBlock);

    var writer = new BitWriter();
    writer.WriteBits(0, 4); // sps_video_parameter_set_id
    writer.WriteBits(0, 3); // sps_max_sub_layers_minus1
    writer.WriteBit(true); // sps_temporal_id_nesting_flag

    // profile_tier_level(): BPG is a subset of Main 4:4:4 16 Still Picture, Level 8.5. The shared
    // decoder does not gate reconstruction on the profile label, but Parse() must step over a valid
    // structure to reach the SPS fields that do affect samples.
    writer.WriteBits(0, 2); // general_profile_space
    writer.WriteBit(false); // general_tier_flag
    writer.WriteBits(H265ProfileTierLevel.FORMAT_RANGE_EXTENSIONS, 5);
    writer.WriteBits(0, 32); // general_profile_compatibility_flags
    writer.WriteBits(0, 32); // first 32 general_constraint_indicator_flags bits
    writer.WriteBits(0, 16); // remaining constraint bits
    writer.WriteBits(255, 8); // general_level_idc: level 8.5

    writer.WriteUnsignedExpGolomb(0); // sps_seq_parameter_set_id
    writer.WriteUnsignedExpGolomb(chromaFormat);
    if (chromaFormat == 3)
      writer.WriteBit(false); // separate_colour_plane_flag

    writer.WriteUnsignedExpGolomb(codedWidth);
    writer.WriteUnsignedExpGolomb(codedHeight);
    writer.WriteBit(false); // conformance_window_flag; BPG crops the lower/right edge itself
    writer.WriteUnsignedExpGolomb(bpg.BitDepth - 8);
    writer.WriteUnsignedExpGolomb(bpg.BitDepth - 8);
    writer.WriteUnsignedExpGolomb(4); // log2_max_pic_order_cnt_lsb_minus4

    writer.WriteBit(false); // sps_sub_layer_ordering_info_present_flag
    writer.WriteUnsignedExpGolomb(0); // sps_max_dec_pic_buffering_minus1
    writer.WriteUnsignedExpGolomb(0); // sps_max_num_reorder_pics
    writer.WriteUnsignedExpGolomb(0); // sps_max_latency_increase_plus1

    writer.WriteUnsignedExpGolomb(header.Log2MinLumaCodingBlockSize - 3);
    writer.WriteUnsignedExpGolomb(header.Log2CtbSize - header.Log2MinLumaCodingBlockSize);
    writer.WriteUnsignedExpGolomb(header.Log2MinTransformBlockSize - 2);
    writer.WriteUnsignedExpGolomb(header.Log2MaxTransformBlockSize - header.Log2MinTransformBlockSize);
    writer.WriteUnsignedExpGolomb(header.MaxTransformHierarchyDepthIntra); // inter = intra in BPG
    writer.WriteUnsignedExpGolomb(header.MaxTransformHierarchyDepthIntra);

    writer.WriteBit(false); // scaling_list_enabled_flag
    writer.WriteBit(true); // amp_enabled_flag
    writer.WriteBit(header.SaoEnabled);
    writer.WriteBit(header.PcmEnabled);
    if (header.PcmEnabled) {
      writer.WriteBits(header.PcmBitDepthLuma - 1, 4);
      writer.WriteBits(header.PcmBitDepthChroma - 1, 4);
      writer.WriteUnsignedExpGolomb(header.Log2MinPcmCodingBlockSize - 3);
      writer.WriteUnsignedExpGolomb(header.Log2MaxPcmCodingBlockSize - header.Log2MinPcmCodingBlockSize);
      writer.WriteBit(header.PcmLoopFilterDisabled);
    }

    writer.WriteUnsignedExpGolomb(0); // num_short_term_ref_pic_sets
    writer.WriteBit(false); // long_term_ref_pics_present_flag
    writer.WriteBit(true); // sps_temporal_mvp_enabled_flag
    writer.WriteBit(header.StrongIntraSmoothingEnabled);
    writer.WriteBit(false); // vui_parameters_present_flag; BPG's container states the colour meaning
    writer.WriteBit(false); // sps_extension_present_flag; supported range-extension flags are all zero
    writer.WriteRbspTrailingBits();

    return H265SequenceParameterSet.Parse(writer.ToArray());
  }

  private static void _ValidateBlockGeometry(BpgSequenceHeader header) {
    if (header.Log2MinLumaCodingBlockSize is < 3 or > 6
        || header.Log2CtbSize < header.Log2MinLumaCodingBlockSize
        || header.Log2CtbSize > 6)
      throw new InvalidDataException(
        $"BPG HEVC coding-block sizes 2^{header.Log2MinLumaCodingBlockSize} through 2^{header.Log2CtbSize} are outside H.265's SPS bounds.");

    if (header.Log2MinTransformBlockSize is < 2 or > 5
        || header.Log2MaxTransformBlockSize < header.Log2MinTransformBlockSize
        || header.Log2MaxTransformBlockSize > 5)
      throw new InvalidDataException(
        $"BPG HEVC transform-block sizes 2^{header.Log2MinTransformBlockSize} through 2^{header.Log2MaxTransformBlockSize} are outside H.265's SPS bounds.");

    if (!header.PcmEnabled)
      return;

    if (header.PcmBitDepthLuma is < 1 or > 16 || header.PcmBitDepthChroma is < 1 or > 16
        || header.Log2MinPcmCodingBlockSize is < 3 or > 5
        || header.Log2MaxPcmCodingBlockSize < header.Log2MinPcmCodingBlockSize
        || header.Log2MaxPcmCodingBlockSize > Math.Min(header.Log2CtbSize, 5))
      throw new InvalidDataException("The BPG PCM fields are outside the bounds of the corresponding H.265 SPS fields.");
  }

  private static int _RoundUp(int value, int multiple) {
    var rounded = ((long)value + multiple - 1) / multiple * multiple;
    if (rounded > int.MaxValue)
      throw new InvalidDataException("The BPG coded picture dimensions exceed this decoder's address space.");
    return (int)rounded;
  }

  private sealed class BitWriter {
    private readonly List<byte> _bytes = [];
    private int _current;
    private int _bits;

    internal void WriteBit(bool value) => this.WriteBits(value ? 1 : 0, 1);

    internal void WriteBits(int value, int count) {
      for (var bit = count - 1; bit >= 0; --bit) {
        this._current = (this._current << 1) | ((value >> bit) & 1);
        if (++this._bits != 8)
          continue;

        this._bytes.Add((byte)this._current);
        this._current = 0;
        this._bits = 0;
      }
    }

    internal void WriteUnsignedExpGolomb(int value) {
      if (value < 0)
        throw new ArgumentOutOfRangeException(nameof(value));

      var code = (uint)value + 1;
      var bits = 32 - System.Numerics.BitOperations.LeadingZeroCount(code);
      for (var i = 1; i < bits; ++i)
        this.WriteBit(false);
      this.WriteBits(unchecked((int)code), bits);
    }

    internal void WriteRbspTrailingBits() {
      this.WriteBit(true);
      while (this._bits != 0)
        this.WriteBit(false);
    }

    internal byte[] ToArray() {
      if (this._bits != 0)
        throw new InvalidOperationException("The synthetic SPS RBSP was not byte aligned.");
      return [.. this._bytes];
    }
  }
}
