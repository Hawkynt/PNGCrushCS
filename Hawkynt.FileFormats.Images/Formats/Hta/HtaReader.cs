using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Hta;

/// <summary>Reads Hemera Thumbs files (.hta) from bytes, streams, or file paths.</summary>
public static class HtaReader {

  /// <summary>The eight bytes every PNG opens with; every member has to be one.</summary>
  private static ReadOnlySpan<byte> _PngMagic => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

  /// <summary>A chunk's length and its type, ahead of the payload.</summary>
  private const int _CHUNK_HEADER = 8;

  /// <summary>The four bytes of CRC behind every chunk payload.</summary>
  private const int _CHUNK_CRC = 4;

  /// <summary>No PNG chunk may declare more than this, and the format says so.</summary>
  private const uint _MAXIMUM_CHUNK_LENGTH = 0x7FFFFFFF;

  public static HtaFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Hemera Thumbs file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HtaFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var buffer = new byte[stream.Length - stream.Position];
      stream.ReadExactly(buffer);
      return FromBytes(buffer);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromBytes(memory.ToArray());
  }

  public static HtaFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static HtaFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < HtaFile.DirectoryOffset)
      throw new InvalidDataException($"Data too small for a Hemera Thumbs file (need at least {HtaFile.DirectoryOffset} bytes, got {data.Length}).");

    if (!data[..HtaFile.Magic.Length].SequenceEqual(HtaFile.Magic))
      throw new InvalidDataException("Not a Hemera Thumbs file: the eight bytes it opens with are not the ones this format uses.");

    var version = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
    if (version != HtaFile.SupportedVersion)
      throw new InvalidDataException($"A Hemera Thumbs file of version {version} is not read; version {HtaFile.SupportedVersion} is.");

    var count = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
    if (count is 0 or > HtaFile.MaximumMemberCount)
      throw new InvalidDataException($"A Hemera Thumbs file states {count} members, which is not a count this reads.");

    var directoryEnd = HtaFile.DirectoryOffset + (long)count * HtaFile.DirectoryEntrySize;
    if (directoryEnd > data.Length)
      throw new InvalidDataException($"The directory of {count} members runs to byte {directoryEnd} and the file has {data.Length}.");

    var members = new byte[count][];
    var lowest = (long)HtaFile.FirstMemberOffset;

    for (var i = 0; i < count; ++i) {
      var at = HtaFile.DirectoryOffset + i * HtaFile.DirectoryEntrySize;
      var position = BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);
      var length = BinaryPrimitives.ReadUInt32LittleEndian(data[(at + 4)..]);

      // XnView starts looking for a member at byte 64 and members are stored in the order the
      // directory lists them, so anything earlier than that or than the member before it is not a
      // file either reader would find its way through.
      if (position < lowest)
        throw new InvalidDataException($"Member {i} stands at {position}, ahead of {lowest}, where the member before it ended.");

      if (length == 0 || position + (long)length > data.Length)
        throw new InvalidDataException($"Member {i} states {length} bytes at {position} and the file has {data.Length}.");

      var member = data.Slice((int)position, (int)length);
      var declared = _PngLength(member);
      if (declared != length)
        throw new InvalidDataException($"Member {i} is stated to be {length} bytes and the picture standing there is {declared}.");

      members[i] = member.ToArray();
      lowest = position + (long)length;
    }

    return new() { Members = members, Version = (int)version };
  }

  /// <summary>
  /// How long the PNG standing at the front of <paramref name="member"/> says it is: its signature
  /// and then its own chunk chain walked to the end of IEND. This is the length the directory entry
  /// has to agree with, and it is what keeps identification inside the file's own structure.
  /// </summary>
  private static long _PngLength(ReadOnlySpan<byte> member) {
    if (member.Length < _PngMagic.Length || !member[.._PngMagic.Length].SequenceEqual(_PngMagic))
      throw new InvalidDataException("A Hemera Thumbs member does not open with the eight bytes a PNG opens with.");

    var at = _PngMagic.Length;
    for (;;) {
      if (at + _CHUNK_HEADER + _CHUNK_CRC > member.Length)
        throw new InvalidDataException("A Hemera Thumbs member ends in the middle of a PNG chunk.");

      var length = BinaryPrimitives.ReadUInt32BigEndian(member[at..]);
      if (length > _MAXIMUM_CHUNK_LENGTH)
        throw new InvalidDataException($"A PNG chunk inside a Hemera Thumbs member declares {length} bytes, which no PNG chunk may.");

      var isEnd = member[at + 4] == 'I' && member[at + 5] == 'E' && member[at + 6] == 'N' && member[at + 7] == 'D';
      var next = at + (long)_CHUNK_HEADER + length + _CHUNK_CRC;
      if (next > member.Length)
        throw new InvalidDataException("A PNG chunk inside a Hemera Thumbs member runs past the end of the member.");

      at = (int)next;
      if (isEnd)
        return at;
    }
  }
}
