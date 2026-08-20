using System;

namespace FileFormat.MpegTs;

/// <summary>The head of one PES packet: which elementary stream it is, how long it says it is, and when it is due.</summary>
/// <param name="StreamId">The byte naming the elementary stream — <c>0xE0</c>–<c>0xEF</c> for pictures,
/// <c>0xC0</c>–<c>0xDF</c> for sound, and a handful of fixed values for things that are neither.</param>
/// <param name="DeclaredLength">The length the header states, counted from the byte after it, or zero
/// for a packet that does not state one.</param>
/// <param name="PayloadOffset">Where the elementary bytes begin, counted from the packet start code.</param>
/// <param name="PresentationTimestamp">When the unit is due for display, in 90 kHz units.</param>
/// <param name="DecodeTimestamp">When it is due for decoding, in the same units.</param>
internal readonly record struct PesHeader(
  int StreamId,
  int DeclaredLength,
  int PayloadOffset,
  long? PresentationTimestamp,
  long? DecodeTimestamp);

/// <summary>
/// Reads the header a PES packet begins with.
/// </summary>
/// <remarks>
/// This is the layer between a multiplex and a codec, and it is the same layer in both of the MPEG
/// systems multiplexes: a transport stream carries PES packets split across 188-byte packets and a
/// program stream carries the same PES packets one after another with a pack header in front of
/// groups of them. So nothing here knows about transport packets — it is handed a span that begins
/// with a packet start code prefix and answers what the header says — and a program stream reader can
/// use it unchanged.
/// <para/>
/// A declared length of zero is not an empty packet. It means the writer did not state one, which is
/// what every muxer does for pictures because a coded picture's length is not known until it has been
/// coded; the packet then runs until the next one starts. Sound is usually stated, because a run of
/// audio frames has a length before it is written.
/// <para/>
/// A timestamp is thirty-three bits scattered across five bytes in three runs, each separated by a
/// marker bit that is always one. The marker bits exist so that a decoder resynchronising mid-stream
/// cannot mistake the middle of a timestamp for the start of one; they carry nothing and are dropped.
/// </remarks>
internal static class PesReader {

  /// <summary>The packet start code prefix and the two bytes that follow it.</summary>
  internal const int PREFIX_SIZE = 6;

  /// <summary>The smallest header a stream with an optional header can have: the prefix, two flag bytes and a length.</summary>
  private const int _OPTIONAL_HEADER_AT = 9;

  /// <summary>
  /// Whether a span begins with the three bytes every PES packet and every start code begins with.
  /// </summary>
  internal static bool StartsPacket(ReadOnlySpan<byte> data)
    => data.Length >= 3 && data[0] == 0x00 && data[1] == 0x00 && data[2] == 0x01;

  /// <summary>
  /// Reads the header, or answers false for a span that does not hold a complete one.
  /// </summary>
  /// <remarks>
  /// Six stream ids have no optional header at all — padding, the two private streams, the ECM and
  /// EMM streams, the program stream map and directory — and their payload begins immediately after
  /// the declared length. Reading the optional header for one of those would take the first byte of
  /// somebody's payload for a flags byte and then skip however much it happened to say.
  /// </remarks>
  internal static bool TryRead(ReadOnlySpan<byte> data, out PesHeader header) {
    header = default;
    if (data.Length < PREFIX_SIZE || !StartsPacket(data))
      return false;

    var streamId = data[3];
    var declared = (data[4] << 8) | data[5];

    if (!_HasOptionalHeader(streamId)) {
      header = new(streamId, declared, PREFIX_SIZE, null, null);
      return true;
    }

    if (data.Length < _OPTIONAL_HEADER_AT)
      return false;

    // Two bits saying which timestamps follow: 2 is a presentation time on its own, 3 is that and a
    // decode time, 0 is neither. 1 is forbidden by the standard and is treated as neither rather than
    // as a reason to refuse the packet.
    var timestamps = (data[7] >> 6) & 0x03;
    var optional = data[8];
    var payload = _OPTIONAL_HEADER_AT + optional;
    if (data.Length < payload)
      return false;

    long? presentation = null;
    long? decode = null;
    if (timestamps >= 2 && optional >= 5) {
      presentation = _Timestamp(data[_OPTIONAL_HEADER_AT..]);

      // The standard says a unit with only a presentation time is decoded when it is presented, and
      // ffprobe reports the two as equal for exactly those packets — the sound of every multiplex
      // measured here. Leaving the decode time unstated would be reporting less than the file says.
      decode = timestamps == 3 && optional >= 10 ? _Timestamp(data[(_OPTIONAL_HEADER_AT + 5)..]) : presentation;
    }

    header = new(streamId, declared, payload, presentation, decode);
    return true;
  }

  /// <summary>How long the elementary bytes of a packet of this length are, or a negative number for one that holds none.</summary>
  internal static int PayloadLength(in PesHeader header, int gathered)
    => (header.DeclaredLength > 0 ? PREFIX_SIZE + header.DeclaredLength : gathered) - header.PayloadOffset;

  /// <summary>How long the whole packet is, or zero for one whose length was not stated.</summary>
  internal static int PacketLength(in PesHeader header)
    => header.DeclaredLength > 0 ? PREFIX_SIZE + header.DeclaredLength : 0;

  private static bool _HasOptionalHeader(int streamId)
    => streamId switch {
      0xBC => false, // program stream map
      0xBE => false, // padding
      0xBF => false, // private stream 2
      0xF0 => false, // entitlement control messages
      0xF1 => false, // entitlement management messages
      0xF2 => false, // DSM-CC
      0xF8 => false, // ITU-T H.222.1 type E
      0xFF => false, // program stream directory
      _ => true,
    };

  /// <summary>Reads a thirty-three-bit timestamp out of the five bytes it is scattered across.</summary>
  private static long _Timestamp(ReadOnlySpan<byte> data)
    => ((long)(data[0] >> 1) & 0x07) << 30
       | (long)(((data[1] << 8) | data[2]) >> 1) << 15
       | (long)(((data[3] << 8) | data[4]) >> 1);
}
