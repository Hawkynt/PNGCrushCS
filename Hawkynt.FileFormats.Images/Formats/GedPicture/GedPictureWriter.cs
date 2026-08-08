using System;
using System.IO;

namespace FileFormat.GedPicture;

/// <summary>Writes a GED picture back out.</summary>
/// <remarks>
/// The reader keeps the file whole, every table being at an absolute offset, so there is nothing to
/// reassemble — only the length to insist on, which the reader will insist on again.
/// </remarks>
public static class GedPictureWriter {

  public static byte[] ToBytes(GedPictureFile file) {
    var data = file.Data;
    if (data == null || data.Length != GedPictureFile.FileSize)
      throw new InvalidDataException($"A GED picture is {GedPictureFile.FileSize} bytes.");

    return data.AsSpan().ToArray();
  }
}
