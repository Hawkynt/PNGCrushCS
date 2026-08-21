using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace FileFormat.RealMedia;

/// <summary>One packet of a RealMedia data chunk: which stream it belongs to, when it is due, and its bytes.</summary>
/// <param name="StreamNumber">The stream number the file gave the stream this belongs to.</param>
/// <param name="Timestamp">When the packet is due, in milliseconds.</param>
/// <param name="IsKeyFrame">Whether the packet's flags mark it as one decoding may begin at.</param>
/// <param name="Data">The packet's payload, as a window onto the file.</param>
/// <param name="DataOffset">Where the payload begins, counted from the file's start.</param>
/// <param name="IsComplete">Whether the packet's whole stated length was inside the file.</param>
internal readonly record struct RealMediaPacket(
  int StreamNumber,
  long Timestamp,
  bool IsKeyFrame,
  ReadOnlyMemory<byte> Data,
  int DataOffset,
  bool IsComplete);

/// <summary>
/// Walks the packets of a RealMedia data chunk.
/// </summary>
/// <remarks>
/// A packet header states an object version, a length counting the header, the stream number, a
/// timestamp in milliseconds and a flags byte. The two versions differ only in what sits between the
/// timestamp and the flags: version 0 has a one-byte packet group, version 1 a two-byte rule number
/// from the file's stream-selection rules. Both are the writer's bookkeeping about which packets
/// belong to which bandwidth alternative, and neither changes where the payload is or what is in it.
/// <para/>
/// Nothing here knows what a payload holds. A packet is a stream number, a moment and a run of bytes;
/// whether those bytes are a whole frame, part of one, or a block of sound is a question for whoever
/// knows the stream's codec.
/// </remarks>
internal static class RealMediaPacketReader {

  /// <summary>The data chunk's own fields before its first packet: a packet count and the next chunk's offset.</summary>
  internal const int DATA_PREFIX = 8;

  /// <summary>The header of a version 0 packet: version, length, stream, timestamp, group, flags.</summary>
  private const int _HEADER_VERSION_0 = 12;

  /// <summary>The header of a version 1 packet, whose two-byte rule number replaces the one-byte group.</summary>
  private const int _HEADER_VERSION_1 = 13;

  /// <summary>The flag marking a packet decoding may begin at.</summary>
  private const int _KEY_FRAME = 0x02;

  /// <summary>
  /// Walks every packet from an offset to the end of the data chunk.
  /// </summary>
  /// <remarks>
  /// The walk ends at the first header it cannot believe rather than throwing, for the same reason
  /// the chunk walk does: a truncated recording is the ordinary state of these files. A packet whose
  /// stated length runs past the end of the file is still reported, with
  /// <see cref="RealMediaPacket.IsComplete"/> false and only the bytes that are actually there, so
  /// that a caller can take the whole frames out of it and refuse the one that was cut in half.
  /// </remarks>
  internal static IEnumerable<RealMediaPacket> Walk(ReadOnlyMemory<byte> file, int start, int end) {
    var offset = start;
    var limit = end < file.Length ? end : file.Length;

    while (offset + _HEADER_VERSION_0 <= limit) {
      var span = file.Span;
      var version = BinaryPrimitives.ReadUInt16BigEndian(span[offset..]);
      var length = BinaryPrimitives.ReadUInt16BigEndian(span[(offset + 2)..]);
      var streamNumber = BinaryPrimitives.ReadUInt16BigEndian(span[(offset + 4)..]);
      var timestamp = BinaryPrimitives.ReadUInt32BigEndian(span[(offset + 6)..]);

      // A version this reader has not been written against lays its header out differently, so the
      // payload it points at would be somebody else's bytes. There is no way to walk past it either,
      // because the length is one of the fields whose position is in doubt.
      var header = version switch {
        0 => _HEADER_VERSION_0,
        1 => _HEADER_VERSION_1,
        _ => -1,
      };

      if (header < 0 || length < header || offset + header > limit)
        yield break;

      var flags = span[offset + (version == 0 ? 11 : 12)];

      // What is actually there, which for the last packet of a file that was cut short is less than
      // the header claims.
      var available = offset + length <= limit ? length - header : limit - offset - header;

      yield return new(
        streamNumber,
        timestamp,
        (flags & _KEY_FRAME) != 0,
        file.Slice(offset + header, available),
        offset + header,
        offset + length <= limit);

      if (offset + length > limit)
        yield break;

      offset += length;
    }
  }
}
