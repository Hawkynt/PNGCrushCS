using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.CmuWindowManager;

/// <summary>Writes Carnegie Mellon University window-manager bitmap files.</summary>
public static class CmuWindowManagerWriter {

  private const int _HeaderSize = 14;
  private const uint _Magic = 0xF10040BB;

  public static byte[] ToBytes(CmuWindowManagerFile file) {
    CmuWindowManagerFile.Validate(file, nameof(file));

    var output = new byte[checked(_HeaderSize + file.RasterData.Length)];
    BinaryPrimitives.WriteUInt32BigEndian(output, _Magic);
    BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(4), file.Width);
    BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(8), file.Height);
    BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(12), file.Depth);
    file.RasterData.CopyTo(output, _HeaderSize);
    return output;
  }

  public static void ToStream(CmuWindowManagerFile file, Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    stream.Write(ToBytes(file));
  }

  public static void ToFile(CmuWindowManagerFile file, FileInfo destination) {
    ArgumentNullException.ThrowIfNull(destination);
    File.WriteAllBytes(destination.FullName, ToBytes(file));
  }
}
