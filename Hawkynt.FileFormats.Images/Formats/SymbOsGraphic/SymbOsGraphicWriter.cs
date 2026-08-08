using System;
using System.IO;

namespace FileFormat.SymbOsGraphic;

/// <summary>Assembles SymbOS graphic (.sgx) file bytes.</summary>
public static class SymbOsGraphicWriter {

  public static byte[] ToBytes(SymbOsGraphicFile file) {
    var data = file.Data ?? [];
    if (data.Length < SymbOsGraphicFile.WideHeaderSize)
      throw new InvalidDataException($"A SymbOS graphic needs a chunk; {data.Length} bytes hold none.");

    return (byte[])data.Clone();
  }
}
