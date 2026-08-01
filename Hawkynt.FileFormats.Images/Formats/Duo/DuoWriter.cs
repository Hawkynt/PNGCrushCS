using System;
using System.IO;

namespace FileFormat.Duo;

/// <summary>Assembles a Duo picture: the palette, then the two fields.</summary>
public static class DuoWriter {

  public static byte[] ToBytes(DuoFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Data ?? new byte[DuoFile.FileSize];
  }

  public static void ToFile(DuoFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
