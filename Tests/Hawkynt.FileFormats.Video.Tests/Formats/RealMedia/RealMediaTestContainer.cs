using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.RealMedia.Tests;

/// <summary>One stream to be declared in a built file.</summary>
/// <param name="Number">The stream number the file gives it, which need not be its index.</param>
/// <param name="MimeType">The mime type that says what kind of stream it is.</param>
/// <param name="Name">The name the writer gave it.</param>
/// <param name="Description">The type-specific bytes describing it.</param>
internal readonly record struct RealMediaTestStream(int Number, string MimeType, string Name, byte[] Description);

/// <summary>One packet to be written into a built file's data chunk.</summary>
/// <param name="StreamNumber">Which stream it belongs to.</param>
/// <param name="Timestamp">When it is due, in milliseconds.</param>
/// <param name="Payload">The bytes after the packet header, exactly as they go into the file.</param>
/// <param name="IsKeyFrame">Whether to set the flag marking a packet decoding may begin at.</param>
/// <param name="Version">The packet header's object version, 0 or 1.</param>
internal readonly record struct RealMediaTestPacket(
  int StreamNumber, long Timestamp, byte[] Payload, bool IsKeyFrame = false, int Version = 0);

/// <summary>
/// Builds RealMedia files byte by byte so the reader can be tested without a sample in the tree.
/// </summary>
/// <remarks>
/// The layout is the one the sample recordings hold, read off their hexdumps: a <c>.RMF</c> header, a
/// <c>PROP</c>, a <c>CONT</c>, one <c>MDPR</c> per stream and a <c>DATA</c> holding the packets.
/// <para/>
/// It exists mostly for the shapes no encoder still made will produce. A picture that arrives in
/// pieces with one of them sent twice needs a lossy network; a picture completing without ever being
/// marked as complete needs an encoder from 1999; a data chunk whose length was never filled in needs
/// a recording that was cut off. Every one of those is a branch of the reader, and every one is where
/// a reader that looks right goes wrong — so each is built here and asserted on, while the shapes real
/// files do hold are measured against ffprobe on the real files separately.
/// <para/>
/// Nothing here is a valid picture or a valid sound. The payloads are whatever bytes a test hands
/// over, which is all a demuxer ever looks at: it reports where a packet is and never what is in it.
/// </remarks>
internal static class RealMediaTestContainer {

  internal const string VIDEO_MIME = "video/x-pn-realvideo";
  internal const string AUDIO_MIME = "audio/x-pn-realaudio";
  internal const string FILE_INFO_MIME = "logical-fileinfo";

  /// <summary>Writes a file declaring the streams given and holding the packets given.</summary>
  /// <param name="streams">The streams, in the order their descriptions go into the file.</param>
  /// <param name="packets">The packets, in the order they go into the data chunk.</param>
  /// <param name="title">The content description's title.</param>
  /// <param name="author">The content description's author.</param>
  /// <param name="copyright">The content description's copyright.</param>
  /// <param name="comment">The content description's comment.</param>
  /// <param name="durationMilliseconds">The duration the file properties claim.</param>
  /// <param name="dataLength">The length to write into the data chunk, or <c>null</c> for the true
  /// one. A writer that never closed the file leaves a zero here.</param>
  internal static byte[] Build(
    IEnumerable<RealMediaTestStream> streams,
    IEnumerable<RealMediaTestPacket> packets,
    string title = "", string author = "", string copyright = "", string comment = "",
    long durationMilliseconds = 0,
    int? dataLength = null) {
    ArgumentNullException.ThrowIfNull(streams);
    ArgumentNullException.ThrowIfNull(packets);

    var streamList = new List<RealMediaTestStream>(streams);

    using var body = new MemoryStream();
    _WriteChunk(body, "PROP", 0, _Properties(streamList.Count, durationMilliseconds));
    _WriteChunk(body, "CONT", 0, _Content(title, author, copyright, comment));
    foreach (var stream in streamList)
      _WriteChunk(body, "MDPR", 0, _MediaProperties(stream));

    using var data = new MemoryStream();
    _WriteUInt32(data, 0);
    _WriteUInt32(data, 0);
    foreach (var packet in packets)
      _WritePacket(data, packet);

    using var file = new MemoryStream();
    _WriteChunk(file, ".RMF", 0, _FileHeader(streamList.Count + 3));
    body.WriteTo(file);
    _WriteChunk(file, "DATA", 0, data.ToArray(), dataLength);

    return file.ToArray();
  }

  // ------------------------------------------------------------------------------------------
  // Stream descriptions
  // ------------------------------------------------------------------------------------------

  /// <summary>A video stream's description: the fixed fields and then the codec's own bytes.</summary>
  internal static RealMediaTestStream VideoStream(
    int number, string fourCharacterCode, int width, int height, string name = "Video Stream",
    uint frameRateFixedPoint = 15 << 16, byte[]? codecPrivateData = null, string marker = "VIDO") {
    using var description = new MemoryStream();
    _WriteUInt32(description, 0);
    description.Write(Encoding.ASCII.GetBytes(marker));
    description.Write(Encoding.ASCII.GetBytes(fourCharacterCode));
    _WriteUInt16(description, width);
    _WriteUInt16(description, height);
    _WriteUInt16(description, 24);
    _WriteUInt16(description, 0);
    _WriteUInt16(description, 0);
    _WriteUInt32(description, frameRateFixedPoint);
    description.Write(codecPrivateData ?? []);

    var bytes = description.ToArray();
    _PatchUInt32(bytes, 0, (uint)bytes.Length);
    return new(number, VIDEO_MIME, name, bytes);
  }

  /// <summary>
  /// A sound stream's description, as a RealAudio header of version 4 or 5.
  /// </summary>
  /// <remarks>
  /// Only the fields the reader looks at are meaningful: the signature, the version and the code
  /// naming the codec, which the two versions put in different places. The rest is zero, because the
  /// reader hands the whole header across untouched and reads nothing else out of it.
  /// </remarks>
  internal static RealMediaTestStream AudioStream(
    int number, string fourCharacterCode, int version = 5, string name = "Audio Stream") {
    var size = version == 4 ? 73 : 86;
    var description = new byte[size];
    description[0] = (byte)'.';
    description[1] = (byte)'r';
    description[2] = (byte)'a';
    description[3] = 0xFD;
    description[4] = 0;
    description[5] = (byte)version;

    var code = Encoding.ASCII.GetBytes(fourCharacterCode);
    if (version == 4) {
      description[0x38] = 4;
      "Int0"u8.CopyTo(description.AsSpan(0x39));
      description[0x3D] = 4;
      code.CopyTo(description.AsSpan(0x3E));
    } else {
      "genr"u8.CopyTo(description.AsSpan(0x3E));
      code.CopyTo(description.AsSpan(0x42));
    }

    return new(number, AUDIO_MIME, name, description);
  }

  /// <summary>The chunk that describes the file rather than a stream, holding text pairs.</summary>
  internal static RealMediaTestStream FileInfoStream(int number, params (string Name, string Value)[] entries) {
    ArgumentNullException.ThrowIfNull(entries);

    using var description = new MemoryStream();
    _WriteUInt32(description, 0);
    _WriteUInt32(description, 0);
    _WriteUInt32(description, (uint)entries.Length);

    foreach (var (name, value) in entries) {
      var nameBytes = Encoding.Latin1.GetBytes(name);
      var valueBytes = Encoding.Latin1.GetBytes(value + '\0');

      _WriteUInt32(description, (uint)(4 + 1 + 2 + nameBytes.Length + 4 + 2 + valueBytes.Length));
      description.WriteByte(0);
      _WriteUInt16(description, nameBytes.Length);
      description.Write(nameBytes);
      _WriteUInt32(description, 2);
      _WriteUInt16(description, valueBytes.Length);
      description.Write(valueBytes);
    }

    var bytes = description.ToArray();
    _PatchUInt32(bytes, 0, (uint)bytes.Length);
    return new(number, FILE_INFO_MIME, string.Empty, bytes);
  }

  // ------------------------------------------------------------------------------------------
  // Video payload elements
  // ------------------------------------------------------------------------------------------

  /// <summary>A whole picture filling the rest of the packet.</summary>
  internal static byte[] WholeFrame(byte[] data, int sequence = 1) {
    ArgumentNullException.ThrowIfNull(data);

    using var element = new MemoryStream();
    element.WriteByte(0x40);
    element.WriteByte((byte)sequence);
    element.Write(data);
    return element.ToArray();
  }

  /// <summary>A whole picture carrying its own length, so another element may follow it.</summary>
  internal static byte[] PackedFrame(byte[] data, int pictureNumber = 0) {
    ArgumentNullException.ThrowIfNull(data);

    using var element = new MemoryStream();
    element.WriteByte(0xC0);
    _WriteNumber(element, data.Length);
    _WriteNumber(element, 0);
    element.WriteByte((byte)pictureNumber);
    element.Write(data);
    return element.ToArray();
  }

  /// <summary>
  /// One piece of a picture that is not the last: the whole length and this piece's offset.
  /// </summary>
  internal static byte[] Piece(byte[] data, int frameLength, int offset, int sequence = 1, int pictureNumber = 0)
    => _Piece(0x00, data, frameLength, offset, sequence, pictureNumber);

  /// <summary>
  /// The last piece of a picture: the whole length and this piece's <em>own</em> length, the offset
  /// following by subtraction.
  /// </summary>
  internal static byte[] LastPiece(byte[] data, int frameLength, int sequence = 1, int pictureNumber = 0) {
    ArgumentNullException.ThrowIfNull(data);

    return _Piece(0x80, data, frameLength, data.Length, sequence, pictureNumber);
  }

  private static byte[] _Piece(byte kind, byte[] data, int frameLength, int second, int sequence, int pictureNumber) {
    ArgumentNullException.ThrowIfNull(data);

    using var element = new MemoryStream();
    element.WriteByte(kind);
    element.WriteByte((byte)sequence);
    _WriteNumber(element, frameLength);
    _WriteNumber(element, second);
    element.WriteByte((byte)pictureNumber);
    element.Write(data);
    return element.ToArray();
  }

  /// <summary>Joins several payload elements into one packet payload.</summary>
  internal static byte[] Elements(params byte[][] elements) {
    ArgumentNullException.ThrowIfNull(elements);

    using var joined = new MemoryStream();
    foreach (var element in elements)
      joined.Write(element);

    return joined.ToArray();
  }

  /// <summary>
  /// Writes one of the two numbers a piece's sub-header carries, in the shorter form where it fits.
  /// </summary>
  /// <remarks>
  /// The long form is written for anything that will not fit in fourteen bits, which is what a
  /// picture longer than sixteen kilobytes needs. Both forms are exercised: the short one by every
  /// small built file, the long one by the test that builds a picture over that size.
  /// </remarks>
  private static void _WriteNumber(Stream into, int value) {
    if (value < 0x4000) {
      _WriteUInt16(into, 0x4000 | value);
      return;
    }

    _WriteUInt16(into, value >> 16);
    _WriteUInt16(into, value & 0xFFFF);
  }

  // ------------------------------------------------------------------------------------------
  // Chunks
  // ------------------------------------------------------------------------------------------

  private static byte[] _FileHeader(int chunkCount) {
    using var body = new MemoryStream();
    _WriteUInt32(body, 0);
    _WriteUInt32(body, (uint)chunkCount);
    return body.ToArray();
  }

  private static byte[] _Properties(int streamCount, long durationMilliseconds) {
    using var body = new MemoryStream();
    for (var i = 0; i < 5; ++i)
      _WriteUInt32(body, 0);

    _WriteUInt32(body, (uint)durationMilliseconds);
    for (var i = 0; i < 3; ++i)
      _WriteUInt32(body, 0);

    _WriteUInt16(body, streamCount);
    _WriteUInt16(body, 0);
    return body.ToArray();
  }

  private static byte[] _Content(string title, string author, string copyright, string comment) {
    using var body = new MemoryStream();
    foreach (var text in new[] { title, author, copyright, comment }) {
      var bytes = Encoding.Latin1.GetBytes(text);
      _WriteUInt16(body, bytes.Length);
      body.Write(bytes);
    }

    return body.ToArray();
  }

  private static byte[] _MediaProperties(RealMediaTestStream stream) {
    using var body = new MemoryStream();
    _WriteUInt16(body, stream.Number);
    for (var i = 0; i < 7; ++i)
      _WriteUInt32(body, 0);

    var name = Encoding.Latin1.GetBytes(stream.Name);
    body.WriteByte((byte)name.Length);
    body.Write(name);

    var mime = Encoding.Latin1.GetBytes(stream.MimeType);
    body.WriteByte((byte)mime.Length);
    body.Write(mime);

    _WriteUInt32(body, (uint)stream.Description.Length);
    body.Write(stream.Description);
    return body.ToArray();
  }

  private static void _WritePacket(Stream into, RealMediaTestPacket packet) {
    var header = packet.Version == 0 ? 12 : 13;
    _WriteUInt16(into, packet.Version);
    _WriteUInt16(into, header + packet.Payload.Length);
    _WriteUInt16(into, packet.StreamNumber);
    _WriteUInt32(into, (uint)packet.Timestamp);

    if (packet.Version == 0)
      into.WriteByte(0);
    else
      _WriteUInt16(into, 0);

    into.WriteByte(packet.IsKeyFrame ? (byte)0x02 : (byte)0x00);
    into.Write(packet.Payload);
  }

  private static void _WriteChunk(Stream into, string name, int version, byte[] body, int? statedLength = null) {
    into.Write(Encoding.ASCII.GetBytes(name));
    _WriteUInt32(into, (uint)(statedLength ?? (10 + body.Length)));
    _WriteUInt16(into, version);
    into.Write(body);
  }

  private static void _WriteUInt32(Stream into, uint value) {
    into.WriteByte((byte)(value >> 24));
    into.WriteByte((byte)(value >> 16));
    into.WriteByte((byte)(value >> 8));
    into.WriteByte((byte)value);
  }

  private static void _WriteUInt16(Stream into, int value) {
    into.WriteByte((byte)(value >> 8));
    into.WriteByte((byte)value);
  }

  private static void _PatchUInt32(byte[] bytes, int at, uint value) {
    bytes[at] = (byte)(value >> 24);
    bytes[at + 1] = (byte)(value >> 16);
    bytes[at + 2] = (byte)(value >> 8);
    bytes[at + 3] = (byte)value;
  }
}
