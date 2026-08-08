using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.Wrappers;

namespace FileFormat.ClipArtCatalog;

/// <summary>Walks a clip-art catalogue's chunks and decodes the thumbnail in each.</summary>
public static class ClipArtCatalogReader {

  private static ReadOnlySpan<byte> _Form => [(byte)'F', (byte)'O', (byte)'R', (byte)'M'];
  private static ReadOnlySpan<byte> _Info => [(byte)'I', (byte)'N', (byte)'F', (byte)'O'];
  private static ReadOnlySpan<byte> _Bitmap => [(byte)'D', (byte)'I', (byte)'B', (byte)' '];

  /// <summary>More drawings than any catalogue holds, and it keeps a false match cheap.</summary>
  private const int _MaxEntries = 65536;

  public static ClipArtCatalogFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Clip-art catalogue not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ClipArtCatalogFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static ClipArtCatalogFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 12 || !data[..4].SequenceEqual(ClipArtCatalogFile.Magic))
      throw new InvalidDataException("Not a clip-art catalogue: it does not open with CAT.");

    // The stated length is everything after the tag and the length itself, so it is the cheapest
    // check there is and the one that says these eight bytes are a header rather than a coincidence.
    var stated = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
    if (stated < 4 || (long)stated + ClipArtCatalogFile.ChunkHeaderSize != data.Length)
      throw new InvalidDataException($"A clip-art catalogue states {stated} bytes after its header and the file has {data.Length - ClipArtCatalogFile.ChunkHeaderSize}.");

    if (!data.Slice(ClipArtCatalogFile.ChunkHeaderSize, 4).SequenceEqual(ClipArtCatalogFile.ClipTag))
      throw new InvalidDataException("A clip-art catalogue names its kind CLIP and this one does not.");

    var entries = new List<ClipArtCatalogEntry>();
    var end = _Walk(data, ClipArtCatalogFile.ChunkHeaderSize + 4, data.Length, entries, string.Empty, 0);
    if (end != data.Length)
      throw new InvalidDataException($"A clip-art catalogue's chunks end at {end} and the file is {data.Length} bytes.");

    if (entries.Count == 0)
      throw new InvalidDataException("A clip-art catalogue holds no thumbnails.");

    return new() { Entries = entries };
  }

  public static ClipArtCatalogFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>Walks the chunks between two offsets, collecting a thumbnail per drawing.</summary>
  private static int _Walk(ReadOnlySpan<byte> data, int at, int end, List<ClipArtCatalogEntry> entries, string name, int depth) {
    if (depth > 4)
      throw new InvalidDataException("A clip-art catalogue nests its chunks deeper than one can.");

    while (at + ClipArtCatalogFile.ChunkHeaderSize <= end) {
      // Two of the tags in this family are eight letters, and both end in INFO or FORM. Reading the
      // second half tells the two shapes apart without having to know every first half there is.
      var tag = data.Slice(at, 4);
      var second = data.Slice(at + 4, 4);
      var eight = second.SequenceEqual(_Info) || second.SequenceEqual(_Form);
      var header = eight ? 12 : ClipArtCatalogFile.ChunkHeaderSize;
      if (at + header > end)
        throw new InvalidDataException($"A clip-art chunk at {at} has no length.");

      var length = BinaryPrimitives.ReadInt32LittleEndian(data[(at + header - 4)..]);
      var body = at + header;
      if (length < 0 || (long)body + length > end)
        throw new InvalidDataException($"A clip-art chunk at {at} states {length} bytes, which the catalogue cannot hold.");

      var stop = body + length;
      if (second.SequenceEqual(_Form) || tag.SequenceEqual(_Form)) {
        if (entries.Count >= _MaxEntries)
          throw new InvalidDataException("A clip-art catalogue states more drawings than one can hold.");

        _Walk(data, body, stop, entries, string.Empty, depth + 1);
      } else if (second.SequenceEqual(_Info)) {
        name = _Name(data.Slice(body, length));
      } else if (tag.SequenceEqual(_Bitmap)) {
        entries.Add(new(name, WrappedDib.Decode(data, body, ClipArtCatalogFile.MaxDimension, "A clip-art catalogue")));
      }

      // The chunks sit on even boundaries, as this family of formats has them do.
      at = stop + (stop & 1);
    }

    return at;
  }

  private static string _Name(ReadOnlySpan<byte> info) {
    var stop = info.IndexOf((byte)0);
    return Encoding.ASCII.GetString(stop < 0 ? info : info[..stop]);
  }
}
