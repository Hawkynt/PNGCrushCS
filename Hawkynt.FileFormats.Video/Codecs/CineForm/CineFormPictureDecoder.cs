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
/// <b>Which prescale table and which colour layout apply is decided from the channels' own
/// dimensions, not guessed from the container.</b> Every channel is parsed before any of them is
/// reconstructed, because a 4:2:2 stream's second and third channels code a lowpass band half the
/// width of the first channel's — genuine horizontal subsampling — and an RGB stream's three channels
/// all agree. That comparison chooses between <see cref="CineFormPrescale.TenBit"/> with channel order
/// Y, V, U and <see cref="CineFormPrescale.TwelveBit"/> with channel order G, R, B.
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

    /// <summary><see langword="true"/> for a horizontally-subsampled three-channel YUV frame (channel
    /// order Y, V, U); <see langword="false"/> for a three-channel RGB frame (channel order G, R, B).</summary>
    internal required bool IsYuv { get; init; }
  }

  internal static Result Decode(ReadOnlyMemory<byte> data) {
    _PeekImageHeader(data.Span, out var imageWidth, out var codedHeight, out var displayHeight, out var channelCount);

    if (imageWidth <= 0 || codedHeight <= 0)
      throw new InvalidDataException("A CineForm frame's tag-value header does not state a positive ImageWidth and ImageHeight before its first channel.");

    var imageHeight = displayHeight > 0 ? displayHeight : codedHeight;
    if (imageHeight > codedHeight)
      throw new InvalidDataException(
        $"A CineForm frame states DisplayHeight {imageHeight}, larger than its coded ImageHeight {codedHeight}.");

    if (channelCount != 3)
      throw new NotSupportedException(
        $"This decoder reads only the three-channel layouts ffmpeg's own cfhd encoder writes — 4:2:2 YUV and RGB without alpha. This frame states ChannelCount {channelCount}, which was never measured against a real file and is refused rather than guessed at.");

    var position = 0;
    var channels = new CineFormChannelDecoder.ParsedChannel[channelCount];
    for (var i = 0; i < channelCount; ++i)
      channels[i] = CineFormChannelDecoder.Parse(data, ref position);

    var isYuv = channels[1].LowpassWidth < channels[0].LowpassWidth;
    var prescale = isYuv ? CineFormPrescale.TenBit : CineFormPrescale.TwelveBit;
    var maxSample = isYuv ? 1023 : 4095;

    var planes = new Plane[channelCount];
    for (var i = 0; i < channelCount; ++i) {
      var samples = CineFormChannelDecoder.Reconstruct(channels[i], prescale, out var width, out var height);
      _ClampToCodedRange(samples, maxSample);
      planes[i] = new(samples, width, height);
    }

    return new() { ImageWidth = imageWidth, ImageHeight = imageHeight, Channels = planes, IsYuv = isYuv };
  }

  private static void _ClampToCodedRange(int[] samples, int maxSample) {
    for (var i = 0; i < samples.Length; ++i) {
      var sample = samples[i];
      samples[i] = sample < 0 ? 0 : sample > maxSample ? maxSample : sample;
    }
  }

  private static void _PeekImageHeader(
    ReadOnlySpan<byte> span,
    out int imageWidth,
    out int imageHeight,
    out int displayHeight,
    out int channelCount) {

    imageWidth = 0;
    imageHeight = 0;
    displayHeight = 0;
    channelCount = 0;

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
    }
  }
}
