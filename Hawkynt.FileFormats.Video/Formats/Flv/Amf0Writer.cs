using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Flv;

/// <summary>
/// Writes the <c>onMetaData</c> script tag an FLV states what it is about itself in.
/// </summary>
/// <remarks>
/// The mirror of <see cref="Amf0Reader"/>, and deliberately much smaller than it: a reader has to
/// take whatever a Flash-era tool wrote, and a writer only has to produce the one message every
/// player looks for. Three of AMF0's types are enough for that — a number, a string and a date —
/// inside the ECMA array <c>onMetaData</c>'s payload is.
/// <para/>
/// <b>Why this exists at all.</b> Without it a remux through FLV silently dropped the title, the
/// author, the album, the encoder's name and every comment the source carried, because the reader
/// read them and the writer had nowhere to put them. A demuxer that reads a field and a muxer that
/// cannot write it are not a round trip; they are a quiet deletion.
/// <para/>
/// <b>What is written, and what is not.</b> Only what a container carries <i>about</i> a film: the
/// title, the author, the album, the encoder, the creation date, the duration, and any further text
/// the source annotated it with. The measurements a writer conventionally also announces — the
/// picture size, the frame rate, the codec numbers, the keyframe index — are not written, because
/// this package's own reader derives every one of them from the tags themselves and announcing a
/// second, possibly disagreeing copy would be inventing a claim rather than carrying one across.
/// </remarks>
internal static class Amf0Writer {

  private const byte _NUMBER = 0x00;
  private const byte _STRING = 0x02;
  private const byte _ECMA_ARRAY = 0x08;
  private const byte _OBJECT_END = 0x09;
  private const byte _DATE = 0x0B;

  /// <summary>
  /// The property names <c>onMetaData</c> reserves for measurements, which an annotation may not be
  /// written under.
  /// </summary>
  /// <remarks>
  /// A source whose comment happens to be filed under the keyword <c>duration</c> would otherwise be
  /// written where every reader — this package's included — expects a number, and would come back as
  /// a duration of nothing rather than as the comment it was. The entry is dropped instead, which
  /// loses one annotation rather than corrupting a field.
  /// </remarks>
  private static readonly HashSet<string> _Reserved = new(StringComparer.OrdinalIgnoreCase) {
    "duration", "width", "height", "framerate", "videoframerate", "videocodecid", "videodatarate",
    "audiocodecid", "audiodatarate", "audiosamplerate", "audiosamplesize", "audiodelay", "stereo",
    "filesize", "lasttimestamp", "lastkeyframetimestamp", "hasVideo", "hasAudio", "hasMetadata",
    "hasKeyframes", "hasCuePoints", "canSeekToEnd", "keyframes", "cuePoints",
    "title", "artist", "album", "encoder", "metadatacreator", "comment", "copyright",
    "creationdate", "datecreated",
  };

  /// <summary>
  /// Builds the body of an <c>onMetaData</c> script tag, or <c>null</c> when there is nothing in the
  /// metadata worth a tag.
  /// </summary>
  /// <remarks>
  /// Nothing rather than an empty message: a file with no title and no comments should look like a
  /// file that was written without a script tag, which is what every FLV of the era with nothing to
  /// say looks like.
  /// </remarks>
  internal static byte[]? OnMetaData(VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(metadata);

    var properties = new List<(string Name, Action<MemoryStream> Write)>();

    void Text(string name, string? value) {
      if (!string.IsNullOrWhiteSpace(value))
        properties.Add((name, output => _WriteString(output, value!)));
    }

    Text("title", metadata.Title);
    Text("artist", metadata.Artist);
    Text("album", metadata.Album);
    Text("encoder", metadata.EncodedBy);

    if (metadata.CreationTime is { } created)
      properties.Add(("creationdate", output => _WriteDate(output, created)));

    if (metadata.Duration is { TotalSeconds: > 0 } duration)
      properties.Add(("duration", output => _WriteNumber(output, duration.TotalSeconds)));

    var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var entry in metadata.TextEntries) {
      if (string.IsNullOrWhiteSpace(entry.Keyword) || string.IsNullOrWhiteSpace(entry.Text))
        continue;

      // The two the reader names for their own sake go back under the names it reads them from;
      // anything else keeps the keyword it arrived with, which is what the reader falls back to.
      var name = entry.Keyword.Equals("Comment", StringComparison.OrdinalIgnoreCase) ? "comment"
        : entry.Keyword.Equals("Copyright", StringComparison.OrdinalIgnoreCase) ? "copyright"
        : entry.Keyword;

      if (_Reserved.Contains(name) && name is not ("comment" or "copyright"))
        continue;

      if (!written.Add(name))
        continue;

      var text = entry.Text;
      properties.Add((name, output => _WriteString(output, text)));
    }

    if (properties.Count == 0)
      return null;

    var body = new MemoryStream();
    // The message's name is a whole AMF0 value and not a bare length-prefixed name: a script tag is
    // two values, and a reader takes the marker byte off the front of each.
    _WriteString(body, "onMetaData");

    body.WriteByte(_ECMA_ARRAY);
    // The count is the writer's claim about how many properties follow. Every reader worth the name
    // stops at the terminator instead, this package's own included, but the field is not optional.
    Span<byte> count = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(count, (uint)properties.Count);
    body.Write(count);

    foreach (var (name, write) in properties) {
      _WriteShortString(body, name);
      write(body);
    }

    // The terminator: an empty name and the object-end marker, the one place in AMF0 where a name is
    // not followed by a value.
    body.WriteByte(0);
    body.WriteByte(0);
    body.WriteByte(_OBJECT_END);

    return body.ToArray();
  }

  private static void _WriteNumber(MemoryStream output, double value) {
    output.WriteByte(_NUMBER);
    Span<byte> bytes = stackalloc byte[8];
    BinaryPrimitives.WriteInt64BigEndian(bytes, BitConverter.DoubleToInt64Bits(value));
    output.Write(bytes);
  }

  private static void _WriteDate(MemoryStream output, DateTimeOffset value) {
    output.WriteByte(_DATE);
    Span<byte> bytes = stackalloc byte[8];
    BinaryPrimitives.WriteInt64BigEndian(bytes, BitConverter.DoubleToInt64Bits(value.ToUnixTimeMilliseconds()));
    output.Write(bytes);

    // Two bytes of time zone the specification reserves and every writer leaves at zero; the instant
    // itself is UTC.
    output.WriteByte(0);
    output.WriteByte(0);
  }

  private static void _WriteString(MemoryStream output, string value) {
    output.WriteByte(_STRING);
    _WriteShortString(output, value);
  }

  /// <summary>Writes a length-prefixed UTF-8 string, which is how AMF0 writes a name.</summary>
  private static void _WriteShortString(MemoryStream output, string value) {
    var bytes = Encoding.UTF8.GetBytes(value);
    if (bytes.Length > ushort.MaxValue)
      throw new NotSupportedException(
        $"An AMF0 name or string is at most {ushort.MaxValue} bytes of UTF-8; '{value[..Math.Min(value.Length, 32)]}…' is {bytes.Length}.");

    Span<byte> length = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)bytes.Length);
    output.Write(length);
    output.Write(bytes);
  }
}
