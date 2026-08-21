using FileFormat.Core;

namespace FileFormat.Ogg;

/// <summary>
/// One logical bitstream of an Ogg file: everything the header scan learned about it, kept for the
/// packet walk to time its packets by.
/// </summary>
/// <remarks>
/// A logical bitstream is what Ogg calls a stream, and a physical bitstream — the file — may hold
/// several of them interleaved page by page. They are told apart by the serial number in each page
/// header and by nothing else: the numbers are chosen at random by the writer, are not ordered, and
/// mean nothing beyond identity, which is why this reader keeps a map from them to stream indices
/// rather than reading anything into the values.
/// </remarks>
internal sealed class OggBitstream {

  /// <summary>What the demuxer reports about this bitstream.</summary>
  internal required MediaStreamInfo Info { get; init; }

  /// <summary>Which page serial number names it.</summary>
  internal required uint SerialNumber { get; init; }

  /// <summary>What its codec's Ogg mapping says about how it is laid out and timed.</summary>
  internal required OggCodecMapping Mapping { get; init; }

  /// <summary>
  /// How many of its packets are headers, resolved to a number by the header scan.
  /// </summary>
  /// <remarks>
  /// Resolved rather than taken from the mapping, because FLAC may leave it unstated and the scan is
  /// what counts the metadata blocks. Every walk of the file skips exactly this many packets, so the
  /// first packet a caller sees is the first one holding coded media.
  /// </remarks>
  internal required int HeaderPacketCount { get; init; }
}
