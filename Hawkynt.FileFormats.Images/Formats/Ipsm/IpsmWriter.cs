using System;
using System.Buffers.Binary;

namespace FileFormat.Ipsm;

/// <summary>Assembles an IPSM panorama: the header, the directory, then the chunks.</summary>
public static class IpsmWriter {

  public static byte[] ToBytes(IpsmFile file) {
    var embedded = file.Embedded ?? [];
    var directory = IpsmFile.HeaderSize + IpsmFile.WrittenChunkCount * IpsmFile.DirectoryEntrySize;
    var bitmapAt = directory + IpsmFile.InitLength;

    var result = new byte[bitmapAt + embedded.Length];
    IpsmFile.Magic.CopyTo(result);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), result.Length);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(8), IpsmFile.WrittenChunkCount);

    _WriteEntry(result, IpsmFile.HeaderSize, IpsmFile.InitTag, directory, IpsmFile.InitLength);
    _WriteEntry(result, IpsmFile.HeaderSize + IpsmFile.DirectoryEntrySize, IpsmFile.BitmapTag, bitmapAt, embedded.Length);

    embedded.CopyTo(result.AsSpan(bitmapAt));

    return result;
  }

  private static void _WriteEntry(Span<byte> target, int at, ReadOnlySpan<byte> tag, int offset, int length) {
    tag.CopyTo(target[at..]);
    BinaryPrimitives.WriteInt32LittleEndian(target[(at + 4)..], offset);
    BinaryPrimitives.WriteInt32LittleEndian(target[(at + 8)..], length);
  }
}
