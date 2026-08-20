using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.MpegPs;

/// <summary>
/// One element of a program stream as it lies in the file: either a pack header or a packet
/// introduced by a stream id.
/// </summary>
/// <remarks>
/// Pack headers are yielded rather than swallowed because two callers want them for different
/// reasons — opening a file has to decide from the first one whether the file is ISO/IEC 11172-1 or
/// ISO/IEC 13818-1, and nothing else in a program stream states which. Everything else is shaped
/// alike: a stream id, a sixteen-bit length, and that many bytes.
/// </remarks>
/// <param name="StreamId">The byte after the start code prefix; <c>0xBA</c> for a pack header.</param>
/// <param name="Position">Offset of the <c>00 00 01</c> that introduces this element.</param>
/// <param name="PayloadOffset">Offset of the first byte after the element's own header. For a PES
/// packet that is past the optional header, so the payload is elementary stream bytes and nothing
/// else.</param>
/// <param name="PayloadLength">How many bytes of payload follow.</param>
/// <param name="PresentationTimestamp">The PTS the PES header states, in 90 kHz ticks, or
/// <c>null</c> where it states none.</param>
/// <param name="DecodeTimestamp">The DTS the PES header states. A header carrying a PTS and no DTS
/// means the two are equal — 13818-1 defines the decoding time of such an access unit as its
/// presentation time — so this is filled in from the PTS rather than left unstated.</param>
/// <param name="SystemsVersion">1 for an ISO/IEC 11172-1 pack header, 2 for an ISO/IEC 13818-1 one,
/// 0 for anything that is not a pack header.</param>
internal readonly record struct MpegPsElement(
  byte StreamId,
  int Position,
  int PayloadOffset,
  int PayloadLength,
  long? PresentationTimestamp,
  long? DecodeTimestamp,
  int SystemsVersion);

/// <summary>
/// Walks a program stream's packs and packets without reading a byte of any payload.
/// </summary>
/// <remarks>
/// A program stream has no index and no table of contents. Its whole structure is a chain: every
/// element states its own length, and the next one begins where it ends. That is why this is a walk
/// and not a lookup, and why seeking to a given packet costs the packets before it.
/// <para/>
/// The chain is followed exactly. An element whose stated length runs past the end of the file, or a
/// position where a start code is due and none is there, stops the walk with a refusal naming the
/// offset — rather than resynchronising to the next start code, which would silently hand back a
/// file with a hole in it as a file that was read.
/// </remarks>
internal static class MpegPsScanner {

  internal const byte PROGRAM_END = 0xB9;
  internal const byte PACK_START = 0xBA;
  internal const byte SYSTEM_HEADER = 0xBB;
  internal const byte PROGRAM_STREAM_MAP = 0xBC;
  internal const byte PRIVATE_STREAM_1 = 0xBD;
  internal const byte PADDING_STREAM = 0xBE;
  internal const byte PRIVATE_STREAM_2 = 0xBF;

  internal const byte FIRST_AUDIO_STREAM = 0xC0;
  internal const byte LAST_AUDIO_STREAM = 0xDF;
  internal const byte FIRST_VIDEO_STREAM = 0xE0;
  internal const byte LAST_VIDEO_STREAM = 0xEF;

  /// <summary>The clock every timestamp in a program stream is counted in: 90 kHz.</summary>
  internal const int SYSTEM_CLOCK_HZ = 90_000;

  private const int _START_CODE_LENGTH = 4;
  private const int _MPEG1_PACK_HEADER_LENGTH = 12;
  private const int _MPEG2_PACK_HEADER_LENGTH = 14;
  private const int _TIMESTAMP_LENGTH = 5;

  /// <summary>Whether a stream id introduces a stream of media rather than a header or a filler.</summary>
  /// <remarks>
  /// Private stream 1 is in and private stream 2 is not, which looks arbitrary and is not: the first
  /// is where a DVD puts its AC-3, DTS, linear PCM and subpictures, and the second is where it puts
  /// the navigation blocks that steer a player around the disc. One carries streams; the other
  /// carries instructions about them.
  /// </remarks>
  internal static bool IsMedia(byte streamId)
    => streamId == PRIVATE_STREAM_1 || streamId is >= FIRST_AUDIO_STREAM and <= LAST_VIDEO_STREAM;

  internal static bool IsVideo(byte streamId) => streamId is >= FIRST_VIDEO_STREAM and <= LAST_VIDEO_STREAM;

  internal static bool IsAudio(byte streamId) => streamId is >= FIRST_AUDIO_STREAM and <= LAST_AUDIO_STREAM;

  /// <summary>
  /// Walks every element of the stream, from the first pack to the end code or the end of the file.
  /// </summary>
  internal static IEnumerable<MpegPsElement> Walk(ReadOnlyMemory<byte> file) {
    var at = 0;

    while (at + _START_CODE_LENGTH <= file.Length) {
      var streamId = _StreamIdAt(file, at);

      // The end code is the writer saying the stream is over. Anything after it — and ffmpeg's
      // muxers do leave padding after it — belongs to no packet.
      if (streamId == PROGRAM_END)
        yield break;

      if (streamId == PACK_START) {
        var (length, version) = _PackHeader(file, at);
        yield return new(PACK_START, at, at + length, 0, null, null, version);
        at += length;
        continue;
      }

      var element = _Packet(file, at, streamId);
      yield return element;
      at = element.PayloadOffset + element.PayloadLength;
    }
  }

  /// <summary>The stream id at an offset, refusing anything that is not a start code.</summary>
  private static byte _StreamIdAt(ReadOnlyMemory<byte> file, int at) {
    var span = file.Span;
    if (span[at] != 0 || span[at + 1] != 0 || span[at + 2] != 1)
      throw new InvalidDataException(
        $"Offset {at} of this program stream is where the next element begins, and the bytes there are "
        + $"{span[at]:X2} {span[at + 1]:X2} {span[at + 2]:X2} rather than a 00 00 01 start code prefix.");

    return span[at + 3];
  }

  /// <summary>
  /// The length of a pack header and which of the two systems standards wrote it.
  /// </summary>
  /// <remarks>
  /// The two forms are not versions of one layout, they are two layouts sharing a start code, and
  /// the byte after the start code is what tells them apart. ISO/IEC 11172-1 opens with the four bits
  /// <c>0010</c> and then packs a 33-bit system clock reference into five bytes and a 22-bit mux rate
  /// into three, for twelve bytes in all. ISO/IEC 13818-1 opens with the two bits <c>01</c>, widens
  /// the clock reference to 33 bits plus a 9-bit extension across six bytes, keeps the mux rate at
  /// three, and adds a byte whose low three bits count the stuffing that follows — fourteen bytes
  /// plus stuffing. Reading either with the other's layout puts the next start code in the wrong
  /// place and loses the rest of the file.
  /// </remarks>
  private static (int Length, int Version) _PackHeader(ReadOnlyMemory<byte> file, int at) {
    var span = file.Span;
    if (at + _START_CODE_LENGTH >= span.Length)
      throw new InvalidDataException($"The pack header at offset {at} is cut off by the end of the file.");

    var marker = span[at + _START_CODE_LENGTH];

    if ((marker & 0xC0) == 0x40) {
      if (at + _MPEG2_PACK_HEADER_LENGTH > span.Length)
        throw new InvalidDataException($"The ISO/IEC 13818-1 pack header at offset {at} is cut off by the end of the file.");

      // The stuffing is counted by the low three bits of the last byte of the header and is bytes of
      // 0xFF that pad the pack out to whatever length the writer chose to align on.
      var stuffing = span[at + _MPEG2_PACK_HEADER_LENGTH - 1] & 0x07;
      var length = _MPEG2_PACK_HEADER_LENGTH + stuffing;
      if (at + length > span.Length)
        throw new InvalidDataException($"The pack header at offset {at} states {stuffing} bytes of stuffing that run past the end of the file.");

      return (length, 2);
    }

    if ((marker & 0xF0) == 0x20) {
      if (at + _MPEG1_PACK_HEADER_LENGTH > span.Length)
        throw new InvalidDataException($"The ISO/IEC 11172-1 pack header at offset {at} is cut off by the end of the file.");

      return (_MPEG1_PACK_HEADER_LENGTH, 1);
    }

    throw new InvalidDataException(
      $"The pack header at offset {at} is followed by 0x{marker:X2}, which is neither the 0010 of an "
      + "ISO/IEC 11172-1 pack nor the 01 of an ISO/IEC 13818-1 one, so its length cannot be known.");
  }

  /// <summary>Reads one packet's header and says where its payload is.</summary>
  private static MpegPsElement _Packet(ReadOnlyMemory<byte> file, int at, byte streamId) {
    var span = file.Span;
    if (at + 6 > span.Length)
      throw new InvalidDataException($"The packet at offset {at} is cut off before its length field.");

    var declared = (span[at + _START_CODE_LENGTH] << 8) | span[at + _START_CODE_LENGTH + 1];
    var bodyOffset = at + 6;
    if (bodyOffset + declared > span.Length)
      throw new InvalidDataException(
        $"The packet at offset {at} states a length of {declared} bytes, which runs {bodyOffset + declared - span.Length} "
        + "bytes past the end of the file.");

    // Only the packets that carry a stream have the optional PES header in front of their payload.
    // A system header, a program stream map, padding and the navigation blocks of private stream 2
    // are their length and then their contents.
    if (!IsMedia(streamId))
      return new(streamId, at, bodyOffset, declared, null, null, 0);

    if (declared == 0)
      throw new InvalidDataException(
        $"The packet for stream 0x{streamId:X2} at offset {at} states a length of zero. An unbounded packet is "
        + "only defined for a transport stream, and in a program stream nothing says where it would end.");

    var (payloadOffset, pts, dts) = _PesHeader(file, bodyOffset, bodyOffset + declared, at, streamId);
    return new(streamId, at, payloadOffset, bodyOffset + declared - payloadOffset, pts, dts, 0);
  }

  /// <summary>
  /// Skips the optional PES header, in whichever of its two spellings this packet uses.
  /// </summary>
  /// <remarks>
  /// ISO/IEC 13818-1 rebuilt the header as two flag bytes and an explicit length, so a reader skips
  /// what it does not understand. ISO/IEC 11172-1 has no such length: the header is a run of stuffing
  /// bytes, then an optional buffer size, then a one-byte code that says whether timestamps follow —
  /// and the only way past it is to read all of it. Both occur in files this was measured against,
  /// one per muxer, so both are read here.
  /// <para/>
  /// Which one it is, is decided by the first byte and not by the pack header the packet happens to
  /// sit in. The two bits <c>10</c> open a 13818-1 header, where a 11172-1 header can only begin with
  /// stuffing (<c>0xFF</c>), a buffer size (<c>01</c>), a timestamp code (<c>0010</c>/<c>0011</c>) or
  /// the <c>0x0F</c> that means neither.
  /// </remarks>
  private static (int PayloadOffset, long? Pts, long? Dts) _PesHeader(
    ReadOnlyMemory<byte> file, int from, int to, int packetAt, byte streamId) {
    var span = file.Span;
    long? pts = null;
    long? dts = null;

    if ((span[from] & 0xC0) == 0x80) {
      if (from + 3 > to)
        throw new InvalidDataException($"The PES header of stream 0x{streamId:X2} at offset {packetAt} is shorter than its own flags.");

      var flags = span[from + 1];
      var headerLength = span[from + 2];
      var payload = from + 3 + headerLength;
      if (payload > to)
        throw new InvalidDataException(
          $"The PES header of stream 0x{streamId:X2} at offset {packetAt} states {headerLength} bytes of header data, "
          + "which is more than the packet holds.");

      var cursor = from + 3;
      if ((flags & 0x80) != 0) {
        _RequireTimestamp(cursor, payload, packetAt, streamId);
        pts = _Timestamp(span, cursor);
        cursor += _TIMESTAMP_LENGTH;
      }

      if ((flags & 0x40) != 0) {
        _RequireTimestamp(cursor, payload, packetAt, streamId);
        dts = _Timestamp(span, cursor);
      }

      return (payload, pts, dts ?? pts);
    }

    var at = from;

    // Up to sixteen bytes a writer may insert to align the payload. More than that is not stuffing,
    // it is a header being read at the wrong offset.
    var stuffing = 0;
    while (at < to && span[at] == 0xFF) {
      if (++stuffing > 16)
        throw new InvalidDataException(
          $"The PES header of stream 0x{streamId:X2} at offset {packetAt} carries more than sixteen stuffing bytes, "
          + "which no ISO/IEC 11172-1 header does.");

      ++at;
    }

    if (at + 2 <= to && (span[at] & 0xC0) == 0x40)
      at += 2; // the decoder buffer scale and size, which say nothing about where the payload is

    if (at >= to)
      throw new InvalidDataException($"The PES header of stream 0x{streamId:X2} at offset {packetAt} runs to the end of the packet.");

    var code = span[at];
    if ((code & 0xF0) == 0x20) {
      _RequireTimestamp(at, to, packetAt, streamId);
      pts = _Timestamp(span, at);
      at += _TIMESTAMP_LENGTH;
    } else if ((code & 0xF0) == 0x30) {
      _RequireTimestamp(at, to, packetAt, streamId);
      pts = _Timestamp(span, at);
      _RequireTimestamp(at + _TIMESTAMP_LENGTH, to, packetAt, streamId);
      dts = _Timestamp(span, at + _TIMESTAMP_LENGTH);
      at += 2 * _TIMESTAMP_LENGTH;
    } else if (code == 0x0F) {
      ++at;
    } else
      throw new InvalidDataException(
        $"The PES header of stream 0x{streamId:X2} at offset {packetAt} ends with 0x{code:X2}, which is neither a "
        + "timestamp code nor the 0x0F that stands for a packet carrying none.");

    return (at, pts, dts ?? pts);
  }

  private static void _RequireTimestamp(int at, int to, int packetAt, byte streamId) {
    if (at + _TIMESTAMP_LENGTH > to)
      throw new InvalidDataException(
        $"The PES header of stream 0x{streamId:X2} at offset {packetAt} says a timestamp follows and the packet ends first.");
  }

  /// <summary>
  /// Unpacks one 33-bit timestamp from the five bytes it is spread over.
  /// </summary>
  /// <remarks>
  /// Four bits of code, then the value in three pieces of 3, 15 and 15 bits, each piece followed by a
  /// marker bit set to one. The markers are there so that a run of timestamp bytes can never spell a
  /// start code; they carry nothing and are dropped.
  /// </remarks>
  private static long _Timestamp(ReadOnlySpan<byte> span, int at)
    => ((long)(span[at] >> 1) & 0x07) << 30
       | (long)span[at + 1] << 22
       | (long)(span[at + 2] >> 1) << 15
       | (long)span[at + 3] << 7
       | (long)(span[at + 4] >> 1);
}
