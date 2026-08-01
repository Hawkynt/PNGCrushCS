using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.Ipl;

/// <summary>Assembles an IPLab file: the tags, the sizes, the planes, then the closing tag.</summary>
public static class IplWriter {

  public static byte[] ToBytes(IplFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var planes = file.PixelData ?? [];
    var result = new byte[IplFile.HeaderSize + planes.Length + 8];

    Encoding.ASCII.GetBytes(IplFile.IntelMagic).CopyTo(result.AsSpan(0));
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), 4);
    Encoding.ASCII.GetBytes(IplFile.Version).CopyTo(result.AsSpan(8));
    Encoding.ASCII.GetBytes(IplFile.DataTag).CopyTo(result.AsSpan(12));

    // The length counts from itself to the end of the planes.
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(16), IplFile.HeaderSize - 16 + planes.Length);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(20), file.Width);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(24), file.Height);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(28), Math.Max(1, file.Channels));
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(32), 1);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(36), 1);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(40), file.SampleBits == 16 ? 1 : 0);

    planes.CopyTo(result.AsSpan(IplFile.HeaderSize));
    Encoding.ASCII.GetBytes(IplFile.EndTag).CopyTo(result.AsSpan(IplFile.HeaderSize + planes.Length));

    return result;
  }

  public static void ToFile(IplFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
