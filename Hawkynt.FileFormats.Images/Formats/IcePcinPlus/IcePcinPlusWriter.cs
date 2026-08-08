using System;
using System.IO;

namespace FileFormat.IcePcinPlus;

/// <summary>Writes an ICE PCIN+ picture back out.</summary>
/// <remarks>
/// The reader keeps the file whole, every character set and the screen being at an absolute offset,
/// so there is nothing to reassemble here — only the length to insist on, which the reader will
/// insist on again.
/// </remarks>
public static class IcePcinPlusWriter {

  public static byte[] ToBytes(IcePcinPlusFile file) {
    var data = file.Data;
    if (data == null || data.Length != IcePcinPlusFile.FileSize)
      throw new InvalidDataException($"An ICE PCIN+ picture is {IcePcinPlusFile.FileSize} bytes.");

    return data.AsSpan().ToArray();
  }
}
