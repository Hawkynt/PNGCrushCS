namespace FileFormat.Codecs.CineForm;

/// <summary>
/// The tag numbers of the codec state parameters this decoder reads, SMPTE ST 2073-1:2017 Table B.2.
/// </summary>
/// <remarks>
/// A tag-value pair is always exactly one segment (Section 8.3.1) whatever tag it carries, which is
/// what lets every tag this decoder does not name here be skipped rather than understood: four bytes,
/// read and discarded, the same whether the tag is one Table B.2 defines or one of the many the real
/// encoder writes that the free standard does not. Only the tags actually needed to place a channel's
/// lowpass and highpass codeblocks and dequantise them are named.
/// </remarks>
internal static class CineFormTags {
  internal const int ImageWidth = 20;
  internal const int ImageHeight = 21;
  internal const int ChannelCount = 12;
  internal const int SubbandCount = 14;
  internal const int ChannelNumber = 62;
  internal const int SubbandNumber = 48;
  internal const int LowpassPrecision = 35;
  internal const int Quantization = 53;

  /// <summary>
  /// The last of a highpass subband's own header tags — not in Table B.2 — immediately after which its
  /// entropy-coded data begins with no marker and no gap. See
  /// <see cref="CineFormChannelDecoder"/>'s remarks for how that boundary was measured.
  /// </summary>
  internal const int HighpassDataFollows = 55;

  /// <summary>
  /// The lowpass band's width and height, tags 27 and 28.
  /// </summary>
  /// <remarks>
  /// Not in Table B.2 — the free standard does not name these, or the highpass pair below, or the
  /// marker segment <see cref="CineFormChannelDecoder"/> skips between <see cref="LowpassPrecision"/>
  /// and the lowpass data. Their positions and meanings were measured against ffmpeg's own encoder
  /// output rather than read from any document; see that class's remarks for how.
  /// </remarks>
  internal const int LowpassWidth = 27;
  internal const int LowpassHeight = 28;

  /// <summary>A highpass subband's own stated width and height, tags 49 and 50 — measured to be
  /// reliable except at the first highpass level of a horizontally-subsampled channel; see
  /// <see cref="CineFormChannelDecoder"/>.</summary>
  internal const int HighpassWidth = 49;
  internal const int HighpassHeight = 50;
}
