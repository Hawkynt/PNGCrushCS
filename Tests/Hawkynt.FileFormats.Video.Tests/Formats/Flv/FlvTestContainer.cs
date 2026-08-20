using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.Flv.Tests;

/// <summary>One tag to be written into a built file, already carrying whatever prefix its type needs.</summary>
/// <param name="Type">The tag type byte, filter bit and all.</param>
/// <param name="Timestamp">When the tag is due, in milliseconds; the writer splits it across the two
/// fields the format keeps it in.</param>
/// <param name="Data">The tag's payload exactly as it goes into the file.</param>
internal readonly record struct FlvTestTag(byte Type, long Timestamp, byte[] Data);

/// <summary>
/// Builds FLV files byte by byte so the reader can be tested without a sample in the tree.
/// </summary>
/// <remarks>
/// The layout is the one ffmpeg writes, read off a hexdump of its own output: a nine-byte header, a
/// zero <c>PreviousTagSize</c>, then tags each preceded by the length of the one before it, and a
/// final <c>PreviousTagSize</c> with nothing after it.
/// <para/>
/// It exists mostly for the shapes ffmpeg will not produce for a file small enough to check by hand. A
/// timestamp past 2^24 milliseconds needs four and a half hours of film; a filtered tag needs an
/// encrypting server; a stream of Screen Video needs an encoder nothing ships any more. Every one of
/// those is a branch of the reader, and the ones ffmpeg does write are measured against ffprobe
/// separately.
/// <para/>
/// Nothing here is a valid picture or a valid sound. The payloads are whatever bytes a test hands
/// over, which is all a demuxer ever looks at: it reports where a packet is and never what is in it.
/// </remarks>
internal static class FlvTestContainer {

  internal const byte AUDIO_TAG = 8;
  internal const byte VIDEO_TAG = 9;
  internal const byte SCRIPT_TAG = 18;

  /// <summary>The bit that says the payload is preceded by a filter header.</summary>
  internal const byte FILTER_FLAG = 0x20;

  internal const byte HAS_VIDEO = 0x01;
  internal const byte HAS_AUDIO = 0x04;

  private const int _HEADER_SIZE = 9;
  private const int _TAG_HEADER_SIZE = 11;

  /// <summary>Writes a file out of the tags given, with the header a writer of both kinds would emit.</summary>
  /// <param name="tags">The tags, in the order they go into the file.</param>
  /// <param name="flags">The header's flags byte, which the reader deliberately does not believe.</param>
  /// <param name="version">The header's version byte.</param>
  /// <param name="padding">Bytes to put between the header and the first tag, which the header's
  /// stated data offset then has to skip.</param>
  internal static byte[] Build(IEnumerable<FlvTestTag> tags, byte flags = HAS_VIDEO | HAS_AUDIO, byte version = 1, int padding = 0) {
    ArgumentNullException.ThrowIfNull(tags);

    using var file = new MemoryStream();
    file.WriteByte((byte)'F');
    file.WriteByte((byte)'L');
    file.WriteByte((byte)'V');
    file.WriteByte(version);
    file.WriteByte(flags);
    _WriteUInt32(file, (uint)(_HEADER_SIZE + padding));

    for (var i = 0; i < padding; ++i)
      file.WriteByte(0);

    var previous = 0u;
    foreach (var tag in tags) {
      _WriteUInt32(file, previous);

      file.WriteByte(tag.Type);
      _WriteUInt24(file, tag.Data.Length);

      // The three low bytes here and the high one four bytes later, which is the field this format is
      // easiest to get backwards.
      _WriteUInt24(file, (int)(tag.Timestamp & 0xFFFFFF));
      file.WriteByte((byte)((tag.Timestamp >> 24) & 0xFF));
      _WriteUInt24(file, 0);

      file.Write(tag.Data, 0, tag.Data.Length);
      previous = (uint)(_TAG_HEADER_SIZE + tag.Data.Length);
    }

    _WriteUInt32(file, previous);
    return file.ToArray();
  }

  /// <summary>A video tag of a codec that has no prefix of its own — everything but AVC.</summary>
  internal static FlvTestTag Video(long timestamp, byte[] payload, int frameType = 1, int codec = 2, bool filtered = false)
    => new((byte)(VIDEO_TAG | (filtered ? FILTER_FLAG : 0)), timestamp, _Concat([(byte)((frameType << 4) | codec)], payload));

  /// <summary>A video tag whose first byte has the extended-header bit set.</summary>
  internal static FlvTestTag ExtendedVideo(long timestamp, byte[] payload)
    => new(VIDEO_TAG, timestamp, _Concat([0x91], payload));

  /// <summary>An AVC video tag: the codec byte, a packet type, a signed composition time, then the payload.</summary>
  internal static FlvTestTag Avc(long timestamp, byte[] payload, int packetType = 1, int compositionTime = 0, int frameType = 1)
    => new(VIDEO_TAG, timestamp, _Concat([
      (byte)((frameType << 4) | 7),
      (byte)packetType,
      (byte)((compositionTime >> 16) & 0xFF),
      (byte)((compositionTime >> 8) & 0xFF),
      (byte)(compositionTime & 0xFF),
    ], payload));

  /// <summary>An audio tag of a sound format that has no prefix of its own — everything but AAC.</summary>
  internal static FlvTestTag Audio(long timestamp, byte[] payload, int soundFormat = 2)
    => new(AUDIO_TAG, timestamp, _Concat([(byte)((soundFormat << 4) | 0x0F)], payload));

  /// <summary>An AAC audio tag: the sound format byte, a packet type, then the payload.</summary>
  internal static FlvTestTag Aac(long timestamp, byte[] payload, int packetType = 1)
    => new(AUDIO_TAG, timestamp, _Concat([(10 << 4) | 0x0F, (byte)packetType], payload));

  /// <summary>An <c>onMetaData</c> script tag holding the properties given, as an AMF0 ECMA array.</summary>
  /// <remarks>
  /// The shape ffmpeg writes: the message name as a plain string, then an ECMA array whose declared
  /// count is the number of properties and which is terminated by an empty name and the object-end
  /// marker like any object.
  /// </remarks>
  internal static FlvTestTag Metadata(params (string Name, object Value)[] properties)
    => Script("onMetaData", properties);

  /// <summary>A script tag under any message name.</summary>
  internal static FlvTestTag Script(string name, params (string Name, object Value)[] properties) {
    ArgumentNullException.ThrowIfNull(name);
    ArgumentNullException.ThrowIfNull(properties);

    using var body = new MemoryStream();
    body.WriteByte(0x02);
    _WriteShortString(body, name);

    body.WriteByte(0x08);
    _WriteUInt32(body, (uint)properties.Length);
    foreach (var (key, value) in properties) {
      _WriteShortString(body, key);
      _WriteValue(body, value);
    }

    _WriteShortString(body, string.Empty);
    body.WriteByte(0x09);

    return new(SCRIPT_TAG, 0, body.ToArray());
  }

  private static void _WriteValue(Stream into, object value) {
    switch (value) {
      case double number:
        into.WriteByte(0x00);
        Span<byte> bits = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bits, BitConverter.DoubleToInt64Bits(number));
        into.Write(bits);
        break;

      case int number:
        _WriteValue(into, (double)number);
        break;

      case bool flag:
        into.WriteByte(0x01);
        into.WriteByte(flag ? (byte)1 : (byte)0);
        break;

      case string text:
        into.WriteByte(0x02);
        _WriteShortString(into, text);
        break;

      default:
        throw new ArgumentException($"nothing here writes an AMF0 {value.GetType().Name}", nameof(value));
    }
  }

  private static void _WriteShortString(Stream into, string text) {
    var bytes = Encoding.UTF8.GetBytes(text);
    into.WriteByte((byte)(bytes.Length >> 8));
    into.WriteByte((byte)bytes.Length);
    into.Write(bytes, 0, bytes.Length);
  }

  private static void _WriteUInt32(Stream into, uint value) {
    into.WriteByte((byte)(value >> 24));
    into.WriteByte((byte)(value >> 16));
    into.WriteByte((byte)(value >> 8));
    into.WriteByte((byte)value);
  }

  private static void _WriteUInt24(Stream into, int value) {
    into.WriteByte((byte)(value >> 16));
    into.WriteByte((byte)(value >> 8));
    into.WriteByte((byte)value);
  }

  private static byte[] _Concat(byte[] prefix, byte[] payload) {
    var result = new byte[prefix.Length + payload.Length];
    prefix.CopyTo(result, 0);
    payload.CopyTo(result, prefix.Length);
    return result;
  }
}
