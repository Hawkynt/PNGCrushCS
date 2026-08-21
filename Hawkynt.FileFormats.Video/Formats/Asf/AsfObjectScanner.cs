using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace FileFormat.Asf;

/// <summary>One ASF object as the scanner found it.</summary>
/// <param name="Id">The object's sixteen-byte identifier, as it lies in the file.</param>
/// <param name="Body">Everything after the identifier and the length, as a window onto the file.</param>
/// <param name="Offset">Where the object begins, header included, counted from the file's start.</param>
internal readonly record struct AsfObject(ReadOnlyMemory<byte> Id, ReadOnlyMemory<byte> Body, int Offset) {

  /// <summary>Whether this object is the one that identifier names.</summary>
  internal bool Is(ReadOnlySpan<byte> known) => AsfGuid.Equals(this.Id.Span, known);
}

/// <summary>
/// Walks an ASF object tree without copying any of it.
/// </summary>
/// <remarks>
/// Every object in the format has the same shape — a sixteen-byte identifier, a little-endian 64-bit
/// length that counts the identifier and the length itself, and a payload (clause 3). Containers of
/// other objects differ only in how many fixed fields sit between the header and the first child, so
/// one walk serves the Header Object, the Header Extension Object and the top level of the file
/// alike; each caller says where its children begin.
/// <para/>
/// Windows rather than copies, for the reason the RIFF and ISO base media scanners do the same: a
/// demuxer that copied the film in order to walk it would double it before a caller had asked for a
/// single frame.
/// </remarks>
internal static class AsfObjectScanner {

  /// <summary>The smallest an object can be: an identifier and the length that follows it.</summary>
  internal const int HEADER_SIZE = AsfGuid.SIZE + 8;

  /// <summary>Walks the objects between two offsets of the file, in the order they are stored.</summary>
  /// <remarks>
  /// Stops rather than throws at the first thing that cannot be walked past. An object claiming a
  /// length shorter than its own header gives no way to find the next one — taking it at face value
  /// would walk backwards and never terminate — and one claiming more than is left is a file that was
  /// cut short, whose remaining objects were read perfectly well and are not withdrawn for the sake of
  /// the one that was truncated.
  /// </remarks>
  internal static IEnumerable<AsfObject> Walk(ReadOnlyMemory<byte> file, int offset, int end) {
    end = Math.Min(end, file.Length);

    while (offset >= 0 && offset + HEADER_SIZE <= end) {
      var declared = BinaryPrimitives.ReadUInt64LittleEndian(file.Span.Slice(offset + AsfGuid.SIZE, 8));
      if (declared < HEADER_SIZE)
        yield break;

      var bodyStart = offset + HEADER_SIZE;
      var bodyLength = declared - HEADER_SIZE > (ulong)(end - bodyStart)
        ? end - bodyStart
        : (int)(declared - HEADER_SIZE);

      yield return new(file.Slice(offset, AsfGuid.SIZE), file.Slice(bodyStart, bodyLength), offset);

      // Past what an int addresses there is nothing left to walk into anyway, since the file was read
      // into a single array to begin with.
      if (declared > (ulong)(end - offset))
        yield break;

      offset += (int)declared;
    }
  }

  /// <summary>Walks the objects one object contains, starting a stated distance into its body.</summary>
  /// <remarks>
  /// The distance is what separates the containers: the Header Object states a count and two reserved
  /// bytes before its children (clause 3.1), the Header Extension Object a reserved identifier, a
  /// reserved word and a size (clause 3.4). Neither number belongs in the scanner.
  /// </remarks>
  internal static IEnumerable<AsfObject> Children(ReadOnlyMemory<byte> file, AsfObject parent, int skip) {
    var start = parent.Offset + HEADER_SIZE + skip;
    var end = parent.Offset + HEADER_SIZE + parent.Body.Length;
    return start > end ? [] : Walk(file, start, end);
  }
}
