using System;
using System.IO;

namespace FileFormat.DuoMedium;

/// <summary>Assembles a medium-resolution Duo picture: the palette, then the two fields.</summary>
public static class DuoMediumWriter {

  public static byte[] ToBytes(DuoMediumFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Data ?? new byte[DuoMediumFile.MinFileSize];
  }

  public static void ToFile(DuoMediumFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
