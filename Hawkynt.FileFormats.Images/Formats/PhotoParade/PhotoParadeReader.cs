using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.EmbeddedPicture;

namespace FileFormat.PhotoParade;

/// <summary>Walks a PhotoParade album's description blocks and takes the photograph before each.</summary>
public static class PhotoParadeReader {

  /// <summary>The chunk carrying a photograph's title.</summary>
  private static ReadOnlySpan<byte> _TitleTag => "TITL"u8;

  public static PhotoParadeFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PhotoParade slide show not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PhotoParadeFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromBytes(memory.ToArray());
  }

  public static PhotoParadeFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static PhotoParadeFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < PhotoParadeFile.HeaderSize)
      throw new InvalidDataException("Data too small to be a PhotoParade slide show.");
    if (!data.Slice(PhotoParadeFile.MagicOffset, 4).SequenceEqual(PhotoParadeFile.Magic))
      throw new InvalidDataException("Not a PhotoParade slide show: XPB! does not stand at offset four.");
    if (!data.Slice(PhotoParadeFile.SubFormatOffset, 4).SequenceEqual(PhotoParadeFile.SubFormat))
      throw new InvalidDataException("Not a PhotoParade slide show: PhP2 does not stand behind the version.");

    var photographs = new List<PhotoParadeFile.Photograph>();

    var previousEnd = PhotoParadeFile.HeaderSize;
    var at = PhotoParadeFile.HeaderSize;

    while (at + PhotoParadeFile.ChunkHeaderSize <= data.Length) {
      if (!data.Slice(at, 4).SequenceEqual(PhotoParadeFile.PictureInfoTag)) {
        ++at;
        continue;
      }

      var blockEnd = _WalkBlock(data, at, out var title);
      if (blockEnd < 0) {
        ++at;
        continue;
      }

      var picture = _FindPictureEndingAt(data, previousEnd, at);
      if (picture.Length == 0)
        throw new InvalidDataException($"A PhotoParade album describes a photograph at {at} that no picture in front of it runs up to.");

      photographs.Add(new(title, picture));
      previousEnd = blockEnd;
      at = blockEnd;
    }

    if (photographs.Count == 0)
      throw new InvalidDataException("A PhotoParade slide show describes no photographs.");

    // The album states its own count. It is not what finds the photographs — the walk above does
    // that — but disagreeing with it means something was misread, and a partial album drawn as a
    // whole one is exactly the kind of quiet wrongness this is meant to catch.
    var stated = _StatedCount(data);
    if (stated >= 0 && stated != photographs.Count)
      throw new InvalidDataException($"A PhotoParade album states {stated} photographs; {photographs.Count} were found.");

    return new() { Photographs = photographs };
  }

  /// <summary>Walks a block's chunks to its close, answering where it ends and what it is titled.</summary>
  private static int _WalkBlock(ReadOnlySpan<byte> data, int at, out string title) {
    title = string.Empty;

    // A block's opening carries a version where a chunk carries a length, so it is stepped over
    // rather than read as one.
    var cursor = at + PhotoParadeFile.ChunkHeaderSize;

    while (cursor + PhotoParadeFile.ChunkHeaderSize <= data.Length) {
      var tag = data.Slice(cursor, 4);
      var size = ((long)data[cursor + 4] << 24) | ((long)data[cursor + 5] << 16)
               | ((long)data[cursor + 6] << 8) | data[cursor + 7];

      var next = cursor + PhotoParadeFile.ChunkHeaderSize + size;
      if (next > data.Length)
        return -1;

      if (tag.SequenceEqual(_TitleTag) && size > 0)
        title = Encoding.Latin1.GetString(data.Slice(cursor + PhotoParadeFile.ChunkHeaderSize, (int)size)).TrimEnd('\0');

      cursor = (int)next;

      if (tag.SequenceEqual(PhotoParadeFile.EndTag))
        return cursor;
    }

    return -1;
  }

  /// <summary>
  /// Finds the picture between two positions that ends on the second of them exactly. Anything that
  /// only begins with a signature — a theme's backdrop, a border tile, a thumbnail — runs out
  /// somewhere else and is passed over.
  /// </summary>
  private static byte[] _FindPictureEndingAt(ReadOnlySpan<byte> data, int from, int to) {
    for (var at = from; at < to; ++at) {
      var measured = EmbeddedPictureExtent.Measure(data, at);
      if (measured > 0 && at + measured == to)
        return data.Slice(at, measured).ToArray();
    }

    return [];
  }

  /// <summary>Reads the album block's count of photographs, or -1 where there is no album block.</summary>
  private static int _StatedCount(ReadOnlySpan<byte> data) {
    for (var at = PhotoParadeFile.HeaderSize; at + PhotoParadeFile.ChunkHeaderSize <= data.Length; ++at) {
      if (!data.Slice(at, 4).SequenceEqual(PhotoParadeFile.AlbumTag))
        continue;

      var cursor = at + PhotoParadeFile.ChunkHeaderSize;
      while (cursor + PhotoParadeFile.ChunkHeaderSize <= data.Length) {
        var tag = data.Slice(cursor, 4);
        var size = ((long)data[cursor + 4] << 24) | ((long)data[cursor + 5] << 16)
                 | ((long)data[cursor + 6] << 8) | data[cursor + 7];

        var body = cursor + PhotoParadeFile.ChunkHeaderSize;
        if (body + size > data.Length)
          break;

        if (tag.SequenceEqual(PhotoParadeFile.PictureCountTag) && size == 4)
          return (data[body] << 24) | (data[body + 1] << 16) | (data[body + 2] << 8) | data[body + 3];

        if (tag.SequenceEqual(PhotoParadeFile.EndTag))
          break;

        cursor = (int)(body + size);
      }
    }

    return -1;
  }
}
