using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.CineForm;

/// <summary>Legacy CineForm stereo/multicam framing around otherwise independent CFHD samples.</summary>
/// <remarks>
/// GoPro's public SDK names tags 92, 93 and 94 as the encoded channel count, channel number and
/// channel quality respectively, and its decoder state describes the right-eye sample as beginning at
/// a byte offset immediately after the left-eye sample. This helper keeps that outer framing separate
/// from the wavelet picture codec: each view remains a complete ordinary CFHD sample and therefore has
/// no prediction dependency on another view.
/// </remarks>
internal static class CineFormMultiViewFraming {
  private const int _SAMPLE_TYPE = 1;
  private const int _INDEX = 2;
  private const int _MARKER = 4;
  private const int _SAMPLE_TYPE_IFRAME = 9;
  private const int _LOWPASS_SEGMENT = 0x1A4A;

  internal readonly record struct SampleInfo(
    int Length,
    int HeaderInsertionOffset,
    int ViewCount,
    int ViewNumber,
    int? QualityRank);

  /// <summary>Adds the three legacy view-identification tags to a complete single-view sample.</summary>
  internal static byte[] AddViewTags(byte[] sample, int viewCount, int viewNumber, int? qualityRank) {
    ArgumentNullException.ThrowIfNull(sample);
    if (viewCount is < 2 or > ushort.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(viewCount));
    if ((uint)viewNumber >= (uint)viewCount)
      throw new ArgumentOutOfRangeException(nameof(viewNumber));
    if (qualityRank is <= 0 or > ushort.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(qualityRank));

    var info = Inspect(sample);
    if (info.ViewCount != 1)
      throw new InvalidDataException("The CineForm sample already carries multi-view framing.");

    const int EXTRA = 3 * 4;
    var result = new byte[sample.Length + EXTRA];
    sample.AsSpan(0, info.HeaderInsertionOffset).CopyTo(result);

    var position = info.HeaderInsertionOffset;
    _WriteTag(result, ref position, CineFormTags.EncodedViewCount, viewCount);
    _WriteTag(result, ref position, CineFormTags.EncodedViewNumber, viewNumber);
    _WriteTag(result, ref position, CineFormTags.EncodedViewQuality, qualityRank ?? 0);

    sample.AsSpan(info.HeaderInsertionOffset).CopyTo(result.AsSpan(position));
    return result;
  }

  /// <summary>Splits one packet into its independently coded view samples and returns their metadata.</summary>
  internal static IReadOnlyList<(ReadOnlyMemory<byte> Sample, SampleInfo Info)> Split(ReadOnlyMemory<byte> packet) {
    if (packet.IsEmpty)
      throw new InvalidDataException("A CineForm multi-view packet is empty.");

    var first = Inspect(packet.Span);
    if (first.ViewCount < 2)
      throw new InvalidDataException("This CineForm packet does not declare multiple encoded views.");

    var result = new (ReadOnlyMemory<byte>, SampleInfo)[first.ViewCount];
    var seen = new bool[first.ViewCount];
    var offset = 0;

    for (var i = 0; i < result.Length; ++i) {
      if (offset >= packet.Length)
        throw new InvalidDataException($"CineForm declares {result.Length} views but the packet ends after {i} samples.");

      var remainder = packet[offset..];
      var info = Inspect(remainder.Span);
      if (info.ViewCount != result.Length)
        throw new InvalidDataException(
          $"CineForm view sample {i} declares {info.ViewCount} encoded views; the first sample declared {result.Length}.");
      if ((uint)info.ViewNumber >= (uint)result.Length || seen[info.ViewNumber])
        throw new InvalidDataException($"CineForm view identifier {info.ViewNumber} is outside the declared set or duplicated.");

      seen[info.ViewNumber] = true;
      result[i] = (remainder[..info.Length], info);
      offset = checked(offset + info.Length);
    }

    if (offset != packet.Length)
      throw new InvalidDataException(
        $"CineForm's {result.Length} declared view samples consume {offset} bytes, leaving {packet.Length - offset} unexplained bytes in the packet.");

    return result;
  }

  /// <summary>
  /// Reads only the ordinary CFHD header and channel-size index to determine one sample's byte length.
  /// No entropy payload is scanned for marker-looking byte patterns.
  /// </summary>
  internal static SampleInfo Inspect(ReadOnlySpan<byte> sample) {
    if (sample.Length < 12)
      throw new InvalidDataException("A CineForm sample is too short to contain its I-frame header and channel index.");

    var position = 0;
    var viewCount = 1;
    var viewNumber = 0;
    int? qualityRank = null;
    uint[]? channelSizes = null;
    var sawIFrame = false;

    while (position + 4 <= sample.Length) {
      var tag = BinaryPrimitives.ReadInt16BigEndian(sample[position..]);
      var value = BinaryPrimitives.ReadUInt16BigEndian(sample[(position + 2)..]);
      var tagOffset = position;
      position += 4;

      if (tag == _SAMPLE_TYPE && value == _SAMPLE_TYPE_IFRAME)
        sawIFrame = true;

      if (tag == _INDEX) {
        if (value is 0 or > 16)
          throw new InvalidDataException($"A CineForm channel-size index declares {value} entries.");

        var bytes = checked(value * 4);
        if (position > sample.Length - bytes)
          throw new InvalidDataException("A CineForm sample ends inside its channel-size index.");

        channelSizes = new uint[value];
        for (var i = 0; i < channelSizes.Length; ++i)
          channelSizes[i] = BinaryPrimitives.ReadUInt32BigEndian(sample[(position + i * 4)..]);
        position += bytes;
        continue;
      }

      switch (tag) {
        case CineFormTags.EncodedViewCount:
          viewCount = value;
          continue;
        case CineFormTags.EncodedViewNumber:
          viewNumber = value;
          continue;
        case CineFormTags.EncodedViewQuality:
          qualityRank = value == 0 ? null : value;
          continue;
      }

      if (tag != _MARKER || value != _LOWPASS_SEGMENT)
        continue;

      if (!sawIFrame)
        throw new InvalidDataException("A CineForm sample reaches its first lowpass band without an I-frame sample header.");
      if (channelSizes is null)
        throw new NotSupportedException(
          "CineForm multi-view framing needs the legacy channel-size index; this sample omits it.");

      // The first channel starts immediately after this lowpass marker. Every later channel in the
      // legacy packet has SampleType + ChannelNumber + lowpass Marker (three tag/value segments)
      // before the byte count held in the index. The complete sample ends with GroupTrailer.
      long length = position + channelSizes[0];
      for (var i = 1; i < channelSizes.Length; ++i)
        length += 12L + channelSizes[i];
      length += 4; // GroupTrailer tag/value pair

      if (length > sample.Length || length > int.MaxValue)
        throw new InvalidDataException(
          $"A CineForm channel index describes a {length}-byte sample but only {sample.Length} bytes remain.");

      var sampleLength = (int)length;
      var trailer = sampleLength - 4;
      if (BinaryPrimitives.ReadInt16BigEndian(sample[trailer..]) != CineFormTags.GroupTrailer)
        throw new InvalidDataException(
          "CineForm's channel-size index does not land on the expected group trailer; this sample uses an unsupported outer framing variant.");

      return new(sampleLength, tagOffset, viewCount, viewNumber, qualityRank);
    }

    throw new InvalidDataException("A CineForm sample never reaches its first lowpass marker.");
  }

  private static void _WriteTag(Span<byte> destination, ref int position, int tag, int value) {
    BinaryPrimitives.WriteInt16BigEndian(destination[position..], checked((short)tag));
    BinaryPrimitives.WriteUInt16BigEndian(destination[(position + 2)..], checked((ushort)value));
    position += 4;
  }
}
