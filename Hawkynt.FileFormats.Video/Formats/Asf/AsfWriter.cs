using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Asf;

/// <summary>Writes seekable ASF files using one complete media object per explicitly-sized data packet.</summary>
public sealed class AsfWriter : IVideoContainerWriter<AsfWriter> {

  private const int _OBJECT_HEADER = 24;
  private const int _VIDEO_TYPE_PREFIX = 11;
  private const int _BITMAP_INFO_HEADER = 40;

  // 20FB5700-5B55-11CF-A8FD-00805F5C442B, in ASF's stored GUID byte order.
  private static ReadOnlySpan<byte> _NoErrorCorrection =>
    [0x00, 0x57, 0xFB, 0x20, 0x55, 0x5B, 0xCF, 0x11, 0xA8, 0xFD, 0x00, 0x80, 0x5F, 0x5C, 0x44, 0x2B];

  private readonly IReadOnlyList<MediaStreamInfo> _streams;
  private readonly VideoMetadata _metadata;
  private readonly List<CodedPacket> _packets = [];
  private bool _finished;

  private AsfWriter(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(metadata);
    if (streams.Count == 0 || streams.Count > 127)
      throw new NotSupportedException("ASF needs between one and 127 streams.");

    for (var i = 0; i < streams.Count; ++i) {
      var stream = streams[i] ?? throw new ArgumentException($"Stream {i} is null.", nameof(streams));
      if (stream.Index != i)
        throw new ArgumentException($"ASF streams must be indexed densely from zero; position {i} has index {stream.Index}.", nameof(streams));
      _ValidateStream(stream);
    }

    this._streams = streams;
    this._metadata = metadata;
  }

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".asf";
  /// <summary>Gets the file extensions supported by this format.</summary>
  public static string[] FileExtensions => [".asf", ".wmv", ".wma", ".wm", ".wmx", ".asx"];

  /// <summary>Creates a writer for the specified stream descriptions and metadata.</summary>
  public static AsfWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams, metadata);

  /// <summary>Writes the specified coded packet to the container.</summary>
  public void WritePacket(CodedPacket packet) {
    if (this._finished)
      throw new InvalidOperationException("ASF writer has already been finished.");
    if ((uint)packet.StreamIndex >= (uint)this._streams.Count)
      throw new ArgumentOutOfRangeException(nameof(packet), packet.StreamIndex, "Packet names no declared ASF stream.");
    if (packet.Data.IsEmpty)
      throw new InvalidDataException("ASF media objects may not be empty in this writer.");
    if (packet.PresentationTimestamp == null && packet.DecodeTimestamp == null)
      throw new NotSupportedException(
        $"ASF packet for stream {packet.StreamIndex} has no timestamp; ASF writes a presentation time on every media object.");

    this._packets.Add(packet);
  }

  /// <summary>Finishes writing the container and returns its encoded bytes.</summary>
  public byte[] Finish() {
    if (this._finished)
      throw new InvalidOperationException("ASF writer has already been finished.");
    this._finished = true;
    if (this._packets.Count == 0)
      throw new InvalidDataException("ASF needs at least one media packet.");

    var fileId = Guid.NewGuid().ToByteArray();
    var mediaNumbers = new uint[this._streams.Count];
    var packetBytes = new byte[this._packets.Count][];
    var minimumPacket = int.MaxValue;
    var maximumPacket = 0;
    long maximumEndMilliseconds = 0;

    for (var i = 0; i < this._packets.Count; ++i) {
      var packet = this._packets[i];
      var stream = this._streams[packet.StreamIndex];
      var time = packet.PresentationTimestamp ?? packet.DecodeTimestamp!.Value;
      var presentationMilliseconds = _Milliseconds(time, stream);
      if (presentationMilliseconds is < 0 or > uint.MaxValue)
        throw new NotSupportedException(
          $"ASF presentation time {presentationMilliseconds} ms for stream {packet.StreamIndex} is outside its 32-bit field.");

      var durationMilliseconds = packet.Duration is > 0
        ? Math.Max(1, _Milliseconds(packet.Duration.Value, stream))
        : _DefaultDurationMilliseconds(stream);
      maximumEndMilliseconds = Math.Max(maximumEndMilliseconds, presentationMilliseconds + durationMilliseconds);

      var bytes = _Packet(
        streamNumber: packet.StreamIndex + 1,
        mediaObjectNumber: mediaNumbers[packet.StreamIndex]++,
        presentationMilliseconds: checked((uint)presentationMilliseconds),
        isKeyFrame: stream.Kind != MediaStreamKind.Video || packet.IsKeyFrame,
        payload: packet.Data);
      packetBytes[i] = bytes;
      minimumPacket = Math.Min(minimumPacket, bytes.Length);
      maximumPacket = Math.Max(maximumPacket, bytes.Length);
    }

    var declaredDurationTicks = this._metadata.Duration?.Ticks
      ?? checked(maximumEndMilliseconds * TimeSpan.TicksPerMillisecond);
    var creationFileTime = this._metadata.CreationTime is { } created
      ? checked((ulong)Math.Max(0, created.ToFileTime()))
      : 0UL;

    var streamObjects = new byte[this._streams.Count][];
    for (var i = 0; i < this._streams.Count; ++i)
      streamObjects[i] = _StreamProperties(this._streams[i], i + 1);

    var content = _ContentDescription(this._metadata);
    var childCount = 1 + streamObjects.Length + (content.Length == 0 ? 0 : 1);

    var data = _DataObject(fileId, packetBytes);
    var header = _HeaderObject(
      childCount,
      _FileProperties(
        fileId, 0, creationFileTime, checked((ulong)packetBytes.Length), checked((ulong)Math.Max(0, declaredDurationTicks)),
        checked((uint)minimumPacket), checked((uint)maximumPacket)),
      streamObjects,
      content);

    var fileSize = checked((ulong)header.Length + (ulong)data.Length);
    header = _HeaderObject(
      childCount,
      _FileProperties(
        fileId, fileSize, creationFileTime, checked((ulong)packetBytes.Length), checked((ulong)Math.Max(0, declaredDurationTicks)),
        checked((uint)minimumPacket), checked((uint)maximumPacket)),
      streamObjects,
      content);

    var result = new byte[checked(header.Length + data.Length)];
    header.CopyTo(result, 0);
    data.CopyTo(result, header.Length);
    return result;
  }

  private static byte[] _HeaderObject(int childCount, byte[] fileProperties, byte[][] streams, byte[] content) {
    using var body = new MemoryStream();
    ContainerWriterTools.WriteUInt32LittleEndian(body, checked((uint)childCount));
    body.WriteByte(1);
    body.WriteByte(2);
    body.Write(fileProperties);
    foreach (var stream in streams)
      body.Write(stream);
    if (content.Length != 0)
      body.Write(content);
    return _Object(AsfGuid.Header, body.ToArray());
  }

  private static byte[] _FileProperties(
    byte[] fileId, ulong fileSize, ulong creationFileTime, ulong packetCount, ulong durationTicks,
    uint minimumPacket, uint maximumPacket) {
    using var body = new MemoryStream(AsfFileProperties.STRUCT_SIZE);
    body.Write(fileId);
    ContainerWriterTools.WriteUInt64LittleEndian(body, fileSize);
    ContainerWriterTools.WriteUInt64LittleEndian(body, creationFileTime);
    ContainerWriterTools.WriteUInt64LittleEndian(body, packetCount);
    ContainerWriterTools.WriteUInt64LittleEndian(body, durationTicks);
    ContainerWriterTools.WriteUInt64LittleEndian(body, durationTicks);
    ContainerWriterTools.WriteUInt64LittleEndian(body, 0); // no preroll
    ContainerWriterTools.WriteUInt32LittleEndian(body, 2); // seekable, not broadcast
    ContainerWriterTools.WriteUInt32LittleEndian(body, minimumPacket);
    ContainerWriterTools.WriteUInt32LittleEndian(body, maximumPacket);
    ContainerWriterTools.WriteUInt32LittleEndian(body, 0); // peak bitrate not claimed
    return _Object(AsfGuid.FileProperties, body.ToArray());
  }

  private static byte[] _StreamProperties(MediaStreamInfo stream, int streamNumber) {
    var type = stream.Kind switch {
      MediaStreamKind.Video => AsfGuid.VideoMedia.ToArray(),
      MediaStreamKind.Audio => AsfGuid.AudioMedia.ToArray(),
      MediaStreamKind.Data => AsfGuid.BinaryMedia.ToArray(),
      _ => throw new NotSupportedException($"ASF writer does not currently serialize {stream.Kind} stream declarations."),
    };
    var typeSpecific = stream.Kind switch {
      MediaStreamKind.Video => _VideoTypeData(stream),
      MediaStreamKind.Audio => stream.CodecPrivateData.ToArray(),
      _ => stream.CodecPrivateData.ToArray(),
    };

    using var body = new MemoryStream();
    body.Write(type);
    body.Write(_NoErrorCorrection);
    ContainerWriterTools.WriteUInt64LittleEndian(body, 0); // time offset
    ContainerWriterTools.WriteUInt32LittleEndian(body, checked((uint)typeSpecific.Length));
    ContainerWriterTools.WriteUInt32LittleEndian(body, 0); // no error-correction data
    ContainerWriterTools.WriteUInt16LittleEndian(body, checked((ushort)streamNumber));
    ContainerWriterTools.WriteUInt32LittleEndian(body, 0);
    body.Write(typeSpecific);
    return _Object(AsfGuid.StreamProperties, body.ToArray());
  }

  private static byte[] _VideoTypeData(MediaStreamInfo stream) {
    var format = _BitmapFormatData(stream);
    if (format.Length > ushort.MaxValue)
      throw new NotSupportedException($"ASF video stream {stream.Index} format data exceeds 65,535 bytes.");

    using var type = new MemoryStream(_VIDEO_TYPE_PREFIX + format.Length);
    ContainerWriterTools.WriteUInt32LittleEndian(type, checked((uint)stream.Width));
    ContainerWriterTools.WriteUInt32LittleEndian(type, checked((uint)stream.Height));
    type.WriteByte(0);
    ContainerWriterTools.WriteUInt16LittleEndian(type, checked((ushort)format.Length));
    type.Write(format);
    return type.ToArray();
  }

  private static byte[] _BitmapFormatData(MediaStreamInfo stream) {
    var data = stream.CodecPrivateData;
    if (data.Length >= _BITMAP_INFO_HEADER) {
      var declared = BinaryPrimitives.ReadUInt32LittleEndian(data.Span);
      if (declared >= _BITMAP_INFO_HEADER && declared <= data.Length)
        return data.ToArray();
    }

    if (stream.Codec.Value == 0)
      throw new NotSupportedException($"ASF video stream {stream.Index} has neither BITMAPINFOHEADER data nor a codec tag to synthesize one from.");

    var result = new byte[_BITMAP_INFO_HEADER];
    BinaryPrimitives.WriteUInt32LittleEndian(result, _BITMAP_INFO_HEADER);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), stream.Width);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(8), stream.Height);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(14), checked((ushort)Math.Max(0, stream.BitsPerPixel)));
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(16), stream.Codec.Value);
    return result;
  }

  private static byte[] _DataObject(byte[] fileId, byte[][] packets) {
    using var body = new MemoryStream();
    body.Write(fileId);
    ContainerWriterTools.WriteUInt64LittleEndian(body, checked((ulong)packets.Length));
    body.WriteByte(1);
    body.WriteByte(1);
    foreach (var packet in packets)
      body.Write(packet);
    return _Object(AsfGuid.Data, body.ToArray());
  }

  private static byte[] _Packet(
    int streamNumber, uint mediaObjectNumber, uint presentationMilliseconds, bool isKeyFrame, ReadOnlyMemory<byte> payload) {
    // Explicit 32-bit packet length; one payload; 1-byte stream number; 32-bit object number and
    // fragment offset; 1-byte replicated-data length. Replicated data is the ordinary eight-byte
    // media-object size + presentation-time form. Deliberately no compression or error correction.
    const byte lengthTypeFlags = 0x60;
    const byte propertyFlags = 0x7D;
    const int fixedBytes = 30;
    var length = checked(fixedBytes + payload.Length);
    if (length > int.MaxValue)
      throw new NotSupportedException("ASF packet exceeds the reader's addressable packet length.");

    using var packet = new MemoryStream(length);
    packet.WriteByte(lengthTypeFlags);
    packet.WriteByte(propertyFlags);
    ContainerWriterTools.WriteUInt32LittleEndian(packet, checked((uint)length));
    ContainerWriterTools.WriteUInt32LittleEndian(packet, presentationMilliseconds); // send time
    ContainerWriterTools.WriteUInt16LittleEndian(packet, 0); // packet duration
    packet.WriteByte((byte)(streamNumber | (isKeyFrame ? 0x80 : 0)));
    ContainerWriterTools.WriteUInt32LittleEndian(packet, mediaObjectNumber);
    ContainerWriterTools.WriteUInt32LittleEndian(packet, 0); // whole media object begins at zero
    packet.WriteByte(8);
    ContainerWriterTools.WriteUInt32LittleEndian(packet, checked((uint)payload.Length));
    ContainerWriterTools.WriteUInt32LittleEndian(packet, presentationMilliseconds);
    packet.Write(payload.Span);
    return packet.ToArray();
  }

  private static byte[] _ContentDescription(VideoMetadata metadata) {
    string? copyright = null, description = null, rating = null;
    foreach (var text in metadata.TextEntries)
      switch (text.Keyword.ToUpperInvariant()) {
        case "COPYRIGHT": copyright ??= text.Text; break;
        case "DESCRIPTION":
        case "COMMENT": description ??= text.Text; break;
        case "RATING": rating ??= text.Text; break;
      }

    var values = new[] { metadata.Title, metadata.Artist, copyright, description, rating };
    var encoded = new byte[5][];
    var any = false;
    for (var i = 0; i < values.Length; ++i) {
      encoded[i] = string.IsNullOrEmpty(values[i]) ? [] : Encoding.Unicode.GetBytes(values[i]!);
      if (encoded[i].Length > ushort.MaxValue)
        throw new NotSupportedException("ASF content-description string exceeds its 16-bit byte length.");
      any |= encoded[i].Length != 0;
    }
    if (!any)
      return [];

    using var body = new MemoryStream();
    foreach (var value in encoded)
      ContainerWriterTools.WriteUInt16LittleEndian(body, checked((ushort)value.Length));
    foreach (var value in encoded)
      body.Write(value);
    return _Object(AsfGuid.ContentDescription, body.ToArray());
  }

  private static byte[] _Object(ReadOnlySpan<byte> guid, byte[] body) {
    using var result = new MemoryStream(checked(_OBJECT_HEADER + body.Length));
    result.Write(guid);
    ContainerWriterTools.WriteUInt64LittleEndian(result, checked((ulong)(_OBJECT_HEADER + body.Length)));
    result.Write(body);
    return result.ToArray();
  }

  private static long _Milliseconds(long value, MediaStreamInfo stream) {
    if (stream.TimeBase.IsKnown)
      return ContainerWriterTools.Rescale(value, stream.TimeBase, 1000);
    if (stream.Kind == MediaStreamKind.Video && stream.FrameRate.IsKnown) {
      var scaled = (Int128)value * stream.FrameRate.Denominator * 1000 / stream.FrameRate.Numerator;
      return checked((long)scaled);
    }
    throw new NotSupportedException($"ASF stream {stream.Index} needs a known time base to place its packets in milliseconds.");
  }

  private static long _DefaultDurationMilliseconds(MediaStreamInfo stream) {
    if (stream.Kind == MediaStreamKind.Video && stream.FrameRate.IsKnown) {
      var scaled = (Int128)stream.FrameRate.Denominator * 1000 / stream.FrameRate.Numerator;
      return Math.Max(1, checked((long)scaled));
    }
    return 1;
  }

  private static void _ValidateStream(MediaStreamInfo stream) {
    switch (stream.Kind) {
      case MediaStreamKind.Video:
        if (stream.Width <= 0 || stream.Height <= 0)
          throw new NotSupportedException($"ASF video stream {stream.Index} needs positive dimensions.");
        break;

      case MediaStreamKind.Audio:
        if (stream.CodecPrivateData.Length < 16)
          throw new NotSupportedException(
            $"ASF audio stream {stream.Index} needs a WAVEFORMATEX-compatible description in CodecPrivateData.");
        break;

      case MediaStreamKind.Data:
        break;

      default:
        throw new NotSupportedException($"ASF writer does not currently serialize {stream.Kind} stream declarations.");
    }
  }
}
