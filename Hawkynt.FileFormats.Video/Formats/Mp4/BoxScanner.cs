using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FileFormat.Riff;

namespace FileFormat.Mp4;

/// <summary>One box of an ISO base media file as the scanner found it.</summary>
/// <param name="Type">The box's four-character type — <c>moov</c>, <c>stbl</c>, <c>mdat</c>, …</param>
/// <param name="Body">Everything after the box header, as a window onto the file rather than a copy.</param>
/// <param name="BodyOffset">Where <see cref="Body"/> begins, counted from the start of the file.</param>
/// <param name="Offset">Where the box itself begins, header included, counted from the file's start.</param>
internal readonly record struct Mp4Box(FourCC Type, ReadOnlyMemory<byte> Body, int BodyOffset, int Offset) {

  /// <summary>The whole box including its header, as a window onto the file.</summary>
  /// <remarks>
  /// What a sample entry has to be handed across as: the codec configuration inside it is named by
  /// boxes of its own, and a decoder given only the payload would have lost the length and the type
  /// that say where they start.
  /// </remarks>
  internal ReadOnlyMemory<byte> Whole(ReadOnlyMemory<byte> file) => file[this.Offset..(this.BodyOffset + this.Body.Length)];
}

/// <summary>
/// Walks an ISO base media file's boxes without copying any of them.
/// </summary>
/// <remarks>
/// The whole format is one shape repeated: a big-endian 32-bit length, a four-character type, and a
/// payload that is either bytes or more boxes. Two escapes from that shape have to be handled or a
/// large file cannot be read at all — a length of 1 means the real, 64-bit length follows the type,
/// and a length of 0 means the box runs to the end of the file, which is what a writer emits for the
/// <c>mdat</c> of a stream whose length it does not yet know.
/// <para/>
/// Every offset here stays counted from the start of the file, never from the start of the box being
/// walked. A sample table's chunk offsets are absolute, so anything read out of <c>moov</c> has to be
/// in the same frame of reference they are — walking a sliced-out body would restart the count at
/// zero and put every packet in the wrong place.
/// <para/>
/// Windows rather than copies, for the same reason the RIFF side does it: a demuxer that copied the
/// film in order to walk it would double it before a caller had asked for one frame.
/// </remarks>
internal static class BoxScanner {

  /// <summary>The smallest a box can be: a 32-bit size and a four-character type.</summary>
  internal const int HEADER_SIZE = 8;

  /// <summary>The header of a box whose size is stated as a 64-bit number after the type.</summary>
  private const int _LARGE_HEADER_SIZE = 16;

  /// <summary>Walks the boxes between two offsets of the file, in the order they are stored.</summary>
  internal static IEnumerable<Mp4Box> Walk(ReadOnlyMemory<byte> file, int offset, int end) {
    end = Math.Min(end, file.Length);

    while (offset + HEADER_SIZE <= end) {
      var (type, size, header) = _ReadHeader(file, offset, end);

      // A box shorter than its own header describes nothing, and there is no way to find the next one
      // after it — taking the size at face value would walk backwards and never terminate.
      if (size < header)
        yield break;

      var bodyStart = offset + header;
      var available = end - bodyStart;
      var bodyLength = size - header;

      // A size larger than what is left is a truncated file. What is there is still walkable up to
      // the end; what is not there is not invented.
      yield return new(type, file.Slice(bodyStart, Math.Min(bodyLength, available)), bodyStart, offset);

      offset += size;
    }
  }

  /// <summary>Walks the boxes one box contains, with the offsets still counted from the file's start.</summary>
  internal static IEnumerable<Mp4Box> Children(ReadOnlyMemory<byte> file, Mp4Box box)
    => Walk(file, box.BodyOffset, box.BodyOffset + box.Body.Length);

  /// <summary>Walks the boxes one box contains, skipping a leading version-and-flags word.</summary>
  /// <remarks>
  /// For <c>meta</c>, which is a full box in ISO base media and a plain one in QuickTime — the two
  /// disagree by exactly these four bytes and a reader that assumed either would miss the whole
  /// tag list of the other.
  /// </remarks>
  internal static IEnumerable<Mp4Box> Children(ReadOnlyMemory<byte> file, Mp4Box box, int skip)
    => Walk(file, box.BodyOffset + skip, box.BodyOffset + box.Body.Length);

  // A span cannot be a local of an iterator method, so the header is read behind a call and the walk
  // itself stays span-free.
  private static (FourCC Type, int Size, int Header) _ReadHeader(ReadOnlyMemory<byte> file, int offset, int end) {
    var span = file.Span;
    var declared = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(offset, 4));
    var type = FourCC.ReadFrom(span.Slice(offset + 4, 4));

    switch (declared) {
      // Zero means "to the end of the file". A one-pass writer emits it for the mdat it is still
      // filling, so a file that was never rewritten with a real length is read by this branch alone.
      case 0:
        return (type, end - offset, HEADER_SIZE);

      // One means the real size is the 64-bit number after the type. Anything past what an int
      // addresses cannot be sliced out of a byte array in the first place, so it is clamped to what
      // was actually read rather than overflowing into a negative size.
      case 1: {
        if (offset + _LARGE_HEADER_SIZE > end)
          return (type, 0, _LARGE_HEADER_SIZE);

        var large = BinaryPrimitives.ReadUInt64BigEndian(span.Slice(offset + 8, 8));
        return (type, large > (ulong)(end - offset) ? end - offset : (int)large, _LARGE_HEADER_SIZE);
      }

      default:
        return (type, declared > (uint)(end - offset) ? end - offset : (int)declared, HEADER_SIZE);
    }
  }
}
