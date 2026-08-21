using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.H265;

/// <summary>
/// The <c>HEVCDecoderConfigurationRecord</c> an ISO base media container carries — ISO/IEC 14496-15,
/// clause 8.3.3.
/// </summary>
/// <remarks>
/// Two things a decoder needs before it sees a sample, and only one of them is the parameter sets.
/// The other is how many bytes each NAL unit's length is written in, which decides whether the
/// packets are read at all — and it is the reason this record is consulted for its presence as much
/// as for its contents. A stream whose container carried one is length-prefixed; one whose container
/// did not is a byte stream with start codes. Guessing per packet would read a three-byte length
/// beginning <c>00 00 01</c> as a start code, which is a perfectly ordinary 256-byte NAL unit.
/// <para/>
/// The rest of it — the profile, the level, the chroma format, the sample depth — is a copy of what
/// the sequence parameter set says, and copies disagree. Nothing here is read for those: they are
/// taken from the parameter set itself, wherever it arrived from.
/// </remarks>
internal sealed class H265DecoderConfiguration {

  /// <summary>The fixed part, before the arrays of parameter sets.</summary>
  private const int _HEADER_LENGTH = 23;

  private H265DecoderConfiguration(int lengthSize, List<byte[]> parameterSets) {
    this.LengthSize = lengthSize;
    this.ParameterSets = parameterSets;
  }

  /// <summary>How many bytes each NAL unit's length occupies in a sample: 1, 2 or 4.</summary>
  internal int LengthSize { get; }

  /// <summary>The video, sequence and picture parameter sets carried out of band, in that order.</summary>
  internal IReadOnlyList<byte[]> ParameterSets { get; }

  /// <summary>Reads the record, or answers <c>null</c> where the container carried none.</summary>
  internal static H265DecoderConfiguration? TryParse(ReadOnlyMemory<byte> data) {
    if (data.Length < _HEADER_LENGTH)
      return null;

    var span = data.Span;
    if (span[0] != 1)
      return null;

    var lengthSize = (span[21] & 3) + 1;
    if (lengthSize == 3)
      throw new InvalidDataException(
        "An HEVCDecoderConfigurationRecord states a NAL unit length of three bytes (lengthSizeMinusOne of 2), which "
        + "ISO/IEC 14496-15 does not define. The record is not one.");

    var arrays = span[22];
    var parameterSets = new List<byte[]>();
    var at = _HEADER_LENGTH;

    for (var i = 0; i < arrays; ++i) {
      if (at + 3 > span.Length)
        throw new InvalidDataException(
          "An HEVCDecoderConfigurationRecord ends in the middle of its parameter set arrays.");

      // The array's own NAL unit type is not read: each unit carries its own header, and this
      // decoder reads that. Two places stating the same thing is two chances to disagree.
      var count = (span[at + 1] << 8) | span[at + 2];
      at += 3;

      for (var j = 0; j < count; ++j) {
        if (at + 2 > span.Length)
          throw new InvalidDataException(
            "An HEVCDecoderConfigurationRecord ends in the middle of a parameter set length.");

        var length = (span[at] << 8) | span[at + 1];
        at += 2;

        if (at + length > span.Length)
          throw new InvalidDataException(
            $"An HEVCDecoderConfigurationRecord states a {length}-byte parameter set with only {span.Length - at} "
            + "bytes left in the record.");

        parameterSets.Add(span.Slice(at, length).ToArray());
        at += length;
      }
    }

    return new(lengthSize, parameterSets);
  }
}
