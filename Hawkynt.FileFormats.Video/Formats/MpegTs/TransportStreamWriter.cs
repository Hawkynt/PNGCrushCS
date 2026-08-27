using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.MpegTs;

/// <summary>Writes a single-program 188-byte MPEG-2 transport stream with PAT, PMT and PES framing.</summary>
public sealed class TransportStreamWriter : IVideoContainerWriter<TransportStreamWriter> {

  private const int _PMT_PID = 0x0100;
  private const int _FIRST_ES_PID = 0x0101;

  private readonly IReadOnlyList<MediaStreamInfo> _streams;
  private readonly int[] _pids;
  private readonly byte[] _streamTypes;
  private readonly byte[] _continuity = new byte[8192];
  private readonly MemoryStream _output = new();
  private bool _finished;

  private TransportStreamWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(metadata);
    if (streams.Count == 0)
      throw new ArgumentException("Transport stream needs at least one elementary stream.", nameof(streams));
    if (_FIRST_ES_PID + streams.Count >= 0x1FFF)
      throw new NotSupportedException("Too many streams for the available transport-stream PID range.");

    this._pids = new int[streams.Count];
    this._streamTypes = new byte[streams.Count];
    for (var i = 0; i < streams.Count; ++i) {
      var stream = streams[i] ?? throw new ArgumentException($"Stream {i} is null.", nameof(streams));
      if (stream.Index != i)
        throw new ArgumentException($"Transport streams must be indexed densely from zero; position {i} has index {stream.Index}.", nameof(streams));
      this._pids[i] = _FIRST_ES_PID + i;
      this._streamTypes[i] = _StreamType(stream);
    }

    this._streams = streams;
    this._WritePsi(TransportPacketScanner.PROGRAM_ASSOCIATION_PID, _Pat());
    this._WritePsi(_PMT_PID, this._Pmt());
  }

  public static string PrimaryExtension => ".ts";
  public static string[] FileExtensions => [".ts", ".m2ts", ".mts", ".m2t", ".tsv"];

  public static TransportStreamWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams, metadata);

  public void WritePacket(CodedPacket packet) {
    if (this._finished)
      throw new InvalidOperationException("Transport-stream writer has already been finished.");
    if ((uint)packet.StreamIndex >= (uint)this._streams.Count)
      throw new ArgumentOutOfRangeException(nameof(packet), packet.StreamIndex, "Packet names no declared transport-stream stream.");

    var stream = this._streams[packet.StreamIndex];
    var pts = packet.PresentationTimestamp is { } p ? ContainerWriterTools.Rescale(p, stream.TimeBase, 90_000) : (long?)null;
    var dts = packet.DecodeTimestamp is { } d ? ContainerWriterTools.Rescale(d, stream.TimeBase, 90_000) : pts;
    var pes = _Pes(stream, packet.Data.Span, pts, dts);
    this._WritePayload(this._pids[packet.StreamIndex], pes, payloadUnitStart: true, randomAccess: packet.IsKeyFrame);
  }

  public byte[] Finish() {
    if (this._finished)
      throw new InvalidOperationException("Transport-stream writer has already been finished.");
    this._finished = true;
    return this._output.ToArray();
  }

  private byte[] _Pmt() {
    var pcrPid = this._pids[0];
    for (var i = 0; i < this._streams.Count; ++i)
      if (this._streams[i].Kind == MediaStreamKind.Video) {
        pcrPid = this._pids[i];
        break;
      }

    using var body = new MemoryStream();
    ContainerWriterTools.WriteUInt16BigEndian(body, 1); // program_number
    body.WriteByte(0xC1); // reserved, version 0, current_next
    body.WriteByte(0);
    body.WriteByte(0);
    ContainerWriterTools.WriteUInt16BigEndian(body, (ushort)(0xE000 | pcrPid));
    ContainerWriterTools.WriteUInt16BigEndian(body, 0xF000);

    for (var i = 0; i < this._streams.Count; ++i) {
      var descriptors = this._streams[i].CodecPrivateData.Span;
      if (descriptors.Length > 0x0FFF)
        throw new NotSupportedException($"PMT descriptor loop for stream {i} exceeds 4095 bytes.");
      body.WriteByte(this._streamTypes[i]);
      ContainerWriterTools.WriteUInt16BigEndian(body, (ushort)(0xE000 | this._pids[i]));
      ContainerWriterTools.WriteUInt16BigEndian(body, (ushort)(0xF000 | descriptors.Length));
      body.Write(descriptors);
    }

    return _LongSection(ProgramTables.PROGRAM_MAP_TABLE, body.ToArray());
  }

  private static byte[] _Pat() {
    using var body = new MemoryStream();
    ContainerWriterTools.WriteUInt16BigEndian(body, 1); // transport_stream_id
    body.WriteByte(0xC1); // version 0, current
    body.WriteByte(0);
    body.WriteByte(0);
    ContainerWriterTools.WriteUInt16BigEndian(body, 1); // program number
    ContainerWriterTools.WriteUInt16BigEndian(body, (ushort)(0xE000 | _PMT_PID));
    return _LongSection(ProgramTables.PROGRAM_ASSOCIATION_TABLE, body.ToArray());
  }

  private static byte[] _LongSection(int tableId, byte[] body) {
    var sectionLength = body.Length + 4;
    if (sectionLength > 0x0FFF)
      throw new NotSupportedException("Transport-stream PSI section exceeds its 12-bit length field.");

    using var section = new MemoryStream();
    section.WriteByte((byte)tableId);
    ContainerWriterTools.WriteUInt16BigEndian(section, (ushort)(0xB000 | sectionLength));
    section.Write(body);
    var withoutCrc = section.ToArray();
    ContainerWriterTools.WriteUInt32BigEndian(section, _MpegCrc(withoutCrc));
    return section.ToArray();
  }

  private void _WritePsi(int pid, byte[] section) {
    var payload = new byte[section.Length + 1];
    section.CopyTo(payload, 1); // pointer_field = 0
    this._WritePayload(pid, payload, payloadUnitStart: true, randomAccess: false);
  }

  private void _WritePayload(int pid, ReadOnlySpan<byte> data, bool payloadUnitStart, bool randomAccess) {
    var at = 0;
    var first = true;
    do {
      var take = Math.Min(184, data.Length - at);
      var needsAdaptation = take < 184 || first && randomAccess;
      var packet = new byte[TransportPacketScanner.PACKET_SIZE];
      Array.Fill(packet, (byte)0xFF);
      packet[0] = TransportPacketScanner.SYNC_BYTE;
      packet[1] = (byte)((pid >> 8) & 0x1F);
      if (first && payloadUnitStart)
        packet[1] |= 0x40;
      packet[2] = (byte)pid;
      packet[3] = (byte)((needsAdaptation ? 0x30 : 0x10) | (this._continuity[pid]++ & 0x0F));

      var body = 4;
      if (needsAdaptation) {
        var adaptationLength = 183 - take;
        packet[body++] = checked((byte)adaptationLength);
        if (adaptationLength > 0) {
          packet[body++] = (byte)(first && randomAccess ? 0x40 : 0);
          body += adaptationLength - 1;
        }
      }

      if (take > 0)
        data.Slice(at, take).CopyTo(packet.AsSpan(body));
      this._output.Write(packet);
      at += take;
      first = false;
    } while (at < data.Length);
  }

  private static byte[] _Pes(MediaStreamInfo stream, ReadOnlySpan<byte> payload, long? pts, long? dts) {
    var streamId = stream.Kind switch {
      MediaStreamKind.Video => (byte)0xE0,
      MediaStreamKind.Audio => (byte)0xC0,
      _ => (byte)0xBD,
    };
    var flags = pts == null ? 0 : dts != pts ? 0xC0 : 0x80;
    var headerLength = flags == 0 ? 0 : flags == 0xC0 ? 10 : 5;
    var declared = 3L + headerLength + payload.Length;
    var pesLength = stream.Kind == MediaStreamKind.Video && declared > ushort.MaxValue ? 0 : checked((ushort)declared);

    return ContainerWriterTools.Build(pes => {
      pes.Write([0x00, 0x00, 0x01, streamId]);
      ContainerWriterTools.WriteUInt16BigEndian(pes, pesLength);
      pes.WriteByte(0x80);
      pes.WriteByte((byte)flags);
      pes.WriteByte((byte)headerLength);
      if (pts != null) {
        _WriteTimestamp(pes, dts != pts ? 3 : 2, pts.Value);
        if (dts != pts)
          _WriteTimestamp(pes, 1, dts!.Value);
      }
      pes.Write(payload);
    });
  }

  private static void _WriteTimestamp(Stream output, int prefix, long timestamp) {
    var value = timestamp & 0x1FFFFFFFFL;
    output.WriteByte((byte)((prefix << 4) | (((value >> 30) & 7) << 1) | 1));
    output.WriteByte((byte)(value >> 22));
    output.WriteByte((byte)((((value >> 15) & 0x7F) << 1) | 1));
    output.WriteByte((byte)(value >> 7));
    output.WriteByte((byte)(((value & 0x7F) << 1) | 1));
  }

  private static byte _StreamType(MediaStreamInfo stream) {
    if (stream.Handler.Value is > 0 and <= 0xFF)
      return (byte)stream.Handler.Value;
    if (_Is(stream, "mpg1", "MPG1")) return 0x01;
    if (_Is(stream, "mpg2", "MPG2")) return 0x02;
    if (_Is(stream, ".mp3", "mpga")) return 0x03;
    if (_Is(stream, "aac ", "mp4a")) return 0x0F;
    if (_Is(stream, "mp4v")) return 0x10;
    if (_Is(stream, "avc1", "H264")) return 0x1B;
    if (_Is(stream, "hvc1", "H265")) return 0x24;
    if (_Is(stream, "vvc1")) return 0x33;
    if (_Is(stream, "ac-3", "dts ") || stream.Kind is MediaStreamKind.Subtitle or MediaStreamKind.Data) return 0x06;
    throw new NotSupportedException($"No MPEG-TS stream_type mapping is known for stream {stream.Index} tagged '{stream.Codec}'.");
  }

  private static bool _Is(MediaStreamInfo stream, params string[] tags) {
    foreach (var tag in tags)
      if (stream.Codec.EqualsIgnoringCase(CodecTag.FromCharacters(tag)))
        return true;
    return false;
  }

  private static uint _MpegCrc(ReadOnlySpan<byte> data) {
    var crc = 0xFFFFFFFFu;
    foreach (var value in data) {
      crc ^= (uint)value << 24;
      for (var bit = 0; bit < 8; ++bit)
        crc = (crc & 0x80000000) != 0 ? (crc << 1) ^ 0x04C11DB7u : crc << 1;
    }
    return crc;
  }
}
