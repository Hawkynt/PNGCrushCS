using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.CineForm;

/// <summary>Builds one progressive three-channel CineForm I-frame packet.</summary>
/// <remarks>
/// The packet shape is the common subset emitted by GoPro's reference SDK and FFmpeg's <c>cfhd</c>
/// encoder: one I-frame header and channel-size index, then Y, V and U channels, each containing a
/// raw sixteen-bit lowpass followed by three spatial wavelet levels and three entropy-coded highpass
/// bands per level. The tag numbers and marker values are interoperability constants, not an imported
/// implementation; all transforms and entropy coding are the managed counterparts of this package's
/// existing decoder.
/// </remarks>
internal static class CineFormPictureEncoder {
  private const int _SAMPLE_TYPE = 1;
  private const int _INDEX = 2;
  private const int _MARKER = 4;
  private const int _TRANSFORM_TYPE = 10;
  private const int _NUM_FRAMES = 11;
  private const int _CHANNEL_COUNT = 12;
  private const int _WAVELET_COUNT = 13;
  private const int _SUBBAND_COUNT = 14;
  private const int _NUM_SPATIAL = 15;
  private const int _FIRST_WAVELET = 16;
  private const int _GROUP_TRAILER = 18;
  private const int _IMAGE_WIDTH = 20;
  private const int _IMAGE_HEIGHT = 21;
  private const int _LOWPASS_SUBBAND = 25;
  private const int _NUM_LEVELS = 26;
  private const int _LOWPASS_WIDTH = 27;
  private const int _LOWPASS_HEIGHT = 28;
  private const int _PIXEL_OFFSET = 33;
  private const int _LOWPASS_QUANTIZATION = 34;
  private const int _LOWPASS_PRECISION = 35;
  private const int _WAVELET_TYPE = 37;
  private const int _WAVELET_NUMBER = 38;
  private const int _WAVELET_LEVEL = 39;
  private const int _NUM_BANDS = 40;
  private const int _HIGHPASS_WIDTH = 41;
  private const int _HIGHPASS_HEIGHT = 42;
  private const int _LOWPASS_BORDER = 43;
  private const int _HIGHPASS_BORDER = 44;
  private const int _LOWPASS_SCALE = 45;
  private const int _LOWPASS_DIVISOR = 46;
  private const int _BAND_NUMBER = 48;
  private const int _BAND_WIDTH = 49;
  private const int _BAND_HEIGHT = 50;
  private const int _BAND_SUBBAND = 51;
  private const int _BAND_ENCODING = 52;
  private const int _BAND_QUANTIZATION = 53;
  private const int _BAND_SCALE = 54;
  private const int _BAND_HEADER = 55;
  private const int _BAND_TRAILER = 56;
  private const int _CHANNEL_NUMBER = 62;
  private const int _SAMPLE_FLAGS = 68;
  private const int _FRAME_NUMBER = 69;
  private const int _PRECISION = 70;
  private const int _BAND_CODING_FLAGS = 72;
  private const int _PRESCALE_TABLE = 83;
  private const int _ENCODED_FORMAT = 84;
  private const int _DISPLAY_HEIGHT = 85;

  private const int _SAMPLE_TYPE_IFRAME = 9;
  private const int _SAMPLE_TYPE_CHANNEL = 3;
  private const int _ENCODED_FORMAT_YUV_422 = 1;
  private const int _BAND_ENCODING_CODEBOOK = 3;

  private const int _LOWPASS_SEGMENT = 0x1A4A;
  private const int _LOWPASS_END_SEGMENT = 0x1B4B;
  private const int _HIGHPASS_SEGMENT = 0x0D0D;
  private const int _BAND_SEGMENT = 0x0E0E;
  private const int _HIGHPASS_END_SEGMENT = 0x0C0C;
  private const int _COEFFICIENT_SEGMENT = 0x0F0F;

  private sealed class ChannelTransform {
    internal required int[] Lowpass { get; init; }
    internal required int LowpassWidth { get; init; }
    internal required int LowpassHeight { get; init; }
    internal required Level[] Levels { get; init; } // coarsest to finest
  }

  private readonly record struct Level(int Width, int Height, int[] Lh, int[] Hl, int[] Hh);

  /// <summary>
  /// Encodes already padded ten-bit Y, V and U planes. Channel widths are supplied independently
  /// because 4:2:2 chroma is half the luma width.
  /// </summary>
  internal static byte[] Encode(
    int[] y, int[] v, int[] u,
    int lumaWidth, int chromaWidth, int encodedHeight, int displayHeight,
    ushort frameNumber = 0) {

    if (lumaWidth <= 0 || chromaWidth * 2 != lumaWidth || encodedHeight <= 0 || displayHeight <= 0 || displayHeight > encodedHeight)
      throw new ArgumentException("The CineForm picture geometry is not a padded 4:2:2 frame.");

    if (y.Length < lumaWidth * encodedHeight || v.Length < chromaWidth * encodedHeight || u.Length < chromaWidth * encodedHeight)
      throw new ArgumentException("A CineForm channel does not contain its complete padded plane.");

    var prescale = CineFormPrescale.TenBit;
    ChannelTransform[] channels = [
      _Transform(y, lumaWidth, encodedHeight, prescale),
      _Transform(v, chromaWidth, encodedHeight, prescale),
      _Transform(u, chromaWidth, encodedHeight, prescale),
    ];

    var writer = new PacketWriter();
    writer.Tag(_SAMPLE_TYPE, _SAMPLE_TYPE_IFRAME);
    writer.Tag(_INDEX, channels.Length);
    var indexPosition = writer.Position;
    for (var i = 0; i < channels.Length; ++i)
      writer.UInt32(0);

    writer.Tag(_TRANSFORM_TYPE, 0);
    writer.Tag(_NUM_FRAMES, 1);
    writer.Tag(_CHANNEL_COUNT, channels.Length);
    writer.Tag(_ENCODED_FORMAT, _ENCODED_FORMAT_YUV_422);
    writer.Tag(_WAVELET_COUNT, 3);
    writer.Tag(_SUBBAND_COUNT, 10);
    writer.Tag(_NUM_SPATIAL, 2);
    writer.Tag(_FIRST_WAVELET, 3);
    writer.Tag(_IMAGE_WIDTH, lumaWidth);
    writer.Tag(_IMAGE_HEIGHT, encodedHeight);
    if (displayHeight != encodedHeight)
      writer.Tag(-_DISPLAY_HEIGHT, displayHeight);
    writer.Tag(-_FRAME_NUMBER, frameNumber);
    writer.Tag(_PRECISION, 10);
    writer.Tag(_PRESCALE_TABLE, 0x2000);
    writer.Tag(_SAMPLE_FLAGS, 1); // progressive

    for (var channelIndex = 0; channelIndex < channels.Length; ++channelIndex) {
      if (channelIndex != 0) {
        writer.Tag(_SAMPLE_TYPE, _SAMPLE_TYPE_CHANNEL);
        writer.Tag(_CHANNEL_NUMBER, channelIndex);
      }

      writer.Tag(_MARKER, _LOWPASS_SEGMENT);
      var channelDataStart = writer.Position;
      _WriteChannel(writer, channels[channelIndex]);
      writer.PatchUInt32(indexPosition + channelIndex * 4, checked((uint)(writer.Position - channelDataStart)));
    }

    writer.Tag(_GROUP_TRAILER, 0);
    return writer.ToArray();
  }

  private static ChannelTransform _Transform(ReadOnlySpan<int> source, int width, int height, ReadOnlySpan<int> prescale) {
    var current = source.ToArray();
    var currentWidth = width;
    var currentHeight = height;
    var fineToCoarse = new Level[3];

    for (var level = 0; level < 3; ++level) {
      var shift = prescale[level];
      if (shift != 0)
        for (var i = 0; i < current.Length; ++i)
          current[i] >>= shift;

      var bands = CineFormWavelet.ForwardSpatial(current, currentWidth, currentHeight);
      currentWidth >>= 1;
      currentHeight >>= 1;
      fineToCoarse[level] = new(currentWidth, currentHeight, bands.Lh, bands.Hl, bands.Hh);
      current = bands.Ll;
    }

    Level[] levels = [fineToCoarse[2], fineToCoarse[1], fineToCoarse[0]];
    return new() {
      Lowpass = current,
      LowpassWidth = currentWidth,
      LowpassHeight = currentHeight,
      Levels = levels,
    };
  }

  private static void _WriteChannel(PacketWriter writer, ChannelTransform channel) {
    writer.Tag(_LOWPASS_SUBBAND, 0);
    writer.Tag(_NUM_LEVELS, 3);
    writer.Tag(_LOWPASS_WIDTH, channel.LowpassWidth);
    writer.Tag(_LOWPASS_HEIGHT, channel.LowpassHeight);
    writer.Tag(_PIXEL_OFFSET, 0);
    writer.Tag(_LOWPASS_QUANTIZATION, 1);
    writer.Tag(_LOWPASS_PRECISION, 16);
    writer.Tag(_MARKER, _COEFFICIENT_SEGMENT);

    foreach (var coefficient in channel.Lowpass) {
      if ((uint)coefficient > ushort.MaxValue)
        throw new NotSupportedException($"A CineForm lowpass coefficient {coefficient} does not fit the sixteen-bit lowpass representation.");
      writer.UInt16((ushort)coefficient);
    }
    writer.Align4();
    writer.Tag(_MARKER, _LOWPASS_END_SEGMENT);

    for (var levelIndex = 0; levelIndex < channel.Levels.Length; ++levelIndex) {
      var level = channel.Levels[levelIndex];
      var waveletLevel = 3 - levelIndex;

      writer.Tag(_MARKER, _HIGHPASS_SEGMENT);
      writer.Tag(_WAVELET_TYPE, levelIndex == 2 ? 5 : 3);
      writer.Tag(_WAVELET_NUMBER, waveletLevel);
      writer.Tag(_WAVELET_LEVEL, waveletLevel);
      writer.Tag(_NUM_BANDS, 4);
      writer.Tag(_HIGHPASS_WIDTH, level.Width);
      writer.Tag(_HIGHPASS_HEIGHT, level.Height);
      writer.Tag(_LOWPASS_BORDER, 0);
      writer.Tag(_HIGHPASS_BORDER, 0);
      writer.Tag(_LOWPASS_SCALE, 1);
      writer.Tag(_LOWPASS_DIVISOR, 1);

      int[][] bands = [level.Lh, level.Hl, level.Hh];
      for (var bandIndex = 0; bandIndex < bands.Length; ++bandIndex) {
        var encoded = CineFormEntropyEncoder.Encode(bands[bandIndex], level.Width, level.Height);

        writer.Tag(_MARKER, _BAND_SEGMENT);
        writer.Tag(_BAND_NUMBER, bandIndex + 1);
        writer.Tag(_BAND_CODING_FLAGS, 1);
        writer.Tag(_BAND_WIDTH, level.Width);
        writer.Tag(_BAND_HEIGHT, level.Height);
        writer.Tag(_BAND_SUBBAND, 1 + levelIndex * 3 + bandIndex);
        writer.Tag(_BAND_ENCODING, _BAND_ENCODING_CODEBOOK);
        writer.Tag(_BAND_QUANTIZATION, encoded.Quantization);
        writer.Tag(_BAND_SCALE, 1);
        writer.Tag(_BAND_HEADER, 0);
        writer.Bytes(encoded.Data);
        writer.Tag(_BAND_TRAILER, 0);
      }

      writer.Tag(_MARKER, _HIGHPASS_END_SEGMENT);
    }
  }

  private sealed class PacketWriter {
    private readonly List<byte> _bytes = [];

    internal int Position => this._bytes.Count;

    internal void Tag(int tag, int value) {
      this.UInt16(unchecked((ushort)tag));
      this.UInt16(checked((ushort)value));
    }

    internal void UInt16(ushort value) {
      this._bytes.Add((byte)(value >> 8));
      this._bytes.Add((byte)value);
    }

    internal void UInt32(uint value) {
      this._bytes.Add((byte)(value >> 24));
      this._bytes.Add((byte)(value >> 16));
      this._bytes.Add((byte)(value >> 8));
      this._bytes.Add((byte)value);
    }

    internal void PatchUInt32(int position, uint value) {
      this._bytes[position] = (byte)(value >> 24);
      this._bytes[position + 1] = (byte)(value >> 16);
      this._bytes[position + 2] = (byte)(value >> 8);
      this._bytes[position + 3] = (byte)value;
    }

    internal void Bytes(ReadOnlySpan<byte> bytes) {
      foreach (var value in bytes)
        this._bytes.Add(value);
    }

    internal void Align4() {
      while ((this._bytes.Count & 3) != 0)
        this._bytes.Add(0);
    }

    internal byte[] ToArray() => [.. this._bytes];
  }
}
