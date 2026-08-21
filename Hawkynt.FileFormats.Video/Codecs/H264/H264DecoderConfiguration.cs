using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.H264;

/// <summary>
/// The <c>AVCDecoderConfigurationRecord</c> of ISO/IEC 14496-15: the parameter sets a container
/// carries out of band, and the size of the length prefix its samples are written with.
/// </summary>
/// <remarks>
/// A container that carries H.264 in the length-prefixed form has to state two things a decoder
/// cannot work out from the samples: how many bytes each NAL unit's length occupies, and what the
/// sequence and picture parameter sets are — because in that form they are usually not in the samples
/// at all, but once in the header. Both are in this record.
/// <para/>
/// Three of the four containers here hand it over as the record itself; MP4 hands over the whole
/// visual sample entry with the record as a box inside it, because the demuxer deliberately does not
/// know which of a sample entry's boxes belongs to which codec. So this looks for the record either
/// way rather than making the containers agree.
/// </remarks>
internal sealed class H264DecoderConfiguration {

  private const int _VISUAL_SAMPLE_ENTRY_HEADER = 86;

  /// <summary>The bytes each NAL unit's length prefix occupies: 1, 2 or 4.</summary>
  internal int LengthSize { get; private init; } = 4;

  /// <summary>The sequence parameter sets, as NAL units with their headers still on.</summary>
  internal IReadOnlyList<byte[]> SequenceParameterSets { get; private init; } = [];

  internal IReadOnlyList<byte[]> PictureParameterSets { get; private init; } = [];

  /// <summary>
  /// Finds and reads the configuration record in whatever a container calls codec private data, or
  /// answers <c>null</c> when there is none there.
  /// </summary>
  /// <remarks>
  /// A missing record is not an error. A transport stream and a bare elementary stream have no such
  /// field at all and carry their parameter sets in the byte stream; so does an MP4 written with
  /// <c>avc3</c> sample entries. The caller falls back to the Annex B form for those.
  /// </remarks>
  internal static H264DecoderConfiguration? TryParse(ReadOnlyMemory<byte> privateData) {
    var record = _FindRecord(privateData);
    if (record.Length < 7)
      return null;

    var span = record.Span;
    if (span[0] != 1)
      return null;

    var lengthSize = (span[4] & 3) + 1;
    if (lengthSize == 3)
      throw new InvalidDataException(
        "This H.264 stream's AVCDecoderConfigurationRecord states a NAL unit length prefix of three bytes "
        + "(lengthSizeMinusOne 2), which ISO/IEC 14496-15 does not define. Only 1, 2 and 4 are valid.");

    var at = 5;
    var sequenceCount = span[at++] & 0x1F;
    var sequenceSets = _ReadSet(span, ref at, sequenceCount, "sequence");
    if (at >= span.Length)
      return new() { LengthSize = lengthSize, SequenceParameterSets = sequenceSets };

    var pictureCount = span[at++];
    var pictureSets = _ReadSet(span, ref at, pictureCount, "picture");

    return new() {
      LengthSize = lengthSize,
      SequenceParameterSets = sequenceSets,
      PictureParameterSets = pictureSets,
    };
  }

  private static byte[][] _ReadSet(ReadOnlySpan<byte> span, ref int at, int count, string what) {
    var sets = new List<byte[]>(count);

    for (var i = 0; i < count; ++i) {
      if (at + 2 > span.Length)
        throw new InvalidDataException(
          $"This H.264 stream's AVCDecoderConfigurationRecord ends inside the length of {what} parameter set {i} of "
          + $"{count}.");

      var length = BinaryPrimitives.ReadUInt16BigEndian(span[at..]);
      at += 2;

      if (at + length > span.Length)
        throw new InvalidDataException(
          $"This H.264 stream's AVCDecoderConfigurationRecord states a {what} parameter set of {length} bytes with "
          + $"only {span.Length - at} left in the record.");

      sets.Add(span.Slice(at, length).ToArray());
      at += length;
    }

    return [.. sets];
  }

  /// <summary>
  /// Answers the record itself, whether it arrived alone or inside an ISO base media sample entry.
  /// </summary>
  private static ReadOnlyMemory<byte> _FindRecord(ReadOnlyMemory<byte> privateData) {
    var span = privateData.Span;

    // The record itself begins with a configuration version of 1. A sample entry begins with its own
    // length, whose top byte is zero for any sample entry small enough to be one — so the two are
    // told apart by the first byte and not by guessing.
    if (span.Length >= 7 && span[0] == 1)
      return privateData;

    if (span.Length < _VISUAL_SAMPLE_ENTRY_HEADER + 8)
      return ReadOnlyMemory<byte>.Empty;

    // A visual sample entry is a fixed 86-byte preamble and then boxes, of which 'avcC' is one
    // (ISO/IEC 14496-12, clause 12.1.3, and 14496-15, clause 5.3.4).
    for (var at = _VISUAL_SAMPLE_ENTRY_HEADER; at + 8 <= span.Length;) {
      var size = (int)BinaryPrimitives.ReadUInt32BigEndian(span[at..]);
      if (size < 8 || at + size > span.Length)
        break;

      if (span[at + 4] == (byte)'a' && span[at + 5] == (byte)'v' && span[at + 6] == (byte)'c' && span[at + 7] == (byte)'C')
        return privateData.Slice(at + 8, size - 8);

      at += size;
    }

    return ReadOnlyMemory<byte>.Empty;
  }
}
