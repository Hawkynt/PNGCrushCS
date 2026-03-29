using System;
using System.IO;

namespace FileFormat.Interfile;

/// <summary>Assembles Interfile nuclear medicine image bytes from pixel data.</summary>
public static class InterfileWriter {

  public static byte[] ToBytes(InterfileFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return _Assemble(file);
  }

  private static byte[] _Assemble(InterfileFile file) {
    using var ms = new MemoryStream();

    var headerBytes = InterfileHeaderParser.Format(file);
    ms.Write(headerBytes);
    ms.Write(file.PixelData);

    return ms.ToArray();
  }
}
