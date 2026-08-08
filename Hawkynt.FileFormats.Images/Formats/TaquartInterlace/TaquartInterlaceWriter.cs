using System;
using System.IO;

namespace FileFormat.TaquartInterlace;

/// <summary>Writes a Taquart Interlace Picture back out.</summary>
/// <remarks>
/// The reader keeps the file whole and checks that its header and its three fields agree about the
/// size, so there is nothing to reassemble here — only that agreement to insist on again.
/// </remarks>
public static class TaquartInterlaceWriter {

  public static byte[] ToBytes(TaquartInterlaceFile file) {
    var data = file.Data;
    var expected = TaquartInterlaceFile.FieldsOffset + file.FieldLength * 3;
    if (data == null || data.Length != expected)
      throw new InvalidDataException($"A {file.StoredWidth}x{file.StoredHeight} Taquart picture is {expected} bytes.");

    return data.AsSpan().ToArray();
  }
}
