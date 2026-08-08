using System;
using System.IO;

namespace FileFormat.SuperHiresEditor;

/// <summary>Assembles Super-hires Editor II (.sh2) file bytes.</summary>
/// <remarks>
/// Only the plain form is written. The packed form stores its sprites column by column rather than
/// the way the hardware wants them, so it is a rearrangement as much as a compression and a picture
/// would have to be rebuilt to produce one.
/// </remarks>
public static class SuperHiresEditor2Writer {

  public static byte[] ToBytes(SuperHiresEditor2File file) {
    var data = file.Data ?? [];
    if (data.Length != SuperHiresEditor2File.PlainFileSize)
      throw new InvalidDataException(
        $"A plain Super-hires Editor II picture is {SuperHiresEditor2File.PlainFileSize} bytes, got {data.Length}.");

    return (byte[])data.Clone();
  }
}
