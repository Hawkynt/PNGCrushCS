using System;
using System.IO;

namespace FileFormat.Codecs.CineForm;

/// <summary>
/// Decodes one CineForm frame into its component channels.
/// </summary>
/// <remarks>
/// A packet is a sequence of tag-value pairs (Section 8.3) followed by, for each channel in turn
/// (Section 8.5(4): "codeblocks from different channels shall not be interleaved"), that channel's ten
/// subbands. This class reads the handful of top-level tags this decoder needs — <c>ChannelCount</c>
/// (tag 12) and <c>ImageWidth</c>/<c>ImageHeight</c> (tags 20/21) — skipping everything else exactly as
/// <see cref="CineFormChannelDecoder"/> does within a channel, then hands each channel in turn to that
/// class.
/// <para/>
/// <b>Which prescale table and which colour layout apply is decided from the channels' own
/// dimensions, not guessed from the container.</b> Every channel is parsed before any of them is
/// reconstructed, because a 4:2:2 stream's second and third channels code a lowpass band half the
/// width of the first channel's — genuine horizontal subsampling, unlike anything about the highpass
/// levels above it — and an RGB stream's three channels all agree. That comparison is what chooses
/// between <see cref="CineFormPrescale.TenBit"/> with channel order Y, V, U and
/// <see cref="CineFormPrescale.TwelveBit"/> with channel order G, R, B; see
/// <see cref="CineFormChannelDecoder"/>'s remarks for how both were measured.
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
    // ImageWidth, ImageHeight and ChannelCount sit inside the very same run of header tags that opens
    // channel 0 — there is no separate top-level header to stop at. This is a non-consuming pre-scan
    // for those three values only, stopping at the first tag that begins a codeblock so it never reads
    // into coefficient data; the real, position-advancing parse below starts at the top again and
    // simply skips these same tags as it walks into channel 0.
    _PeekImageHeader(data.Span, out var imageWidth, out var imageHeight, out var channelCount);

    if (imageWidth <= 0 || imageHeight <= 0)
      throw new InvalidDataException("A CineForm frame's tag-value header does not state a positive ImageWidth and ImageHeight before its first channel.");

    if (channelCount != 3)
      throw new NotSupportedException(
        $"This decoder reads only the three-channel layouts ffmpeg's own cfhd encoder writes — 4:2:2 YUV and RGB without alpha. This frame states ChannelCount {channelCount}, which was never measured against a real file and is refused rather than guessed at.");

    var position = 0;
    var channels = new CineFormChannelDecoder.ParsedChannel[channelCount];
    for (var i = 0; i < channelCount; ++i)
      channels[i] = CineFormChannelDecoder.Parse(data, ref position);

    // Genuine horizontal subsampling shows up nowhere else this early: a 4:2:2 stream's chroma
    // channels code a lowpass band half the width of the luma channel's, before any wavelet level or
    // prescale shift has touched either. An RGB stream's three channels always agree.
    var isYuv = channels[1].LowpassWidth < channels[0].LowpassWidth;
    var prescale = isYuv ? CineFormPrescale.TenBit : CineFormPrescale.TwelveBit;

    // The maximum a coded sample is entitled to: ten bits for 4:2:2, twelve for RGB — see
    // CineFormChannelDecoder's remarks on the depth each layout is coded at.
    var maxSample = isYuv ? 1023 : 4095;

    var planes = new Plane[channelCount];
    for (var i = 0; i < channelCount; ++i) {
      var samples = CineFormChannelDecoder.Reconstruct(channels[i], prescale, out var width, out var height);
      _ClampToCodedRange(samples, maxSample);
      planes[i] = new(samples, width, height);
    }

    return new() { ImageWidth = imageWidth, ImageHeight = imageHeight, Channels = planes, IsYuv = isYuv };
  }

  /// <summary>
  /// Clamps every reconstructed sample to the range the coded depth actually holds.
  /// </summary>
  /// <remarks>
  /// The wavelet transform's ordinary overshoot near a hard edge — the same ringing every linear
  /// transform codec has — puts a reconstructed sample a few levels below zero or above the coded
  /// maximum now and again; nothing about Annex A's arithmetic forbids it, and nothing states a
  /// decoder must undo it. ffmpeg's own decode cannot even show the alternative: <c>yuv422p10le</c>
  /// and <c>gbrp12le</c> are unsigned formats, so whatever it reconstructs internally is clamped
  /// before it can be written out at all. Comparing this decoder's own unclamped samples against that
  /// clamped reference is what reported small differences at the very positions this overshoot
  /// reaches — not a different decode, an unclamped one being read against a clamped one. Clamping
  /// here, once, on the finished picture rather than in every caller that narrows it further, is what
  /// makes the comparison the two decoders' agreement rather than an artefact of the difference.
  /// </remarks>
  private static void _ClampToCodedRange(int[] samples, int maxSample) {
    for (var i = 0; i < samples.Length; ++i) {
      var sample = samples[i];
      samples[i] = sample < 0 ? 0 : sample > maxSample ? maxSample : sample;
    }
  }

  private static void _PeekImageHeader(ReadOnlySpan<byte> span, out int imageWidth, out int imageHeight, out int channelCount) {
    imageWidth = 0;
    imageHeight = 0;
    channelCount = 0;
    var n = span.Length;

    for (var position = 0; position + 4 <= n; position += 4) {
      var tag16 = (span[position] << 8) | span[position + 1];
      var tag = tag16 >= 0x8000 ? tag16 - 0x10000 : tag16;
      var value = (span[position + 2] << 8) | span[position + 3];

      if (tag == CineFormTags.LowpassPrecision || tag == CineFormTags.HighpassDataFollows)
        return; // channel 0's own codeblocks begin here; every value needed is already found by now.

      if (tag == CineFormTags.ImageWidth) imageWidth = value;
      else if (tag == CineFormTags.ImageHeight) imageHeight = value;
      else if (tag == CineFormTags.ChannelCount) channelCount = value;
    }
  }
}
