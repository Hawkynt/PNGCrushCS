using System;
using System.IO;

namespace FileFormat.Codecs.CineForm;

/// <summary>Decodes one CineForm frame into its component channels.</summary>
/// <remarks>
/// A packet is a sequence of tag-value pairs followed by, for each channel in turn, that channel's
/// ten subbands. The older CineForm framing used by GoPro and FFmpeg also places a raw channel-size
/// index after tag 2; that payload is skipped explicitly rather than accidentally interpreted as more
/// tags. Optional DisplayHeight (negative tag 85) crops the vertical padding real encoders add.
/// <para/>
/// A progressive CFHD sample uses three spatial levels. A legacy interlaced YUV sample can keep the
/// same transform type and ten-subband layout while clearing SampleFlags' progressive bit: the two
/// coarser levels remain spatial and the finest level becomes horizontal plus an adjacent-field
/// low/high pair. This decoder implements that layout and deliberately still refuses transform types
/// 1/2, which are the separate 14/17-subband field/field-plus organizations.
/// <para/>
/// Real CFHD frames name their colour layout with tag 84: 1 is ten-bit YUV 4:2:2, 2 is twelve-bit
/// Bayer RAW, 3 is twelve-bit RGB 4:4:4 and 4 is twelve-bit RGBA 4:4:4:4. Bayer is unusual: all four
/// coded channels are half the final image width and height and carry decorrelated CFA components;
/// the public FFmpeg decoder likewise doubles the header geometry after recognizing format 2.
/// Older sparse fixtures which omit tag 84 retain the measured channel-width fallback for YUV/RGB[A].
/// </remarks>
internal static class CineFormPictureDecoder {

  private const int _SAMPLE_FLAGS_PROGRESSIVE = 1;
  private const int _INTERLACED = 1;
  private const int _FIELD1_FIRST = 2;

  internal readonly struct Plane(int[] samples, int width, int height) {
    internal int[] Samples { get; } = samples;
    internal int Width { get; } = width;
    internal int Height { get; } = height;
  }

  internal sealed class Result {
    internal required int ImageWidth { get; init; }
    internal required int ImageHeight { get; init; }
    internal required Plane[] Channels { get; init; }
    internal required CineFormEncodedFormat EncodedFormat { get; init; }
    internal required int Precision { get; init; }
    internal required bool IsInterlaced { get; init; }
    internal bool? UpperFieldFirst { get; init; }

    internal bool IsYuv => this.EncodedFormat == CineFormEncodedFormat.Yuv422;

    /// <summary><see langword="true"/> for four decorrelated half-resolution CFA channels.</summary>
    internal bool IsBayer => this.EncodedFormat == CineFormEncodedFormat.Bayer;

    /// <summary><see langword="true"/> when channel 3 carries the decoded alpha component.</summary>
    internal bool HasAlpha => this.EncodedFormat == CineFormEncodedFormat.Rgba4444;
  }

  internal static Result Decode(ReadOnlyMemory<byte> data) {
    _PeekImageHeader(
      data.Span,
      out var imageWidth,
      out var codedHeight,
      out var displayHeight,
      out var channelCount,
      out var encodedFormat,
      out var precision,
      out var transformType,
      out var sampleFlags,
      out var sampleFlagsSeen,
      out var interlacedFlags,
      out var interlacedFlagsSeen,
      out var channelHeaderPosition);

    if (imageWidth <= 0 || codedHeight <= 0)
      throw new InvalidDataException("A CineForm frame's tag-value header does not state a positive ImageWidth and ImageHeight before its first channel.");

    var imageHeight = displayHeight > 0 ? displayHeight : codedHeight;
    if (imageHeight > codedHeight)
      throw new InvalidDataException(
        $"A CineForm frame states DisplayHeight {imageHeight}, larger than its coded ImageHeight {codedHeight}.");

    if (transformType > 0)
      throw new NotSupportedException(
        $"CineForm transform type {transformType} is a separate multi-frame field/field-plus transform with 14/17 subbands; this decoder currently reads transform type 0 only.");

    if (channelCount is < 3 or > 4)
      throw new NotSupportedException(
        $"This decoder reads CineForm's three-channel YUV/RGB and four-channel Bayer/RGBA layouts; this frame states ChannelCount {channelCount}.");

    _ValidateHeaderLayout(encodedFormat, precision, channelCount);

    var isInterlaced = sampleFlagsSeen
      ? (sampleFlags & _SAMPLE_FLAGS_PROGRESSIVE) == 0
      : interlacedFlagsSeen && (interlacedFlags & _INTERLACED) != 0;

    if (isInterlaced && (codedHeight & 1) != 0)
      throw new InvalidDataException($"An interlaced CineForm frame needs an even coded height; this frame states {codedHeight}.");

    var position = channelHeaderPosition;
    var channels = new CineFormChannelDecoder.ParsedChannel[channelCount];
    for (var i = 0; i < channelCount; ++i)
      channels[i] = CineFormChannelDecoder.Parse(data, ref position);

    var format = _ResolveFormat(encodedFormat, channels);
    if (isInterlaced && format != CineFormEncodedFormat.Yuv422)
      throw new NotSupportedException("CineForm's legacy ten-subband interlaced transform is supported only for YUV 4:2:2.");

    var codedPrecision = format == CineFormEncodedFormat.Yuv422 ? 10 : 12;
    if (precision != 0 && precision != codedPrecision)
      throw new InvalidDataException(
        $"CineForm EncodedFormat {(int)format} ({format}) is coded at {codedPrecision} bits, but this frame states Precision {precision}.");

    var prescale = format == CineFormEncodedFormat.Yuv422 ? CineFormPrescale.TenBit : CineFormPrescale.TwelveBit;
    var maxSample = format == CineFormEncodedFormat.Yuv422 ? 1023 : 4095;

    var planes = new Plane[channelCount];
    for (var i = 0; i < channelCount; ++i) {
      int width;
      int height;
      var samples = isInterlaced
        ? _ReconstructInterlaced(channels[i], prescale, out width, out height)
        : CineFormChannelDecoder.Reconstruct(channels[i], prescale, out width, out height);
      _ClampToCodedRange(samples, maxSample);
      if (format == CineFormEncodedFormat.Rgba4444 && i == 3)
        _ExpandAlpha(samples);
      planes[i] = new(samples, width, height);
    }

    var geometryScale = format == CineFormEncodedFormat.Bayer ? 2 : 1;
    return new() {
      ImageWidth = checked(imageWidth * geometryScale),
      ImageHeight = checked(imageHeight * geometryScale),
      Channels = planes,
      EncodedFormat = format,
      Precision = codedPrecision,
      IsInterlaced = isInterlaced,
      UpperFieldFirst = isInterlaced && interlacedFlagsSeen
        ? (interlacedFlags & _FIELD1_FIRST) != 0
        : null,
    };
  }

  private static int[] _ReconstructInterlaced(
    CineFormChannelDecoder.ParsedChannel channel,
    ReadOnlySpan<int> prescaleShift,
    out int outputWidth,
    out int outputHeight) {

    var current = channel.Lowpass;
    var currentWidth = channel.LowpassWidth;
    var currentHeight = channel.LowpassHeight;

    for (var levelIndex = 0; levelIndex < 3; ++levelIndex) {
      var bands = channel.HighpassByLevel[levelIndex]
        ?? throw new InvalidDataException("A CineForm channel is missing one of its three wavelet levels of highpass subbands.");
      var lh = bands[0] ?? throw new InvalidDataException("A CineForm channel's first highpass subband was never coded.");
      var hl = bands[1] ?? throw new InvalidDataException("A CineForm channel's second highpass subband was never coded.");
      var hh = bands[2] ?? throw new InvalidDataException("A CineForm channel's third highpass subband was never coded.");

      current = levelIndex == 2
        ? _InverseInterlaced(current, lh, hl, hh, currentWidth, currentHeight, out currentWidth, out currentHeight)
        : CineFormWavelet.InverseSpatial(current, lh, hl, hh, currentWidth, currentHeight, out currentWidth, out currentHeight);

      var shift = prescaleShift[2 - levelIndex];
      if (shift != 0)
        for (var i = 0; i < current.Length; ++i)
          current[i] <<= shift;
    }

    outputWidth = currentWidth;
    outputHeight = currentHeight;
    return current;
  }

  private static int[] _InverseInterlaced(
    ReadOnlySpan<int> ll,
    ReadOnlySpan<int> lh,
    ReadOnlySpan<int> hl,
    ReadOnlySpan<int> hh,
    int width,
    int height,
    out int outputWidth,
    out int outputHeight) {

    outputWidth = width * 2;
    outputHeight = height * 2;
    var output = new int[outputWidth * outputHeight];
    var temporalLow = new int[outputWidth];
    var temporalHigh = new int[outputWidth];

    for (var y = 0; y < height; ++y) {
      var row = y * width;
      CineFormWavelet.InverseOneDimensional(ll.Slice(row, width), lh.Slice(row, width), temporalLow);
      CineFormWavelet.InverseOneDimensional(hl.Slice(row, width), hh.Slice(row, width), temporalHigh);

      var evenRow = (y << 1) * outputWidth;
      var oddRow = evenRow + outputWidth;
      for (var x = 0; x < outputWidth; ++x) {
        output[evenRow + x] = (temporalLow[x] - temporalHigh[x]) >> 1;
        output[oddRow + x] = (temporalLow[x] + temporalHigh[x]) >> 1;
      }
    }

    return output;
  }

  private static void _ValidateHeaderLayout(CineFormEncodedFormat encodedFormat, int precision, int channelCount) {
    switch (encodedFormat) {
      case CineFormEncodedFormat.Unspecified:
        return;
      case CineFormEncodedFormat.Bayer:
        if (channelCount != 4)
          throw new InvalidDataException($"CineForm Bayer RAW needs four decorrelated channels, but the frame states {channelCount}.");
        if (precision != 0 && precision != 12)
          throw new InvalidDataException($"CineForm Bayer RAW is compressed at 12 bits, but the frame states Precision {precision}.");
        return;
      case CineFormEncodedFormat.Yuv422:
        if (channelCount != 3)
          throw new InvalidDataException($"CineForm YUV 4:2:2 needs three channels, but the frame states {channelCount}.");
        if (precision != 0 && precision != 10)
          throw new InvalidDataException($"CineForm YUV 4:2:2 is coded at 10 bits, but the frame states Precision {precision}.");
        return;
      case CineFormEncodedFormat.Rgb444:
        if (channelCount != 3)
          throw new InvalidDataException($"CineForm RGB 4:4:4 needs three channels, but the frame states {channelCount}.");
        if (precision != 0 && precision != 12)
          throw new InvalidDataException($"CineForm RGB 4:4:4 is coded at 12 bits, but the frame states Precision {precision}.");
        return;
      case CineFormEncodedFormat.Rgba4444:
        if (channelCount != 4)
          throw new InvalidDataException($"CineForm RGBA 4:4:4:4 needs four channels, but the frame states {channelCount}.");
        if (precision != 0 && precision != 12)
          throw new InvalidDataException($"CineForm RGBA 4:4:4:4 is coded at 12 bits, but the frame states Precision {precision}.");
        return;
      default:
        throw new NotSupportedException($"CineForm EncodedFormat {(int)encodedFormat} is not known to this decoder.");
    }
  }

  private static CineFormEncodedFormat _ResolveFormat(
    CineFormEncodedFormat encodedFormat,
    CineFormChannelDecoder.ParsedChannel[] channels) {

    if (encodedFormat != CineFormEncodedFormat.Unspecified)
      return encodedFormat;
    if (channels.Length == 4)
      return CineFormEncodedFormat.Rgba4444;

    return channels[1].LowpassWidth < channels[0].LowpassWidth
      ? CineFormEncodedFormat.Yuv422
      : CineFormEncodedFormat.Rgb444;
  }

  private static void _ClampToCodedRange(int[] samples, int maxSample) {
    for (var i = 0; i < samples.Length; ++i) {
      var sample = samples[i];
      samples[i] = sample < 0 ? 0 : sample > maxSample ? maxSample : sample;
    }
  }

  private static void _ExpandAlpha(int[] samples) {
    for (var i = 0; i < samples.Length; ++i) {
      var channel = (samples[i] - 256) << 3;
      channel = channel * 9400 >> 16;
      samples[i] = channel < 0 ? 0 : channel > 4095 ? 4095 : channel;
    }
  }

  private static void _PeekImageHeader(
    ReadOnlySpan<byte> span,
    out int imageWidth,
    out int imageHeight,
    out int displayHeight,
    out int channelCount,
    out CineFormEncodedFormat encodedFormat,
    out int precision,
    out int transformType,
    out int sampleFlags,
    out bool sampleFlagsSeen,
    out int interlacedFlags,
    out bool interlacedFlagsSeen,
    out int channelHeaderPosition) {

    imageWidth = 0;
    imageHeight = 0;
    displayHeight = 0;
    channelCount = 0;
    encodedFormat = CineFormEncodedFormat.Unspecified;
    precision = 0;
    transformType = 0;
    sampleFlags = _SAMPLE_FLAGS_PROGRESSIVE;
    sampleFlagsSeen = false;
    interlacedFlags = 0;
    interlacedFlagsSeen = false;
    channelHeaderPosition = 0;

    var position = 0;
    while (position + 4 <= span.Length) {
      var tag16 = (span[position] << 8) | span[position + 1];
      var tag = tag16 >= 0x8000 ? tag16 - 0x10000 : tag16;
      var value = (span[position + 2] << 8) | span[position + 3];
      position += 4;

      if (tag == CineFormTags.Index) {
        var bytes = (long)value * 4;
        if (position + bytes > span.Length)
          throw new InvalidDataException(
            $"A CineForm channel-size index declares {value} entries but the packet ends inside the index.");
        position += (int)bytes;
        channelHeaderPosition = position;
        continue;
      }

      if (tag == CineFormTags.LowpassPrecision || tag == CineFormTags.HighpassDataFollows)
        return;

      if (tag == CineFormTags.TransformType)
        transformType = value;
      else if (tag == CineFormTags.ImageWidth)
        imageWidth = value;
      else if (tag == CineFormTags.ImageHeight)
        imageHeight = value;
      else if (tag == -CineFormTags.DisplayHeight || tag == CineFormTags.DisplayHeight)
        displayHeight = value;
      else if (tag == CineFormTags.ChannelCount)
        channelCount = value;
      else if (tag == CineFormTags.EncodedFormat)
        encodedFormat = (CineFormEncodedFormat)value;
      else if (tag == CineFormTags.Precision)
        precision = value;
      else if (tag == CineFormTags.SampleFlags || tag == -CineFormTags.SampleFlags) {
        sampleFlags = value;
        sampleFlagsSeen = true;
      } else if (tag == CineFormTags.InterlacedFlags || tag == -CineFormTags.InterlacedFlags) {
        interlacedFlags = value;
        interlacedFlagsSeen = true;
      }
    }
  }
}
