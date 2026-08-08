using System;
using System.Buffers.Binary;

namespace FileFormat.TilezTexture;

/// <summary>Assembles a Tilez texture: the name, the length of the picture, then the picture.</summary>
public static class TilezTextureWriter {

  public static byte[] ToBytes(TilezTextureFile file) {
    var embedded = file.Embedded ?? [];
    var result = new byte[TilezTextureFile.HeaderSize + embedded.Length];

    TilezTextureFile.Magic.CopyTo(result);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(TilezTextureFile.Magic.Length), embedded.Length);
    embedded.CopyTo(result.AsSpan(TilezTextureFile.HeaderSize));

    return result;
  }
}
