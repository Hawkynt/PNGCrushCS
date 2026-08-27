using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.RealMedia;

/// <summary>Writes RealMedia chunks and version-zero packets, preserving RealVideo slice boundaries.</summary>
public sealed class RealMediaWriter : IVideoContainerWriter<RealMediaWriter> {

  private const int _PACKET_HEADER = 12;
  private const int _MAX_PACKET_LENGTH = ushort.MaxValue;
  private const int _MAX_WHOLE_VIDEO = _MAX_PACKET_LENGTH - _PACKET_HEADER - 2;
  private const int _MAX_AUDIO_PAYLOAD = _MAX_PACKET_LENGTH - _PACKET_HEADER;

  private readonly IReadOnlyList<MediaStreamInfo> _streams;
  private readonly VideoMetadata _metadata;
  private readonly List<CodedPacket> _packets = [];
  private bool _finished;

  private RealMediaWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(metadata);
    if (streams.Count == 0 || streams.Count > ushort.MaxValue)
      throw new NotSupportedException("RealMedia needs at least one stream and uses 16-bit stream numbers.");

    for (var i = 0; i < streams.Count; ++i) {
      var stream = streams[i] ?? throw new ArgumentException($"Stream {i} is null.", nameof(streams));
      if (stream.Index != i)
        throw new ArgumentException($"RealMedia streams must be indexed densely from zero; position {i} has index {stream.Index}.", nameof(streams));
      _ValidateStream(stream);
    }

    this._streams = streams;
    this._metadata = metadata;
  }

  public static string PrimaryExtension => ".rm";
  public static string[] FileExtensions => [".rm", ".rmvb", ".ra", ".rmj", ".rms"];

  public static RealMediaWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams, metadata);

  public void WritePacket(CodedPacket packet) {
    if (this._finished)
      throw new InvalidOperationException("RealMedia writer has already been finished.");
    if ((uint)packet.StreamIndex >= (uint)this._streams.Count)
      throw new ArgumentOutOfRangeException(nameof(packet), packet.StreamIndex, "Packet names no declared RealMedia stream.");
    if (packet.Data.IsEmpty)
      throw new InvalidDataException("RealMedia packets may not be empty in this writer.");
    if (packet.PresentationTimestamp == null && packet.DecodeTimestamp == null)
      throw new NotSupportedException(
        $"RealMedia packet for stream {packet.StreamIndex} has no timestamp; RealMedia packet headers always carry one.");

    this._packets.Add(packet);
  }

  public byte[] Finish() {
    if (this._finished)
      throw new InvalidOperationException("RealMedia writer has already been finished.");
    this._finished = true;
    if (this._packets.Count == 0)
      throw new InvalidDataException("RealMedia needs at least one packet.");

    var encodedPackets = new List<byte[]>();
    var mediaObjectNumbers = new uint[this._streams.Count];
    long maximumEndMilliseconds = 0;
    var maximumPacketSize = 0;
    long totalPacketSize = 0;

    foreach (var packet in this._packets) {
      var stream = this._streams[packet.StreamIndex];
      var sourceTime = packet.PresentationTimestamp ?? packet.DecodeTimestamp!.Value;
      var timestamp = _Milliseconds(sourceTime, stream);
      if (timestamp is < 0 or > uint.MaxValue)
        throw new NotSupportedException($"RealMedia timestamp {timestamp} ms is outside its 32-bit packet field.");

      var duration = packet.Duration is > 0 ? Math.Max(1, _Milliseconds(packet.Duration.Value, stream)) : _DefaultDurationMilliseconds(stream);
      maximumEndMilliseconds = Math.Max(maximumEndMilliseconds, timestamp + duration);

      if (stream.Kind == MediaStreamKind.Video) {
        _EncodeVideoPacket(
          encodedPackets,
          streamNumber: packet.StreamIndex,
          mediaObjectNumber: mediaObjectNumbers[packet.StreamIndex]++,
          timestamp: checked((uint)timestamp),
          packet);
      } else {
        if (packet.Data.Length > _MAX_AUDIO_PAYLOAD)
          throw new NotSupportedException(
            $"RealMedia non-video packet on stream {packet.StreamIndex} is {packet.Data.Length} bytes; splitting it would change the stored codec/interleaver packetization.");
        encodedPackets.Add(_Packet(packet.StreamIndex, checked((uint)timestamp), packet.IsKeyFrame, packet.Data.Span));
        ++mediaObjectNumbers[packet.StreamIndex];
      }
    }

    foreach (var packet in encodedPackets) {
      maximumPacketSize = Math.Max(maximumPacketSize, packet.Length);
      totalPacketSize += packet.Length;
    }

    var durationMilliseconds = this._metadata.Duration is { } declared
      ? Math.Max(0L, checked((long)declared.TotalMilliseconds))
      : maximumEndMilliseconds;

    var content = _Chunk("CONT", 0, _Content(this._metadata));
    var mdpr = new byte[this._streams.Count][];
    for (var i = 0; i < this._streams.Count; ++i)
      mdpr[i] = _Chunk("MDPR", 0, _MediaProperties(this._streams[i], i));

    var rmf = _Chunk(".RMF", 0, _FileHeader(this._streams.Count + 3));
    var propertiesLength = RealMediaChunkScanner.PREFIX + 40;
    var dataOffset = checked(rmf.Length + propertiesLength + content.Length);
    foreach (var stream in mdpr)
      dataOffset = checked(dataOffset + stream.Length);

    var averagePacketSize = encodedPackets.Count == 0 ? 0 : checked((uint)(totalPacketSize / encodedPackets.Count));
    var prop = _Chunk(
      "PROP", 0,
      _Properties(
        packetCount: checked((uint)encodedPackets.Count),
        durationMilliseconds: checked((uint)Math.Min(durationMilliseconds, uint.MaxValue)),
        maximumPacketSize: checked((uint)maximumPacketSize),
        averagePacketSize,
        dataOffset: checked((uint)dataOffset),
        streamCount: checked((ushort)this._streams.Count)));

    using var dataBody = new MemoryStream();
    ContainerWriterTools.WriteUInt32BigEndian(dataBody, checked((uint)encodedPackets.Count));
    ContainerWriterTools.WriteUInt32BigEndian(dataBody, 0); // no chained DATA object
    foreach (var packet in encodedPackets)
      dataBody.Write(packet);
    var data = _Chunk("DATA", 0, dataBody.ToArray());

    using var output = new MemoryStream();
    output.Write(rmf);
    output.Write(prop);
    output.Write(content);
    foreach (var stream in mdpr)
      output.Write(stream);
    output.Write(data);
    return output.ToArray();
  }

  private static void _EncodeVideoPacket(
    List<byte[]> into, int streamNumber, uint mediaObjectNumber, uint timestamp, CodedPacket packet) {
    if (packet.Data.Length <= _MAX_WHOLE_VIDEO) {
      using var element = new MemoryStream(checked(packet.Data.Length + 2));
      element.WriteByte(0x40); // whole frame occupying the rest of this RealMedia packet
      element.WriteByte((byte)mediaObjectNumber);
      element.Write(packet.Data.Span);
      into.Add(_Packet(streamNumber, timestamp, packet.IsKeyFrame, element.ToArray()));
      return;
    }

    var offsets = packet.FragmentOffsets;
    if (offsets == null || offsets.Count < 2)
      throw new NotSupportedException(
        $"RealMedia video frame is {packet.Data.Length} bytes and cannot fit one 16-bit packet. Its slice offsets are required to split it without cutting through RealVideo slices.");
    if (offsets[0] != 0)
      throw new InvalidDataException("RealMedia FragmentOffsets must begin at zero.");

    for (var i = 0; i < offsets.Count; ++i) {
      var start = offsets[i];
      var end = i + 1 < offsets.Count ? offsets[i + 1] : packet.Data.Length;
      if (start < 0 || end <= start || end > packet.Data.Length || i > 0 && start <= offsets[i - 1])
        throw new InvalidDataException("RealMedia FragmentOffsets must be strictly increasing positions inside the frame.");

      var last = i + 1 == offsets.Count;
      var piece = packet.Data.Slice(start, end - start);
      using var element = new MemoryStream();
      element.WriteByte(last ? (byte)0x80 : (byte)0x00);
      element.WriteByte((byte)mediaObjectNumber);
      _WriteNumber(element, packet.Data.Length);
      _WriteNumber(element, last ? piece.Length : start);
      element.WriteByte((byte)mediaObjectNumber);
      element.Write(piece.Span);

      var payload = element.ToArray();
      if (_PACKET_HEADER + payload.Length > _MAX_PACKET_LENGTH)
        throw new NotSupportedException(
          $"RealMedia slice {i} of frame {mediaObjectNumber} is {piece.Length} bytes and cannot fit a single 16-bit RealMedia packet without splitting the slice itself.");

      into.Add(_Packet(streamNumber, timestamp, i == 0 && packet.IsKeyFrame, payload));
    }
  }

  private static byte[] _Packet(int streamNumber, uint timestamp, bool keyFrame, ReadOnlySpan<byte> payload) {
    var length = checked(_PACKET_HEADER + payload.Length);
    if (length > _MAX_PACKET_LENGTH)
      throw new NotSupportedException($"RealMedia packet is {length} bytes, beyond its 16-bit length field.");

    using var packet = new MemoryStream(length);
    ContainerWriterTools.WriteUInt16BigEndian(packet, 0); // packet object version
    ContainerWriterTools.WriteUInt16BigEndian(packet, checked((ushort)length));
    ContainerWriterTools.WriteUInt16BigEndian(packet, checked((ushort)streamNumber));
    ContainerWriterTools.WriteUInt32BigEndian(packet, timestamp);
    packet.WriteByte(0); // packet group
    packet.WriteByte(keyFrame ? (byte)0x02 : (byte)0x00);
    packet.Write(payload);
    return packet.ToArray();
  }

  private static byte[] _MediaProperties(MediaStreamInfo stream, int streamNumber) {
    var description = stream.Kind switch {
      MediaStreamKind.Video => _VideoDescription(stream),
      MediaStreamKind.Audio => stream.CodecPrivateData.ToArray(),
      _ => throw new NotSupportedException($"RealMedia writer does not currently serialize {stream.Kind} stream declarations."),
    };
    var mime = stream.Kind == MediaStreamKind.Video ? "video/x-pn-realvideo" : "audio/x-pn-realaudio";
    var name = string.IsNullOrWhiteSpace(stream.Name) ? (stream.Kind == MediaStreamKind.Video ? "Video Stream" : "Audio Stream") : stream.Name!;
    var nameBytes = Encoding.Latin1.GetBytes(name);
    var mimeBytes = Encoding.ASCII.GetBytes(mime);
    if (nameBytes.Length > byte.MaxValue || mimeBytes.Length > byte.MaxValue)
      throw new NotSupportedException("RealMedia stream name or MIME type exceeds its one-byte length field.");

    using var body = new MemoryStream();
    ContainerWriterTools.WriteUInt16BigEndian(body, checked((ushort)streamNumber));
    for (var i = 0; i < 7; ++i)
      ContainerWriterTools.WriteUInt32BigEndian(body, 0);
    body.WriteByte(checked((byte)nameBytes.Length));
    body.Write(nameBytes);
    body.WriteByte(checked((byte)mimeBytes.Length));
    body.Write(mimeBytes);
    ContainerWriterTools.WriteUInt32BigEndian(body, checked((uint)description.Length));
    body.Write(description);
    return body.ToArray();
  }

  private static byte[] _VideoDescription(MediaStreamInfo stream) {
    var privateData = stream.CodecPrivateData;
    var length = checked(26 + privateData.Length);
    using var description = new MemoryStream(length);
    ContainerWriterTools.WriteUInt32BigEndian(description, checked((uint)length));
    description.Write("VIDO"u8);
    _WriteCodec(description, stream.Codec);
    ContainerWriterTools.WriteUInt16BigEndian(description, checked((ushort)stream.Width));
    ContainerWriterTools.WriteUInt16BigEndian(description, checked((ushort)stream.Height));
    ContainerWriterTools.WriteUInt16BigEndian(description, checked((ushort)Math.Max(0, stream.BitsPerPixel)));
    ContainerWriterTools.WriteUInt16BigEndian(description, 0);
    ContainerWriterTools.WriteUInt16BigEndian(description, 0);
    ContainerWriterTools.WriteUInt32BigEndian(description, _FixedPointRate(stream.FrameRate));
    description.Write(privateData.Span);
    return description.ToArray();
  }

  private static byte[] _Properties(
    uint packetCount, uint durationMilliseconds, uint maximumPacketSize, uint averagePacketSize,
    uint dataOffset, ushort streamCount) {
    using var body = new MemoryStream(40);
    ContainerWriterTools.WriteUInt32BigEndian(body, 0); // maximum bit rate not claimed
    ContainerWriterTools.WriteUInt32BigEndian(body, 0); // average bit rate not claimed
    ContainerWriterTools.WriteUInt32BigEndian(body, maximumPacketSize);
    ContainerWriterTools.WriteUInt32BigEndian(body, averagePacketSize);
    ContainerWriterTools.WriteUInt32BigEndian(body, packetCount);
    ContainerWriterTools.WriteUInt32BigEndian(body, durationMilliseconds);
    ContainerWriterTools.WriteUInt32BigEndian(body, 0); // preroll
    ContainerWriterTools.WriteUInt32BigEndian(body, 0); // no index
    ContainerWriterTools.WriteUInt32BigEndian(body, dataOffset);
    ContainerWriterTools.WriteUInt16BigEndian(body, streamCount);
    ContainerWriterTools.WriteUInt16BigEndian(body, 0);
    return body.ToArray();
  }

  private static byte[] _FileHeader(int followingChunkCount) {
    using var body = new MemoryStream(8);
    ContainerWriterTools.WriteUInt32BigEndian(body, 0);
    ContainerWriterTools.WriteUInt32BigEndian(body, checked((uint)followingChunkCount));
    return body.ToArray();
  }

  private static byte[] _Content(VideoMetadata metadata) {
    string copyright = "", comment = "";
    foreach (var text in metadata.TextEntries)
      if (text.Keyword.Equals("Copyright", StringComparison.OrdinalIgnoreCase))
        copyright = text.Text;
      else if (text.Keyword.Equals("Comment", StringComparison.OrdinalIgnoreCase)
               || text.Keyword.Equals("Description", StringComparison.OrdinalIgnoreCase))
        comment = text.Text;

    using var body = new MemoryStream();
    foreach (var value in new[] { metadata.Title ?? "", metadata.Artist ?? "", copyright, comment }) {
      var bytes = Encoding.Latin1.GetBytes(value);
      if (bytes.Length > ushort.MaxValue)
        throw new NotSupportedException("RealMedia content string exceeds its 16-bit byte length.");
      ContainerWriterTools.WriteUInt16BigEndian(body, checked((ushort)bytes.Length));
      body.Write(bytes);
    }
    return body.ToArray();
  }

  private static byte[] _Chunk(string name, ushort version, byte[] body) {
    using var chunk = new MemoryStream(checked(RealMediaChunkScanner.PREFIX + body.Length));
    ContainerWriterTools.WriteAscii(chunk, name);
    ContainerWriterTools.WriteUInt32BigEndian(chunk, checked((uint)(RealMediaChunkScanner.PREFIX + body.Length)));
    ContainerWriterTools.WriteUInt16BigEndian(chunk, version);
    chunk.Write(body);
    return chunk.ToArray();
  }

  private static void _WriteNumber(Stream output, int value) {
    if (value < 0)
      throw new ArgumentOutOfRangeException(nameof(value));
    if (value < 0x4000) {
      ContainerWriterTools.WriteUInt16BigEndian(output, checked((ushort)(0x4000 | value)));
      return;
    }
    ContainerWriterTools.WriteUInt16BigEndian(output, checked((ushort)(value >> 16)));
    ContainerWriterTools.WriteUInt16BigEndian(output, checked((ushort)value));
  }

  private static void _WriteCodec(Stream output, CodecTag codec) {
    var value = codec.Value;
    Span<byte> bytes = stackalloc byte[4] {
      (byte)value,
      (byte)(value >> 8),
      (byte)(value >> 16),
      (byte)(value >> 24),
    };
    foreach (var b in bytes)
      if (b is < 0x20 or > 0x7E)
        throw new NotSupportedException($"RealMedia video codec tag '{codec}' is not a four-character code.");
    output.Write(bytes);
  }

  private static uint _FixedPointRate(Rational rate) {
    if (!rate.IsKnown || rate.Numerator <= 0 || rate.Denominator <= 0)
      return 0;
    var numerator = (Int128)rate.Numerator * 65536;
    var rounded = (numerator + rate.Denominator / 2) / rate.Denominator;
    if (rounded > uint.MaxValue)
      throw new NotSupportedException($"RealMedia frame rate {rate} exceeds its 16.16 field.");
    return checked((uint)rounded);
  }

  private static long _Milliseconds(long value, MediaStreamInfo stream) {
    if (stream.TimeBase.IsKnown)
      return ContainerWriterTools.Rescale(value, stream.TimeBase, 1000);
    if (stream.Kind == MediaStreamKind.Video && stream.FrameRate.IsKnown) {
      var result = (Int128)value * stream.FrameRate.Denominator * 1000 / stream.FrameRate.Numerator;
      return checked((long)result);
    }
    throw new NotSupportedException($"RealMedia stream {stream.Index} needs a known time base to place its packets in milliseconds.");
  }

  private static long _DefaultDurationMilliseconds(MediaStreamInfo stream) {
    if (stream.Kind == MediaStreamKind.Video && stream.FrameRate.IsKnown) {
      var result = (Int128)stream.FrameRate.Denominator * 1000 / stream.FrameRate.Numerator;
      return Math.Max(1, checked((long)result));
    }
    return 1;
  }

  private static void _ValidateStream(MediaStreamInfo stream) {
    switch (stream.Kind) {
      case MediaStreamKind.Video:
        if (stream.Width is <= 0 or > ushort.MaxValue || stream.Height is <= 0 or > ushort.MaxValue)
          throw new NotSupportedException($"RealMedia video stream {stream.Index} dimensions do not fit its 16-bit fields.");
        if (stream.Codec.Value == 0)
          throw new NotSupportedException($"RealMedia video stream {stream.Index} needs a four-character codec tag.");
        break;

      case MediaStreamKind.Audio:
        if (stream.CodecPrivateData.Length < 6)
          throw new NotSupportedException($"RealMedia audio stream {stream.Index} needs its RealAudio header in CodecPrivateData.");
        break;

      default:
        throw new NotSupportedException($"RealMedia writer does not currently serialize {stream.Kind} stream declarations.");
    }
  }
}
