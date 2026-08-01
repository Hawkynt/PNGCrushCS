using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.FirstPublisher;

/// <summary>Assembles 1st Publisher clip-art bytes.</summary>
public static class FirstPublisherWriter {

  public static byte[] ToBytes(FirstPublisherFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var rows = file.PixelData ?? [];
    var result = new byte[FirstPublisherFile.HeaderSize + rows.Length];

    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), (ushort)file.Width);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), (ushort)file.Height);
    rows.CopyTo(result.AsSpan(FirstPublisherFile.HeaderSize));

    return result;
  }

  public static void ToFile(FirstPublisherFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
