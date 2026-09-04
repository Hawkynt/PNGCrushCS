using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Ogg;

/// <summary>
/// Writes what a file says about itself into the comment header of one logical bitstream.
/// </summary>
/// <remarks>
/// <b>Why this exists.</b> Ogg itself carries no metadata whatever — a page states a serial number
/// and a granule position and nothing else — so a title has exactly one place to live: the comment
/// header the codec mappings put second among their header packets. This reader had always read it
/// and this writer had always copied whatever comment header arrived in
/// <see cref="MediaStreamInfo.CodecPrivateData"/>, which meant a title read out of an AVI and muxed
/// into an Ogg was simply gone, and a title changed on the way through was silently the old one.
/// <para/>
/// <b>The shape.</b> One structure serves Theora, Vorbis and Opus, and each introduces it with a
/// magic of its own — <c>0x81 "theora"</c>, <c>0x03 "vorbis"</c>, <c>"OpusTags"</c>. After the magic
/// come a vendor string and a list of <c>NAME=value</c> lines, every length a little-endian 32-bit
/// count and every string UTF-8: Vorbis I section 5 for the structure, Theora specification
/// section 6.3 and RFC 7845 section 5.2 for the two that borrow it. Only Vorbis ends the header with
/// a framing bit; the other two end at the last comment, which is what libtheora and libopus write
/// and what a decoder of either expects.
/// <para/>
/// <b>The vendor string.</b> Taken from <see cref="VideoMetadata.EncodedBy"/> where the source stated
/// one, because that is where this package's reader reads it back from, and otherwise from the header
/// that arrived — the library that produced the coded packets did not change on the way through, and
/// overwriting its name with this one's would be a false claim about who encoded the film.
/// <para/>
/// <b>What is not written.</b> The duration, which Ogg states as the granule position of the last
/// page rather than as a comment and which the writer already sets; and cover art, which a Vorbis
/// comment carries as a base64 <c>METADATA_BLOCK_PICTURE</c> line that nothing here reads back.
/// </remarks>
internal static class VorbisCommentWriter {

  /// <summary>The vendor string written when neither the metadata nor the source header names one.</summary>
  private const string _VENDOR = "PNGCrushCS";

  /// <summary>
  /// The field names filled from the metadata's own fields, which an annotation may not be written
  /// under a second time.
  /// </summary>
  /// <remarks>
  /// The reader files every comment it reads under its own name as well as under the field it fills,
  /// so a file read and written back arrives here with a <c>TITLE</c> annotation and a title that are
  /// the same thing. Writing both would double the line on every pass.
  /// </remarks>
  private static readonly HashSet<string> _Reserved = new(StringComparer.OrdinalIgnoreCase) {
    "TITLE", "ARTIST", "ALBUM", "ENCODER", "DATE",
  };

  /// <summary>
  /// Rebuilds a bitstream's comment header around the metadata, or leaves it alone.
  /// </summary>
  /// <param name="header">The header packet as it arrived, magic and all.</param>
  /// <param name="metadata">What the file says about itself.</param>
  /// <param name="rewritten">The header to write in its place, where one was built.</param>
  /// <returns>
  /// <see langword="false"/> when the packet is no comment header this knows how to write — a FLAC
  /// mapping, whose comment is a metadata block rather than a packet, or a mapping nothing here
  /// recognises — and <see langword="false"/> equally when the metadata has nothing to say, so that a
  /// file with no title comes out looking like the one that went in rather than one this package
  /// signed on the way through.
  /// </returns>
  internal static bool TryRewrite(ReadOnlyMemory<byte> header, VideoMetadata metadata, out ReadOnlyMemory<byte> rewritten) {
    ArgumentNullException.ThrowIfNull(metadata);
    rewritten = header;

    var magic = _MagicLength(header.Span, out var framingBit);
    if (magic < 0)
      return false;

    var comments = _Comments(metadata);
    if (comments.Count == 0)
      return false;

    var vendor = _Text(metadata.EncodedBy) ?? _StatedVendor(header.Span[magic..]) ?? _VENDOR;

    using var body = new MemoryStream();
    body.Write(header.Span[..magic]);
    _WriteString(body, vendor);

    Span<byte> count = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(count, (uint)comments.Count);
    body.Write(count);

    foreach (var comment in comments)
      _WriteString(body, comment);

    if (framingBit)
      body.WriteByte(0x01);

    rewritten = body.ToArray();
    return true;
  }

  /// <summary>How long the magic introducing this comment header is, or -1 for none this knows.</summary>
  private static int _MagicLength(ReadOnlySpan<byte> header, out bool framingBit) {
    framingBit = false;

    if (header.Length >= 8 && header[..8].SequenceEqual("OpusTags"u8))
      return 8;

    if (header.Length < 7)
      return -1;

    if (header[0] == 0x81 && header[1..7].SequenceEqual("theora"u8))
      return 7;

    if (header[0] != 0x03 || !header[1..7].SequenceEqual("vorbis"u8))
      return -1;

    // Vorbis I section 5.2: the comment header, and only it, ends with a set framing bit.
    framingBit = true;
    return 7;
  }

  /// <summary>The vendor string the arriving header stated, where it stated a readable one.</summary>
  private static string? _StatedVendor(ReadOnlySpan<byte> comment) {
    if (comment.Length < 4)
      return null;

    var length = BinaryPrimitives.ReadUInt32LittleEndian(comment);

    return length <= (uint)(comment.Length - 4) ? Encoding.UTF8.GetString(comment.Slice(4, (int)length)) : null;
  }

  /// <summary>The <c>NAME=value</c> lines the metadata amounts to, in the order they are written.</summary>
  private static List<string> _Comments(VideoMetadata metadata) {
    var comments = new List<string>();
    var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    void Add(string name, string? value) {
      var text = _Text(value);
      if (text != null && written.Add(name))
        comments.Add($"{name}={text}");
    }

    Add("TITLE", metadata.Title);
    Add("ARTIST", metadata.Artist);
    Add("ALBUM", metadata.Album);
    Add("ENCODER", metadata.EncodedBy);

    // The Vorbis comment field for a date is ISO 8601, and written in full so that the offset the
    // source stated survives rather than being flattened to a day.
    if (metadata.CreationTime is { } created)
      Add("DATE", created.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture));

    foreach (var entry in metadata.TextEntries) {
      var text = _Text(entry.Text);
      if (text == null)
        continue;

      var name = _FieldName(entry.Keyword);
      if (name == null || _Reserved.Contains(name) || !written.Add(name))
        continue;

      comments.Add($"{name}={text}");
    }

    return comments;
  }

  /// <summary>
  /// An annotation's keyword as a comment field name, or <see langword="null"/> where it cannot be
  /// one.
  /// </summary>
  /// <remarks>
  /// A field name is printable ASCII without the equals sign that separates it from its value —
  /// Vorbis comment specification section 5.4.2.1 — so a keyword carrying one would be read back as a
  /// different field with a different value. The annotation is dropped rather than written as
  /// something it is not. Case is not significant in a field name and upper case is the convention,
  /// which is also what this package's reader reports, so a keyword round-trips through its own name.
  /// </remarks>
  private static string? _FieldName(string? keyword) {
    if (string.IsNullOrWhiteSpace(keyword))
      return null;

    foreach (var character in keyword)
      if (character is < ' ' or > '}' or '=')
        return null;

    return keyword.ToUpperInvariant();
  }

  private static string? _Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

  private static void _WriteString(Stream output, string value) {
    var bytes = Encoding.UTF8.GetBytes(value);
    Span<byte> length = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)bytes.Length);
    output.Write(length);
    output.Write(bytes);
  }
}
