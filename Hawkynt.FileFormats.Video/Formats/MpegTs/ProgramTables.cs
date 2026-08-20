using System;
using System.Collections.Generic;
using System.Text;

namespace FileFormat.MpegTs;

/// <summary>One elementary stream as the program map table describes it.</summary>
/// <param name="Pid">Which PID of the multiplex carries it.</param>
/// <param name="StreamType">The number the table names its coding by.</param>
/// <param name="Descriptors">The descriptors the table carries for it, verbatim.</param>
internal readonly record struct ElementaryStream(int Pid, int StreamType, ReadOnlyMemory<byte> Descriptors);

/// <summary>
/// Gathers the tables a transport stream describes itself with, out of the packets that carry them.
/// </summary>
/// <remarks>
/// A section is not a packet. It has a length of its own, may be longer than the 184 bytes a packet
/// has room for, and begins at a pointer stated in the first byte of the packet that starts it —
/// which exists so a section may begin part way through a packet, behind the tail of the one before.
/// A reader that took a section to be a packet's payload would be right about the small tables and
/// wrong about a program map describing more than about twenty streams.
/// <para/>
/// Every section ends with a CRC over itself, and one whose CRC does not check out is dropped rather
/// than read. That is not defensive programming: the tables are repeated every hundred milliseconds
/// or so precisely because a broadcast loses packets, and a section assembled across a loss is bytes
/// from two different copies of the table. Dropping it costs nothing because the next copy is a tenth
/// of a second away, and reading it would invent streams.
/// </remarks>
internal static class ProgramTables {

  internal const int PROGRAM_ASSOCIATION_TABLE = 0x00;
  internal const int PROGRAM_MAP_TABLE = 0x02;
  internal const int SERVICE_DESCRIPTION_TABLE = 0x42;

  /// <summary>
  /// Where the body of a long-form section begins.
  /// </summary>
  /// <remarks>
  /// A table id, two bytes carrying the syntax indicator and a twelve-bit length, two bytes of
  /// whatever the table numbers itself by, and three of version and section numbering. Eight in all,
  /// and the same eight for every long-form table there is.
  /// </remarks>
  private const int _SECTION_BODY_AT = 8;

  private const int _CRC_SIZE = 4;

  /// <summary>The descriptor naming the language a stream is in.</summary>
  private const int _LANGUAGE_DESCRIPTOR = 0x0A;

  /// <summary>The descriptor naming a coding by a four-character code where the stream type does not.</summary>
  private const int _REGISTRATION_DESCRIPTOR = 0x05;

  private const int _AC3_DESCRIPTOR = 0x6A;
  private const int _ENHANCED_AC3_DESCRIPTOR = 0x7A;
  private const int _TELETEXT_DESCRIPTOR = 0x56;
  private const int _SUBTITLING_DESCRIPTOR = 0x59;
  private const int _SERVICE_DESCRIPTOR = 0x48;

  /// <summary>
  /// Gathers the sections arriving on one PID, answering each complete one exactly once.
  /// </summary>
  /// <remarks>
  /// One of these per PID that carries tables. It holds the bytes of the section being assembled and
  /// nothing else; a caller decides what to do with a completed one.
  /// </remarks>
  internal sealed class Assembler {

    private readonly List<byte> _section = [];
    private int _wanted;

    /// <summary>Feeds one packet in, answering the section it completed where it completed one.</summary>
    internal byte[]? Accept(in TransportPacket packet) {
      var payload = packet.Payload.Span;
      if (payload.IsEmpty)
        return null;

      var at = 0;
      if (packet.PayloadUnitStart) {
        // The pointer says how many bytes of the previous section come first. Anything this reader
        // was assembling and did not finish before it is a section the stream stopped sending.
        var pointer = payload[0];
        at = 1 + pointer;
        if (at > payload.Length)
          return null;

        this._section.Clear();
        this._wanted = 0;
      } else if (this._section.Count == 0) {
        // The middle of a section whose beginning was never seen, which is what the first packets of
        // a recording that started mid-broadcast look like.
        return null;
      }

      for (; at < payload.Length; ++at) {
        // Table id 0xFF is the stuffing a writer pads the rest of a packet with once its section has
        // ended. It is not a table and there is nothing after it in this packet.
        if (this._section.Count == 0 && payload[at] == 0xFF)
          return null;

        this._section.Add(payload[at]);

        if (this._section.Count == 3)
          this._wanted = 3 + (((this._section[1] & 0x0F) << 8) | this._section[2]);

        if (this._wanted > 0 && this._section.Count == this._wanted) {
          var complete = this._section.ToArray();
          this._section.Clear();
          this._wanted = 0;
          return _Checks(complete) ? complete : null;
        }
      }

      return null;
    }
  }

  /// <summary>The program numbers and the PIDs their program map tables are on.</summary>
  /// <remarks>
  /// Program number zero is not a program: it names the PID of the network information table, which
  /// describes the transmission rather than anything in it. Taking it for a program would send the
  /// reader looking for a program map on a PID that carries something else entirely.
  /// </remarks>
  internal static IEnumerable<(int Program, int Pid)> ProgramMapPids(byte[] section) {
    for (var at = _SECTION_BODY_AT; at + 4 <= section.Length - _CRC_SIZE; at += 4) {
      var program = (section[at] << 8) | section[at + 1];
      if (program == 0)
        continue;

      yield return (program, ((section[at + 2] & 0x1F) << 8) | section[at + 3]);
    }
  }

  /// <summary>The elementary streams a program map table describes, in the order it describes them.</summary>
  internal static IReadOnlyList<ElementaryStream> ElementaryStreams(byte[] section) {
    var result = new List<ElementaryStream>();

    var at = _SECTION_BODY_AT;
    var end = section.Length - _CRC_SIZE;

    // The PCR PID and then the descriptors that describe the program as a whole, neither of which
    // names a stream: the first is the clock and the second is about the program.
    if (at + 4 > end)
      return result;

    at += 2;
    var programInfo = ((section[at] & 0x0F) << 8) | section[at + 1];
    at += 2 + programInfo;

    while (at + 5 <= end) {
      var streamType = section[at];
      var pid = ((section[at + 1] & 0x1F) << 8) | section[at + 2];
      var length = ((section[at + 3] & 0x0F) << 8) | section[at + 4];
      at += 5;

      if (at + length > end)
        break;

      result.Add(new(pid, streamType, new ReadOnlyMemory<byte>(section, at, length)));
      at += length;
    }

    return result;
  }

  /// <summary>The service name and its provider, out of DVB's service description table.</summary>
  /// <remarks>
  /// The only thing in a transport stream that is a title. It is per service rather than per file,
  /// and a multiplex carrying several would have several — the first is taken, which for a recording
  /// of one channel is the channel it is a recording of.
  /// </remarks>
  internal static (string? Name, string? Provider) Service(byte[] section) {
    // Beyond the long-form header: two bytes of original network id and one reserved, then the
    // services.
    var at = _SECTION_BODY_AT + 3;
    var end = section.Length - _CRC_SIZE;

    while (at + 5 <= end) {
      var descriptors = ((section[at + 3] & 0x0F) << 8) | section[at + 4];
      at += 5;
      if (at + descriptors > end)
        break;

      foreach (var (tag, body) in Descriptors(new ReadOnlyMemory<byte>(section, at, descriptors))) {
        if (tag != _SERVICE_DESCRIPTOR)
          continue;

        var span = body.Span;
        if (span.Length < 2)
          continue;

        // One byte of service type, then two counted strings: the provider and then the name.
        var providerLength = span[1];
        if (2 + providerLength >= span.Length)
          continue;

        var nameLength = span[2 + providerLength];
        if (3 + providerLength + nameLength > span.Length)
          continue;

        return (
          _Text(span.Slice(3 + providerLength, nameLength)),
          _Text(span.Slice(2, providerLength)));
      }

      at += descriptors;
    }

    return (null, null);
  }

  /// <summary>Walks a descriptor loop, which is a tag, a length and that many bytes, repeated.</summary>
  internal static IEnumerable<(int Tag, ReadOnlyMemory<byte> Body)> Descriptors(ReadOnlyMemory<byte> loop) {
    var at = 0;
    while (at + 2 <= loop.Length) {
      var tag = loop.Span[at];
      var length = loop.Span[at + 1];
      at += 2;
      if (at + length > loop.Length)
        yield break;

      yield return (tag, loop.Slice(at, length));
      at += length;
    }
  }

  /// <summary>The RFC 5646 language tag a stream's descriptors state, where they state one.</summary>
  internal static string? Language(ReadOnlyMemory<byte> descriptors) {
    foreach (var (tag, body) in Descriptors(descriptors)) {
      if (tag != _LANGUAGE_DESCRIPTOR || body.Length < 3)
        continue;

      var span = body.Span;
      for (var i = 0; i < 3; ++i)
        if (span[i] is < (byte)'a' or > (byte)'z')
          return null;

      return Encoding.ASCII.GetString(span[..3]);
    }

    return null;
  }

  /// <summary>The four-character code a registration descriptor names the coding by, or none.</summary>
  /// <remarks>
  /// What a stream type of 0x06 — "private data in PES packets" — is decided by. The number says
  /// nothing at all about the coding, and the code in this descriptor is what a muxer writes to say
  /// what it really is: <c>AC-3</c>, <c>HEVC</c>, <c>AV01</c>, <c>Opus</c>.
  /// </remarks>
  internal static uint Registration(ReadOnlyMemory<byte> descriptors) {
    foreach (var (tag, body) in Descriptors(descriptors)) {
      if (tag != _REGISTRATION_DESCRIPTOR || body.Length < 4)
        continue;

      var span = body.Span;
      return span[0] | ((uint)span[1] << 8) | ((uint)span[2] << 16) | ((uint)span[3] << 24);
    }

    return 0;
  }

  /// <summary>Whether a private stream's descriptors say it is sound, and whether they say it is text.</summary>
  internal static (bool Audio, bool Subtitle) PrivateKind(ReadOnlyMemory<byte> descriptors) {
    var audio = false;
    var subtitle = false;

    foreach (var (tag, _) in Descriptors(descriptors))
      switch (tag) {
        case _AC3_DESCRIPTOR:
        case _ENHANCED_AC3_DESCRIPTOR:
          audio = true;
          break;
        case _TELETEXT_DESCRIPTOR:
        case _SUBTITLING_DESCRIPTOR:
          subtitle = true;
          break;
      }

    return (audio, subtitle);
  }

  /// <summary>
  /// Whether a section's CRC checks out.
  /// </summary>
  /// <remarks>
  /// MPEG's CRC-32 rather than the one everything else uses: the same polynomial as Ethernet's but
  /// with the bits the other way up, no reflection at either end and no final inversion. Run over the
  /// section including its own CRC it leaves zero, which is what is checked here — a section shorter
  /// than a CRC cannot be one and is refused before the loop rather than by it.
  /// <para/>
  /// A short-form section has no CRC at all. Only the tables read here are checked, and all three of
  /// them are long-form, which their second byte states.
  /// </remarks>
  private static bool _Checks(byte[] section) {
    if (section.Length < 3 + _CRC_SIZE)
      return false;

    // The section syntax indicator; a section without it ends where its length says and carries no CRC.
    if ((section[1] & 0x80) == 0)
      return true;

    var crc = 0xFFFFFFFFu;
    foreach (var value in section) {
      crc ^= (uint)value << 24;
      for (var bit = 0; bit < 8; ++bit)
        crc = (crc & 0x80000000u) != 0 ? (crc << 1) ^ 0x04C11DB7u : crc << 1;
    }

    return crc == 0;
  }

  /// <summary>Reads a DVB text string, which is Latin-1 unless its first byte says otherwise.</summary>
  /// <remarks>
  /// A byte below 0x20 at the front is a character table selector rather than a character. Only the
  /// default table is read; a string announcing another is left unstated rather than decoded as
  /// Latin-1, because the wrong table gives a name that looks like a name and is not one.
  /// </remarks>
  private static string? _Text(ReadOnlySpan<byte> data) {
    if (data.IsEmpty)
      return null;

    if (data[0] < 0x20)
      return null;

    return Encoding.Latin1.GetString(data).Trim() is { Length: > 0 } text ? text : null;
  }
}
