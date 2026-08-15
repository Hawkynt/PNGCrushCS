using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FileFormat.Riff;

namespace FileFormat.Avi;

/// <summary>One element of a RIFF file as the scanner found it: a chunk, or a list and its body.</summary>
/// <param name="Id">The element's four-character identifier — <c>LIST</c> for a list.</param>
/// <param name="ListType">The list's own type (<c>hdrl</c>, <c>movi</c>, …), or the default for a chunk.</param>
/// <param name="Body">The element's payload, as a window onto the file rather than a copy. For a list
/// this is what follows its type, i.e. the elements it contains.</param>
/// <param name="IsList">Whether this element is a list.</param>
internal readonly record struct RiffElement(
  FourCC Id,
  FourCC ListType,
  ReadOnlyMemory<byte> Body,
  bool IsList);

/// <summary>
/// Walks a RIFF file's elements without copying any of them.
/// </summary>
/// <remarks>
/// <see cref="FileFormat.Riff.RiffReader"/> exists and is used everywhere else, but it parses a whole
/// file into lists of chunks whose data it copies into fresh arrays. For a still that is nothing; for
/// a film it is the entire film in memory a second time, before a caller has asked for a single
/// frame. So the video side walks instead of parsing: the same layout, read on demand, with every
/// payload a window onto the buffer the file was read into.
/// <para/>
/// This is the whole reason a demuxer can be lazy at all. Everything above it — packets, decoding,
/// frames — is an enumerable chain, and one eager copy at the bottom would make all of it eager.
/// </remarks>
internal static class RiffScanner {

  /// <summary>Walks the elements between two offsets of the file, in the order they are stored.</summary>
  internal static IEnumerable<RiffElement> Walk(ReadOnlyMemory<byte> data, int offset, int end) {
    end = Math.Min(end, data.Length);

    while (offset + RiffChunkHeader.StructSize <= end) {
      var (id, size) = _ReadHeader(data, offset);
      var bodyStart = offset + RiffChunkHeader.StructSize;

      // A size larger than what is left is a truncated file. What is there is still walkable up to
      // the end; what is not there is not invented.
      var bodyEnd = size > (uint)(end - bodyStart) ? end : bodyStart + (int)size;

      if (id.ToString() == "LIST") {
        if (bodyStart + 4 > end)
          yield break;

        yield return new(id, _ReadFourCC(data, bodyStart), data[(bodyStart + 4)..bodyEnd], true);
      } else
        yield return new(id, default, data[bodyStart..bodyEnd], false);

      // RIFF elements are word-aligned: an odd-sized payload is followed by one pad byte that is not
      // part of it.
      var advanced = bodyStart + (int)size + (int)(size & 1);
      if (advanced <= offset)
        yield break; // a zero-sized element with a broken header would otherwise walk forever

      offset = advanced;
    }
  }

  /// <summary>Walks the elements of one element's body.</summary>
  internal static IEnumerable<RiffElement> Walk(RiffElement element)
    => Walk(element.Body, 0, element.Body.Length);

  // Both of these exist because a span cannot be a local of an iterator method. Reading the eight
  // header bytes behind a call keeps the walk itself span-free.
  private static (FourCC Id, uint Size) _ReadHeader(ReadOnlyMemory<byte> data, int offset) {
    var span = data.Span;
    return (FourCC.ReadFrom(span.Slice(offset, 4)), BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset + 4, 4)));
  }

  private static FourCC _ReadFourCC(ReadOnlyMemory<byte> data, int offset) => FourCC.ReadFrom(data.Span.Slice(offset, 4));
}
