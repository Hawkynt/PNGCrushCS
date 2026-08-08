using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Ipsm;

/// <summary>Reads IPSM panoramas from bytes, streams, or file paths.</summary>
public static class IpsmReader {

  /// <summary>More chunks than any of these has, and it keeps a false match cheap.</summary>
  private const int _MaxChunks = 4096;

  public static IpsmFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("IPSM panorama not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IpsmFile FromStream(Stream stream) {
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

  public static IpsmFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < IpsmFile.HeaderSize || !data[..4].SequenceEqual(IpsmFile.Magic))
      throw new InvalidDataException("Not an IPSM panorama: it does not open with IPSM.");

    // The header states the whole file's length, so it is the cheapest thing there is to check and
    // the one that says these sixteen bytes are a header rather than somebody else's first sixteen.
    var stated = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
    if (stated != data.Length)
      throw new InvalidDataException($"An IPSM panorama states its length as {stated} and is {data.Length} bytes.");

    var chunks = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
    if (chunks is < 1 or > _MaxChunks)
      throw new InvalidDataException($"Invalid IPSM chunk count: {chunks}.");

    var directory = IpsmFile.HeaderSize + chunks * IpsmFile.DirectoryEntrySize;
    if (directory > data.Length)
      throw new InvalidDataException($"An IPSM directory of {chunks} chunks does not fit in {data.Length} bytes.");

    for (var i = 0; i < chunks; ++i) {
      var at = IpsmFile.HeaderSize + i * IpsmFile.DirectoryEntrySize;
      if (!data.Slice(at, 4).SequenceEqual(IpsmFile.BitmapTag))
        continue;

      var offset = BinaryPrimitives.ReadInt32LittleEndian(data[(at + 4)..]);
      var length = BinaryPrimitives.ReadInt32LittleEndian(data[(at + 8)..]);

      if (offset < directory || length < 1 || (long)offset + length > data.Length)
        throw new InvalidDataException($"An IPSM BTMP chunk states {length} bytes at {offset}, which the file cannot hold.");

      return new() { Embedded = data.Slice(offset, length).ToArray() };
    }

    throw new InvalidDataException("An IPSM panorama carries its picture in a BTMP chunk and this one has none.");
  }

  public static IpsmFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
