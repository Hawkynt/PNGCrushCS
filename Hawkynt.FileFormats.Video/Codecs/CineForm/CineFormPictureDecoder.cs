using System;
using System.IO;

namespace FileFormat.Codecs.CineForm;

/// <summary>
/// Decodes one CineForm frame into its component channels.
/// </summary>
/// <remarks>
/// A packet is a sequence of tag-value pairs followed by, for each channel in turn, that channel's
/// ten subbands. The older CineForm framing used by GoPro and FFmpeg also places a raw channel-size
/// index after tag 2; that payload is skipped explicitly rather than accidentally interpreted as more
/// tags. Optional DisplayHeight (negative tag 85) crops the vertical padding real encoders add.
/// <para/>
/// Real CFHD frames name their colour layout with tag 84: 1 is ten-bit YUV 4:2:2, 2 is twelve-bit
/// Bayer RAW, 3 is twelve-bit RGB 4:4:4 and 4 is twelve-bit RGBA 4:4:4:4. Bayer is unusual: all four
/// coded channels are half the final image width and height and carry decorrelated CFA components;
/// the public FFmpeg decoder likewise doubles the header geometry after recognizing format 2.
/// Older sparse fixtures which omit tag 84 retain the measured channel-width fallback for YUV/RGB[A].
/// </remarks>
internal static class CineFormPictureDecoder {

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

    /// <summary><see langword="true"/> for horizontally-subsampled YUV (channel order Y, V, U).</summary>
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
      out var channelHeaderPosition);

    if (imageWidth <= 0 || codedHeight <= 0)
      throw new InvalidDataException("A CineForm frame's tag-value header does not state a positive ImageWidth and ImageHeight before its first channel.");

    var imageHeight = displayHeight > 0 ? displayHeight : codedHeight;
    if (imageHeight > codedHeight)
      throw new InvalidDataException(
        $"A CineForm frame states DisplayHeight {imageHeight}, larger than its coded ImageHeight {codedHeight}.");

    if (channelCount is < 3 or > 4)
      throw new NotSupportedException(
        $"This decoder reads CineForm's three-channel YUV/RGB and four-channel Bayer/RGBA layouts; this frame states ChannelCount {channelCount}.");

    _ValidateHeaderLayout(encodedFormat, precision, channelCount);

    // With a raw index present, begin after its size words. Every tag the channel decoder needs sits
    // after the index; starting at packet zero would reinterpret those size words as tag/value pairs.
    // Sparse VC-5-style fixtures have no index and therefore keep the historical start at zero.
    var position = channelHeaderPosition;
    var channels = new CineFormChannelDecoder.ParsedChannel[channelCount];
    for (var i = 0; i < channelCount; ++i)
      channels[i] = CineFormChannelDecoder.Parse(data, ref position);

    var format = _ResolveFormat(encodedFormat, channels);
    var codedPrecision = format == CineFormEncodedFormat.Yuv422 ? 10 : 12;
    if (precision != 0 && precision != codedPrecision)
      throw new InvalidDataException(
        $"CineForm EncodedFormat {(int)format} ({format}) is coded at {codedPrecision} bits, but this frame states Precision {precision}.");

    var prescale = format == CineFormEncodedFormat.Yuv422 ? CineFormPrescale.TenBit : CineFormPrescale.TwelveBit;
    var maxSample = format == CineFormEncodedFormat.Yuv422 ? 1023 : 4095;

    var planes = new Plane[channelCount];
    for (var i = 0; i < channelCount; ++i) {
      var samples = CineFormChannelDecoder.Reconstruct(channels[i], prescale, out var width, out var height);
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
    };
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

  /// <summary>Undo the alpha companding used by CineForm RGBA before the spatial transform.</summary>
  /// <remarks>
  /// The constants are format interoperability values also used by the public GoPro implementation
  /// and FFmpeg: coded alpha is offset by 256, expanded by eight and scaled by 9400/65536. Applying
  /// this after reconstruction is what makes the fourth twelve-bit channel an alpha plane rather than
  /// merely another colour component.
  /// </remarks>
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
    out int channelHeaderPosition) {

    imageWidth = 0;
    imageHeight = 0;
    displayHeight = 0;
    channelCount = 0;
    encodedFormat = CineFormEncodedFormat.Unspecified;
    precision = 0;
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

      if (tag == CineFormTags.ImageWidth)
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
    }
  }
}
