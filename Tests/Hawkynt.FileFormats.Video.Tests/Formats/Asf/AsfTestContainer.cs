using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.Asf.Tests;

/// <summary>What a built stream carries, which decides its Stream Type identifier.</summary>
/// <remarks>Public because a test case runs over it, and NUnit reaches those from outside.</remarks>
public enum AsfTestMedia {
  Video = 0,
  Audio = 1,
  Command = 2,
  Unknown = 3,
}

/// <summary>One stream to be declared in a built file.</summary>
internal sealed class AsfTestStream {

  /// <summary>The ASF stream number, one to 127 — which is not the stream's index.</summary>
  public int Number { get; init; } = 1;

  public AsfTestMedia Media { get; init; } = AsfTestMedia.Video;

  /// <summary>The code in the format data's <c>biCompression</c>, which is what names the codec.</summary>
  public string FourCharacterCode { get; init; } = "MP43";

  /// <summary>The tag in the WAVEFORMATEX's first field, for a sound stream.</summary>
  public ushort FormatTag { get; init; } = 0x0161;

  public int Width { get; init; } = 8;

  public int Height { get; init; } = 4;

  public int BitsPerPixel { get; init; } = 24;

  /// <summary>Bytes written past the fortieth of the format data, where a codec's own header lives.</summary>
  public byte[]? ExtraFormatData { get; init; }

  /// <summary>Sets the Encrypted Content flag, which is bit 15 of the stream's flags.</summary>
  public bool Encrypted { get; init; }

  /// <summary>Declares the stream only inside an Extended Stream Properties Object, never beside it.</summary>
  public bool DeclaredInsideExtendedProperties { get; init; }

  /// <summary>100-nanosecond units a frame lasts, or zero to write no Extended Stream Properties at all.</summary>
  public long AverageTimePerFrame { get; init; }

  /// <summary>Which entry of the Language List Object this stream's language is.</summary>
  public int LanguageIndex { get; init; }

  /// <summary>The stream's name, written into its Extended Stream Properties Object.</summary>
  public string? Name { get; init; }

  /// <summary>Writes an Extended Stream Properties Object even when nothing needs one.</summary>
  public bool ForceExtendedProperties { get; init; }

  internal bool NeedsExtendedProperties
    => this.DeclaredInsideExtendedProperties || this.ForceExtendedProperties
       || this.AverageTimePerFrame > 0 || this.LanguageIndex > 0 || this.Name != null;
}

/// <summary>One payload to be written into a built packet.</summary>
internal sealed class AsfTestPayload {

  public int Stream { get; init; } = 1;

  public int MediaObjectNumber { get; init; }

  /// <summary>How far into the media object this piece begins.</summary>
  public int Offset { get; init; }

  /// <summary>How long the whole media object is; defaults to this piece's length, meaning a whole one.</summary>
  public int? MediaObjectSize { get; init; }

  /// <summary>When the object is due, in milliseconds, before the file's preroll is added on.</summary>
  public long PresentationTime { get; init; }

  public bool KeyFrame { get; init; }

  public byte[] Data { get; init; } = [];

  /// <summary>
  /// Writes the payload in the compressed form: a run of whole objects rather than a fragment of one.
  /// </summary>
  /// <remarks>
  /// <see cref="Data"/> is ignored when this is set, and <see cref="SubObjects"/> written instead.
  /// ffmpeg's ASF muxer never emits this form, so every test of it was assembled here and read back
  /// with ffprobe before being written down.
  /// </remarks>
  public IReadOnlyList<byte[]>? SubObjects { get; init; }

  /// <summary>Milliseconds between one sub-object and the next, for the compressed form.</summary>
  public byte PresentationTimeDelta { get; init; }
}

/// <summary>One data packet of a built file.</summary>
internal sealed class AsfTestPacket {

  public IReadOnlyList<AsfTestPayload> Payloads { get; init; } = [];

  /// <summary>Writes the error correction block ffmpeg always writes; clear it to leave it out.</summary>
  public bool ErrorCorrection { get; init; } = true;

  /// <summary>Writes the packet's length explicitly rather than letting the fixed size imply it.</summary>
  public bool ExplicitLength { get; init; }

  /// <summary>Writes the packet in the single-payload form, which states no payload length.</summary>
  /// <remarks>Only meaningful with exactly one payload, which is what the form is for.</remarks>
  public bool SinglePayload { get; init; }
}

/// <summary>
/// Builds ASF files byte by byte so the reader can be tested without a sample in the tree.
/// </summary>
/// <remarks>
/// The layout copied here is the one ffmpeg writes, read off a dump of its own output rather than off
/// the specification alone: a Header Object stating how many objects follow, then File Properties, one
/// Stream Properties per stream, a Header Extension, and whatever text there is; then a Data Object of
/// fixed-size packets. What ffmpeg will not produce is reachable through the options above — a packet
/// with no error correction block, a packet stating its own length, a compressed payload carrying
/// several whole objects at once, a <c>WM/Picture</c> — and each of those forms was assembled the same
/// way here, written out, put past ffprobe, and only written down as a test once ffprobe read the same
/// packets out of it.
/// <para/>
/// Nothing here is a valid picture unless a test makes it one. The payloads are whatever bytes a test
/// hands over, which is all a demuxer needs: what is being tested is where the packets are, how big
/// they are and when each is due.
/// </remarks>
internal static class AsfTestContainer {

  /// <summary>The preroll <see cref="Build"/> states, which is the one ffmpeg writes.</summary>
  internal const long PREROLL = 3100L;

  /// <summary>The fixed packet size <see cref="Build"/> states, which is the one ffmpeg writes.</summary>
  internal const int PACKET_SIZE = 3200;

  /// <summary>100-nanosecond units in a second, which is what the format counts durations in.</summary>
  internal const long UNITS_PER_SECOND = 10_000_000L;

  // The identifiers are written out here rather than taken from the reader's own table, so that a test
  // asserts the specification's values instead of agreeing with whatever the reader happens to hold.
  private static ReadOnlySpan<byte> _HeaderId => [0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11, 0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C];
  private static ReadOnlySpan<byte> _DataId => [0x36, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11, 0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C];
  private static ReadOnlySpan<byte> _FilePropertiesId => [0xA1, 0xDC, 0xAB, 0x8C, 0x47, 0xA9, 0xCF, 0x11, 0x8E, 0xE4, 0x00, 0xC0, 0x0C, 0x20, 0x53, 0x65];
  private static ReadOnlySpan<byte> _StreamPropertiesId => [0x91, 0x07, 0xDC, 0xB7, 0xB7, 0xA9, 0xCF, 0x11, 0x8E, 0xE6, 0x00, 0xC0, 0x0C, 0x20, 0x53, 0x65];
  private static ReadOnlySpan<byte> _HeaderExtensionId => [0xB5, 0x03, 0xBF, 0x5F, 0x2E, 0xA9, 0xCF, 0x11, 0x8E, 0xE3, 0x00, 0xC0, 0x0C, 0x20, 0x53, 0x65];
  private static ReadOnlySpan<byte> _ExtendedStreamPropertiesId => [0xCB, 0xA5, 0xE6, 0x14, 0x72, 0xC6, 0x32, 0x43, 0x83, 0x99, 0xA9, 0x69, 0x52, 0x06, 0x5B, 0x5A];
  private static ReadOnlySpan<byte> _LanguageListId => [0xA9, 0x46, 0x43, 0x7C, 0xE0, 0xEF, 0xFC, 0x4B, 0xB2, 0x29, 0x39, 0x3E, 0xDE, 0x41, 0x5C, 0x85];
  private static ReadOnlySpan<byte> _ContentDescriptionId => [0x33, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11, 0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C];
  private static ReadOnlySpan<byte> _ExtendedContentDescriptionId => [0x40, 0xA4, 0xD0, 0xD2, 0x07, 0xE3, 0xD2, 0x11, 0x97, 0xF0, 0x00, 0xA0, 0xC9, 0x5E, 0xA8, 0x50];
  private static ReadOnlySpan<byte> _CodecListId => [0x40, 0x52, 0xD1, 0x86, 0x1D, 0x31, 0xD0, 0x11, 0xA3, 0xA4, 0x00, 0xA0, 0xC9, 0x03, 0x48, 0xF6];
  private static ReadOnlySpan<byte> _PaddingId => [0x74, 0xD4, 0x06, 0x18, 0xDF, 0xCA, 0x09, 0x45, 0xA4, 0xBA, 0x9A, 0xAB, 0xCB, 0x96, 0xAA, 0xE8];

  private static ReadOnlySpan<byte> _VideoMediaId => [0xC0, 0xEF, 0x19, 0xBC, 0x4D, 0x5B, 0xCF, 0x11, 0xA8, 0xFD, 0x00, 0x80, 0x5F, 0x5C, 0x44, 0x2B];
  private static ReadOnlySpan<byte> _AudioMediaId => [0x40, 0x9E, 0x69, 0xF8, 0x4D, 0x5B, 0xCF, 0x11, 0xA8, 0xFD, 0x00, 0x80, 0x5F, 0x5C, 0x44, 0x2B];
  private static ReadOnlySpan<byte> _CommandMediaId => [0xC0, 0xCF, 0xDA, 0x59, 0xE6, 0x59, 0xD0, 0x11, 0xA3, 0xAC, 0x00, 0xA0, 0xC9, 0x03, 0x48, 0xF6];
  private static ReadOnlySpan<byte> _UnknownMediaId => [0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x00];
  private static ReadOnlySpan<byte> _NoErrorCorrectionId => [0x00, 0x57, 0xFB, 0x20, 0x55, 0x5B, 0xCF, 0x11, 0xA8, 0xFD, 0x00, 0x80, 0x5F, 0x5C, 0x44, 0x2B];

  /// <summary>Assembles a file around the given streams and packets.</summary>
  /// <param name="streams">One per stream to declare, in the order they are to be declared.</param>
  /// <param name="packets">The data packets, in the order they are to be stored.</param>
  /// <param name="preroll">How far ahead of real time every stated timestamp is, in milliseconds.</param>
  /// <param name="packetSize">The fixed size every packet is written at.</param>
  /// <param name="playDuration">The Play Duration in 100-nanosecond units; preroll is added on top.</param>
  /// <param name="creationDate">The Creation Date, in 100-nanosecond units from 1601-01-01 UTC.</param>
  /// <param name="broadcast">Sets the broadcast flag, which makes the packet count meaningless.</param>
  /// <param name="declaredPacketCount">Overrides the packet count the header states.</param>
  /// <param name="title">The Content Description Object's Title, and the rest beside it.</param>
  /// <param name="descriptors">Extended Content Description entries as (name, data type, value).</param>
  /// <param name="languages">The Language List Object's entries, which streams refer to by position.</param>
  /// <param name="codecs">Codec List entries as (type, name, description).</param>
  /// <param name="padding">Writes a Padding Object of this many bytes among the header's children.</param>
  /// <param name="withoutFileProperties">Leaves the File Properties Object out entirely.</param>
  /// <param name="withoutDataObject">Leaves the Data Object out entirely.</param>
  /// <param name="truncateBy">Cuts this many bytes off the end of the finished file.</param>
  internal static byte[] Build(
    IReadOnlyList<AsfTestStream> streams,
    IReadOnlyList<AsfTestPacket>? packets = null,
    long preroll = PREROLL,
    int packetSize = PACKET_SIZE,
    long playDuration = UNITS_PER_SECOND,
    ulong creationDate = 0,
    bool broadcast = false,
    long? declaredPacketCount = null,
    string? title = null,
    string? author = null,
    string? copyright = null,
    string? description = null,
    string? rating = null,
    IReadOnlyList<(string Name, ushort DataType, byte[] Value)>? descriptors = null,
    IReadOnlyList<string>? languages = null,
    IReadOnlyList<(ushort Type, string Name, string Description)>? codecs = null,
    int padding = 0,
    bool withoutFileProperties = false,
    bool withoutDataObject = false,
    int truncateBy = 0) {
    ArgumentNullException.ThrowIfNull(streams);
    packets ??= [];

    var body = new MemoryStream();
    var count = 0;

    if (!withoutFileProperties) {
      body.Write(_FileProperties(preroll, packetSize, playDuration, creationDate, broadcast,
        declaredPacketCount ?? packets.Count));
      ++count;
    }

    foreach (var stream in streams) {
      if (stream.DeclaredInsideExtendedProperties)
        continue;

      body.Write(_Object(_StreamPropertiesId, _StreamPropertiesBody(stream)));
      ++count;
    }

    var extension = new MemoryStream();
    foreach (var stream in streams)
      if (stream.NeedsExtendedProperties)
        extension.Write(_Object(_ExtendedStreamPropertiesId, _ExtendedStreamPropertiesBody(stream)));

    if (languages is { Count: > 0 })
      extension.Write(_Object(_LanguageListId, _LanguageListBody(languages)));

    if (extension.Length > 0) {
      var wrapper = new MemoryStream();
      wrapper.Write(_HeaderExtensionId);
      wrapper.Write(_UInt16(6));
      wrapper.Write(_UInt32((uint)extension.Length));
      wrapper.Write(extension.ToArray());
      body.Write(_Object(_HeaderExtensionId, wrapper.ToArray()));
      ++count;
    }

    if (title != null || author != null || copyright != null || description != null || rating != null) {
      body.Write(_Object(_ContentDescriptionId, _ContentDescriptionBody(title, author, copyright, description, rating)));
      ++count;
    }

    if (descriptors is { Count: > 0 }) {
      body.Write(_Object(_ExtendedContentDescriptionId, _ExtendedContentDescriptionBody(descriptors)));
      ++count;
    }

    if (codecs is { Count: > 0 }) {
      body.Write(_Object(_CodecListId, _CodecListBody(codecs)));
      ++count;
    }

    if (padding > 0) {
      body.Write(_Object(_PaddingId, new byte[padding]));
      ++count;
    }

    var header = new MemoryStream();
    header.Write(_HeaderId);
    header.Write(_UInt64((ulong)(24 + 6 + body.Length)));
    header.Write(_UInt32((uint)count));
    header.WriteByte(0x01);
    header.WriteByte(0x02);
    header.Write(body.ToArray());

    var file = new MemoryStream();
    file.Write(header.ToArray());

    if (!withoutDataObject) {
      var written = new MemoryStream();
      foreach (var packet in packets)
        written.Write(_Packet(packet, packetSize, preroll));

      var data = new MemoryStream();
      data.Write(_DataId);
      data.Write(_UInt64((ulong)(24 + 26 + written.Length)));
      data.Write(new byte[16]);
      data.Write(_UInt64((ulong)packets.Count));
      data.WriteByte(0x01);
      data.WriteByte(0x01);
      data.Write(written.ToArray());
      file.Write(data.ToArray());
    }

    var result = file.ToArray();
    return truncateBy <= 0 ? result : result[..Math.Max(0, result.Length - truncateBy)];
  }

  /// <summary>Convenience for the ordinary case: one video stream and one whole frame per packet.</summary>
  internal static byte[] Build(string fourCharacterCode, IReadOnlyList<byte[]> frames, long firstTimestamp = 0, int step = 40) {
    var packets = new AsfTestPacket[frames.Count];
    for (var i = 0; i < frames.Count; ++i)
      packets[i] = new() {
        Payloads = [
          new AsfTestPayload {
            Data = frames[i],
            PresentationTime = firstTimestamp + (i * step),
            KeyFrame = i == 0,
          },
        ],
      };

    return Build([new AsfTestStream { FourCharacterCode = fourCharacterCode }], packets);
  }

  // ------------------------------------------------------------------------------------------
  // Header objects
  // ------------------------------------------------------------------------------------------

  private static byte[] _FileProperties(
    long preroll, int packetSize, long playDuration, ulong creationDate, bool broadcast, long packetCount) {
    var body = new MemoryStream();
    body.Write(new byte[16]);
    body.Write(_UInt64(0));
    body.Write(_UInt64(creationDate));
    body.Write(_UInt64((ulong)packetCount));

    // The play duration counts the preroll as well as the film, which is why a reader has to take the
    // one off the other to report what ffprobe reports.
    body.Write(_UInt64((ulong)(playDuration + (preroll * (UNITS_PER_SECOND / 1000)))));
    body.Write(_UInt64((ulong)playDuration));
    body.Write(_UInt64((ulong)preroll));
    body.Write(_UInt32(broadcast ? 0x01u : 0x02u));
    body.Write(_UInt32((uint)packetSize));
    body.Write(_UInt32((uint)packetSize));
    body.Write(_UInt32(0));
    return _Object(_FilePropertiesId, body.ToArray());
  }

  private static byte[] _StreamPropertiesBody(AsfTestStream stream) {
    var typeSpecific = stream.Media switch {
      AsfTestMedia.Video => _VideoTypeSpecific(stream),
      AsfTestMedia.Audio => _AudioTypeSpecific(stream),
      _ => [],
    };

    var body = new MemoryStream();
    body.Write(stream.Media switch {
      AsfTestMedia.Video => _VideoMediaId,
      AsfTestMedia.Audio => _AudioMediaId,
      AsfTestMedia.Command => _CommandMediaId,
      _ => _UnknownMediaId,
    });

    body.Write(_NoErrorCorrectionId);
    body.Write(_UInt64(0));
    body.Write(_UInt32((uint)typeSpecific.Length));
    body.Write(_UInt32(0));
    body.Write(_UInt16((ushort)(stream.Number | (stream.Encrypted ? 0x8000 : 0))));
    body.Write(_UInt32(0));
    body.Write(typeSpecific);
    return body.ToArray();
  }

  /// <summary>A video stream's type-specific data: an encoded size, then a <c>BITMAPINFOHEADER</c>.</summary>
  private static byte[] _VideoTypeSpecific(AsfTestStream stream) {
    var extra = stream.ExtraFormatData ?? [];
    var format = new byte[40 + extra.Length];
    BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(0), 40 + extra.Length);
    BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(4), stream.Width);
    BinaryPrimitives.WriteInt32LittleEndian(format.AsSpan(8), stream.Height);
    BinaryPrimitives.WriteInt16LittleEndian(format.AsSpan(12), 1);
    BinaryPrimitives.WriteInt16LittleEndian(format.AsSpan(14), (short)stream.BitsPerPixel);
    for (var i = 0; i < 4; ++i)
      format[16 + i] = (byte)(i < stream.FourCharacterCode.Length ? stream.FourCharacterCode[i] : 0);

    extra.CopyTo(format, 40);

    var body = new MemoryStream();
    body.Write(_UInt32((uint)stream.Width));
    body.Write(_UInt32((uint)stream.Height));
    body.WriteByte(0x02);
    body.Write(_UInt16((ushort)format.Length));
    body.Write(format);
    return body.ToArray();
  }

  /// <summary>A sound stream's type-specific data: a WAVEFORMATEX.</summary>
  private static byte[] _AudioTypeSpecific(AsfTestStream stream) {
    var extra = stream.ExtraFormatData ?? [];
    var body = new MemoryStream();
    body.Write(_UInt16(stream.FormatTag));
    body.Write(_UInt16(2));
    body.Write(_UInt32(44100));
    body.Write(_UInt32(16000));
    body.Write(_UInt16(1487));
    body.Write(_UInt16(16));
    body.Write(_UInt16((ushort)extra.Length));
    body.Write(extra);
    return body.ToArray();
  }

  private static byte[] _ExtendedStreamPropertiesBody(AsfTestStream stream) {
    var body = new MemoryStream();
    body.Write(_UInt64(0));
    body.Write(_UInt64(0));
    for (var i = 0; i < 7; ++i)
      body.Write(_UInt32(0));

    body.Write(_UInt32(0));
    body.Write(_UInt16((ushort)stream.Number));
    body.Write(_UInt16((ushort)stream.LanguageIndex));
    body.Write(_UInt64((ulong)stream.AverageTimePerFrame));
    body.Write(_UInt16((ushort)(stream.Name == null ? 0 : 1)));
    body.Write(_UInt16(0));

    if (stream.Name != null) {
      var name = Encoding.Unicode.GetBytes(stream.Name + '\0');
      body.Write(_UInt16((ushort)stream.LanguageIndex));
      body.Write(_UInt16((ushort)name.Length));
      body.Write(name);
    }

    // A stream declared nowhere else puts its whole Stream Properties Object at the tail of this one,
    // which the format allows and which is the only way some files declare a stream at all.
    if (stream.DeclaredInsideExtendedProperties)
      body.Write(_Object(_StreamPropertiesId, _StreamPropertiesBody(stream)));

    return body.ToArray();
  }

  private static byte[] _LanguageListBody(IReadOnlyList<string> languages) {
    var body = new MemoryStream();
    body.Write(_UInt16((ushort)languages.Count));
    foreach (var language in languages) {
      var text = Encoding.Unicode.GetBytes(language + '\0');
      body.WriteByte((byte)text.Length);
      body.Write(text);
    }

    return body.ToArray();
  }

  /// <summary>The five strings, each stated by a length in bytes rather than in characters.</summary>
  private static byte[] _ContentDescriptionBody(
    string? title, string? author, string? copyright, string? description, string? rating) {
    var values = new[] { title, author, copyright, description, rating };
    var encoded = new byte[5][];
    for (var i = 0; i < 5; ++i)
      encoded[i] = values[i] == null ? [] : Encoding.Unicode.GetBytes(values[i] + '\0');

    var body = new MemoryStream();
    foreach (var value in encoded)
      body.Write(_UInt16((ushort)value.Length));

    foreach (var value in encoded)
      body.Write(value);

    return body.ToArray();
  }

  private static byte[] _ExtendedContentDescriptionBody(IReadOnlyList<(string Name, ushort DataType, byte[] Value)> descriptors) {
    var body = new MemoryStream();
    body.Write(_UInt16((ushort)descriptors.Count));
    foreach (var (name, dataType, value) in descriptors) {
      var encoded = Encoding.Unicode.GetBytes(name + '\0');
      body.Write(_UInt16((ushort)encoded.Length));
      body.Write(encoded);
      body.Write(_UInt16(dataType));
      body.Write(_UInt16((ushort)value.Length));
      body.Write(value);
    }

    return body.ToArray();
  }

  /// <summary>The Codec List, whose two string lengths count characters where everything else counts bytes.</summary>
  private static byte[] _CodecListBody(IReadOnlyList<(ushort Type, string Name, string Description)> codecs) {
    var body = new MemoryStream();
    body.Write(new byte[16]);
    body.Write(_UInt32((uint)codecs.Count));
    foreach (var (type, name, description) in codecs) {
      body.Write(_UInt16(type));
      body.Write(_UInt16((ushort)(name.Length + 1)));
      body.Write(Encoding.Unicode.GetBytes(name + '\0'));
      body.Write(_UInt16((ushort)(description.Length + 1)));
      body.Write(Encoding.Unicode.GetBytes(description + '\0'));
      body.Write(_UInt16(0));
    }

    return body.ToArray();
  }

  /// <summary>A <c>WM/Picture</c> value: what it is for, the picture's length, two wide strings, the picture.</summary>
  internal static byte[] Picture(byte kind, string mimeType, string description, byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    var body = new MemoryStream();
    body.WriteByte(kind);
    body.Write(_UInt32((uint)data.Length));
    body.Write(Encoding.Unicode.GetBytes(mimeType + '\0'));
    body.Write(Encoding.Unicode.GetBytes(description + '\0'));
    body.Write(data);
    return body.ToArray();
  }

  // ------------------------------------------------------------------------------------------
  // Data packets
  // ------------------------------------------------------------------------------------------

  /// <summary>
  /// Writes one data packet at the file's fixed size.
  /// </summary>
  /// <remarks>
  /// The padding is what makes the size fixed: a packet holds what it holds and the rest of the room is
  /// declared as padding, which is why the padding length is computed here rather than chosen. A
  /// single-payload packet states no length for its payload at all — the payload is whatever lies
  /// between the header and the padding, which only works because the packet's own length is known.
  /// </remarks>
  private static byte[] _Packet(AsfTestPacket packet, int packetSize, long preroll) {
    var multiple = !packet.SinglePayload;

    var payloads = new MemoryStream();
    foreach (var payload in packet.Payloads)
      payloads.Write(_Payload(payload, multiple, preroll));

    // Error correction flags: present, length type zero, two bytes of data — which is what ffmpeg writes.
    var head = new MemoryStream();
    if (packet.ErrorCorrection) {
      head.WriteByte(0x82);
      head.WriteByte(0x00);
      head.WriteByte(0x00);
    }

    // Replicated data one byte long, offset four, media object number one, stream number one — the
    // widths ffmpeg writes for every packet it produces.
    const byte PROPERTY_FLAGS = 0x01 | (0x03 << 2) | (0x01 << 4) | (0x01 << 6);

    // The padding field has to be wide enough for the padding, and how much padding there is depends on
    // how wide the field is. One byte covers a nearly full packet; a packet holding one small frame
    // needs two, and writing it in one wraps the count — which ffmpeg reads as a payload running off
    // the end of the packet rather than as a short one followed by filler.
    var baseLength = (int)head.Length + 2 + (packet.ExplicitLength ? 4 : 0) + 6 + (multiple ? 1 : 0);
    var paddingWidth = packetSize - (baseLength + 1) - (int)payloads.Length <= byte.MaxValue ? 1 : 2;
    var padding = packetSize - (baseLength + paddingWidth) - (int)payloads.Length;
    if (padding < 0)
      throw new InvalidOperationException($"The packet holds {payloads.Length} bytes of payload, which does not fit a {packetSize}-byte packet.");

    var lengthTypeFlags = (byte)(multiple ? 0x01 : 0x00);
    lengthTypeFlags |= (byte)(paddingWidth == 1 ? 0x08 : 0x10);
    if (packet.ExplicitLength)
      lengthTypeFlags |= 0x60;
    if (packet.ErrorCorrection)
      lengthTypeFlags |= 0x80;

    var written = new MemoryStream();
    written.Write(head.ToArray());
    written.WriteByte(lengthTypeFlags);
    written.WriteByte(PROPERTY_FLAGS);
    if (packet.ExplicitLength)
      written.Write(_UInt32((uint)packetSize));

    if (paddingWidth == 1)
      written.WriteByte((byte)padding);
    else
      written.Write(_UInt16((ushort)padding));

    written.Write(_UInt32(0));
    written.Write(_UInt16(0));

    if (multiple) {
      // Number of payloads in the low six bits, payload length type two bytes in the top two.
      written.WriteByte((byte)(packet.Payloads.Count | (0x02 << 6)));
    }

    written.Write(payloads.ToArray());
    written.Write(new byte[padding]);

    var result = written.ToArray();
    if (result.Length != packetSize)
      throw new InvalidOperationException($"Built a {result.Length}-byte packet where {packetSize} was wanted.");

    return result;
  }

  private static byte[] _Payload(AsfTestPayload payload, bool multiple, long preroll) {
    var body = new MemoryStream();
    body.WriteByte((byte)(payload.Stream | (payload.KeyFrame ? 0x80 : 0)));
    body.WriteByte((byte)payload.MediaObjectNumber);

    if (payload.SubObjects != null) {
      // The compressed form: what would have been the offset is the first object's presentation time,
      // and the one byte of replicated data is the step between one object and the next.
      body.Write(_UInt32((uint)(payload.PresentationTime + preroll)));
      body.WriteByte(1);
      body.WriteByte(payload.PresentationTimeDelta);

      var packed = new MemoryStream();
      foreach (var sub in payload.SubObjects) {
        packed.WriteByte((byte)sub.Length);
        packed.Write(sub);
      }

      if (multiple)
        body.Write(_UInt16((ushort)packed.Length));

      body.Write(packed.ToArray());
      return body.ToArray();
    }

    body.Write(_UInt32((uint)payload.Offset));
    body.WriteByte(8);
    body.Write(_UInt32((uint)(payload.MediaObjectSize ?? payload.Data.Length)));
    body.Write(_UInt32((uint)(payload.PresentationTime + preroll)));

    if (multiple)
      body.Write(_UInt16((ushort)payload.Data.Length));

    body.Write(payload.Data);
    return body.ToArray();
  }

  // ------------------------------------------------------------------------------------------
  // Plumbing
  // ------------------------------------------------------------------------------------------

  /// <summary>An object: its identifier, its length counting the header, and its payload.</summary>
  private static byte[] _Object(ReadOnlySpan<byte> id, byte[] body) {
    var element = new MemoryStream();
    element.Write(id);
    element.Write(_UInt64((ulong)(24 + body.Length)));
    element.Write(body);
    return element.ToArray();
  }

  private static byte[] _UInt16(ushort value) {
    var bytes = new byte[2];
    BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
    return bytes;
  }

  private static byte[] _UInt32(uint value) {
    var bytes = new byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
    return bytes;
  }

  private static byte[] _UInt64(ulong value) {
    var bytes = new byte[8];
    BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
    return bytes;
  }
}
