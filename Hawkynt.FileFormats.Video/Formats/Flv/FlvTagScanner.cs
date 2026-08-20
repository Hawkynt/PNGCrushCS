using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Flv;

/// <summary>What a tag of an FLV carries.</summary>
/// <remarks>
/// The numbers are the ones written into the file. Everything outside this set is undefined by the
/// specification, and a tag naming one of them is refused rather than guessed at — there is no
/// generic tag shape to fall back on.
/// </remarks>
internal enum FlvTagType : byte {

  /// <summary>Sound.</summary>
  Audio = 8,

  /// <summary>Pictures.</summary>
  Video = 9,

  /// <summary>An AMF0 message about the file — <c>onMetaData</c> and its relatives.</summary>
  Script = 18,
}

/// <summary>One tag of an FLV as the scanner found it.</summary>
/// <param name="Type">What the tag carries.</param>
/// <param name="Filtered">Whether the writer marked the payload as processed by a filter — encrypted,
/// in every case that occurs — which puts a filter header in front of the data.</param>
/// <param name="Timestamp">When the tag is due, in milliseconds, as the file states it.</param>
/// <param name="StreamId">The tag's stream id, which every writer leaves at zero.</param>
/// <param name="Data">The tag's payload, as a window onto the file rather than a copy.</param>
/// <param name="Offset">Where the tag's header begins, counted from the start of the file.</param>
internal readonly record struct FlvTag(
  FlvTagType Type,
  bool Filtered,
  long Timestamp,
  int StreamId,
  ReadOnlyMemory<byte> Data,
  int Offset);

/// <summary>
/// Walks an FLV's tags without copying any of them.
/// </summary>
/// <remarks>
/// The body of an FLV is one shape repeated: four bytes saying how long the previous tag was, then an
/// eleven-byte tag header, then the payload. There is no index and no table of contents anywhere in
/// the file, so the only way to the tag after this one is through this one's declared length — which
/// is why a length that runs past the end of the file is refused here rather than clamped. An ISO
/// base media box may be clamped and stepped over because its neighbours are reachable without it;
/// an FLV tag's neighbours are not, and handing back the short payload as a packet would be handing
/// back part of a frame as though it were one.
/// <para/>
/// The timestamp is the field this format is easiest to get wrong on. It is not the twenty-four bits
/// where it appears to be: a fourth byte follows them holding the <em>high</em> eight bits, so the
/// value is <c>extended &lt;&lt; 24 | lower</c> and not <c>lower &lt;&lt; 8 | extended</c>. A reader
/// that stops at the three bytes is right for the first 2^24 milliseconds — four hours and thirty-six
/// minutes — and then silently starts every packet over again from zero.
/// <para/>
/// The <c>PreviousTagSize</c> in front of each tag is read past and never checked. It exists so a
/// player can seek backwards, ffmpeg does not verify it forwards either, and a file whose values are
/// wrong plays everywhere — refusing on it would refuse files nothing else has trouble with.
/// </remarks>
internal static class FlvTagScanner {

  /// <summary>The tag header: type, data size, timestamp, timestamp high byte, stream id.</summary>
  internal const int HEADER_SIZE = 11;

  /// <summary>The length of the previous tag, written in front of every tag and after the last.</summary>
  internal const int PREVIOUS_TAG_SIZE = 4;

  /// <summary>The bit of the type byte saying the payload is preceded by a filter header.</summary>
  private const byte _FILTER_FLAG = 0x20;

  /// <summary>The bits of the type byte that are the type; the two above the filter bit are reserved.</summary>
  private const byte _TYPE_MASK = 0x1F;

  /// <summary>Walks the tags from an offset to the end of the file, in the order they are stored.</summary>
  /// <exception cref="InvalidDataException">A tag declares more data than the file holds.</exception>
  internal static IEnumerable<FlvTag> Walk(ReadOnlyMemory<byte> file, int offset) {
    var end = file.Length;

    // A complete tag needs the size of the one before it and a header of its own. What is left when
    // that no longer fits is the trailing PreviousTagSize the format ends with, and stopping on it is
    // the ordinary end of a well-formed file rather than a truncation.
    while (offset + PREVIOUS_TAG_SIZE + HEADER_SIZE <= end) {
      var at = offset + PREVIOUS_TAG_SIZE;
      var (type, filtered, size, timestamp, streamId) = _ReadHeader(file, at);

      var body = at + HEADER_SIZE;
      var available = end - body;
      if (size > available)
        throw new InvalidDataException(
          $"The tag at offset {at} declares {size} bytes of data but the file holds {available} more. "
          + "An FLV has no index, so nothing after a tag of unknown length is reachable and the payload that is there is part of a unit rather than one.");

      yield return new(type, filtered, timestamp, streamId, file.Slice(body, size), at);

      offset = body + size;
    }
  }

  // A span cannot be a local of an iterator method, so the header is read behind a call.
  private static (FlvTagType Type, bool Filtered, int Size, long Timestamp, int StreamId) _ReadHeader(ReadOnlyMemory<byte> file, int at) {
    var span = file.Span;

    var kind = span[at];
    var size = _ReadUInt24(span, at + 1);

    // Three bytes of timestamp and then, four bytes later than a reader would expect it, the byte
    // that goes on top of them.
    var timestamp = (long)((uint)span[at + 7] << 24 | (uint)_ReadUInt24(span, at + 4));

    return ((FlvTagType)(kind & _TYPE_MASK), (kind & _FILTER_FLAG) != 0, size, timestamp, _ReadUInt24(span, at + 8));
  }

  private static int _ReadUInt24(ReadOnlySpan<byte> data, int at) => (data[at] << 16) | (data[at + 1] << 8) | data[at + 2];
}
