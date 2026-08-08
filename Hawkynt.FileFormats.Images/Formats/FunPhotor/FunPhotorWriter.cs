using System;
using System.Buffers.Binary;

namespace FileFormat.FunPhotor;

/// <summary>Assembles a FunPhotor frame: the length of the picture, then the picture.</summary>
public static class FunPhotorWriter {

  public static byte[] ToBytes(FunPhotorFile file) {
    var embedded = file.Embedded ?? [];
    var result = new byte[FunPhotorFile.HeaderSize + embedded.Length];

    BinaryPrimitives.WriteInt32LittleEndian(result, embedded.Length);
    embedded.CopyTo(result.AsSpan(FunPhotorFile.HeaderSize));

    return result;
  }
}
