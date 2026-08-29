using System;
using System.Collections.Generic;

namespace FileFormat.Avif.Codec;

/// <summary>
/// Writes the uncompressed AV1 OBU syntax shared by the managed AVIF still-picture encoder.
/// </summary>
internal static class Av1StillPictureHeaderWriter {

  /// <summary>
  /// Emits a Profile-0, 8-bit, 4:2:0 reduced-still-picture sequence-header OBU for one image size.
  /// </summary>
  internal static byte[] WriteSequenceHeaderObu(int width, int height) {
    if (width <= 0)
      throw new ArgumentOutOfRangeException(nameof(width));
    if (height <= 0)
      throw new ArgumentOutOfRangeException(nameof(height));
    if (width > 65536 || height > 65536)
      throw new NotSupportedException("The minimal AV1 still-picture profile currently limits either dimension to 65536 samples.");

    var widthBits = _BitsRequired(width - 1);
    var heightBits = _BitsRequired(height - 1);
    var bits = new _BitWriter();

    // AV1 5.5 sequence_header_obu(). Profile 0 is the interoperable 8-bit 4:2:0 profile used by AVIF.
    bits.Write(0, 3); // seq_profile
    bits.WriteBit(1); // still_picture
    bits.WriteBit(1); // reduced_still_picture_header

    // Reduced headers have one implicit operating point. Level 2.0 (seq_level_idx = 0) comfortably
    // covers the small still-image baseline; a later level selector can raise this for very large images.
    bits.Write(0, 5); // seq_level_idx[0]

    bits.Write((uint)(widthBits - 1), 4);
    bits.Write((uint)(heightBits - 1), 4);
    bits.Write((uint)(width - 1), widthBits);
    bits.Write((uint)(height - 1), heightBits);

    // Deliberately disable optional prediction/filtering tools. None is needed by the first intra-only
    // lossless baseline and omitting them removes syntax from both the frame header and tile body.
    bits.WriteBit(0); // use_128x128_superblock -> 64x64 SB
    bits.WriteBit(0); // enable_filter_intra
    bits.WriteBit(0); // enable_intra_edge_filter
    bits.WriteBit(0); // enable_superres
    bits.WriteBit(0); // enable_cdef
    bits.WriteBit(0); // enable_restoration

    // color_config(): 8-bit Profile 0, three planes, unspecified colorimetry, full-range 4:2:0.
    bits.WriteBit(0); // high_bitdepth
    bits.WriteBit(0); // mono_chrome
    bits.WriteBit(0); // color_description_present_flag
    bits.WriteBit(1); // color_range = full
    // Profile 0 fixes subsampling_x = subsampling_y = 1.
    bits.Write(0, 2); // chroma_sample_position = CSP_UNKNOWN
    bits.WriteBit(0); // separate_uv_delta_q

    bits.WriteBit(0); // film_grain_params_present
    bits.WriteTrailingBits();

    return _WrapSizedObu(Av1ObuType.SequenceHeader, bits.ToArray());
  }

  private static byte[] _WrapSizedObu(Av1ObuType type, byte[] payload) {
    var result = new List<byte>(payload.Length + 10) {
      (byte)(((int)type << 3) | 0x02), // obu_extension_flag=0, obu_has_size_field=1, reserved=0
    };
    _WriteLeb128(result, (ulong)payload.Length);
    result.AddRange(payload);
    return result.ToArray();
  }

  private static void _WriteLeb128(List<byte> target, ulong value) {
    do {
      var next = (byte)(value & 0x7F);
      value >>= 7;
      if (value != 0)
        next |= 0x80;
      target.Add(next);
    } while (value != 0);
  }

  private static int _BitsRequired(int value) {
    var bits = 1;
    while ((value >>= 1) != 0)
      ++bits;
    return bits;
  }

  private sealed class _BitWriter {
    private readonly List<byte> _bytes = [];
    private int _bitsInCurrent;
    private byte _current;

    internal void WriteBit(int bit) {
      if ((uint)bit > 1)
        throw new ArgumentOutOfRangeException(nameof(bit));
      this._current |= (byte)(bit << (7 - this._bitsInCurrent));
      if (++this._bitsInCurrent == 8)
        this._FlushCurrent();
    }

    internal void Write(uint value, int count) {
      if (count is < 0 or > 32)
        throw new ArgumentOutOfRangeException(nameof(count));
      if (count < 32 && value >= (1u << count))
        throw new ArgumentOutOfRangeException(nameof(value));
      for (var bit = count - 1; bit >= 0; --bit)
        this.WriteBit((int)((value >> bit) & 1));
    }

    internal void WriteTrailingBits() {
      this.WriteBit(1); // trailing_one_bit
      while (this._bitsInCurrent != 0)
        this.WriteBit(0);
    }

    internal byte[] ToArray() {
      if (this._bitsInCurrent != 0)
        throw new InvalidOperationException("AV1 OBU payload was not byte-aligned with trailing_bits().");
      return this._bytes.ToArray();
    }

    private void _FlushCurrent() {
      this._bytes.Add(this._current);
      this._current = 0;
      this._bitsInCurrent = 0;
    }
  }
}
