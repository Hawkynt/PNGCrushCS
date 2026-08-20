using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.MpegTs;

/// <summary>One transport packet as the scanner found it.</summary>
/// <param name="Pid">Which stream of the multiplex this packet belongs to.</param>
/// <param name="PayloadUnitStart">Whether a PES packet or a table section begins in this packet.</param>
/// <param name="TransportError">Whether whoever handed the packet over knows it is corrupt.</param>
/// <param name="Scrambling">Nought for a packet in the clear; anything else names a key.</param>
/// <param name="ContinuityCounter">The four-bit counter that says whether a packet went missing.</param>
/// <param name="HasPayload">Whether anything of the stream is in this packet at all.</param>
/// <param name="Discontinuity">Whether the writer states that the counter and the clock jump here.</param>
/// <param name="RandomAccess">Whether the elementary stream may be entered at this packet.</param>
/// <param name="Payload">The bytes after the header and the adaptation field, as a window onto the file.</param>
/// <param name="Offset">Where the packet begins, counted from the start of the file.</param>
internal readonly record struct TransportPacket(
  int Pid,
  bool PayloadUnitStart,
  bool TransportError,
  int Scrambling,
  int ContinuityCounter,
  bool HasPayload,
  bool Discontinuity,
  bool RandomAccess,
  ReadOnlyMemory<byte> Payload,
  int Offset);

/// <summary>
/// Walks a transport stream's packets, whichever of the two ways round the file happens to be
/// framed.
/// </summary>
/// <remarks>
/// A transport packet is 188 bytes beginning with <c>0x47</c>, and that is the whole of the framing —
/// there is no length field anywhere, because the format was designed to be recovered from part way
/// through a broadcast. What makes a file of them ambiguous is that Blu-ray and AVCHD put a four-byte
/// arrival timecode in front of every one, so the packets are 188 bytes long inside a stride of 192.
/// The stride is therefore measured rather than assumed: a reader that took 188 for granted would
/// read a <c>.m2ts</c> as a stream whose sync byte is missing from the second packet onwards, and one
/// that took 192 would do the same to an ordinary <c>.ts</c>.
/// <para/>
/// The adaptation field is skipped rather than read as payload, which is the other thing that must be
/// right before anything else can be. It is a length byte followed by that many bytes, and the two
/// fields worth having are in the first of them: the discontinuity indicator, which says the counter
/// is about to jump on purpose, and the random access indicator, which says the elementary stream may
/// be entered here. The program clock reference sits behind them and is stepped over — it is the
/// program's clock rather than any packet's timestamp, a packet's own timestamps are in its PES
/// header and are counted in the same 90 kHz units, and there is nowhere in the model for a clock.
/// </remarks>
internal static class TransportPacketScanner {

  /// <summary>The length of a transport packet, which is the same in every framing.</summary>
  internal const int PACKET_SIZE = 188;

  /// <summary>The byte every transport packet begins with.</summary>
  internal const byte SYNC_BYTE = 0x47;

  /// <summary>The PID of the program association table, which is fixed by the standard.</summary>
  internal const int PROGRAM_ASSOCIATION_PID = 0x0000;

  /// <summary>The PID of DVB's service description table.</summary>
  internal const int SERVICE_DESCRIPTION_PID = 0x0011;

  /// <summary>The PID of a packet that carries nothing and exists to fill a constant bit rate.</summary>
  internal const int NULL_PID = 0x1FFF;

  /// <summary>The strides a file of transport packets is written with, in the order they are tried.</summary>
  /// <remarks>
  /// 188 is the packet on its own; 192 is the packet behind the four-byte arrival timecode Blu-ray
  /// and AVCHD write. 188 is tried first because it is the plain case, and a file of the other kind
  /// cannot be mistaken for it: agreeing with 188 would need every 188th byte from the chosen offset
  /// to be 0x47 through a timecode that changes with every packet.
  /// </remarks>
  private static readonly int[] _STRIDES = [188, 192];

  /// <summary>How many packets in a row must begin with the sync byte before a framing is believed.</summary>
  /// <remarks>
  /// Four, which puts a coincidence at one in sixteen million and is reachable in a file of 752
  /// bytes. It matters that this is more than one: a GIF begins with the letter <c>G</c>, which is
  /// the sync byte, and claiming every file that starts with one would claim a good deal that is not
  /// this format. Whether the framing holds all the way to the end is not asked here — it is enforced
  /// packet by packet by the walk, which refuses by name at the first packet that is not where the
  /// framing says it should be.
  /// </remarks>
  private const int _SYNCS_WANTED = 4;

  /// <summary>Finds how the file is framed: the distance between packets, and where the first one starts.</summary>
  /// <exception cref="InvalidDataException">Nothing in the file is laid out as transport packets.</exception>
  internal static (int Stride, int Offset) Layout(ReadOnlySpan<byte> data) {
    foreach (var stride in _STRIDES)
      for (var offset = 0; offset < stride && offset + PACKET_SIZE <= data.Length; ++offset)
        if (_Syncs(data, stride, offset) >= Math.Min(_SYNCS_WANTED, _Packets(data.Length, stride, offset)))
          return (stride, offset);

    throw new InvalidDataException(
      $"No run of packets beginning with 0x{SYNC_BYTE:X2} was found at a stride of {string.Join(" or ", _STRIDES)} bytes, "
      + "so this is not a transport stream in either of the framings there are.");
  }

  /// <summary>Walks the packets of a file, in the order they are stored.</summary>
  /// <exception cref="InvalidDataException">A packet is not where the framing says it should be, or
  /// its adaptation field runs past the end of it.</exception>
  internal static IEnumerable<TransportPacket> Walk(ReadOnlyMemory<byte> file, int stride, int offset) {
    while (offset + PACKET_SIZE <= file.Length) {
      yield return _Read(file, offset);

      offset += stride;
    }
  }

  private static int _Packets(int length, int stride, int offset) => (length - offset + stride - PACKET_SIZE) / stride;

  private static int _Syncs(ReadOnlySpan<byte> data, int stride, int offset) {
    var found = 0;
    for (var at = offset; at + PACKET_SIZE <= data.Length; at += stride) {
      if (data[at] != SYNC_BYTE)
        break;

      ++found;
      if (found >= _SYNCS_WANTED)
        break;
    }

    return found;
  }

  // A span cannot be a local of an iterator method, so a packet is read behind a call.
  private static TransportPacket _Read(ReadOnlyMemory<byte> file, int at) {
    var span = file.Span;
    if (span[at] != SYNC_BYTE)
      throw new InvalidDataException(
        $"The packet at offset {at} begins with 0x{span[at]:X2} rather than the sync byte 0x{SYNC_BYTE:X2}, "
        + "so the framing this file was read with does not hold here.");

    var pid = ((span[at + 1] & 0x1F) << 8) | span[at + 2];
    var error = (span[at + 1] & 0x80) != 0;
    var start = (span[at + 1] & 0x40) != 0;
    var scrambling = (span[at + 3] >> 6) & 0x03;

    // Two bits saying which of the two things follow the header: 1 is payload only, 2 is an
    // adaptation field only, 3 is an adaptation field and then payload. 0 is reserved and means
    // neither, so such a packet carries nothing at all.
    var control = (span[at + 3] >> 4) & 0x03;
    var counter = span[at + 3] & 0x0F;

    var body = at + 4;
    var end = at + PACKET_SIZE;
    var discontinuity = false;
    var randomAccess = false;

    if ((control & 0x02) != 0) {
      // The length byte does not count itself, so a field of length zero is one byte of nothing —
      // which is what a writer emits to pad a packet by exactly one byte.
      var length = span[body];
      ++body;

      if (length > 0) {
        var flags = span[body];
        discontinuity = (flags & 0x80) != 0;
        randomAccess = (flags & 0x40) != 0;
      }

      body += length;
      if (body > end)
        throw new InvalidDataException(
          $"The adaptation field of the packet at offset {at} states {length} bytes, which runs past the end of the {PACKET_SIZE}-byte packet.");
    }

    var hasPayload = (control & 0x01) != 0;
    return new(
      pid, start, error, scrambling, counter, hasPayload, discontinuity, randomAccess,
      hasPayload ? file[body..end] : ReadOnlyMemory<byte>.Empty,
      at);
  }
}
