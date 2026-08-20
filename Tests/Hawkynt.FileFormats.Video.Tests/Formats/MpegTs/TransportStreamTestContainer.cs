using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.MpegTs.Tests;

/// <summary>One elementary stream to be declared in the built file's program map.</summary>
/// <param name="Pid">The PID its packets are on.</param>
/// <param name="StreamType">The number the map names its coding by.</param>
/// <param name="Descriptors">The descriptor loop for it, or empty for none.</param>
internal readonly record struct TsTestStream(int Pid, int StreamType, byte[]? Descriptors = null);

/// <summary>One PES packet to be cut into transport packets.</summary>
/// <param name="Pid">Which stream it belongs to.</param>
/// <param name="Payload">The elementary bytes, which are whatever a test hands over.</param>
/// <param name="PresentationTimestamp">The presentation time to write, or none.</param>
/// <param name="DecodeTimestamp">The decode time to write, or none.</param>
/// <param name="DeclareLength">Whether to state the packet's length. A muxer states it for sound and
/// writes zero for pictures, whose length is not known until they have been coded.</param>
/// <param name="RandomAccess">Whether the first packet's adaptation field says the stream may be
/// entered here.</param>
/// <param name="StreamId">The byte naming the elementary stream inside the PES header.</param>
/// <param name="StatedLength">A length to write instead of the true one, for the case of a unit whose
/// stated length is never reached.</param>
internal readonly record struct TsTestUnit(
  int Pid,
  byte[] Payload,
  long? PresentationTimestamp = null,
  long? DecodeTimestamp = null,
  bool DeclareLength = false,
  bool RandomAccess = false,
  int StreamId = 0xE0,
  int? StatedLength = null);

/// <summary>
/// Builds transport streams byte by byte so the reader can be tested without a sample in the tree.
/// </summary>
/// <remarks>
/// The layout is the one ffmpeg writes, read off a hexdump of its own output: a service description,
/// a program association table naming one program, a program map on the PID that table names, and
/// then the units cut into 188-byte packets with an adaptation field wherever one is needed to
/// signal random access or to pad the last packet of a unit out to the full length.
/// <para/>
/// It exists for the shapes ffmpeg will not produce on demand: a lost packet, a file that stops in
/// the middle of a unit, a scrambled packet, a section longer than one transport packet, a timestamp
/// near the top of its thirty-three bits. Every one of those is a branch of the reader, and the files
/// ffmpeg does write are measured against ffprobe separately.
/// <para/>
/// Nothing here is a valid picture or a valid sound. The payloads are whatever bytes a test hands
/// over, which is all a demuxer ever looks at.
/// </remarks>
internal static class TransportStreamTestContainer {

  internal const int PACKET_SIZE = 188;
  internal const int PROGRAM_MAP_PID = 0x1000;
  internal const int PROGRAM_NUMBER = 1;

  /// <summary>The payload of a transport packet that carries no adaptation field.</summary>
  private const int _FULL_PAYLOAD = PACKET_SIZE - 4;

  /// <summary>Writes a file out of the streams and units given.</summary>
  /// <param name="streams">The streams to declare, in the order the program map declares them.</param>
  /// <param name="units">The PES packets, in the order they go into the file.</param>
  /// <param name="stride">188 for a plain file, 192 for one with an arrival timecode per packet.</param>
  /// <param name="serviceName">The DVB service name to write, or null to write no service description.</param>
  /// <param name="serviceProvider">The provider name that goes beside it.</param>
  /// <param name="withProgramMap">Whether to write the program map at all.</param>
  /// <param name="programInfo">The program-level descriptor loop, or null for none.</param>
  internal static byte[] Build(
    IReadOnlyList<TsTestStream> streams,
    IEnumerable<TsTestUnit> units,
    int stride = PACKET_SIZE,
    string? serviceName = null,
    string? serviceProvider = null,
    bool withProgramMap = true,
    byte[]? programInfo = null) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(units);

    using var file = new MemoryStream();
    var counters = new Dictionary<int, int>();

    if (serviceName != null)
      _Write(file, stride, _SectionPackets(0x0011, _ServiceDescription(serviceName, serviceProvider ?? string.Empty), counters));

    _Write(file, stride, _SectionPackets(0x0000, _ProgramAssociation(), counters));

    if (withProgramMap)
      _Write(file, stride, _SectionPackets(PROGRAM_MAP_PID, _ProgramMap(streams, programInfo), counters));

    foreach (var unit in units)
      _Write(file, stride, _UnitPackets(unit, counters));

    return file.ToArray();
  }

  /// <summary>Removes one whole transport packet, which is what a lost packet in a broadcast leaves behind.</summary>
  internal static byte[] Drop(byte[] file, int index, int stride = PACKET_SIZE) {
    var at = index * stride;
    var result = new byte[file.Length - stride];
    Array.Copy(file, 0, result, 0, at);
    Array.Copy(file, at + stride, result, at, file.Length - at - stride);
    return result;
  }

  /// <summary>Where a packet of a PID is, counted in packets, for a test that wants to patch one.</summary>
  internal static int IndexOf(byte[] file, int pid, int which = 0, int stride = PACKET_SIZE, int offset = 0) {
    var found = 0;
    for (var index = 0; offset + index * stride + PACKET_SIZE <= file.Length; ++index) {
      var at = offset + index * stride;
      if ((((file[at + 1] & 0x1F) << 8) | file[at + 2]) != pid)
        continue;

      if (found++ == which)
        return index;
    }

    throw new InvalidOperationException($"no packet {which} of PID {pid} in the built file");
  }

  // ------------------------------------------------------------------------------------------
  // Tables
  // ------------------------------------------------------------------------------------------

  private static byte[] _ProgramAssociation() {
    using var body = new MemoryStream();
    body.WriteByte(PROGRAM_NUMBER >> 8);
    body.WriteByte(PROGRAM_NUMBER & 0xFF);
    body.WriteByte((byte)(0xE0 | (PROGRAM_MAP_PID >> 8)));
    body.WriteByte(PROGRAM_MAP_PID & 0xFF);

    return _Section(0x00, PROGRAM_NUMBER, body.ToArray());
  }

  private static byte[] _ProgramMap(IReadOnlyList<TsTestStream> streams, byte[]? programInfo) {
    var info = programInfo ?? [];

    using var body = new MemoryStream();
    body.WriteByte((byte)(0xE0 | (streams.Count > 0 ? streams[0].Pid >> 8 : 0)));
    body.WriteByte((byte)(streams.Count > 0 ? streams[0].Pid & 0xFF : 0));
    body.WriteByte((byte)(0xF0 | (info.Length >> 8)));
    body.WriteByte((byte)(info.Length & 0xFF));
    body.Write(info, 0, info.Length);

    foreach (var stream in streams) {
      var descriptors = stream.Descriptors ?? [];
      body.WriteByte((byte)stream.StreamType);
      body.WriteByte((byte)(0xE0 | (stream.Pid >> 8)));
      body.WriteByte((byte)(stream.Pid & 0xFF));
      body.WriteByte((byte)(0xF0 | (descriptors.Length >> 8)));
      body.WriteByte((byte)(descriptors.Length & 0xFF));
      body.Write(descriptors, 0, descriptors.Length);
    }

    return _Section(0x02, PROGRAM_NUMBER, body.ToArray());
  }

  private static byte[] _ServiceDescription(string name, string provider) {
    var nameBytes = Encoding.Latin1.GetBytes(name);
    var providerBytes = Encoding.Latin1.GetBytes(provider);

    using var descriptor = new MemoryStream();
    descriptor.WriteByte(0x01); // digital television
    descriptor.WriteByte((byte)providerBytes.Length);
    descriptor.Write(providerBytes, 0, providerBytes.Length);
    descriptor.WriteByte((byte)nameBytes.Length);
    descriptor.Write(nameBytes, 0, nameBytes.Length);

    var body = new MemoryStream();
    body.WriteByte(0xFF); // original network id, high
    body.WriteByte(0x01); // original network id, low
    body.WriteByte(0xFF); // reserved

    var loop = 2 + descriptor.Length;
    body.WriteByte(0x00); // service id, high
    body.WriteByte(0x01); // service id, low
    body.WriteByte(0xFC); // EIT flags
    body.WriteByte((byte)(0x80 | ((int)loop >> 8)));
    body.WriteByte((byte)(loop & 0xFF));
    body.WriteByte(0x48); // service descriptor
    body.WriteByte((byte)descriptor.Length);
    body.Write(descriptor.ToArray(), 0, (int)descriptor.Length);

    return _Section(0x42, 1, body.ToArray());
  }

  /// <summary>Wraps a table body in the long-form section header every table here uses, and its CRC.</summary>
  private static byte[] _Section(int tableId, int extension, byte[] body) {
    // The length counts everything after itself, which is the five bytes of the rest of the header,
    // the body, and the four of CRC.
    var length = 5 + body.Length + 4;

    using var section = new MemoryStream();
    section.WriteByte((byte)tableId);
    section.WriteByte((byte)(0xB0 | (length >> 8)));
    section.WriteByte((byte)(length & 0xFF));
    section.WriteByte((byte)(extension >> 8));
    section.WriteByte((byte)(extension & 0xFF));
    section.WriteByte(0xC1); // version 0, current
    section.WriteByte(0x00); // section number
    section.WriteByte(0x00); // last section number
    section.Write(body, 0, body.Length);

    var crc = _Crc32(section.ToArray());
    section.WriteByte((byte)(crc >> 24));
    section.WriteByte((byte)(crc >> 16));
    section.WriteByte((byte)(crc >> 8));
    section.WriteByte((byte)crc);

    return section.ToArray();
  }

  /// <summary>MPEG's CRC-32: Ethernet's polynomial with no reflection at either end and no final inversion.</summary>
  private static uint _Crc32(byte[] data) {
    var crc = 0xFFFFFFFFu;
    foreach (var value in data) {
      crc ^= (uint)value << 24;
      for (var bit = 0; bit < 8; ++bit)
        crc = (crc & 0x80000000u) != 0 ? (crc << 1) ^ 0x04C11DB7u : crc << 1;
    }

    return crc;
  }

  // ------------------------------------------------------------------------------------------
  // Packets
  // ------------------------------------------------------------------------------------------

  private static IEnumerable<byte[]> _SectionPackets(int pid, byte[] section, Dictionary<int, int> counters) {
    // A pointer field of zero: the section starts immediately. The rest of the last packet is padded
    // with the 0xFF a writer stuffs a section packet with.
    var payload = new byte[1 + section.Length];
    section.CopyTo(payload, 1);

    var at = 0;
    var first = true;
    while (at < payload.Length) {
      var take = Math.Min(_FULL_PAYLOAD, payload.Length - at);
      var packet = _Packet(pid, first, counters, hasPayload: true);
      Array.Copy(payload, at, packet, 4, take);
      for (var i = 4 + take; i < PACKET_SIZE; ++i)
        packet[i] = 0xFF;

      yield return packet;
      at += take;
      first = false;
    }
  }

  private static IEnumerable<byte[]> _UnitPackets(TsTestUnit unit, Dictionary<int, int> counters) {
    var pes = _Pes(unit);

    var at = 0;
    var first = true;
    while (at < pes.Length) {
      var wantsField = first && unit.RandomAccess;

      // An adaptation field costs its own length byte plus, once it holds anything, a flags byte. The
      // last packet of a unit is padded out to the full 188 with one whatever else it carries.
      var room = wantsField ? _FULL_PAYLOAD - 2 : _FULL_PAYLOAD;
      var take = Math.Min(room, pes.Length - at);
      var fieldLength = _FULL_PAYLOAD - 1 - take;
      var withField = wantsField || fieldLength > 0;

      var packet = _Packet(unit.Pid, first, counters, hasPayload: true, withField);
      var body = 4;
      if (withField) {
        packet[body++] = (byte)fieldLength;
        if (fieldLength > 0) {
          packet[body++] = (byte)(unit.RandomAccess && first ? 0x40 : 0x00);
          for (var i = 0; i < fieldLength - 1; ++i)
            packet[body + i] = 0xFF;

          body += fieldLength - 1;
        }
      }

      Array.Copy(pes, at, packet, body, take);
      yield return packet;

      at += take;
      first = false;
    }
  }

  private static byte[] _Pes(TsTestUnit unit) {
    var flags = unit.PresentationTimestamp == null ? 0 : unit.DecodeTimestamp == null ? 2 : 3;
    var optional = flags switch { 3 => 10, 2 => 5, _ => 0 };

    using var pes = new MemoryStream();
    pes.WriteByte(0x00);
    pes.WriteByte(0x00);
    pes.WriteByte(0x01);
    pes.WriteByte((byte)unit.StreamId);

    var declared = unit.StatedLength ?? (unit.DeclareLength ? 3 + optional + unit.Payload.Length : 0);
    pes.WriteByte((byte)(declared >> 8));
    pes.WriteByte((byte)(declared & 0xFF));
    pes.WriteByte(0x80);
    pes.WriteByte((byte)(flags << 6));
    pes.WriteByte((byte)optional);

    if (flags >= 2)
      _WriteTimestamp(pes, flags == 3 ? 0x03 : 0x02, unit.PresentationTimestamp!.Value);
    if (flags == 3)
      _WriteTimestamp(pes, 0x01, unit.DecodeTimestamp!.Value);

    pes.Write(unit.Payload, 0, unit.Payload.Length);
    return pes.ToArray();
  }

  /// <summary>Writes a thirty-three-bit timestamp scattered across five bytes with a marker bit in each.</summary>
  private static void _WriteTimestamp(Stream into, int prefix, long value) {
    into.WriteByte((byte)((prefix << 4) | (int)((value >> 29) & 0x0E) | 0x01));
    into.WriteByte((byte)((value >> 22) & 0xFF));
    into.WriteByte((byte)(((value >> 14) & 0xFE) | 0x01));
    into.WriteByte((byte)((value >> 7) & 0xFF));
    into.WriteByte((byte)(((value << 1) & 0xFE) | 0x01));
  }

  private static byte[] _Packet(int pid, bool start, Dictionary<int, int> counters, bool hasPayload, bool withField = false) {
    var packet = new byte[PACKET_SIZE];
    packet[0] = 0x47;
    packet[1] = (byte)((start ? 0x40 : 0x00) | (pid >> 8));
    packet[2] = (byte)(pid & 0xFF);

    counters.TryGetValue(pid, out var counter);
    packet[3] = (byte)(((withField ? 0x02 : 0x00) | (hasPayload ? 0x01 : 0x00)) << 4 | (counter & 0x0F));

    // The counter advances for packets that carry payload and stands still for the ones that do not.
    if (hasPayload)
      counters[pid] = (counter + 1) & 0x0F;

    return packet;
  }

  private static void _Write(Stream file, int stride, IEnumerable<byte[]> packets) {
    foreach (var packet in packets) {
      // The four bytes Blu-ray and AVCHD put in front of every packet: a two-bit copy indicator and a
      // thirty-bit arrival time. Nothing reads them; they are here to move the sync byte.
      for (var i = 0; i < stride - PACKET_SIZE; ++i)
        file.WriteByte((byte)(0x30 + i));

      file.Write(packet, 0, packet.Length);
    }
  }
}
