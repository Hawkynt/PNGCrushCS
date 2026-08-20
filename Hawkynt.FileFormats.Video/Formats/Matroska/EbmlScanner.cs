using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace FileFormat.Matroska;

/// <summary>One EBML element as the scanner found it.</summary>
/// <param name="Id">The element's identifier, as the bytes sit in the file — length marker and all,
/// so the EBML header is <c>0x1A45DFA3</c> and a <c>Cluster</c> is <c>0x1F43B675</c>.</param>
/// <param name="Body">Everything after the identifier and the size, as a window onto the file.</param>
/// <param name="BodyOffset">Where <see cref="Body"/> begins, counted from the start of the file.</param>
/// <param name="Offset">Where the element begins, identifier included, from the file's start.</param>
/// <param name="SizeIsUnknown">Whether the file stated no length for this element, so its extent was
/// found by looking for what comes after it rather than read out of it.</param>
/// <param name="IsTruncated">Whether the file stated a length longer than what is actually there, so
/// <see cref="Body"/> is the part of the element that was written and not the element.</param>
internal readonly record struct EbmlElement(
  uint Id,
  ReadOnlyMemory<byte> Body,
  int BodyOffset,
  int Offset,
  bool SizeIsUnknown,
  bool IsTruncated = false) {

  /// <summary>The body read as an unsigned integer, which is how EBML stores counts and codes.</summary>
  /// <remarks>
  /// Big-endian and of whatever length the writer chose — an EBML unsigned integer is one to eight
  /// bytes and carries no padding, so a <c>TrackNumber</c> of one occupies one byte and the same
  /// element in another file may occupy four. Reading a fixed width would misread every file that
  /// chose the other.
  /// </remarks>
  internal ulong UnsignedValue() {
    var span = this.Body.Span;
    if (span.Length > 8)
      span = span[..8];

    var value = 0UL;
    foreach (var b in span)
      value = (value << 8) | b;

    return value;
  }

  /// <summary>The body read as a signed integer, sign-extended from whatever width it occupies.</summary>
  internal long SignedValue() {
    var span = this.Body.Span;
    if (span.IsEmpty)
      return 0;

    if (span.Length > 8)
      span = span[..8];

    var value = (long)(sbyte)span[0];
    for (var i = 1; i < span.Length; ++i)
      value = (value << 8) | span[i];

    return value;
  }

  /// <summary>The body read as a float, which EBML stores as four or eight big-endian bytes.</summary>
  /// <remarks>
  /// A zero-length float is legal and means zero. Any other length is not a float at all, and
  /// <c>null</c> says so rather than a number being invented from the bytes that are there.
  /// </remarks>
  internal double? FloatValue() {
    var span = this.Body.Span;
    return span.Length switch {
      0 => 0d,
      4 => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(span)),
      8 => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(span)),
      _ => null,
    };
  }

  /// <summary>The body read as text, with the terminator a writer may pad it with taken off.</summary>
  /// <remarks>
  /// EBML strings carry their length, so a terminator is not needed — but a writer may pad a string
  /// out to a length it reserved earlier, and the padding is zero bytes. ffmpeg writes exactly that
  /// for the duration tag of a file it is still muxing.
  /// </remarks>
  internal string? TextValue() {
    var span = this.Body.Span;
    var end = span.IndexOf((byte)0);
    if (end >= 0)
      span = span[..end];

    return span.IsEmpty ? null : Encoding.UTF8.GetString(span);
  }
}

/// <summary>
/// Walks the elements of an EBML document without copying any of them.
/// </summary>
/// <remarks>
/// The whole of EBML is one shape repeated: an identifier, a length, and a payload that is either
/// bytes or more elements. Both the identifier and the length are variable-length integers whose
/// first byte says how long they are — the number of leading zero bits before the first one bit is
/// the number of extra bytes that follow. Getting that one bit wrong shifts every later read by a
/// byte and desynchronises the rest of the file, and the damage reads like corruption rather than
/// like a parser fault, which is why it is decoded in one place and nowhere else.
/// <para/>
/// The two integers differ in what the marker bit means once it has been read. An identifier keeps
/// it — <c>Cluster</c> is <c>0x1F43B675</c> and not <c>0x0F43B675</c>, because the identifier is a
/// key rather than a number and its stored form is its identity. A length drops it, so a one-byte
/// length spans 0 to 126 and the same value written in two bytes means exactly the same thing.
/// <para/>
/// A length whose every value bit is set means the writer did not know the length. That happens for
/// real: a <c>Segment</c> being written to a pipe cannot know its own size, and ffmpeg's <c>-live</c>
/// mode writes both the segment and its clusters that way. Such an element ends where the next
/// element that cannot be one of its children begins — which is why <see cref="Walk"/> takes a
/// predicate for that rather than assuming the document is well formed.
/// <para/>
/// Unknown identifiers are yielded like any other and skipped by whoever is reading. That is not
/// leniency, it is how EBML is specified to be read: <c>Void</c>, <c>CRC-32</c> and every element a
/// later version of the specification adds sit among the ones a reader knows, and a reader that
/// stopped at the first one it did not recognise would not get past the first cluster of a file
/// ffmpeg wrote.
/// </remarks>
internal static class EbmlScanner {

  /// <summary>The longest identifier the EBML header's own default permits.</summary>
  /// <remarks>
  /// <c>EBMLMaxIDLength</c> is written as 4 by every writer measured here, and the specification
  /// gives 4 as its default. A first byte of zero would claim an identifier of at least nine bytes,
  /// which is not an identifier but a desynchronised read.
  /// </remarks>
  internal const int MAX_ID_LENGTH = 4;

  /// <summary>The longest length field EBML allows, which is what <c>EBMLMaxSizeLength</c> defaults to.</summary>
  internal const int MAX_SIZE_LENGTH = 8;

  /// <summary>Reads a variable-length integer used as an element identifier.</summary>
  /// <remarks>
  /// The marker bit is kept, because an identifier is the bytes it is written as. Returns zero
  /// bytes read when there is nothing readable there, which is how a walk learns to stop.
  /// </remarks>
  internal static int ReadId(ReadOnlySpan<byte> data, int offset, int end, out uint id) {
    id = 0;
    if (offset >= end)
      return 0;

    var first = data[offset];
    if (first == 0)
      return 0;

    var length = 1;
    for (var mask = 0x80; (first & mask) == 0; mask >>= 1)
      ++length;

    if (length > MAX_ID_LENGTH || offset + length > end)
      return 0;

    var value = 0u;
    for (var i = 0; i < length; ++i)
      value = (value << 8) | data[offset + i];

    id = value;
    return length;
  }

  /// <summary>
  /// Reads a variable-length integer used as an element length.
  /// </summary>
  /// <remarks>
  /// The marker bit is dropped here, unlike in an identifier: what is left is the length. When every
  /// remaining bit is set the writer did not know the length, which <paramref name="size"/> reports
  /// as <c>-1</c> rather than as the enormous number those bits spell.
  /// </remarks>
  internal static int ReadSize(ReadOnlySpan<byte> data, int offset, int end, out long size) {
    size = 0;
    if (offset >= end)
      return 0;

    var first = data[offset];
    if (first == 0)
      return 0;

    var length = 1;
    var mask = 0x80;
    for (; (first & mask) == 0; mask >>= 1)
      ++length;

    if (length > MAX_SIZE_LENGTH || offset + length > end)
      return 0;

    var value = (ulong)(first & (mask - 1));
    var unknown = value == (ulong)(mask - 1);
    for (var i = 1; i < length; ++i) {
      var b = data[offset + i];
      value = (value << 8) | b;
      if (b != 0xFF)
        unknown = false;
    }

    size = unknown ? -1 : (long)value;
    return length;
  }

  /// <summary>Walks the elements lying between two offsets of the file, in storage order.</summary>
  /// <param name="file">The whole file, which every element is a window onto.</param>
  /// <param name="offset">Where to begin, counted from the start of the file.</param>
  /// <param name="end">Where to stop, counted from the start of the file.</param>
  /// <param name="endsUnknownSize">Which identifiers close an element the file stated no length for.
  /// Null means such an element runs to <paramref name="end"/> — which is right for the
  /// <c>Segment</c>, whose siblings there are none of, and wrong for a <c>Cluster</c>, which is
  /// followed by the next one.</param>
  internal static IEnumerable<EbmlElement> Walk(
    ReadOnlyMemory<byte> file, int offset, int end, Func<uint, bool>? endsUnknownSize = null) {
    end = Math.Min(end, file.Length);

    while (offset < end) {
      if (!_TryReadHeader(file.Span, offset, end, out var id, out var size, out var header))
        yield break;

      var bodyStart = offset + header;
      if (size >= 0) {
        // A length longer than what is left is a truncated file. What is there is still walkable; what
        // is not there is not invented, and the walk stops after it rather than restarting inside the
        // next element's payload. The element is marked so that whoever reads it can tell a short
        // payload from a complete one — for a block, the difference between a frame and half of one.
        var available = end - bodyStart;
        var length = (int)Math.Min(size, available);
        var truncated = size > available;
        yield return new(id, file.Slice(bodyStart, length), bodyStart, offset, false, truncated);

        if (truncated)
          yield break;

        offset = bodyStart + length;
        continue;
      }

      var extent = endsUnknownSize == null ? end : _FindUnknownEnd(file, bodyStart, end, endsUnknownSize);
      yield return new(id, file[bodyStart..extent], bodyStart, offset, true);
      offset = extent;
    }
  }

  /// <summary>Walks what one element contains, with offsets still counted from the file's start.</summary>
  internal static IEnumerable<EbmlElement> Children(
    ReadOnlyMemory<byte> file, EbmlElement element, Func<uint, bool>? endsUnknownSize = null)
    => Walk(file, element.BodyOffset, element.BodyOffset + element.Body.Length, endsUnknownSize);

  /// <summary>
  /// Finds where an element the file stated no length for stops.
  /// </summary>
  /// <remarks>
  /// By reading forward over its children until one turns up that cannot be a child of it. For a
  /// <c>Cluster</c> that is the next <c>Cluster</c> or any other element of the segment's own level,
  /// which is exactly what ffmpeg's live muxer produces and what this was measured on. Only the
  /// headers are read, so the cost is one variable-length integer pair per child rather than a pass
  /// over the frames.
  /// </remarks>
  private static int _FindUnknownEnd(ReadOnlyMemory<byte> file, int offset, int end, Func<uint, bool> closes) {
    var span = file.Span;
    while (offset < end) {
      if (!_TryReadHeader(span, offset, end, out var id, out var size, out var header))
        return end;

      if (closes(id))
        return offset;

      // A child that states no length of its own has to be resolved the same way before its sibling
      // can be found, and the same identifiers close it: an unknown-size element nested in one is
      // ended by whatever ends the outer one.
      offset = size < 0
        ? _FindUnknownEnd(file, offset + header, end, closes)
        : (int)Math.Min((long)offset + header + size, end);
    }

    return end;
  }

  private static bool _TryReadHeader(ReadOnlySpan<byte> data, int offset, int end, out uint id, out long size, out int header) {
    id = 0;
    size = 0;
    header = 0;

    var idLength = ReadId(data, offset, end, out id);
    if (idLength == 0)
      return false;

    var sizeLength = ReadSize(data, offset + idLength, end, out size);
    if (sizeLength == 0)
      return false;

    header = idLength + sizeLength;
    return true;
  }
}
