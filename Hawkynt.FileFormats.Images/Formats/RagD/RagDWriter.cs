using System;
using System.IO;

namespace FileFormat.RagD;

/// <summary>Assembles RAG-D picture (.rag) file bytes.</summary>
public static class RagDWriter {

  public static byte[] ToBytes(RagDFile file) {
    var data = file.Data ?? [];
    var needed = RagDFile.PaletteOffset + file.PaletteLength
                 + (file.Width >> 3) * file.Planes * file.Height;

    if (data.Length < needed)
      throw new InvalidDataException(
        $"A {file.Width}x{file.Height} RAG-D picture of {file.Planes} planes needs {needed} bytes, got {data.Length}.");

    return (byte[])data.Clone();
  }
}
