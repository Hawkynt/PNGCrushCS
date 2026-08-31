using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.MpegPs;

/// <summary>Writes an MPEG-2 program stream using bounded PES packets and a pack before each coded packet.</summary>
public sealed class MpegProgramStreamWriter : IVideoContainerWriter<MpegProgramStreamWriter> {

  private readonly record struct StreamAddress(byte StreamId, byte? SubstreamId, int PrivatePrefixLength);
  private readonly record struct StreamMapEntry(byte StreamType, byte StreamId);

  private readonly IReadOnlyList<MediaStreamInfo> _streams;
  private readonly StreamAddress[] _addresses;
  private readonly StreamMapEntry[] _streamMap;
  private readonly MemoryStream _output = new();
  private bool _finished;

  private MpegProgramStreamWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(metadata);
    if (streams.Count == 0)
      throw new ArgumentException("MPEG program stream needs at least one elementary stream.", nameof(streams));

    this._addresses = new StreamAddress[streams.Count];
    var streamMap = new List<StreamMapEntry>(streams.Count);
    var video = 0;
    var audio = 0;
    var ac3 = 0;
    var dts = 0;
    var subpicture = 0;

    for (var i = 0; i < streams.Count; ++i) {
      var stream = streams[i] ?? throw new ArgumentException($"Stream {i} is null.", nameof(streams));
      if (stream.Index != i)
        throw new ArgumentException($"Program-stream streams must be indexed densely from zero; position {i} has index {stream.Index}.", nameof(streams));

      StreamAddress address;
      if (stream.Kind == MediaStreamKind.Video) {
        if (video >= 16)
          throw new NotSupportedException("MPEG program stream has only sixteen standard video stream IDs.");
        address = new((byte)(0xE0 + video++), null, 0);
      } else if (stream.Kind == MediaStreamKind.Audio && _Is(stream, ".mp3", "mpga", "aac ")) {
        if (audio >= 32)
          throw new NotSupportedException("MPEG program stream has only thirty-two standard audio stream IDs.");
        address = new((byte)(0xC0 + audio++), null, 0);
      } else if (stream.Kind == MediaStreamKind.Audio && _Is(stream, "ac-3")) {
        if (ac3 >= 8)
          throw new NotSupportedException("DVD private stream 1 has eight AC-3 substream IDs.");
        address = new(MpegPsScanner.PRIVATE_STREAM_1, (byte)(0x80 + ac3++), 4);
      } else if (stream.Kind == MediaStreamKind.Audio && _Is(stream, "dts ")) {
        if (dts >= 8)
          throw new NotSupportedException("DVD private stream 1 has eight DTS substream IDs.");
        address = new(MpegPsScanner.PRIVATE_STREAM_1, (byte)(0x88 + dts++), 4);
      } else if (stream.Kind == MediaStreamKind.Subtitle) {
        if (subpicture >= 32)
          throw new NotSupportedException("DVD private stream 1 has thirty-two subpicture substream IDs.");
        address = new(MpegPsScanner.PRIVATE_STREAM_1, (byte)(0x20 + subpicture++), 1);
      } else
        throw new NotSupportedException($"MPEG program-stream muxing has no unambiguous stream-id mapping for stream {i} ({stream.Kind}, '{stream.Codec}').");

      this._addresses[i] = address;
      var streamType = _StreamType(stream, address);
      var duplicate = false;
      foreach (var entry in streamMap)
        if (entry.StreamId == address.StreamId) {
          duplicate = true;
          if (entry.StreamType != streamType)
            throw new NotSupportedException($"Program-stream id 0x{address.StreamId:X2} would need conflicting stream types 0x{entry.StreamType:X2} and 0x{streamType:X2}.");
          break;
        }

      if (!duplicate)
        streamMap.Add(new(streamType, address.StreamId));
    }

    this._streams = streams;
    this._streamMap = streamMap.ToArray();

    _WritePack(this._output);
    this._WriteProgramStreamMap();
  }

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".mpg";
  /// <summary>Gets the file extensions supported by this format.</summary>
  public static string[] FileExtensions => [".mpg", ".mpeg", ".vob", ".m2p", ".m2ps"];

  /// <summary>Creates a writer for the specified stream descriptions and metadata.</summary>
  public static MpegProgramStreamWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams, metadata);

  /// <summary>Writes the specified coded packet to the container.</summary>
  public void WritePacket(CodedPacket packet) {
    if (this._finished)
      throw new InvalidOperationException("MPEG program-stream writer has already been finished.");
    if ((uint)packet.StreamIndex >= (uint)this._streams.Count)
      throw new ArgumentOutOfRangeException(nameof(packet), packet.StreamIndex, "Packet names no declared program-stream stream.");

    _WritePack(this._output);

    var stream = this._streams[packet.StreamIndex];
    var address = this._addresses[packet.StreamIndex];
    var pts = packet.PresentationTimestamp is { } p ? ContainerWriterTools.Rescale(p, stream.TimeBase, MpegPsScanner.SYSTEM_CLOCK_HZ) : (long?)null;
    var dts = packet.DecodeTimestamp is { } d ? ContainerWriterTools.Rescale(d, stream.TimeBase, MpegPsScanner.SYSTEM_CLOCK_HZ) : pts;

    var prefixLength = address.PrivatePrefixLength;
    var timestampHeaderLength = pts == null ? 0 : dts != pts ? 10 : 5;
    var pesHeaderLength = 3 + timestampHeaderLength;
    var maxPayload = ushort.MaxValue - pesHeaderLength - prefixLength;

    if (stream.Kind != MediaStreamKind.Video && packet.Data.Length > maxPayload)
      throw new NotSupportedException(
        $"One non-video program-stream packet may carry at most {maxPayload} coded bytes without changing its packet boundary; stream {packet.StreamIndex} supplied {packet.Data.Length}.");

    var data = packet.Data.Span;
    var at = 0;
    do {
      var take = Math.Min(maxPayload, data.Length - at);
      this._WritePes(address, data.Slice(at, take), at == 0 ? pts : null, at == 0 ? dts : null);
      at += take;
      // A zero-length coded packet still becomes one PES packet so its stream is declared.
    } while (at < data.Length);
  }

  /// <summary>Finishes writing the container and returns its encoded bytes.</summary>
  public byte[] Finish() {
    if (this._finished)
      throw new InvalidOperationException("MPEG program-stream writer has already been finished.");
    this._finished = true;
    this._output.Write([0x00, 0x00, 0x01, MpegPsScanner.PROGRAM_END]);
    return this._output.ToArray();
  }

  private void _WriteProgramStreamMap() {
    var elementaryMapLength = checked(4 * this._streamMap.Length);
    var mapLength = checked(10 + elementaryMapLength);
    if (mapLength > 0x03FA)
      throw new NotSupportedException("MPEG program-stream map exceeds its 1018-byte H.222.0 limit.");

    using var map = new MemoryStream(6 + mapLength);
    map.Write([0x00, 0x00, 0x01, MpegPsScanner.PROGRAM_STREAM_MAP]);
    ContainerWriterTools.WriteUInt16BigEndian(map, checked((ushort)mapLength));
    map.WriteByte(0xE0); // current_next=1, reserved=3, version=0
    map.WriteByte(0xFF); // reserved and marker bit
    ContainerWriterTools.WriteUInt16BigEndian(map, 0); // no program descriptors
    ContainerWriterTools.WriteUInt16BigEndian(map, checked((ushort)elementaryMapLength));

    foreach (var entry in this._streamMap) {
      map.WriteByte(entry.StreamType);
      map.WriteByte(entry.StreamId);
      ContainerWriterTools.WriteUInt16BigEndian(map, 0); // no elementary-stream descriptors
    }

    var withoutCrc = map.ToArray();
    ContainerWriterTools.WriteUInt32BigEndian(map, MpegSystemsTools.Crc32(withoutCrc));
    map.Position = 0;
    map.CopyTo(this._output);
  }

  private void _WritePes(StreamAddress address, ReadOnlySpan<byte> payload, long? pts, long? dts) {
    var flags = pts == null ? 0 : dts != pts ? 0xC0 : 0x80;
    var headerDataLength = flags == 0 ? 0 : flags == 0xC0 ? 10 : 5;
    var declared = checked((ushort)(3 + headerDataLength + address.PrivatePrefixLength + payload.Length));

    this._output.Write([0x00, 0x00, 0x01, address.StreamId]);
    ContainerWriterTools.WriteUInt16BigEndian(this._output, declared);
    this._output.WriteByte(0x80);
    this._output.WriteByte((byte)flags);
    this._output.WriteByte((byte)headerDataLength);

    if (pts != null) {
      _WriteTimestamp(this._output, dts != pts ? 0x3 : 0x2, pts.Value);
      if (dts != pts)
        _WriteTimestamp(this._output, 0x1, dts!.Value);
    }

    if (address.SubstreamId is { } substream) {
      this._output.WriteByte(substream);
      if (address.PrivatePrefixLength >= 4) {
        // Number of access-unit headers and pointer to the first one. One packet is supplied here and
        // its first coded byte follows immediately after this four-byte private-stream header.
        this._output.WriteByte(1);
        ContainerWriterTools.WriteUInt16BigEndian(this._output, 1);
      }
      if (address.PrivatePrefixLength == 7)
        this._output.Write([0, 0, 0]);
    }

    this._output.Write(payload);
  }

  private static void _WritePack(Stream output) {
    output.Write([0x00, 0x00, 0x01, MpegPsScanner.PACK_START]);
    // MPEG-2 pack header: SCR=0, SCR extension=0, a small non-zero program_mux_rate, no stuffing.
    // Every marker bit is present; the systems reader therefore sees the canonical 14-byte form.
    output.Write([0x44, 0x00, 0x04, 0x00, 0x04, 0x01, 0x00, 0x00, 0x03, 0xF8]);
  }

  private static void _WriteTimestamp(Stream output, int prefix, long timestamp) {
    var value = timestamp & 0x1FFFFFFFFL;
    output.WriteByte((byte)((prefix << 4) | (((value >> 30) & 7) << 1) | 1));
    output.WriteByte((byte)(value >> 22));
    output.WriteByte((byte)((((value >> 15) & 0x7F) << 1) | 1));
    output.WriteByte((byte)(value >> 7));
    output.WriteByte((byte)(((value & 0x7F) << 1) | 1));
  }

  private static byte _StreamType(MediaStreamInfo stream, StreamAddress address) {
    if (address.StreamId == MpegPsScanner.PRIVATE_STREAM_1)
      return 0x06;

    if (stream.Handler.Value is > 0 and <= 0xFF) {
      var declared = (byte)stream.Handler.Value;
      if (declared == 0x05)
        throw new NotSupportedException("H.222.0 prohibits stream_type 0x05 in a program stream map.");
      return declared;
    }

    if (_Is(stream, "mpg1", "MPG1")) return 0x01;
    if (_Is(stream, "mpg2", "MPG2")) return 0x02;
    if (_Is(stream, ".mp3", "mpga")) return 0x03;
    if (_Is(stream, "aac ", "mp4a")) return 0x0F;
    if (_Is(stream, "mp4v")) return 0x10;
    if (_Is(stream, "avc1", "H264")) return 0x1B;
    if (_Is(stream, "hvc1", "H265")) return 0x24;
    if (_Is(stream, "vvc1")) return 0x33;

    throw new NotSupportedException($"No H.222.0 program-stream stream_type mapping is known for stream {stream.Index} tagged '{stream.Codec}'.");
  }

  private static bool _Is(MediaStreamInfo stream, params string[] tags) {
    foreach (var tag in tags)
      if (stream.Codec.EqualsIgnoringCase(CodecTag.FromCharacters(tag)))
        return true;
    return false;
  }
}
