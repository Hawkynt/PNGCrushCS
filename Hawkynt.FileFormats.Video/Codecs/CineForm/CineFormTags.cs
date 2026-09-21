namespace FileFormat.Codecs.CineForm;

/// <summary>
/// The tag numbers of the codec state parameters this implementation reads, SMPTE ST 2073-1:2017
/// Table B.2 plus the older CineForm tags present in GoPro/FFmpeg bitstreams.
/// </summary>
/// <remarks>
/// A tag-value pair is always exactly one segment (Section 8.3.1) whatever tag it carries, which is
/// what lets every tag this decoder does not name here be skipped rather than understood. The one
/// exception is <see cref="Index"/>: its value is followed by that many raw 32-bit channel sizes, so
/// treating those words as ordinary tag/value segments is not merely wasteful but wrong once a size's
/// high half happens to equal a meaningful tag.
/// </remarks>
internal static class CineFormTags {
  internal const int Index = 2;
  internal const int TransformType = 10;
  internal const int ChannelCount = 12;
  internal const int SubbandCount = 14;
  internal const int GroupTrailer = 18;
  internal const int ImageWidth = 20;
  internal const int ImageHeight = 21;
  internal const int LowpassPrecision = 35;
  internal const int SubbandNumber = 48;
  internal const int Quantization = 53;
  internal const int ChannelNumber = 62;
  internal const int InterlacedFlags = 63;
  internal const int SampleFlags = 68;
  internal const int Precision = 70;
  internal const int InputFormat = 71;
  internal const int PrescaleTable = 83;
  internal const int EncodedFormat = 84;

  /// <summary>Optional older CineForm tag 85. Its optional form is encoded as the negative tag.</summary>
  internal const int DisplayHeight = 85;

  /// <summary>Total independently coded views/samples in one legacy CineForm stereo/multicam packet.</summary>
  /// <remarks>GoPro names this legacy codec tag <c>CODEC_TAG_ENCODED_CHANNELS</c> (92).</remarks>
  internal const int EncodedViewCount = 92;

  /// <summary>Zero-based native view/sample identifier inside a legacy stereo/multicam packet.</summary>
  /// <remarks>GoPro names this legacy codec tag <c>CODEC_TAG_ENCODED_CHANNEL_NUMBER</c> (93).</remarks>
  internal const int EncodedViewNumber = 93;

  /// <summary>Optional one-based quality/preference rank for the view; zero means unspecified.</summary>
  /// <remarks>GoPro names this legacy codec tag <c>CODEC_TAG_ENCODED_CHANNEL_QUALITY</c> (94).</remarks>
  internal const int EncodedViewQuality = 94;

  /// <summary>
  /// The last of a highpass subband's own header tags — not in Table B.2 — immediately after which its
  /// entropy-coded data begins with no marker and no gap. See
  /// <see cref="CineFormChannelDecoder"/>'s remarks for how that boundary was measured.
  /// </summary>
  internal const int HighpassDataFollows = 55;

  /// <summary>The lowpass band's width and height, tags 27 and 28.</summary>
  /// <remarks>
  /// Not in Table B.2 — the free standard does not name these, or the highpass pair below, or the
  /// marker segment <see cref="CineFormChannelDecoder"/> skips between <see cref="LowpassPrecision"/>
  /// and the lowpass data. Their positions and meanings were measured against ffmpeg's own encoder
  /// output and then cross-checked against GoPro's SDK.
  /// </remarks>
  internal const int LowpassWidth = 27;
  internal const int LowpassHeight = 28;

  /// <summary>A highpass subband's own stated width and height, tags 49 and 50.</summary>
  internal const int HighpassWidth = 49;
  internal const int HighpassHeight = 50;
}

/// <summary>The one-based values carried by CineForm tag 84 in real CFHD frames.</summary>
internal enum CineFormEncodedFormat {
  Unspecified = 0,
  Yuv422 = 1,
  Bayer = 2,
  Rgb444 = 3,
  Rgba4444 = 4,
}
