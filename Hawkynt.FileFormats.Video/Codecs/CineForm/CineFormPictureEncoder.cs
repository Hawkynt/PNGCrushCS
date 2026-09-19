using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.CineForm;

/// <summary>Builds one progressive or interlaced CineForm I-frame packet.</summary>
/// <remarks>
/// Progressive frames use three ordinary spatial 2/6 levels. Legacy interlaced YUV keeps the same
/// ten-subband transform and changes only the finest level: adjacent field lines are split into a
/// temporal low/high pair and each pair is transformed horizontally. GoPro documents this as the
/// interlaced first transform and FFmpeg reconstructs the same four bands when SampleFlags clears the
/// progressive bit. It is distinct from CineForm's older 17-subband two-frame field-plus transform.
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
  private const int _INTERLACED_FLAGS = 63;
  private const int _SAMPLE_FLAGS = 68;
  private const int _FRAME_NUMBER = 69;
  private const int _PRECISION = 70;
  private const int _BAND_CODING_FLAGS = 72;
  private const int _PRESCALE_TABLE = 83;
  private const int _ENCODED_FORMAT = 84;
  private const int _DISPLAY_HEIGHT = 85;

  private const int _SAMPLE_TYPE_IFRAME = 9;
  private const int _SAMPLE_TYPE_CHANNEL = 3;
  private const int _BAND_ENCODING_CODEBOOK = 3;
  private const int _SAMPLE_FLAGS_PROGRESSIVE = 1;
  private const int _INTERLACED = 1;
  private const int _FIELD1_FIRST = 2;

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

  internal static byte[] Encode(
    int[] y, int[] v, int[] u,
    int lumaWidth, int chromaWidth, int encodedHeight, int displayHeight,
    ushort frameNumber = 0,
    bool interlaced = false,
    bool upperFieldFirst = true)
    => Encode(
      [y, v, u], [lumaWidth, chromaWidth, chromaWidth], encodedHeight, displayHeight,
      CineFormEncodedFormat.Yuv422, 10, 0x2000, CineFormPrescale.TenBit, frameNumber, interlaced, upperFieldFirst);

  internal static byte[] Encode(
    int[][] planes,
    int[] widths,
    int encodedHeight,
    int displayHeight,
    CineFormEncodedFormat encodedFormat,
    int precision,
    int prescaleTable,
    ReadOnlySpan<int> prescale,
    ushort frameNumber = 0,
    bool interlaced = false,
    bool upperFieldFirst = true) {

    ArgumentNullException.ThrowIfNull(planes);
    ArgumentNullException.ThrowIfNull(widths);

    var expectedChannels = encodedFormat switch {
      CineFormEncodedFormat.Yuv422 or CineFormEncodedFormat.Rgb444 => 3,
      CineFormEncodedFormat.Rgba4444 => 4,
      _ => throw new NotSupportedException($"This CineForm writer does not encode {encodedFormat}."),
    };

    var expectedPrecision = encodedFormat == CineFormEncodedFormat.Yuv422 ? 10 : 12;
    if (precision != expectedPrecision)
      throw new ArgumentException($"CineForm {encodedFormat} is written at {expectedPrecision} bits, not {precision}.", nameof(precision));

    if (interlaced && encodedFormat != CineFormEncodedFormat.Yuv422)
      throw new NotSupportedException("Legacy CineForm interlaced coding is defined for YUV 4:2:2; RGB/RGBA remain progressive.");

    if (interlaced && (encodedHeight & 1) != 0)
      throw new ArgumentException("An interlaced CineForm coded frame must contain a whole pair of fields.", nameof(encodedHeight));

    if (planes.Length != expectedChannels || widths.Length != expectedChannels)
      throw new ArgumentException($"CineForm {encodedFormat} needs exactly {expectedChannels} channel planes and widths.");

    var imageWidth = widths[0];
    if (imageWidth <= 0 || encodedHeight <= 0 || displayHeight <= 0 || displayHeight > encodedHeight)
      throw new ArgumentException("The CineForm picture geometry is not a positive padded frame.");

    if (encodedFormat == CineFormEncodedFormat.Yuv422) {
      if ((imageWidth & 1) != 0 || widths[1] * 2 != imageWidth || widths[2] != widths[1])
        throw new ArgumentException("A CineForm YUV 4:2:2 frame needs two half-width chroma channels.");
    } else {
      for (var i = 1; i < widths.Length; ++i)
        if (widths[i] != imageWidth)
          throw new ArgumentException("CineForm RGB and RGBA channels must all have the full image width.");
    }

    var channels = new ChannelTransform[expectedChannels];
    for (var i = 0; i < channels.Length; ++i) {
      var width = widths[i];
      if (planes[i].Length < width * encodedHeight)
        throw new ArgumentException($"CineForm channel {i} does not contain its complete padded plane.");
      channels[i] = _Transform(planes[i], width, encodedHeight, prescale, interlaced);
    }

    var writer = new PacketWriter();
    writer.Tag(_SAMPLE_TYPE, _SAMPLE_TYPE_IFRAME);
    writer.Tag(_INDEX, channels.Length);
    var indexPosition = writer.Position;
    for (var i = 0; i < channels.Length; ++i)
      writer.UInt32(0);

    writer.Tag(_TRANSFORM_TYPE, 0);
    writer.Tag(_NUM_FRAMES, 1);
    writer.Tag(_CHANNEL_COUNT, channels.Length);
    writer.Tag(_ENCODED_FORMAT, (int)encodedFormat);
    writer.Tag(_WAVELET_COUNT, 3);
    writer.Tag(_SUBBAND_COUNT, 10);
    writer.Tag(_NUM_SPATIAL, 2);
    writer.Tag(_FIRST_WAVELET, 3);
    writer.Tag(_IMAGE_WIDTH, imageWidth);
    writer.Tag(_IMAGE_HEIGHT, encodedHeight);
    if (displayHeight != encodedHeight)
      writer.Tag(-_DISPLAY_HEIGHT, displayHeight);
    writer.Tag(-_FRAME_NUMBER, frameNumber);
    writer.Tag(_PRECISION, precision);
    writer.Tag(_PRESCALE_TABLE, prescaleTable);
    writer.Tag(_SAMPLE_FLAGS, interlaced ? 0 : _SAMPLE_FLAGS_PROGRESSIVE);
    if (interlaced)
      writer.Tag(-_INTERLACED_FLAGS, _INTERLACED | (upperFieldFirst ? _FIELD1_FIRST : 0));

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

  private static ChannelTransform _Transform(
    ReadOnlySpan<int> source,
    int width,
    int height,
    ReadOnlySpan<int> prescale,
    bool interlaced) {

    if (prescale.Length != 3)
      throw new ArgumentException("CineForm's three-level transform needs three prescale entries.", nameof(prescale));

    var current = source.ToArray();
    var currentWidth = width;
    var currentHeight = height;
    var fineToCoarse = new Level[3];

    for (var level = 0; level < 3; ++level) {
      var shift = prescale[level];
      if (shift != 0)
        for (var i = 0; i < current.Length; ++i)
          current[i] >>= shift;

      var bands = interlaced && level == 0
        ? _ForwardInterlaced(current, currentWidth, currentHeight)
        : CineFormWavelet.ForwardSpatial(current, currentWidth, currentHeight);

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

  /// <summary>
  /// Forward transform for the finest level of an interlaced sample. For each adjacent pair of field
  /// lines the temporal low/high values are even+odd and odd-even; each is then transformed only in
  /// the horizontal direction. The four resulting bands occupy the same subband slots as a spatial
  /// level, which is why the rest of the packet writer does not need an interlace-specific layout.
  /// </summary>
  private static (int[] Ll, int[] Lh, int[] Hl, int[] Hh) _ForwardInterlaced(
    ReadOnlySpan<int> input, int width, int height) {

    if (width < 6 || (width & 1) != 0 || height < 2 || (height & 1) != 0 || input.Length < width * height)
      throw new ArgumentException("A CineForm interlaced level needs an even width of at least six and an even number of lines.");

    var bandWidth = width >> 1;
    var bandHeight = height >> 1;
    var ll = new int[bandWidth * bandHeight];
    var lh = new int[bandWidth * bandHeight];
    var hl = new int[bandWidth * bandHeight];
    var hh = new int[bandWidth * bandHeight];
    var temporalLow = new int[width];
    var temporalHigh = new int[width];

    for (var pair = 0; pair < bandHeight; ++pair) {
      var evenRow = (pair << 1) * width;
      var oddRow = evenRow + width;
      for (var x = 0; x < width; ++x) {
        var even = input[evenRow + x];
        var odd = input[oddRow + x];
        temporalLow[x] = even + odd;
        temporalHigh[x] = odd - even;
      }

      CineFormWavelet.ForwardOneDimensional(
        temporalLow,
        ll.AsSpan(pair * bandWidth, bandWidth),
        lh.AsSpan(pair * bandWidth, bandWidth));
      CineFormWavelet.ForwardOneDimensional(
        temporalHigh,
        hl.AsSpan(pair * bandWidth, bandWidth),
        hh.AsSpan(pair * bandWidth, bandWidth));
    }

    return (ll, lh, hl, hh);
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
