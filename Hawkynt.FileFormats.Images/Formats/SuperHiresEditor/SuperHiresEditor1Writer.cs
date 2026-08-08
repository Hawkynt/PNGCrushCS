using System;
using System.IO;

namespace FileFormat.SuperHiresEditor;

/// <summary>Assembles Super-hires Editor I (.sh1) file bytes.</summary>
/// <remarks>
/// Only the plain form is written. A packed file is not merely these bytes compressed — it is a
/// different arrangement of them, with the two colour tables folded into one and the sprites stored
/// column by column — so packing would mean rebuilding the picture, not shrinking it.
/// </remarks>
public static class SuperHiresEditor1Writer {

  public static byte[] ToBytes(SuperHiresEditor1File file) {
    var data = file.Data ?? [];
    if (data.Length != SuperHiresEditor1File.PlainFileSize)
      throw new InvalidDataException(
        $"A plain Super-hires Editor I picture is {SuperHiresEditor1File.PlainFileSize} bytes, got {data.Length}.");

    return (byte[])data.Clone();
  }
}
