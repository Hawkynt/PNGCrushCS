using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Codecs.CineForm.Tests;

/// <summary>
/// Assembles minimal, valid CineForm frames a tag-value pair and a codeword at a time.
/// </summary>
/// <remarks>
/// Built rather than checked in, exactly as <c>ProResTestStream</c> is for Apple ProRes. The tag
/// numbers and the codeblock framing were measured against ffmpeg's own <c>cfhd</c> encoder — see
/// <see cref="CineFormChannelDecoder"/>'s remarks — and are reproduced here by number rather than by
/// re-deriving them, so a stream built by this class is byte-for-byte what that measurement found,
/// just with the entropy-coded content chosen by the test rather than by an encoder's rate control.
/// <para/>
/// Every highpass subband a stream built here carries is coded as a flat run of the codebook's own
/// shortest codeword — <c>0</c>, one bit, a single zero coefficient — because a wavelet whose highpass
/// is entirely zero reconstructs a channel from its lowpass alone by three exact halvings and whatever
/// prescale shifts land on the levels between them, which is an outcome a test can state as one number
/// rather than as a rendered picture.
/// </remarks>
internal static class CineFormTestStream {

  // ==============================================================================================
  // Bit-level codeword writing
  // ==============================================================================================

  internal sealed class BitWriter {

    private readonly List<byte> _bytes = [];
    private uint _partial;
    private int _partialBits;

    /// <summary>Writes the low <paramref name="count"/> bits of <paramref name="value"/>, most
    /// significant bit first — the same order <see cref="CineFormBitReader"/> reads in.</summary>
    internal void WriteBits(uint value, int count) {
      for (var i = count - 1; i >= 0; --i)
        this._Bit((int)((value >> i) & 1));
    }

    /// <summary>The codebook's shortest codeword — Table C.1's first row: one bit, a single zero
    /// coefficient (run count one, value zero).</summary>
    internal void WriteZeroCoefficient() => this.WriteBits(0, 1);

    /// <summary>Table C.2's band end marker: twenty-six bits, <c>0x03114BA3</c>.</summary>
    internal void WriteBandEndMarker() => this.WriteBits(0x03114BA3, 26);

    /// <summary>A magnitude-one coefficient (Table C.1: two bits, <c>10</c>) and its sign bit.</summary>
    internal void WriteCoefficient(int value) {
      if (value == 0) {
        this.WriteZeroCoefficient();
        return;
      }

      // Table C.1's second entry: codeword 0x02, two bits, magnitude one.
      if (value is not (1 or -1))
        throw new ArgumentOutOfRangeException(nameof(value), value, "This test builder only writes the codebook's magnitude-one codeword.");

      this.WriteBits(0x02, 2);
      this.WriteBits(value < 0 ? 1u : 0u, 1);
    }

    /// <summary>Pads to the next four-byte segment boundary with zero bits, matching 10.2(4).</summary>
    internal byte[] ToSegmentAlignedBytes() {
      while (this._partialBits != 0)
        this._Bit(0);

      while (this._bytes.Count % 4 != 0)
        this._bytes.Add(0);

      return this._bytes.ToArray();
    }

    private void _Bit(int bit) {
      this._partial = (this._partial << 1) | (uint)bit;
      if (++this._partialBits != 8)
        return;

      this._bytes.Add((byte)this._partial);
      this._partial = 0;
      this._partialBits = 0;
    }
  }

  // ==============================================================================================
  // Tag-value framing
  // ==============================================================================================

  private static void _Tag(List<byte> bytes, int tag, int value) {
    var segment = new byte[4];
    BinaryPrimitives.WriteInt16BigEndian(segment, (short)tag);
    BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)value);
    bytes.AddRange(segment);
  }

  // ==============================================================================================
  // One channel
  // ==============================================================================================

  /// <summary>What one wavelet level's highpass subband states about itself.</summary>
  internal sealed class Level {
    internal required int Width { get; init; }
    internal required int Height { get; init; }
    internal int Quantization { get; init; } = 1;

    /// <summary>Coefficients at raster positions this level's three subbands should carry a nonzero
    /// value at, all others zero. Empty means every subband of this level is entirely zero.</summary>
    internal (int Subband, int Index, int Value)[] Coefficients { get; init; } = [];
  }

  /// <summary>
  /// Builds one channel: its lowpass band, constant at <paramref name="lowpassValue"/>, followed by
  /// nine highpass subbands, coarsest level first, each all zero unless <paramref name="levels"/>
  /// states otherwise for that level.
  /// </summary>
  internal static byte[] Channel(int lowpassWidth, int lowpassHeight, int lowpassValue, Level level3, Level level2, Level level1) {
    var bytes = new List<byte>();

    _Tag(bytes, CineFormTags.LowpassWidth, lowpassWidth);
    _Tag(bytes, CineFormTags.LowpassHeight, lowpassHeight);
    _Tag(bytes, CineFormTags.LowpassPrecision, 16);

    // The four-byte marker CineFormChannelDecoder skips by position between LowpassPrecision and the
    // raw coefficients — its value is never interpreted, so any four bytes serve here.
    bytes.AddRange(new byte[] { 0, 0, 0, 0 });

    var lowpassCount = lowpassWidth * lowpassHeight;
    for (var i = 0; i < lowpassCount; ++i) {
      var sample = new byte[2];
      BinaryPrimitives.WriteUInt16BigEndian(sample, (ushort)lowpassValue);
      bytes.AddRange(sample);
    }

    while (bytes.Count % 4 != 0)
      bytes.Add(0);

    foreach (var level in new[] { level3, level2, level1 })
      for (var subband = 1; subband <= 3; ++subband) {
        _Tag(bytes, CineFormTags.HighpassWidth, level.Width);
        _Tag(bytes, CineFormTags.HighpassHeight, level.Height);
        _Tag(bytes, CineFormTags.SubbandNumber, subband);
        _Tag(bytes, 53 /* Quantization, restated explicitly for clarity even though it equals the constant below */, level.Quantization);
        _Tag(bytes, CineFormTags.HighpassDataFollows, 0);

        var count = level.Width * level.Height;
        var values = new int[count];
        foreach (var (sb, index, value) in level.Coefficients)
          if (sb == subband)
            values[index] = value;

        var writer = new BitWriter();
        foreach (var value in values)
          writer.WriteCoefficient(value);
        writer.WriteBandEndMarker();
        bytes.AddRange(writer.ToSegmentAlignedBytes());
      }

    return bytes.ToArray();
  }

  // ==============================================================================================
  // A whole frame
  // ==============================================================================================

  /// <summary>An all-zero level of the given dimensions, quantised by one.</summary>
  internal static Level FlatLevel(int width, int height) => new() { Width = width, Height = height };

  /// <summary>
  /// A three-channel frame: <c>ImageWidth</c>/<c>ImageHeight</c>/<c>ChannelCount</c>, then the three
  /// channels in order.
  /// </summary>
  internal static byte[] Frame(int imageWidth, int imageHeight, params byte[][] channels) {
    var bytes = new List<byte>();
    _Tag(bytes, CineFormTags.ImageWidth, imageWidth);
    _Tag(bytes, CineFormTags.ImageHeight, imageHeight);
    _Tag(bytes, CineFormTags.ChannelCount, channels.Length);

    for (var i = 0; i < channels.Length; ++i) {
      bytes.AddRange(channels[i]);
      if (i + 1 < channels.Length)
        _Tag(bytes, CineFormTags.ChannelNumber, i + 1);
    }

    return bytes.ToArray();
  }

  /// <summary>A stream description naming CineForm at the size the frame will state.</summary>
  internal static MediaStreamInfo Stream(int width = 48, int height = 24) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("CFHD"),
    Width = width,
    Height = height,
  };
}
